using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text;
using System.Threading;

namespace DiagIT
{
    /// <summary>Correções que o usuário pode aplicar pela interface (todas pedem confirmação antes).</summary>
    public static class Fixes
    {
        static readonly CultureInfo Pt = new CultureInfo("pt-BR");

        // ------------------------------------------------------------------ memória virtual
        public static void RecommendedPagefile(out int initMb, out int maxMb)
        {
            double ram = NativeInfo.TotalRamMB();
            initMb = (int)Math.Max(8192, Math.Round(ram / 1024) * 1024);
            maxMb = initMb * 2;
            try
            {
                DriveInfo c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                long freeMb = c.AvailableFreeSpace / 1048576;
                if (freeMb - 10240 < maxMb) { maxMb = (int)Math.Max(4096, freeMb - 10240); initMb = Math.Min(initMb, maxMb); }
            }
            catch { }
        }

        public static string PagefileStatus()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Memória virtual em uso agora: " + NativeInfo.CommitPercent().ToString("0", Pt) + "% de " + NativeInfo.CommitLimitGB().ToString("0.0", Pt) + " GB.");
            try
            {
                foreach (ManagementObject o in new ManagementObjectSearcher("SELECT Name,AllocatedBaseSize FROM Win32_PageFileUsage").Get())
                    sb.Append(" Arquivo " + o["Name"] + ": " + o["AllocatedBaseSize"] + " MB.");
                foreach (ManagementObject o in new ManagementObjectSearcher("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem").Get())
                    sb.Append(Convert.ToBoolean(o["AutomaticManagedPagefile"]) ? " Gerenciado automaticamente pelo Windows." : " Tamanho definido manualmente.");
            }
            catch { }
            return sb.ToString();
        }

        public static bool SetPagefile(int initMb, int maxMb, out string msg)
        {
            try
            {
                ConnectionOptions co = new ConnectionOptions();
                co.EnablePrivileges = true;
                ManagementScope sc = new ManagementScope(@"\\.\root\cimv2", co);
                sc.Connect();
                foreach (ManagementObject cs in new ManagementObjectSearcher(sc, new ObjectQuery("SELECT * FROM Win32_ComputerSystem")).Get())
                {
                    cs["AutomaticManagedPagefile"] = false;
                    cs.Put();
                }
                string drive = Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\');
                string name = drive + "\\pagefile.sys";
                ManagementObject setting = null;
                foreach (ManagementObject o in new ManagementObjectSearcher(sc, new ObjectQuery("SELECT * FROM Win32_PageFileSetting")).Get())
                    if (string.Equals(Convert.ToString(o["Name"]), name, StringComparison.OrdinalIgnoreCase)) setting = o;
                if (setting == null)
                {
                    ManagementClass mc = new ManagementClass(sc, new ManagementPath("Win32_PageFileSetting"), null);
                    setting = mc.CreateInstance();
                    setting["Name"] = name;
                }
                setting["InitialSize"] = (uint)initMb;
                setting["MaximumSize"] = (uint)maxMb;
                setting.Put();
                msg = "Memória virtual definida: " + initMb.ToString("N0", Pt) + " MB a " + maxMb.ToString("N0", Pt) + " MB em " + drive +
                    ". Reinicie o computador para aplicar.";
                Log.Write("Correção aplicada: " + msg);
                return true;
            }
            catch (Exception e)
            {
                msg = "Não foi possível alterar a memória virtual: " + e.Message + (SysUtil.IsAdmin() ? "" : " (é preciso executar o DiagIT como administrador)");
                return false;
            }
        }

        public static bool RestorePagefileAuto(out string msg)
        {
            try
            {
                ConnectionOptions co = new ConnectionOptions();
                co.EnablePrivileges = true;
                ManagementScope sc = new ManagementScope(@"\\.\root\cimv2", co);
                sc.Connect();
                foreach (ManagementObject cs in new ManagementObjectSearcher(sc, new ObjectQuery("SELECT * FROM Win32_ComputerSystem")).Get())
                {
                    cs["AutomaticManagedPagefile"] = true;
                    cs.Put();
                }
                msg = "Memória virtual voltou ao gerenciamento automático do Windows. Reinicie para aplicar.";
                return true;
            }
            catch (Exception e) { msg = "Falhou: " + e.Message; return false; }
        }

