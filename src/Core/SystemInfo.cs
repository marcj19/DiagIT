using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace DiagIT
{
    public class BootEntry
    {
        public string Id = "", Description = "", Device = "", OsDevice = "", Path = "";
        public bool IsBootMgr { get { return Path.EndsWith("bootmgfw.efi", StringComparison.OrdinalIgnoreCase); } }
        public bool IsLoader { get { return Path.EndsWith("winload.efi", StringComparison.OrdinalIgnoreCase) || Path.EndsWith("winload.exe", StringComparison.OrdinalIgnoreCase); } }
        public bool IsRamdisk { get { return OsDevice.StartsWith("ramdisk", StringComparison.OrdinalIgnoreCase) || Device.StartsWith("ramdisk", StringComparison.OrdinalIgnoreCase); } }
    }

    /// <summary>Configuração de inicialização (BCD) e discos envolvidos.</summary>
    public class BootInfo
    {
        public bool Read;
        public List<BootEntry> Entries = new List<BootEntry>();
        public List<string> DisplayOrder = new List<string>();
        public string Default = "";
        public int EfiDisk = -1, WindowsDisk = -1;
        public List<int> EfiDisks = new List<int>();

        /// <summary>
        /// Entradas de Windows que são restos de instalações anteriores: apontam para um volume sem Windows (quebradas)
        /// ou não estão no menu e não são a entrada atual (duplicadas). Nunca inclui a entrada padrão.
        /// </summary>
        public List<BootEntry> Stale()
        {
            List<BootEntry> r = new List<BootEntry>();
            foreach (BootEntry e in Entries)
            {
                if (!e.IsLoader || e.IsRamdisk || e.Id == Default) continue;
                if (Broken(e) || !DisplayOrder.Contains(e.Id)) r.Add(e);
            }
            return r;
        }

        /// <summary>O Windows apontado pela entrada não existe mais no volume.</summary>
        public static bool Broken(BootEntry e)
        {
            Match m = Regex.Match(e.OsDevice, @"partition=([A-Za-z]):", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            try { return !File.Exists(m.Groups[1].Value + @":\Windows\System32\winload.efi") && !File.Exists(m.Groups[1].Value + @":\Windows\System32\ntoskrnl.exe"); }
            catch { return false; }
        }

        /// <summary>Entradas que aparecem no menu de inicialização (o "escolha um sistema operacional").</summary>
        public int MenuCount { get { return DisplayOrder.Count; } }
    }

    public class ProblemDevice
    {
        public string Name = "", Class = "", Id = "";
        public int Code;
        public string Meaning
        {
            get
            {
                switch (Code)
                {
                    case 1: case 18: case 28: return "sem driver instalado";
                    case 10: return "o dispositivo não pôde iniciar";
                    case 12: return "conflito de recursos";
                    case 22: return "desativado";
                    case 24: return "ausente ou desconectado";
                    case 31: return "não está funcionando corretamente";
                    case 39: return "driver corrompido ou ausente";
                    case 43: return "o Windows parou o dispositivo porque ele reportou falha";
                    case 45: return "não está conectado agora";
                    case 52: return "driver sem assinatura válida";
                    default: return "código de erro " + Code;
                }
            }
        }
    }

    public class VolumeInfo
    {
        public string Letter = "", Label = "", FileSystem = "";
        public double SizeGB, FreeGB;
        public int Disk = -1;
        public string Health = "";
    }

    public static class SystemInfo
    {
        public static DateTime WindowsInstallDate()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    object v = k == null ? null : k.GetValue("InstallDate");
                    if (v != null) return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Convert.ToInt64(v)).ToLocalTime();
                }
            }
            catch { }
            return DateTime.MinValue;
        }

        public static string WindowsOld(out DateTime created)
        {
            created = DateTime.MinValue;
            try
            {
                string p = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "Windows.old");
                if (!Directory.Exists(p)) return null;
                created = Directory.GetCreationTime(p);
                return p;
            }
            catch { return null; }
        }

        public static List<ProblemDevice> ProblemDevices()
        {
            List<ProblemDevice> r = new List<ProblemDevice>();
            try
            {
                foreach (ManagementObject o in new ManagementObjectSearcher(
                    "SELECT Name,PNPClass,PNPDeviceID,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0").Get())
                {
                    ProblemDevice d = new ProblemDevice();
                    d.Name = Convert.ToString(o["Name"]);
                    d.Class = Convert.ToString(o["PNPClass"]);
                    d.Id = Convert.ToString(o["PNPDeviceID"]);
                    d.Code = Convert.ToInt32(o["ConfigManagerErrorCode"]);
                    if (d.Code == 45 || d.Code == 24) continue;   // desconectado: não é problema
                    if (d.Name.Length == 0) d.Name = d.Id;
                    r.Add(d);
                }
            }
            catch (Exception e) { Log.Write("ProblemDevices: " + e.Message); }
            return r;
        }

        public static List<VolumeInfo> Volumes()
        {
            List<VolumeInfo> r = new List<VolumeInfo>();
            try
            {
                ManagementScope sc = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                Dictionary<string, int> diskOf = new Dictionary<string, int>();
                foreach (ManagementObject p in new ManagementObjectSearcher(sc, new ObjectQuery("SELECT DiskNumber,DriveLetter FROM MSFT_Partition")).Get())
                {
                    object dl = p["DriveLetter"];
                    if (dl == null) continue;
                    char c = Convert.ToChar(dl);
                    if (c != '\0') diskOf[c.ToString()] = Convert.ToInt32(p["DiskNumber"]);
                }
                foreach (ManagementObject v in new ManagementObjectSearcher(sc, new ObjectQuery("SELECT DriveLetter,FileSystemLabel,FileSystem,Size,SizeRemaining,HealthStatus,DriveType FROM MSFT_Volume")).Get())
                {
                    object dl = v["DriveLetter"];
                    if (dl == null || Convert.ToChar(dl) == '\0') continue;
                    if (Convert.ToInt32(v["DriveType"]) != 3) continue;   // só discos fixos
                    VolumeInfo vi = new VolumeInfo();
                    vi.Letter = Convert.ToChar(dl).ToString();
                    vi.Label = Convert.ToString(v["FileSystemLabel"]);
                    vi.FileSystem = Convert.ToString(v["FileSystem"]);
                    vi.SizeGB = Convert.ToDouble(v["Size"]) / 1073741824.0;
                    vi.FreeGB = Convert.ToDouble(v["SizeRemaining"]) / 1073741824.0;
                    int hs = Convert.ToInt32(v["HealthStatus"]);
                    vi.Health = hs == 0 ? "saudável" : hs == 1 ? "ALERTA" : hs == 2 ? "NÃO SAUDÁVEL" : "desconhecido";
                    int dk; if (diskOf.TryGetValue(vi.Letter, out dk)) vi.Disk = dk;
                    r.Add(vi);
                }
            }
            catch (Exception e) { Log.Write("Volumes: " + e.Message); }
            r.Sort(delegate(VolumeInfo a, VolumeInfo b) { return string.Compare(a.Letter, b.Letter, StringComparison.Ordinal); });
            return r;
        }

        /// <summary>Lê a configuração de boot (bcdedit, somente leitura). Requer administrador.</summary>
        public static BootInfo ReadBoot()
        {
            BootInfo b = new BootInfo();
            // discos com partição EFI e disco do Windows (não requer administrador)
            try
            {
                ManagementScope sc = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                string sysLetter = Path.GetPathRoot(Environment.SystemDirectory).Substring(0, 1).ToUpperInvariant();
                foreach (ManagementObject p in new ManagementObjectSearcher(sc, new ObjectQuery("SELECT DiskNumber,DriveLetter,GptType FROM MSFT_Partition")).Get())
                {
                    int disk = Convert.ToInt32(p["DiskNumber"]);
                    string gpt = Convert.ToString(p["GptType"]).ToLowerInvariant();
                    if (gpt.Contains("c12a7328-f81f-11d2-ba4b-00a0c93ec93b") && !b.EfiDisks.Contains(disk)) b.EfiDisks.Add(disk);
                    object dl = p["DriveLetter"];
                    if (dl != null && Convert.ToChar(dl).ToString().ToUpperInvariant() == sysLetter) b.WindowsDisk = disk;
                }
                if (b.EfiDisks.Count > 0) b.EfiDisk = b.EfiDisks.Contains(b.WindowsDisk) ? b.WindowsDisk : b.EfiDisks[0];
            }
            catch (Exception e) { Log.Write("Boot/partições: " + e.Message); }

            if (!SysUtil.IsAdmin()) return b;
            try
            {
                string text = Run(Fixes.Sys("bcdedit.exe"), "/enum all /v");
                BootEntry cur = null;
                Dictionary<BootEntry, Dictionary<string, List<string>>> kv = new Dictionary<BootEntry, Dictionary<string, List<string>>>();
                string lastKey = null;
                foreach (string l in text.Replace("\r", "").Split('\n'))
                {
                    if (Regex.IsMatch(l, @"^-{3,}\s*$")) { cur = new BootEntry(); b.Entries.Add(cur); kv[cur] = new Dictionary<string, List<string>>(); lastKey = null; continue; }
                    if (cur == null) continue;
                    Match m = Regex.Match(l, @"^(\S+)\s{2,}(.*)$");
                    if (m.Success)
                    {
                        lastKey = m.Groups[1].Value.ToLowerInvariant();
                        if (lastKey == "identificador") lastKey = "identifier";
                        if (!kv[cur].ContainsKey(lastKey)) kv[cur][lastKey] = new List<string>();
                        kv[cur][lastKey].Add(m.Groups[2].Value.Trim());
                    }
                    else if (lastKey != null && Regex.IsMatch(l, @"^\s+\S")) kv[cur][lastKey].Add(l.Trim());
                }
                foreach (BootEntry be in b.Entries)
                {
                    Dictionary<string, List<string>> d = kv[be];
                    be.Id = First(d, "identifier");
                    be.Description = First(d, "description").Replace('\u00FF', ' ').Replace('\u00A0', ' ');
                    be.Device = First(d, "device");
                    be.OsDevice = First(d, "osdevice");
                    be.Path = First(d, "path");
                }
                foreach (BootEntry be in b.Entries)
                {
                    if (!be.IsBootMgr) continue;
                    b.Default = First(kv[be], "default");
                    if (kv[be].ContainsKey("displayorder")) b.DisplayOrder.AddRange(kv[be]["displayorder"]);
                    break;
                }                b.Read = b.Entries.Count > 0;
            }
            catch (Exception e) { Log.Write("bcdedit: " + e.Message); }
            return b;
        }

        static string First(Dictionary<string, List<string>> d, string k)
        {
            List<string> l;
            return d.TryGetValue(k, out l) && l.Count > 0 ? l[0] : "";
        }

        public static string Run(string exe, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            using (Process p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return o;
            }
        }
    }
}
