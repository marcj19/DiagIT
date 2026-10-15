using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Text;
using System.Text.RegularExpressions;

namespace DiagIT
{
    public class WinEvent
    {
        public DateTime Time;
        public string Log = "";
        public string Provider = "";
        public int Id;
        public int Level;
        public string Msg = "";
        public string[] P = new string[0];   // propriedades (dados do evento) como texto

        public string Prop(int i) { return i >= 0 && i < P.Length ? P[i] : ""; }
    }

    public static class EventScanner
    {
        static string Since(DateTime utc) { return "TimeCreated[@SystemTime>='" + utc.ToString("yyyy-MM-ddTHH:mm:ss.000Z", System.Globalization.CultureInfo.InvariantCulture) + "']"; }

        static string Q(string cond, int days)
        {
            return "*[System[(" + cond + ") and " + Since(DateTime.UtcNow.AddDays(-days)) + "]]";
        }

        static string Prov(params string[] names)
        {
            List<string> p = new List<string>();
            foreach (string n in names) p.Add("Provider[@Name='" + n + "']");
            return "(" + string.Join(" or ", p.ToArray()) + ")";
        }

        static string Ids(params int[] ids)
        {
            List<string> p = new List<string>();
            foreach (int i in ids) p.Add("EventID=" + i);
            return "(" + string.Join(" or ", p.ToArray()) + ")";
        }

        public static List<WinEvent> ScanAll(int days, Action<string> progress)
        {
            List<WinEvent> r = new List<WinEvent>();
            Action<string> say = progress ?? delegate(string x) { };

            say("Erros de hardware (WHEA)...");
            Run(r, "System", Q(Prov("Microsoft-Windows-WHEA-Logger"), days), 400, true);

            say("Falhas do driver de vídeo...");
            Run(r, "System", Q("(" + Prov("Display") + " and " + Ids(4101) + ") or " +
                Prov("amdkmdag", "amdwddmg", "atikmdag", "nvlddmkm", "igfx"), days), 400, true);

            say("Quedas de energia e telas azuis...");
            Run(r, "System", Q("(" + Prov("Microsoft-Windows-Kernel-Power") + " and " + Ids(41) + ") or (" +
                Prov("EventLog") + " and " + Ids(6008) + ") or (" +
                Prov("Microsoft-Windows-WER-SystemErrorReporting") + " and " + Ids(1001, 1019) + ")", days), 400, true);   // 1019 = driver apontado pelo Windows

            say("Erros de disco...");
            Run(r, "System", Q(Prov("disk", "Ntfs", "Microsoft-Windows-Ntfs", "stornvme", "storahci", "volmgr", "partmgr", "iaStorA", "iaStorAC", "nvme") +
                " and (Level=1 or Level=2 or Level=3)", days), 400, true);

            say("Limitação de CPU pelo firmware...");
            Run(r, "System", Q(Prov("Microsoft-Windows-Kernel-Processor-Power") + " and " + Ids(37), days), 200, false);

            say("Diagnóstico de memória...");
            Run(r, "System", Q(Prov("Microsoft-Windows-MemoryDiagnostics-Results"), days * 3), 50, true);

            say("Travamentos de aplicativos...");
            Run(r, "Application", Q("(" + Prov("Application Error") + " and " + Ids(1000) + ") or (" +
                Prov("Application Hang") + " and " + Ids(1002) + ")", days), 600, false);
            Run(r, "Application", Q(Prov("Windows Error Reporting") + " and " + Ids(1001), days), 400, false);

            say("Instalações de drivers e atualizações...");
            Run(r, "Application", Q(Prov("MsiInstaller") + " and " + Ids(1033), days), 300, true);
            Run(r, "System", Q("(" + Prov("Microsoft-Windows-WindowsUpdateClient") + " and " + Ids(19) + ") or (" + Prov("Microsoft-Windows-UserPnp") + " and " + Ids(20001, 20003) + ")", days), 500, true);

            r.Sort(delegate(WinEvent a, WinEvent b) { return b.Time.CompareTo(a.Time); });
            say("Concluído.");
            return r;
        }

