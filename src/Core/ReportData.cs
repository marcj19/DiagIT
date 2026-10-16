using System;
using System.Collections.Generic;
using System.Management;
using System.Text.RegularExpressions;

namespace DiagIT
{
    public class DriverRow { public string Class = "", Device = "", Provider = "", Version = ""; public DateTime Date = DateTime.MinValue; }

    /// <summary>Um acontecimento na linha do tempo do relatório. Level: 2 grave, 1 atenção, 0 informação, -1 ação (teste/correção).</summary>
    public class TimelineItem { public DateTime Time; public string Kind = "", Text = ""; public int Level; }

    /// <summary>Dados extras do relatório técnico, coletados na hora de exportar (consultas ao Windows que levam alguns segundos).</summary>
    public class ReportExtras
    {
        public List<WinEvent> Events = new List<WinEvent>();
        public List<DriverRow> Drivers = new List<DriverRow>();
        public List<FixEntry> Fixes = new List<FixEntry>();
        public string Windows = "";
        public List<SensorItem> Sensors = new List<SensorItem>();   // leitura crua de todos os sensores (para conferir cada plataforma)
        public string SensorSources = "";

        public static ReportExtras Collect(List<WinEvent> events) { return Collect(events, null); }

        public static ReportExtras Collect(List<WinEvent> events, SensorHub hub)
        {
            ReportExtras x = new ReportExtras();
            if (hub != null)
            {
                List<SensorItem> l = hub.Latest;   // a lista é trocada inteira a cada leitura: a referência é segura
                if (l != null) x.Sensors = new List<SensorItem>(l);
                x.SensorSources = hub.ActiveName ?? "";
            }
            if (events != null) x.Events = events;
            x.Windows = WindowsVersion();
            x.Drivers = InstalledDrivers();
            x.Fixes = FixLog.Load();
            return x;
        }