        /// <summary>Volumes em HD com arquivo de paginação, quando o disco do sistema é SSD (vazio = nada a fazer).</summary>
        public static List<string> PagefileOnHdd()
        {
            List<string> r = new List<string>();
            try
            {
                List<VolumeInfo> vols = SystemInfo.Volumes();
                string sys = Path.GetPathRoot(Environment.SystemDirectory).Substring(0, 1);
                VolumeInfo sv = vols.Find(delegate(VolumeInfo v) { return v.Letter.Equals(sys, StringComparison.OrdinalIgnoreCase); });
                if (sv == null || sv.Media != "SSD") return r;
                foreach (ManagementObject o in new ManagementObjectSearcher("SELECT Name FROM Win32_PageFileUsage").Get())
                {
                    string n = Convert.ToString(o["Name"]);
                    if (n.Length == 0) continue;
                    VolumeInfo v = vols.Find(delegate(VolumeInfo x) { return x.Letter.Equals(n.Substring(0, 1), StringComparison.OrdinalIgnoreCase); });
                    if (v != null && v.Media == "HDD") r.Add(v.Letter);
                }
            }
            catch { }
            return r;
        }

        /// <summary>Tira o arquivo de paginação dos HDs e deixa um "gerenciado pelo sistema" no SSD do Windows.</summary>
        public static bool MovePagefileOffHdd(out string msg)
        {
            try
            {
                List<string> hdd = PagefileOnHdd();
                if (hdd.Count == 0) { msg = "Nenhum arquivo de paginação em HD (ou o Windows não está em SSD). Nada a alterar."; return true; }
                ConnectionOptions co = new ConnectionOptions();
                co.EnablePrivileges = true;
                ManagementScope sc = new ManagementScope(@"\\.\root\cimv2", co);
                sc.Connect();
                foreach (ManagementObject cs in new ManagementObjectSearcher(sc, new ObjectQuery("SELECT * FROM Win32_ComputerSystem")).Get())
                {
                    cs["AutomaticManagedPagefile"] = false;
                    cs.Put();
                }
                string sysName = Path.GetPathRoot(Environment.SystemDirectory).Substring(0, 2) + "\\pagefile.sys";
                bool hasSys = false;
                foreach (ManagementObject o in new ManagementObjectSearcher(sc, new ObjectQuery("SELECT * FROM Win32_PageFileSetting")).Get())
                {
                    string n = Convert.ToString(o["Name"]);
                    if (string.Equals(n, sysName, StringComparison.OrdinalIgnoreCase)) hasSys = true;
                    else if (n.Length > 0 && hdd.Exists(delegate(string l) { return l.Equals(n.Substring(0, 1), StringComparison.OrdinalIgnoreCase); })) o.Delete();
                }
                if (!hasSys)
                {
                    ManagementClass mc = new ManagementClass(sc, new ManagementPath("Win32_PageFileSetting"), null);
                    ManagementObject s = mc.CreateInstance();
                    s["Name"] = sysName;
                    s["InitialSize"] = (uint)0;   // 0/0 = tamanho gerenciado pelo sistema
                    s["MaximumSize"] = (uint)0;
                    s.Put();
                }
                msg = "Arquivo de paginação removido do HD (" + string.Join(", ", hdd.ToArray()) + ":) e mantido no SSD " + sysName.Substring(0, 2) +
                    (hasSys ? " com o tamanho que já estava definido" : " com tamanho gerenciado pelo sistema") + ". Reinicie o computador para aplicar.";
                Log.Write("Correção aplicada: " + msg);
                return true;
            }
            catch (Exception e)
            {
                msg = "Não foi possível alterar a memória virtual: " + e.Message + (SysUtil.IsAdmin() ? "" : " (é preciso executar o DiagIT como administrador)");
                return false;
            }
        }

        // ------------------------------------------------------------------ Windows Update x drivers
        const string WuPolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