        static void Run(List<WinEvent> res, string log, string xpath, int max, bool withMsg)
        {
            try
            {
                EventLogQuery q = new EventLogQuery(log, PathType.LogName, xpath);
                q.ReverseDirection = true;
                using (EventLogReader rd = new EventLogReader(q))
                {
                    int n = 0;
                    EventRecord ev;
                    while (n < max && (ev = rd.ReadEvent()) != null)
                    {
                        using (ev)
                        {
                            WinEvent w = new WinEvent();
                            w.Time = ev.TimeCreated ?? DateTime.MinValue;
                            w.Log = log;
                            w.Provider = ev.ProviderName ?? "";
                            w.Id = ev.Id;
                            w.Level = ev.Level ?? 4;
                            try
                            {
                                List<string> ps = new List<string>();
                                foreach (EventProperty p in ev.Properties) ps.Add(p.Value == null ? "" : p.Value.ToString());
                                w.P = ps.ToArray();
                            }
                            catch { }
                            if (withMsg)
                            {
                                try
                                {
                                    string m = ev.FormatDescription();
                                    if (m != null) w.Msg = m.Length > 600 ? m.Substring(0, 600) : m;
                                }
                                catch { }
                            }
                            res.Add(w);
                            n++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write("Eventos [" + log + "]: " + e.Message);
            }
        }

        /// <summary>Procura registro de falha do executável do jogo num intervalo curto (após o encerramento da sessão).</summary>
        public static string FindCrash(string exeName, DateTime from, DateTime to)
        {
            string result = null;
            try
            {
                string xp = "*[System[(" + Prov("Application Error") + " and " + Ids(1000) + ") and " + Since(DateTime.UtcNow.AddMinutes(-15)) + "]]";
                EventLogQuery q = new EventLogQuery("Application", PathType.LogName, xp);
                using (EventLogReader rd = new EventLogReader(q))
                {
                    EventRecord ev;
                    while ((ev = rd.ReadEvent()) != null)
                    {
                        using (ev)
                        {
                            DateTime t = ev.TimeCreated ?? DateTime.MinValue;
                            if (t < from || t > to) continue;
                            string app = ev.Properties.Count > 0 ? Convert.ToString(ev.Properties[0].Value) : "";
                            if (app.IndexOf(exeName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            string mod = ev.Properties.Count > 3 ? Convert.ToString(ev.Properties[3].Value) : "?";
                            string code = ev.Properties.Count > 6 ? Convert.ToString(ev.Properties[6].Value) : "?";
                            result = "O jogo fechou com erro (módulo " + mod + ", código " + code + ")";
                        }
                    }
                }
            }
            catch (Exception e) { Log.Write("FindCrash: " + e.Message); }
            return result;
        }
    }

    public class DiskHealth
    {
        public string Name = "";
        public string MediaType = "";
        public int Health = 0;           // 0 saudável, 1 alerta, 2 não saudável, 5 desconhecido
        public double Temp = double.NaN;
        public double Wear = double.NaN;
        public double ReadErrUncorrected = double.NaN;
        public double WriteErrUncorrected = double.NaN;
        public double PowerOnHours = double.NaN;
        public string DeviceId = "";
    }

    public static class DiskInfo
    {
        public static List<DiskHealth> Read()
        {
            List<DiskHealth> r = new List<DiskHealth>();
            try
            {
                System.Management.ManagementScope sc = new System.Management.ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                sc.Connect();
                System.Management.ManagementObjectSearcher s = new System.Management.ManagementObjectSearcher(sc,
                    new System.Management.ObjectQuery("SELECT DeviceId,FriendlyName,MediaType,HealthStatus FROM MSFT_PhysicalDisk"));
                foreach (System.Management.ManagementObject o in s.Get())
                {
                    DiskHealth d = new DiskHealth();
                    d.DeviceId = Convert.ToString(o["DeviceId"]);
                    d.Name = Convert.ToString(o["FriendlyName"]);
                    int mt = o["MediaType"] == null ? 0 : Convert.ToInt32(o["MediaType"]);
                    d.MediaType = mt == 3 ? "HDD" : mt == 4 ? "SSD" : mt == 5 ? "SCM" : "";
                    d.Health = o["HealthStatus"] == null ? 5 : Convert.ToInt32(o["HealthStatus"]);
                    r.Add(d);
                }
                try
                {
                    System.Management.ManagementObjectSearcher rs = new System.Management.ManagementObjectSearcher(sc,
                        new System.Management.ObjectQuery("SELECT * FROM MSFT_StorageReliabilityCounter"));
                    foreach (System.Management.ManagementObject o in rs.Get())
                    {
                        string id = Convert.ToString(o["DeviceId"]);
                        foreach (DiskHealth d in r)
                        {
                            if (d.DeviceId != id) continue;
                            d.Temp = Num(o, "Temperature");
                            d.Wear = Num(o, "Wear");
                            d.ReadErrUncorrected = Num(o, "ReadErrorsUncorrected");
                            d.WriteErrUncorrected = Num(o, "WriteErrorsUncorrected");
                            d.PowerOnHours = Num(o, "PowerOnHours");
                        }
                    }
                }
                catch (Exception e) { Log.Write("Confiabilidade de disco: " + e.Message); }
            }
            catch (Exception e) { Log.Write("DiskInfo: " + e.Message); }
            return r;
        }

        static double Num(System.Management.ManagementObject o, string p)
        {
            try
            {
                object v = o[p];
                if (v == null) return double.NaN;
                double d = Convert.ToDouble(v);
                return d;
            }
            catch { return double.NaN; }
        }
    }

    public class MachineInfo
    {
        public string Cpu = "", Gpu = "", GpuDriver = "", Board = "", Os = "", VideoSignature = "";
        /// <summary>O Windows só enxerga o adaptador básico: falta o driver do fabricante.</summary>
        public bool GpuMissingDriver;
        public double RamGB, VramGB;
        public int Cores, Threads;

        public static MachineInfo Read(double vramMb)
        {
            MachineInfo m = new MachineInfo();
            try
            {
                foreach (System.Management.ManagementObject o in new System.Management.ManagementObjectSearcher(
                    "SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor").Get())
                {
                    m.Cpu = Convert.ToString(o["Name"]).Trim();
                    m.Cores = Convert.ToInt32(o["NumberOfCores"]);
                    m.Threads = Convert.ToInt32(o["NumberOfLogicalProcessors"]);
                    break;
                }
                List<VideoAdapter> va = VideoAdapters.ReadAll();
                VideoAdapter gpu = VideoAdapters.Primary(va);
                if (gpu != null) { m.Gpu = gpu.Name; m.GpuDriver = gpu.DriverVersion; }
                else
                {
                    // só o adaptador básico do Windows: a placa existe, mas está sem o driver do fabricante
                    m.GpuMissingDriver = va.Count > 0;
                    string brand = va.Count > 0 ? VideoAdapters.VendorFromPnp(va[0].PnpId) : "";
                    m.Gpu = va.Count > 0 ? (brand.Length > 0 ? "Placa " + brand + " (sem driver)" : "Placa de vídeo (sem driver)") : "";
                }
                m.VideoSignature = VideoAdapters.Signature(va);
                foreach (System.Management.ManagementObject o in new System.Management.ManagementObjectSearcher(
                    "SELECT Manufacturer,Product FROM Win32_BaseBoard").Get())
                {
                    m.Board = (Convert.ToString(o["Manufacturer"]) + " " + Convert.ToString(o["Product"])).Trim();
                    break;
                }
                foreach (System.Management.ManagementObject o in new System.Management.ManagementObjectSearcher(
                    "SELECT Caption,BuildNumber FROM Win32_OperatingSystem").Get())
                {
                    m.Os = Convert.ToString(o["Caption"]).Trim() + " (build " + Convert.ToString(o["BuildNumber"]) + ")";
                    break;
                }
                m.RamGB = NativeInfoRam() / 1024.0;
                m.VramGB = double.IsNaN(vramMb) ? 0 : vramMb / 1024.0;
            }
            catch (Exception e) { Log.Write("MachineInfo: " + e.Message); }
            return m;
        }

        static double NativeInfoRam()
        {
            try { return NativeInfo.TotalRamMB(); } catch { return 0; }
        }
    }
}