        public static string WindowsVersion()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k == null) return Environment.OSVersion.VersionString;
                    string product = Convert.ToString(k.GetValue("ProductName") ?? "Windows");
                    string build = Convert.ToString(k.GetValue("CurrentBuild") ?? "");
                    int b; if (int.TryParse(build, out b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");   // o registro ainda diz "10" no 11
                    return product + " " + Convert.ToString(k.GetValue("DisplayVersion") ?? "") + " (build " + build + "." + Convert.ToString(k.GetValue("UBR") ?? "") + ")";
                }
            }
            catch { return Environment.OSVersion.VersionString; }
        }

        static readonly string[] Classes = { "DISPLAY", "NET", "MEDIA", "SYSTEM", "HDC", "SCSIADAPTER", "USB" };

        /// <summary>Drivers dos dispositivos principais (vídeo, rede, áudio, chipset, armazenamento, USB), sem os genéricos da Microsoft (exceto vídeo).</summary>
        public static List<DriverRow> InstalledDrivers()
        {
            List<DriverRow> r = new List<DriverRow>();
            try
            {
                foreach (ManagementObject o in new ManagementObjectSearcher("SELECT DeviceClass,DeviceName,DriverProviderName,DriverVersion,DriverDate FROM Win32_PnPSignedDriver").Get())
                {
                    string cls = Convert.ToString(o["DeviceClass"] ?? "").ToUpperInvariant();
                    if (Array.IndexOf(Classes, cls) < 0) continue;
                    string prov = Convert.ToString(o["DriverProviderName"] ?? "");
                    if (cls != "DISPLAY" && prov.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    DriverRow d = new DriverRow();
                    d.Class = cls; d.Device = Convert.ToString(o["DeviceName"] ?? ""); d.Provider = prov; d.Version = Convert.ToString(o["DriverVersion"] ?? "");
                    try { d.Date = ManagementDateTimeConverter.ToDateTime(Convert.ToString(o["DriverDate"])); } catch { }
                    if (d.Device.Length == 0 || r.Exists(delegate(DriverRow x) { return x.Device == d.Device && x.Version == d.Version; })) continue;
                    r.Add(d);
                }
            }
            catch (Exception e) { Log.Write("Relatório (drivers): " + e.Message); }
            r.Sort(delegate(DriverRow a, DriverRow b) { int c = string.Compare(a.Class, b.Class, StringComparison.Ordinal); return c != 0 ? c : string.Compare(a.Device, b.Device, StringComparison.OrdinalIgnoreCase); });
            if (r.Count > 120) r.RemoveRange(120, r.Count - 120);
            return r;
        }
    }

    public static partial class Analyzer
    {
        /// <summary>Linha do tempo única (mais recente primeiro): telas azuis com o driver culpado, travamentos, desligamentos, erros de hardware, testes, correções e partidas que caíram.</summary>
        public static List<TimelineItem> Timeline(List<WinEvent> ev, HwSnapshot hw, List<SessionData> sessions, List<TestResult> tests, List<FixEntry> fixes, int days)
        {
            List<TimelineItem> r = new List<TimelineItem>();
            DateTime cut = DateTime.Now.AddDays(-days);
            Action<DateTime, string, string, int> add = delegate(DateTime t, string kind, string text, int level)
            {
                if (t < cut) return;
                if (r.Exists(delegate(TimelineItem x) { return x.Kind == kind && Math.Abs((x.Time - t).TotalMinutes) < 2; })) return;
                TimelineItem i = new TimelineItem(); i.Time = t; i.Kind = kind; i.Text = text; i.Level = level; r.Add(i);
            };
            List<DateTime[]> installs = InstallWindows(ev ?? new List<WinEvent>());
            if (ev != null)
                foreach (WinEvent e in ev)
                {
                    int code = BugcodeOf(e);
                    if (code > 0)
                    {
                        string how, cul = CulpritNear(ev, hw, e.Time, out how);
                        DriverRule dr = Rules.Driver(cul);
                        add(e.Time, "Tela azul", "0x" + code.ToString("X") + " " + Rules.Bug(code).Name +
                            (cul != null ? " — driver apontado: " + cul + (dr != null ? " (" + dr.Name + ")" : "") : ""), 2);
                        continue;
                    }
                    if ((e.Provider == "Display" && e.Id == 4101) || Has(e.Provider, "amdkmdag", "amdwddmg", "atikmdag", "nvlddmkm"))
                    { if (!DuringInstall(installs, e.Time)) add(e.Time, "Vídeo", "O driver de vídeo parou de responder e foi reiniciado (" + e.Provider + " " + e.Id + ")", 1); continue; }
                    if (e.Provider == "Windows Error Reporting" && e.Id == 1001 && e.Prop(2) == "LiveKernelEvent")
                    {
                        bool swap = DuringInstall(installs, e.Time);
                        add(e.Time, "Travamento", "LiveKernelEvent 0x" + e.Prop(5) + (swap ? " (durante a troca do driver de vídeo: esperado)" : ""), !swap && (e.Prop(5) == "141" || e.Prop(5) == "117") ? 1 : 0);
                        continue;
                    }
                    if (e.Provider == "Microsoft-Windows-Kernel-Power" && e.Id == 41 && ParseCode(e.Prop(0)) == 0)
                    { add(e.Time, "Desligamento", "O PC desligou ou reiniciou sem aviso (Kernel-Power 41)", 2); continue; }
                    if (Has(e.Provider, "WHEA"))
                    { add(e.Time, "Hardware", "Erro de hardware registrado (WHEA " + e.Id + ")", e.Level == 1 || e.Id == 1 || e.Id == 18 ? 2 : 1); continue; }
                    if (e.Provider == "Microsoft-Windows-WindowsUpdateClient" && e.Id == 19)
                    {
                        Match m = Regex.Match(e.Prop(0), @"Display\s+-\s+([0-9.]+)");
                        if (m.Success) add(e.Time, "Driver", "O Windows Update instalou o driver de vídeo " + e.Prop(0), 0);
                        continue;
                    }
                    if (e.Provider == "Microsoft-Windows-MemoryDiagnostics-Results")
                    { add(e.Time, "Teste", "Diagnóstico de memória do Windows: " + Trunc(e.Msg, 120), 0); continue; }
                }
            if (sessions != null)
                foreach (SessionData sd in sessions)
                {
                    if (!GameSession(sd)) continue;
                    if (sd.Info.Status == "interrupted") add(sd.Info.End ?? sd.Info.Start, "Partida", sd.Info.Game + " terminou de repente (o PC pode ter caído)", 2);
                    else if (sd.Info.Note.StartsWith("O jogo fechou com erro", StringComparison.Ordinal)) add(sd.Info.End ?? sd.Info.Start, "Partida", sd.Info.Game + ": " + sd.Info.Note, 1);
                }
            if (tests != null)
                foreach (TestResult t in tests)
                    add(t.Start, "Teste", TestResult.KindLabel(t.Kind) + ": " + t.StatusLabel + " — " + t.Summary, t.Status == "passed" ? -1 : t.Status == "failed" || t.Status == "crashed" ? 2 : 0);
            if (fixes != null)
                foreach (FixEntry f in fixes) add(f.When, "Correção", f.Title, -1);
            r.Sort(delegate(TimelineItem a, TimelineItem b) { return b.Time.CompareTo(a.Time); });
            if (r.Count > 200) r.RemoveRange(200, r.Count - 200);
            return r;
        }
    }
}