        public static bool WuDriversBlocked()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(WuPolicy))
                {
                    object v = k == null ? null : k.GetValue("ExcludeWUDriversInQualityUpdate");
                    return v != null && Convert.ToInt32(v) == 1;
                }
            }
            catch { return false; }
        }

        /// <summary>Política oficial "Não incluir drivers com as atualizações do Windows" (reversível).</summary>
        public static bool SetWuDriversBlocked(bool block, out string msg)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(WuPolicy))
                {
                    if (block) k.SetValue("ExcludeWUDriversInQualityUpdate", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    else k.DeleteValue("ExcludeWUDriversInQualityUpdate", false);
                }
                msg = block ? "O Windows Update não vai mais instalar drivers (vídeo, chipset, rede...). Atualize-os pelo site do fabricante. Vale a partir da próxima busca de atualizações."
                            : "O Windows Update voltou a poder instalar drivers.";
                Log.Write("Correção aplicada: " + msg);
                return true;
            }
            catch (Exception e)
            {
                msg = "Não foi possível alterar a política do Windows Update: " + e.Message + (SysUtil.IsAdmin() ? "" : " (é preciso executar o DiagIT como administrador)");
                return false;
            }
        }

        // ------------------------------------------------------------------ entradas antigas do boot
        public static bool CleanBootEntries(Action<string> log)
        {
            BootInfo b = SystemInfo.ReadBoot();
            if (!b.Read) { log("Não foi possível ler a configuração de boot (requer administrador)."); return false; }
            List<BootEntry> stale = b.Stale();
            if (stale.Count == 0) { log("Nenhuma entrada antiga encontrada."); return true; }
            Paths.Ensure();
            string bak = Path.Combine(Paths.Root, "backup_boot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bcd");
            string o = SystemInfo.Run(Sys("bcdedit.exe"), "/export \"" + bak + "\"");
            if (!File.Exists(bak)) { log("Backup falhou, nada foi alterado: " + o.Trim()); return false; }
            log("Backup da configuração de boot salvo em " + bak + " (para restaurar: bcdedit /import \"" + bak + "\").");
            foreach (BootEntry e in stale)
            {
                o = SystemInfo.Run(Sys("bcdedit.exe"), "/delete " + e.Id);
                log("Removida: \"" + e.Description + "\" (" + e.OsDevice + ") — " + o.Trim());
            }
            Log.Write("Correção aplicada: " + stale.Count + " entradas antigas removidas do boot (backup " + bak + ")");
            return true;
        }

        // ------------------------------------------------------------------ registro de telas azuis
        public static bool EnableMinidumps(out string msg)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl", true))
                {
                    k.SetValue("CrashDumpEnabled", 3, Microsoft.Win32.RegistryValueKind.DWord);
                    k.SetValue("MinidumpDir", @"%SystemRoot%\Minidump", Microsoft.Win32.RegistryValueKind.ExpandString);
                }
                msg = "Registro de telas azuis (minidump) ativado.";
                return true;
            }
            catch (Exception e) { msg = "Falhou: " + e.Message; return false; }
        }

        // ------------------------------------------------------------------ teste de memória do Windows (no boot)
        public static bool ScheduleMemoryDiagnostic(out string msg)
        {
            try
            {
                Process.Start(Path.Combine(Environment.SystemDirectory, "mdsched.exe"));
                msg = "A ferramenta de diagnóstico de memória do Windows foi aberta. Escolha \"Reiniciar agora\" ou \"na próxima inicialização\". O resultado aparece no Diagnóstico depois.";
                return true;
            }
            catch (Exception e) { msg = "Falhou: " + e.Message; return false; }
        }

        // ------------------------------------------------------------------ comandos de reparo com saída ao vivo
        /// <summary>Executa um comando do sistema e entrega a saída linha a linha. Retorna o código de saída.</summary>
        public static int RunTool(string exe, string args, bool unicodeOutput, Action<string> line, Func<bool> cancel)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = unicodeOutput ? Encoding.Unicode : Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            psi.StandardErrorEncoding = psi.StandardOutputEncoding;
            using (Process p = Process.Start(psi))
            {
                Thread err = new Thread(delegate() { try { string l; while ((l = p.StandardError.ReadLine()) != null) Emit(l, line); } catch { } });
                err.IsBackground = true;
                err.Start();
                StringBuilder cur = new StringBuilder();
                int ch;
                while ((ch = p.StandardOutput.Read()) >= 0)
                {
                    if (cancel != null && cancel()) { try { p.Kill(); } catch { } break; }
                    if (ch == '\r' || ch == '\n') { if (cur.Length > 0) { Emit(cur.ToString(), line); cur.Length = 0; } }
                    else if (ch != 0) cur.Append((char)ch);
                }
                if (cur.Length > 0) Emit(cur.ToString(), line);
                p.WaitForExit();
                err.Join(2000);
                return p.ExitCode;
            }
        }

        static void Emit(string s, Action<string> line)
        {
            s = s.Trim();
            if (s.Length > 0) line(s);
        }

        public static string Sys(string exe) { return Path.Combine(Environment.SystemDirectory, exe); }
    }
}
