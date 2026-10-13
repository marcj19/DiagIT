using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace DiagIT
{
    /// <summary>Um fato verificável sobre o hardware/sistema (não é estimativa).</summary>
    public class Fact
    {
        public string Group = "", Name = "", Value = "", Note = "";
        public int Level = -1;   // -1 informativo, 0 ok, 1 atenção, 2 problema
        public Fact(string g, string n, string v, int level, string note) { Group = g; Name = n; Value = v; Level = level; Note = note ?? ""; }
    }

    public class CrashDump
    {
        public string File = "";
        public DateTime Time;
        public uint Code;
        public ulong[] P = new ulong[4];
        public bool Live;   // LiveKernelReports (travamento recuperado, sem tela azul)
        public string CodeHex { get { return "0x" + Code.ToString("X"); } }
    }

    public class HwSnapshot
    {
        public List<Fact> Facts = new List<Fact>();
        public List<DriveHealth> Drives = new List<DriveHealth>();
        public List<CrashDump> Dumps = new List<CrashDump>();
        public bool DumpsReadable;
        public int RamModules;
        public bool DualChannel;
        public bool MixedRam;
        public int PcieLanes = -1, PcieMaxLanes = -1, PcieGenLevel = -1;
        public double CommitPct = double.NaN, CommitLimitGB = double.NaN, PagefileGB = double.NaN;
        public bool PagefileAuto;
        public int GpuDriverAgeDays = -1;
        public string GpuDriver = "";
        public int BiosAgeDays = -1;
        public bool CrashDumpEnabled = true;
        public DateTime When = DateTime.Now;
        public BootInfo Boot;
        public List<ProblemDevice> Problems = new List<ProblemDevice>();
        public List<VolumeInfo> Volumes = new List<VolumeInfo>();
        public DateTime WindowsInstalled = DateTime.MinValue;
        public string WindowsOld;
        public DateTime WindowsOldCreated;
        public string GpuName = "", CpuName = "";
        /// <summary>Arquivos de paginação em uso agora (ex.: "C:\pagefile.sys").</summary>
        public List<string> PagefileFiles = new List<string>();
        /// <summary>Programas conhecidos por causar engasgos abertos no momento da coleta.</summary>
        public List<string> Background = new List<string>();
        public string RealtekNic, RealtekDriver = "";
        /// <summary>Política "não incluir drivers nas atualizações do Windows" ativa.</summary>
        public bool WuDriversBlocked;
    }

    public static class HwFacts
    {
        static readonly CultureInfo Pt = new CultureInfo("pt-BR");

        static IEnumerable<ManagementObject> Q(string wql)
        {
            List<ManagementObject> l = new List<ManagementObject>();
            try { foreach (ManagementObject o in new ManagementObjectSearcher(wql).Get()) l.Add(o); }
            catch { }
            return l;
        }

        static string S(ManagementObject o, string p) { try { object v = o[p]; return v == null ? "" : Convert.ToString(v).Trim(); } catch { return ""; } }

        static DateTime WmiDate(string s)
        {
            try { return ManagementDateTimeConverter.ToDateTime(s); } catch { return DateTime.MinValue; }
        }

        public static HwSnapshot Collect(SensorHub hub)
        {
            HwSnapshot h = new HwSnapshot();
            List<Fact> f = h.Facts;

            // ---------- Sistema
            foreach (ManagementObject o in Q("SELECT Caption,BuildNumber,LastBootUpTime FROM Win32_OperatingSystem"))
            {
                f.Add(new Fact("Sistema", "Windows", S(o, "Caption") + " (build " + S(o, "BuildNumber") + ")", -1, null));
                DateTime boot = WmiDate(S(o, "LastBootUpTime"));
                if (boot > DateTime.MinValue)
                {
                    TimeSpan up = DateTime.Now - boot;
                    f.Add(new Fact("Sistema", "Ligado desde", boot.ToString("dd/MM HH:mm", Pt) + " (" + (int)up.TotalHours + " h)", -1, null));
                }
            }
            f.Add(new Fact("Sistema", "DiagIT como administrador", SysUtil.IsAdmin() ? "sim" : "não", SysUtil.IsAdmin() ? 0 : 1,
                SysUtil.IsAdmin() ? null : "Sem administrador não é possível ler a temperatura da CPU, a placa-mãe, o SMART dos discos e os arquivos de tela azul."));

            // ---------- Instalação do Windows, boot e volumes
            h.WindowsInstalled = SystemInfo.WindowsInstallDate();
            if (h.WindowsInstalled > DateTime.MinValue)
                f.Add(new Fact("Windows e inicialização", "Windows instalado em", h.WindowsInstalled.ToString("dd/MM/yyyy HH:mm", Pt) +
                    " (há " + Math.Max(0, (int)(DateTime.Now - h.WindowsInstalled).TotalDays) + " dia(s))", -1, null));
            h.WindowsOld = SystemInfo.WindowsOld(out h.WindowsOldCreated);
            if (h.WindowsOld != null)
                f.Add(new Fact("Windows e inicialização", "Instalação anterior guardada", h.WindowsOld + " (de " + h.WindowsOldCreated.ToString("dd/MM/yyyy HH:mm", Pt) + ")", 1,
                    "O Windows foi instalado sem apagar a partição: a instalação antiga ficou guardada. Confira se há arquivos seus em Windows.old\\Users e depois remova pela página Resolver para liberar espaço."));
            h.Boot = SystemInfo.ReadBoot();
            if (h.Boot.WindowsDisk >= 0 && h.Boot.EfiDisks.Count > 0)
            {
                bool other = !h.Boot.EfiDisks.Contains(h.Boot.WindowsDisk);
                f.Add(new Fact("Windows e inicialização", "Disco que dá a partida no Windows",
                    other ? "disco " + h.Boot.EfiDisks[0] + " (o Windows está no disco " + h.Boot.WindowsDisk + ")" : "o mesmo disco do Windows (disco " + h.Boot.WindowsDisk + ")",
                    other ? 1 : 0, other ? "O gerenciador de boot ficou em outro disco. Se esse disco for removido, formatado ou falhar, o Windows deixa de iniciar. Em uma próxima instalação limpa, desconecte os outros discos durante a instalação." : null));
            }
            if (h.Boot.Read)
            {
                List<BootEntry> stale = h.Boot.Stale();
                if (stale.Count > 0)
                {
                    List<string> names = new List<string>();
                    foreach (BootEntry e in stale) names.Add("\"" + e.Description + "\" em " + e.OsDevice.Replace("partition=", "") +
                        (BootInfo.Broken(e) ? " (esse Windows não existe mais)" : " (duplicada)") + (h.Boot.DisplayOrder.Contains(e.Id) ? " — APARECE NO MENU" : ""));
                    f.Add(new Fact("Windows e inicialização", "Entradas antigas no boot", string.Join("; ", names.ToArray()), 1,
                        "Restos de instalações anteriores do Windows. As marcadas \"aparece no menu\" fazem o PC mostrar a escolha entre dois Windows ao ligar (a antiga não inicia). Remova na página Resolver (com backup)."));
                    f.Add(new Fact("Windows e inicialização", "Opções no menu de inicialização", h.Boot.MenuCount.ToString(), h.Boot.MenuCount > 1 ? 1 : 0, null));
                }
                else f.Add(new Fact("Windows e inicialização", "Entradas no boot", "somente o Windows atual", 0, null));
            }
            h.Volumes = SystemInfo.Volumes();
            foreach (VolumeInfo v in h.Volumes)
            {
                double pct = v.SizeGB > 0 ? 100 * v.FreeGB / v.SizeGB : 0;
                bool low = pct < 10 || v.FreeGB < 15;
                f.Add(new Fact("Volumes", v.Letter + ":" + (v.Label.Length > 0 ? " (" + v.Label + ")" : "") + (v.Disk >= 0 ? " — disco " + v.Disk : ""),
                    v.FreeGB.ToString("0", Pt) + " GB livres de " + v.SizeGB.ToString("0", Pt) + " GB (" + pct.ToString("0") + "%), " + v.FileSystem + ", " + v.Health,
                    v.Health != "saudável" ? 2 : low ? 1 : 0, low ? "Pouco espaço livre deixa o SSD lento e pode impedir atualizações e o arquivo de paginação de crescer." : null));
            }
            h.Problems = SystemInfo.ProblemDevices();
            if (h.Problems.Count == 0) f.Add(new Fact("Dispositivos", "Dispositivos com problema ou sem driver", "nenhum", 0, null));
            foreach (ProblemDevice d in h.Problems)
                f.Add(new Fact("Dispositivos", d.Name, d.Meaning + " (código " + d.Code + ")", d.Code == 22 ? -1 : 1, DeviceHint(d)));

            // ---------- CPU
            foreach (ManagementObject o in Q("SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor"))
            {
                h.CpuName = S(o, "Name");
                f.Add(new Fact("Processador", "Modelo", S(o, "Name"), -1, S(o, "NumberOfCores") + " núcleos / " + S(o, "NumberOfLogicalProcessors") + " threads"));
                break;
            }

            // ---------- Placa-mãe e BIOS
            foreach (ManagementObject o in Q("SELECT Manufacturer,Product FROM Win32_BaseBoard"))
                f.Add(new Fact("Placa-mãe", "Modelo", (S(o, "Manufacturer") + " " + S(o, "Product")).Trim(), -1, null));
            foreach (ManagementObject o in Q("SELECT SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS"))
            {
                DateTime d = WmiDate(S(o, "ReleaseDate"));
                h.BiosAgeDays = d > DateTime.MinValue ? (int)(DateTime.Now - d).TotalDays : -1;
                f.Add(new Fact("Placa-mãe", "BIOS", S(o, "SMBIOSBIOSVersion") + (d > DateTime.MinValue ? " de " + d.ToString("dd/MM/yyyy", Pt) : ""),
                    h.BiosAgeDays > 1095 ? 1 : -1, h.BiosAgeDays > 1095 ? "BIOS com mais de 3 anos: atualizações costumam corrigir estabilidade de memória/CPU." : null));
            }

            // ---------- RAM
            HashSet<string> parts = new HashSet<string>();
            HashSet<string> channels = new HashSet<string>();
            int speedMin = int.MaxValue;
            double totalGb = 0;
            foreach (ManagementObject o in Q("SELECT BankLabel,DeviceLocator,Capacity,Speed,ConfiguredClockSpeed,PartNumber,Manufacturer FROM Win32_PhysicalMemory"))
            {
                h.RamModules++;
                double gb = 0; try { gb = Convert.ToDouble(o["Capacity"]) / 1073741824.0; } catch { }
                totalGb += gb;
                int sp = 0, cf = 0; int.TryParse(S(o, "Speed"), out sp); int.TryParse(S(o, "ConfiguredClockSpeed"), out cf);
                if (cf > 0) speedMin = Math.Min(speedMin, cf);
                string bank = S(o, "BankLabel");
                if (bank.IndexOf("CHANNEL A", StringComparison.OrdinalIgnoreCase) >= 0) channels.Add("A");
                if (bank.IndexOf("CHANNEL B", StringComparison.OrdinalIgnoreCase) >= 0) channels.Add("B");
                parts.Add(S(o, "PartNumber"));
                f.Add(new Fact("Memória RAM", bank + " / " + S(o, "DeviceLocator"), gb.ToString("0", Pt) + " GB  " + S(o, "PartNumber") + "  " + cf + " MT/s" +
                    (sp > cf && cf > 0 ? " (suporta " + sp + ")" : ""), -1, null));
            }
            h.DualChannel = channels.Count >= 2 || (h.RamModules >= 2 && channels.Count == 0);
            h.MixedRam = parts.Count > 1;
            if (h.RamModules > 0)
            {
                f.Add(new Fact("Memória RAM", "Configuração", totalGb.ToString("0", Pt) + " GB em " + h.RamModules + " pente(s), " + (h.DualChannel ? "dual channel" : "single channel"),
                    h.DualChannel ? 0 : 1, h.DualChannel ? null : "Single channel reduz bastante o desempenho em jogos (use 2 pentes nos slots indicados no manual)."));
                if (h.MixedRam)
                    f.Add(new Fact("Memória RAM", "Pentes", "modelos diferentes misturados", 1, "Kits diferentes misturados são causa comum de instabilidade; teste cada pente separadamente."));
            }

            // ---------- Memória virtual
            try
            {
                h.CommitPct = NativeInfo.CommitPercent();
                h.CommitLimitGB = NativeInfo.CommitLimitGB();
                foreach (ManagementObject o in Q("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem"))
                    h.PagefileAuto = S(o, "AutomaticManagedPagefile").Equals("True", StringComparison.OrdinalIgnoreCase);
                double pf = 0;
                foreach (ManagementObject o in Q("SELECT Name,AllocatedBaseSize FROM Win32_PageFileUsage")) { double v; double.TryParse(S(o, "AllocatedBaseSize"), out v); pf += v; h.PagefileFiles.Add(S(o, "Name")); }
                h.PagefileGB = pf / 1024.0;
                int lvl = h.CommitPct >= 95 ? 2 : h.CommitPct >= 85 ? 1 : 0;
                f.Add(new Fact("Memória RAM", "Memória virtual em uso", h.CommitPct.ToString("0", Pt) + "% de " + h.CommitLimitGB.ToString("0.0", Pt) + " GB (arquivo de paginação " +
                    h.PagefileGB.ToString("0.0", Pt) + " GB" + (h.PagefileAuto ? ", automático" : "") + ")", lvl,
                    lvl > 0 ? "Perto de 100% o Windows recusa memória: jogos fecham ou o PC congela. A página Resolver ajusta isso." : null));
            }
            catch { }

            // ---------- GPU
            foreach (ManagementObject o in Q("SELECT Name,DriverVersion,DriverDate FROM Win32_VideoController"))
            {
                string n = S(o, "Name");
                if (n.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                DateTime d = WmiDate(S(o, "DriverDate"));
                h.GpuDriver = S(o, "DriverVersion");
                h.GpuName = n;
                h.GpuDriverAgeDays = d > DateTime.MinValue ? (int)(DateTime.Now - d).TotalDays : -1;
                f.Add(new Fact("Placa de vídeo", "Modelo", n, -1, null));
                f.Add(new Fact("Placa de vídeo", "Driver", h.GpuDriver + (d > DateTime.MinValue ? " de " + d.ToString("dd/MM/yyyy", Pt) : ""),
                    h.GpuDriverAgeDays > 240 ? 1 : 0, h.GpuDriverAgeDays > 240 ? "Driver com mais de 8 meses." : null));
                break;
            }
            double vram = NativeInfo.TotalVramMB();
            if (!double.IsNaN(vram)) f.Add(new Fact("Placa de vídeo", "Memória de vídeo", (vram / 1024).ToString("0.0", Pt) + " GB", -1, null));
            PcieMaxFromPnp(h);
            if (hub != null && hub.Adl != null)
            {
                int g, w; hub.Adl.PcieLink(out g, out w);
                h.PcieLanes = w; h.PcieGenLevel = g;
            }
            else if (hub != null && hub.Nvml != null) { h.PcieLanes = hub.Nvml.PcieLanes; h.PcieGenLevel = hub.Nvml.PcieGen; }
            if (h.PcieLanes > 0)
            {
                bool reduced = h.PcieMaxLanes > 0 && h.PcieLanes < h.PcieMaxLanes;
                string gen = h.PcieGenLevel >= 0 && hub != null && hub.Adl != null ? "PCIe " + (h.PcieGenLevel + 1) + ".0 " : h.PcieGenLevel > 0 ? "PCIe " + h.PcieGenLevel + ".0 " : "";
                f.Add(new Fact("Placa de vídeo", "Link PCIe", gen + "x" + h.PcieLanes + (h.PcieMaxLanes > 0 ? " (a placa suporta até x" + h.PcieMaxLanes + ")" : ""), reduced ? 2 : 0,
                    reduced ? "A GPU está usando menos pistas do que suporta: slot sujo, placa mal encaixada, riser defeituoso ou slot da placa-mãe com problema." : null));
            }

            // ---------- Discos (saúde lida do próprio disco)
            try { h.Drives = Smart.ReadAll(); } catch { }
            foreach (DriveHealth d in h.Drives)
            {
                string g = "Armazenamento";
                string head = d.Model + " (" + d.Bus + ", " + (d.SizeBytes / 1e9).ToString("0") + " GB)";
                if (!d.Read) { f.Add(new Fact(g, head, "saúde não lida", -1, d.Error)); continue; }
                List<string> pr = d.Problems();
                List<string> info = new List<string>();
                if (!double.IsNaN(d.UsedPct)) info.Add("desgaste " + d.UsedPct.ToString("0") + "%");
                if (!double.IsNaN(d.TempC)) info.Add(d.TempC.ToString("0") + " °C");
                if (!double.IsNaN(d.PowerOnHours)) info.Add(d.PowerOnHours.ToString("N0", Pt) + " h");
                if (!double.IsNaN(d.UnsafeShutdowns)) info.Add(d.UnsafeShutdowns.ToString("N0", Pt) + " desligamentos inseguros");
                f.Add(new Fact(g, head, pr.Count == 0 ? "saudável — " + string.Join(", ", info.ToArray()) : string.Join("; ", pr.ToArray()), pr.Count == 0 ? 0 : 2,
                    pr.Count == 0 ? null : "Faça backup imediatamente."));
            }

            // ---------- Telas azuis e travamentos com dump
            ReadDumps(h);
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl"))
                {
                    object v = k == null ? null : k.GetValue("CrashDumpEnabled");
                    h.CrashDumpEnabled = v == null || Convert.ToInt32(v) != 0;
                }
            }
            catch { }
            int bsod = 0, live = 0;
            foreach (CrashDump d in h.Dumps) { if (d.Live) live++; else bsod++; }
            if (!h.CrashDumpEnabled)
                f.Add(new Fact("Travamentos", "Registro de telas azuis", "desativado", 1, "Sem isso o Windows não guarda a causa das telas azuis. Ative em Resolver."));
            if (h.DumpsReadable)
            {
                f.Add(new Fact("Travamentos", "Telas azuis registradas", bsod == 0 ? "nenhuma" : bsod + " (última " + Last(h.Dumps, false) + ")", bsod == 0 ? 0 : 2, null));
                f.Add(new Fact("Travamentos", "Travamentos recuperados (LiveKernelReports)", live == 0 ? "nenhum" : live + " (último " + Last(h.Dumps, true) + ")", live == 0 ? 0 : 1, null));
            }
            else f.Add(new Fact("Travamentos", "Arquivos de tela azul", "não lidos", -1, "Requer administrador."));

            // ---------- Sensores ao vivo
            if (hub != null)
            {
                Sample s = new Sample();
                hub.Apply(s);
                if (!double.IsNaN(s[F.V12])) f.Add(new Fact("Energia", "+12 V (placa-mãe)", s[F.V12].ToString("0.000", Pt) + " V", (s[F.V12] < 11.4 || s[F.V12] > 12.6) ? 1 : 0, null));
                if (!double.IsNaN(s[F.V5])) f.Add(new Fact("Energia", "+5 V", s[F.V5].ToString("0.000", Pt) + " V", (s[F.V5] < 4.75 || s[F.V5] > 5.25) ? 1 : 0, null));
                if (!double.IsNaN(s[F.V33])) f.Add(new Fact("Energia", "+3,3 V", s[F.V33].ToString("0.000", Pt) + " V", (s[F.V33] < 3.135 || s[F.V33] > 3.465) ? 1 : 0, null));
                if (!double.IsNaN(s[F.CpuFan])) f.Add(new Fact("Refrigeração", "Ventoinha da CPU", s[F.CpuFan].ToString("0") + " RPM", s[F.CpuFan] < 200 ? 2 : 0,
                    s[F.CpuFan] < 200 ? "A ventoinha do cooler da CPU parece parada. Verifique o cabo no conector CPU_FAN." : null));
                if (!double.IsNaN(s[F.CpuTemp])) f.Add(new Fact("Refrigeração", "Temperatura da CPU agora", s[F.CpuTemp].ToString("0") + " °C", s[F.CpuTemp] >= 90 ? 2 : s[F.CpuTemp] >= 80 ? 1 : 0, null));
                if (!double.IsNaN(s[F.GpuTemp])) f.Add(new Fact("Refrigeração", "Temperatura da GPU agora", s[F.GpuTemp].ToString("0") + " °C" +
                    (double.IsNaN(s[F.GpuHot]) ? "" : " (hotspot " + s[F.GpuHot].ToString("0") + " °C)") + (double.IsNaN(s[F.GpuMemTemp]) ? "" : ", VRAM " + s[F.GpuMemTemp].ToString("0") + " °C"),
                    s[F.GpuHot] >= 100 ? 1 : 0, null));
                if (!double.IsNaN(s[F.BoardTemp])) f.Add(new Fact("Refrigeração", "Placa-mãe (sensor mais quente)", s[F.BoardTemp].ToString("0") + " °C", s[F.BoardTemp] >= 85 ? 1 : 0, null));
                f.Add(new Fact("Sistema", "Sensores disponíveis", hub.Coverage, -1, null));
            }

            // ---------- Jogos: itens do guia da comunidade que dá para conferir
            const string G = "Jogos e desempenho";
            foreach (string pf in h.PagefileFiles)
            {
                string letter = pf.Length > 0 ? pf.Substring(0, 1).ToUpperInvariant() : "";
                VolumeInfo v = h.Volumes.Find(delegate(VolumeInfo x) { return x.Letter.Equals(letter, StringComparison.OrdinalIgnoreCase); });
                string media = v == null || v.Media.Length == 0 ? "tipo de disco desconhecido" : v.Media;
                f.Add(new Fact(G, "Arquivo de paginação", pf + " (" + media + ")", v != null && v.Media == "HDD" ? 1 : 0,
                    v != null && v.Media == "HDD" ? "No HD, a troca de memória durante o jogo causa engasgos. A página Resolver tira do HD e mantém no SSD." : null));
            }
            h.Background = Interference.Running();
            f.Add(new Fact(G, "Programas que costumam causar engasgos", h.Background.Count == 0 ? "nenhum aberto agora" : string.Join(", ", h.Background.ToArray()), h.Background.Count == 0 ? 0 : -1,
                h.Background.Count == 0 ? null : "Só é problema se as partidas com eles abertos tiverem mais travamentos; os Resultados comparam."));
            string drv;
            h.RealtekNic = Interference.RealtekNic(out drv);
            h.RealtekDriver = drv;
            if (h.RealtekNic != null) f.Add(new Fact(G, "Placa de rede", h.RealtekNic + (drv.Length > 0 ? " — driver " + drv : ""), -1,
                "Relatada pela comunidade como causa de engasgos e estalos no áudio mesmo sem cabo de rede."));
            h.WuDriversBlocked = Fixes.WuDriversBlocked();
            f.Add(new Fact(G, "Windows Update trocando drivers", h.WuDriversBlocked ? "bloqueado" : "permitido", -1,
                h.WuDriversBlocked ? null : "O Windows pode substituir o driver de vídeo instalado por você; os Resultados avisam se isso aconteceu."));
            return h;
        }

        public static string DeviceHint(ProblemDevice d)
        {
            string n = d.Name.ToLowerInvariant();
            if (n.Contains("h80i") || n.Contains("h100i") || n.Contains("h115i") || n.Contains("h150i") || n.Contains("corsair"))
                return "É a conexão USB do water cooler Corsair. O cooler funciona sem ela, mas para monitorar a bomba e a temperatura do líquido instale o Corsair iCUE.";
            if (d.Code == 28 || d.Code == 1 || d.Code == 18) return "Instale o driver do fabricante (site do fabricante da placa-mãe ou do próprio dispositivo) ou procure em Windows Update > Atualizações opcionais.";
            if (d.Code == 43) return "O dispositivo reportou falha: reconecte-o, troque de porta USB e reinstale o driver. Se repetir, o dispositivo pode estar com defeito.";
            return "Abra o Gerenciador de Dispositivos para ver os detalhes e reinstalar o driver.";
        }

        static string Last(List<CrashDump> l, bool live)
        {
            DateTime t = DateTime.MinValue;
            foreach (CrashDump d in l) if (d.Live == live && d.Time > t) t = d.Time;
            return t == DateTime.MinValue ? "?" : t.ToString("dd/MM HH:mm", Pt);
        }

        /// <summary>Número máximo de pistas que a GPU suporta (do próprio dispositivo PCI).</summary>
        static void PcieMaxFromPnp(HwSnapshot h)
        {
            try
            {
                foreach (ManagementObject o in Q("SELECT PNPDeviceID FROM Win32_VideoController"))
                {
                    string id = S(o, "PNPDeviceID");
                    if (!id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) continue;
                    object v = DevProp.Get(id, DevProp.MaxLinkWidth);
                    if (v != null) h.PcieMaxLanes = Convert.ToInt32(v);
                    break;
                }
            }
            catch { }
        }

        public static void ReadDumps(HwSnapshot h)
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            List<string> files = new List<string>();
            bool any = false;
            try
            {
                string md = Path.Combine(win, "Minidump");
                if (Directory.Exists(md)) { files.AddRange(Directory.GetFiles(md, "*.dmp")); any = true; }
                else any = true;
                string mem = Path.Combine(win, "MEMORY.DMP");
                if (File.Exists(mem)) files.Add(mem);
            }
            catch { any = false; }
            List<string> live = new List<string>();
            try
            {
                string lk = Path.Combine(win, "LiveKernelReports");
                if (Directory.Exists(lk)) live.AddRange(Directory.GetFiles(lk, "*.dmp", SearchOption.AllDirectories));
            }
            catch { any = false; }
            h.DumpsReadable = any && SysUtil.IsAdmin();
            foreach (string fl in files) { CrashDump d = Parse(fl); if (d != null) h.Dumps.Add(d); }
            foreach (string fl in live) { CrashDump d = Parse(fl); if (d != null) { d.Live = true; h.Dumps.Add(d); } }
            h.Dumps.Sort(delegate(CrashDump a, CrashDump b) { return b.Time.CompareTo(a.Time); });
        }

        /// <summary>Lê o código de erro (bugcheck) do cabeçalho de um arquivo de dump do Windows.</summary>
        public static CrashDump Parse(string path)
        {
            try
            {
                byte[] b = new byte[0x60];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    if (fs.Read(b, 0, b.Length) < b.Length) return null;
                CrashDump d = new CrashDump();
                d.File = path;
                d.Time = File.GetLastWriteTime(path);
                string sig = System.Text.Encoding.ASCII.GetString(b, 0, 8);
                if (sig == "PAGEDU64")
                {
                    d.Code = BitConverter.ToUInt32(b, 0x38);
                    for (int i = 0; i < 4; i++) d.P[i] = BitConverter.ToUInt64(b, 0x40 + i * 8);
                }
                else if (sig == "PAGEDUMP")
                {
                    d.Code = BitConverter.ToUInt32(b, 0x20);
                    for (int i = 0; i < 4; i++) d.P[i] = BitConverter.ToUInt32(b, 0x24 + i * 4);
                }
                else return null;
                return d;
            }
            catch { return null; }
        }
    }

    /// <summary>Propriedades de dispositivo (SetupAPI) — usado para ler o máximo de pistas PCIe da GPU.</summary>
    public static class DevProp
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct DEVPROPKEY { public Guid fmtid; public uint pid; }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(IntPtr guid, string enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiOpenDeviceInfo(IntPtr set, [MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr hwnd, uint flags, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type, byte[] buf, uint size, out uint req, uint flags);
        [DllImport("setupapi.dll")]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr guid, IntPtr hwnd);

        static readonly Guid PciProps = new Guid("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");
        public static DEVPROPKEY CurLinkSpeed { get { DEVPROPKEY k; k.fmtid = PciProps; k.pid = 9; return k; } }
        public static DEVPROPKEY CurLinkWidth { get { DEVPROPKEY k; k.fmtid = PciProps; k.pid = 10; return k; } }
        public static DEVPROPKEY MaxLinkSpeed { get { DEVPROPKEY k; k.fmtid = PciProps; k.pid = 11; return k; } }
        public static DEVPROPKEY MaxLinkWidth { get { DEVPROPKEY k; k.fmtid = PciProps; k.pid = 12; return k; } }

        public static object Get(string instanceId, DEVPROPKEY key)
        {
            IntPtr set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return null;
            try
            {
                SP_DEVINFO_DATA d = new SP_DEVINFO_DATA();
                d.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                if (!SetupDiOpenDeviceInfo(set, instanceId, IntPtr.Zero, 0, ref d)) return null;
                byte[] buf = new byte[64];
                uint type, req;
                if (!SetupDiGetDevicePropertyW(set, ref d, ref key, out type, buf, (uint)buf.Length, out req, 0)) return null;
                if (type == 7) return BitConverter.ToUInt32(buf, 0);   // DEVPROP_TYPE_UINT32
                return null;
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
    }
}
