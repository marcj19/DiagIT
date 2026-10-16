using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DiagIT
{
    /// <summary>
    /// Um ajuste de Windows que liga e desliga com um clique, sempre reversível. Dois grupos:
    /// "desempenho" (deixar o Windows mais leve; não entram no "Resolveu?") e "estabilidade" (experimentos para descobrir a causa de travamentos,
    /// um de cada vez; cada um é acompanhado pelo "Resolveu?").
    /// </summary>
    public class Tweak
    {
        public string Id = "", Name = "", Done = "", Scope = "video", What = "", Group = "estabilidade";
        /// <summary>O Windows só aplica depois de reiniciar.</summary>
        public bool Reboot;
        /// <summary>Precisa do DiagIT como administrador (grava em HKLM).</summary>
        public bool Admin = true;
        /// <summary>Ao aplicar, entra no acompanhamento "Resolveu?".</summary>
        public bool Track = true;
        public Func<bool> IsOn;
        public Action Apply, Undo;
    }

    public static class Tweaks
    {
        const string Gfx = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        const string Dwm = @"SOFTWARE\Microsoft\Windows\Dwm";
        const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

        static int HklmInt(string path, string name, int def)
        {
            try { using (RegistryKey k = Registry.LocalMachine.OpenSubKey(path)) { object v = k == null ? null : k.GetValue(name); return v == null ? def : Convert.ToInt32(v); } }
            catch { return def; }
        }
        static void HklmSet(string path, string name, int v) { using (RegistryKey k = Registry.LocalMachine.CreateSubKey(path)) k.SetValue(name, v, RegistryValueKind.DWord); }
        static void HklmDel(string path, string name) { using (RegistryKey k = Registry.LocalMachine.CreateSubKey(path)) k.DeleteValue(name, false); }
        static int HkcuInt(string path, string name, int def)
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(path)) { object v = k == null ? null : k.GetValue(name); return v == null ? def : Convert.ToInt32(v); } }
            catch { return def; }
        }
        static void HkcuSet(string path, string name, int v) { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(path)) k.SetValue(name, v, RegistryValueKind.DWord); }

        /// <summary>Roda um programa do Windows sem janela e devolve a saída (vazia se falhar).</summary>
        static string Run(string exe, string args, out int code)
        {
            code = -1;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(15000);
                    code = p.HasExited ? p.ExitCode : -1;
                    return o;
                }
            }
            catch { return ""; }
        }

        // ------------------------------------------------------------------ processador (estado máximo do plano de energia)
        /// <summary>"Estado máximo do processador" do plano de energia em uso (100 = sem limite), na tomada.</summary>
        public static int CpuMaxPercent()
        {
            int code;
            string o = Run("powercfg", "/query SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX", out code);
            MatchCollection m = Regex.Matches(o, @"0x[0-9a-fA-F]{8}");
            if (code != 0 || m.Count < 2) return 100;
            // os dois últimos valores são o índice atual na tomada (CA) e na bateria (CC)
            return Convert.ToInt32(m[m.Count - 2].Value.Substring(2), 16);
        }

        static void SetCpuMax(int pct)
        {
            int c;
            Run("powercfg", "/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX " + pct, out c);
            if (c != 0) throw new InvalidOperationException("o powercfg recusou a mudança");
            Run("powercfg", "/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX " + pct, out c);
            Run("powercfg", "/setactive SCHEME_CURRENT", out c);
        }

        // ------------------------------------------------------------------ plano de energia
        /// <summary>Plano de energia ativo: GUID e nome (ex.: "381b4222-...", "Equilibrado"), ou vazios se não der para ler.</summary>
        public static void ActivePlan(out string guid, out string name)
        {
            int code;
            string o = Run("powercfg", "/getactivescheme", out code);
            Match m = Regex.Match(o, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*(?:\(([^)]*)\))?");
            guid = m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";
            name = m.Success ? m.Groups[2].Value : "";
        }

        static string PlanFile { get { return Path.Combine(Paths.Root, "plano-anterior.txt"); } }

        static void SetHighPerformance()
        {
            string cur, nm; ActivePlan(out cur, out nm);
            if (cur == HighPerfGuid) return;
            int c;
            string list = Run("powercfg", "/list", out c);
            // em PCs com Modern Standby o plano Alto desempenho pode não existir; a Microsoft documenta recriá-lo a partir do GUID conhecido
            if (list.IndexOf(HighPerfGuid, StringComparison.OrdinalIgnoreCase) < 0)
            {
                Run("powercfg", "/duplicatescheme " + HighPerfGuid, out c);
                list = Run("powercfg", "/list", out c);
                if (list.IndexOf(HighPerfGuid, StringComparison.OrdinalIgnoreCase) < 0) throw new InvalidOperationException("este PC não oferece o plano Alto desempenho");
            }
            if (cur.Length > 0) { Paths.Ensure(); File.WriteAllText(PlanFile, cur); }   // guarda o plano anterior para o Desfazer
            Run("powercfg", "/setactive " + HighPerfGuid, out c);
            if (c != 0) throw new InvalidOperationException("o powercfg recusou a mudança");
        }

        static void RestorePlan()
        {
            string prev = BalancedGuid;
            try { if (File.Exists(PlanFile)) { string s = File.ReadAllText(PlanFile).Trim(); if (Regex.IsMatch(s, "^[0-9a-fA-F-]{36}$")) prev = s; } } catch { }
            int c;
            Run("powercfg", "/setactive " + prev, out c);
            if (c != 0) { Run("powercfg", "/setactive " + BalancedGuid, out c); if (c != 0) throw new InvalidOperationException("o powercfg recusou a mudança"); }
        }

        // ------------------------------------------------------------------ programas que iniciam com o Windows
        /// <summary>
        /// Programas que iniciam com o Windows e estão ativos (Run do usuário e da máquina e pastas Inicializar, menos os que o Gerenciador de Tarefas desativou).
        /// A marcação de "desativado" (StartupApproved, primeiro byte ímpar) é comportamento observado, não documentado pela Microsoft: a contagem é aproximada.
        /// </summary>
        public static List<string> StartupPrograms()
        {
            List<string> l = new List<string>();
            AddRun(l, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            AddRun(l, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            AddRun(l, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32");
            AddFolder(l, Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser);
            AddFolder(l, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine);
            return l;
        }

        static bool Approved(RegistryKey root, string approvedPath, string name)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(approvedPath))
                {
                    byte[] b = k == null ? null : k.GetValue(name) as byte[];
                    return b == null || b.Length == 0 || (b[0] & 1) == 0;   // sem marcação = ativo; primeiro byte ímpar (03) = desativado
                }
            }
            catch { return true; }
        }

        static void AddRun(List<string> l, RegistryKey root, string runPath, string approvedPath)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(runPath))
                {
                    if (k == null) return;
                    foreach (string n in k.GetValueNames()) if (n.Length > 0 && Approved(root, approvedPath, n)) l.Add(Regex.Replace(n, "_[0-9A-Fa-f]{16,}$", ""));   // o Edge acrescenta um código ao nome
                }
            }
            catch { }
        }

        static void AddFolder(List<string> l, string folder, RegistryKey root)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                foreach (string f in Directory.GetFiles(folder))
                {
                    string n = Path.GetFileName(f);
                    if (n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Approved(root, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", n)) l.Add(Path.GetFileNameWithoutExtension(n));
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ arquivos temporários
        const int TempAgeDays = 3;

        /// <summary>Arquivos da pasta temporária do usuário com mais de 3 dias (os que o Windows também considera lixo): quantidade e tamanho.</summary>
        public static void TempInfo(out int files, out long bytes) { WalkTemp(false, out files, out bytes); }

        /// <summary>Apaga esses arquivos (os que estiverem em uso são pulados). Não dá para desfazer. Devolve quantos e quanto foi apagado.</summary>
        public static void CleanTemp(out int files, out long bytes) { WalkTemp(true, out files, out bytes); }

        static void WalkTemp(bool delete, out int files, out long bytes)
        {
            files = 0; bytes = 0;
            string root = Path.GetTempPath().TrimEnd('\\');
            // trava de segurança: só a pasta Temp dentro do perfil do usuário
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(profile) || !root.StartsWith(profile, StringComparison.OrdinalIgnoreCase) || !root.EndsWith("Temp", StringComparison.OrdinalIgnoreCase)) return;
            DateTime limit = DateTime.Now.AddDays(-TempAgeDays);
            Stack<string> dirs = new Stack<string>(); dirs.Push(root);
            List<string> all = new List<string>();
            while (dirs.Count > 0)
            {
                string d = dirs.Pop(); all.Add(d);
                try
                {
                    foreach (string f in Directory.GetFiles(d))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(f);
                            if (fi.LastWriteTime > limit || fi.LastAccessTime > limit || (fi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            long len = fi.Length;
                            if (delete) fi.Delete();
                            files++; bytes += len;
                        }
                        catch { }
                    }
                    foreach (string s in Directory.GetDirectories(d))
                    {
                        try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) == 0) dirs.Push(s); } catch { }
                    }
                }
                catch { }
            }
            if (delete)   // pastas que ficaram vazias (de baixo para cima)
                for (int i = all.Count - 1; i > 0; i--)
                    try { if (Directory.GetFileSystemEntries(all[i]).Length == 0) Directory.Delete(all[i]); } catch { }
        }

        // ------------------------------------------------------------------ modo de jogo
        /// <summary>Modo de Jogo do Windows: 1 ligado, 0 desligado, -1 sem marcação (padrão do Windows, que é ligado nas versões atuais).</summary>
        public static int GameMode() { return HkcuInt(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", -1); }

        public static readonly List<Tweak> All = Build();

        static List<Tweak> Build()
        {
            List<Tweak> l = new List<Tweak>();

            // ---------------------------------------------------------------- desempenho
            Tweak plan = new Tweak();
            plan.Id = "energia"; plan.Group = "desempenho"; plan.Admin = false; plan.Track = false;
            plan.Name = "Plano de energia Alto desempenho";
            plan.Done = "Mudei o plano de energia para Alto desempenho";
            plan.What = "O plano Alto desempenho mantém o processador pronto para trabalhar, em vez de esperar a carga subir. Em PC de mesa o ganho costuma ser pequeno e o consumo de energia sobe. Não use em notebook na bateria. Se o seu PC usa o plano da AMD para Ryzen, ele já é ajustado para o processador: nesse caso não troque. O Desfazer volta ao plano que estava antes.";
            plan.IsOn = delegate { string g, n; ActivePlan(out g, out n); return g == HighPerfGuid; };
            plan.Apply = SetHighPerformance;
            plan.Undo = RestorePlan;
            l.Add(plan);

            Tweak dvr = new Tweak();
            dvr.Id = "gamedvr"; dvr.Reboot = false; dvr.Admin = false; dvr.Group = "desempenho";
            dvr.Name = "Gravação de jogo em segundo plano do Windows (Game DVR) desligada";
            dvr.Done = "Desliguei a gravação de jogo em segundo plano do Windows (Game DVR)";
            dvr.What = "O Windows pode gravar o jogo em segundo plano (Xbox Game Bar), usando a placa de vídeo e o disco. Desligar não perde nada que você use. Reversível.";
            dvr.IsOn = delegate { return HkcuInt(@"System\GameConfigStore", "GameDVR_Enabled", 1) == 0 && HkcuInt(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1) == 0; };
            dvr.Apply = delegate { HkcuSet(@"System\GameConfigStore", "GameDVR_Enabled", 0); HkcuSet(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0); };
            dvr.Undo = delegate { HkcuSet(@"System\GameConfigStore", "GameDVR_Enabled", 1); HkcuSet(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1); };
            l.Add(dvr);

            // ---------------------------------------------------------------- estabilidade (um de cada vez)
            Tweak mpo = new Tweak();
            mpo.Id = "mpo"; mpo.Reboot = true;
            mpo.Name = "Sobreposições de vídeo do Windows (MPO) desligadas";
            mpo.Done = "Desliguei as sobreposições de vídeo do Windows (MPO)";
            mpo.What = "O Windows desenha algumas telas em camadas separadas (MPO). Há muitos relatos de que isso causa tela preta, piscadas e reset do vídeo, e desligar é um contorno conhecido. Mas a chave usada (OverlayTestMode) não é documentada oficialmente pela Microsoft: trate como um teste, reversível com o botão Desfazer.";
            mpo.IsOn = delegate { return HklmInt(Dwm, "OverlayTestMode", 0) == 5; };
            mpo.Apply = delegate { HklmSet(Dwm, "OverlayTestMode", 5); };
            mpo.Undo = delegate { HklmDel(Dwm, "OverlayTestMode"); };
            l.Add(mpo);

            Tweak hags = new Tweak();
            hags.Id = "hags"; hags.Reboot = true;
            hags.Name = "Agendamento de GPU acelerado por hardware (HAGS) desligado";
            hags.Done = "Desliguei o agendamento de GPU por hardware (HAGS)";
            hags.What = "O Windows deixa a própria placa organizar o trabalho dela (HAGS). Em alguns conjuntos de placa e driver isso causa travamentos; desligar devolve essa tarefa ao Windows. O caminho oficial é Configurações > Sistema > Tela > Gráficos > Alterar configurações gráficas padrão; este botão grava o mesmo ajuste direto no registro (valor que os guias usam, mas que a Microsoft não documenta). Reversível.";
            hags.IsOn = delegate { return HklmInt(Gfx, "HwSchMode", 0) == 1; };
            hags.Apply = delegate { HklmSet(Gfx, "HwSchMode", 1); };
            hags.Undo = delegate { HklmDel(Gfx, "HwSchMode"); };
            l.Add(hags);

            Tweak tdr = new Tweak();
            tdr.Id = "tdr"; tdr.Reboot = true;
            tdr.Name = "Mais tempo para o vídeo responder antes do Windows reiniciá-lo (TDR 10 s)";
            tdr.Done = "Dei mais tempo para o vídeo responder antes do Windows reiniciá-lo (TDR)";
            tdr.What = "Se a placa demora para responder, o Windows a reinicia depois de 2 segundos (padrão documentado pela Microsoft). Com 10 segundos, cargas pesadas e pontuais passam sem reset. Atenção: a Microsoft documenta essa configuração só para desenvolvedores de driver e não a recomenda como correção para usuários. Use apenas como teste curto e desfaça se não ajudar; ela pode esconder um defeito real em vez de resolvê-lo.";
            tdr.IsOn = delegate { return HklmInt(Gfx, "TdrDelay", 2) >= 8; };
            tdr.Apply = delegate { HklmSet(Gfx, "TdrDelay", 10); };
            tdr.Undo = delegate { HklmDel(Gfx, "TdrDelay"); };
            l.Add(tdr);

            Tweak boost = new Tweak();
            boost.Id = "cpu-boost"; boost.Reboot = false; boost.Scope = "geral"; boost.Admin = false;
            boost.Name = "Processador sem o turbo (estado máximo 99%)";
            boost.Done = "Limitei o processador a 99% para desligar o turbo";
            boost.What = "Com o máximo do processador em 99%, o Windows desliga o turbo (a frequência acima da nominal). O PC esquenta menos e fica mais estável; perde um pouco de desempenho. Serve para saber se o problema é calor ou o turbo instável.";
            boost.IsOn = delegate { return CpuMaxPercent() <= 99; };
            boost.Apply = delegate { SetCpuMax(99); };
            boost.Undo = delegate { SetCpuMax(100); };
            l.Add(boost);

            return l;
        }

        public static Tweak ById(string id)
        {
            foreach (Tweak t in All) if (t.Id == id) return t;
            return null;
        }

        public static bool Set(string id, bool on, out string msg) { Tweak t = ById(id); return Set(id, on, t != null && t.Track, out msg); }

        /// <summary>
        /// Liga (on) ou desfaz o ajuste. Com track, ligar entra no acompanhamento "Resolveu?" e desfazer sai dele; os ajustes de desempenho não entram.
        /// </summary>
        public static bool Set(string id, bool on, bool track, out string msg)
        {
            Tweak t = ById(id);
            if (t == null) { msg = "Ajuste desconhecido: " + id; return false; }
            try
            {
                if (on) t.Apply(); else t.Undo();
                if (track) { if (on) FixLog.Add("exp:" + t.Id, t.Done, t.Scope); else FixLog.RemoveKind("exp:" + t.Id); }
                msg = (on ? "Pronto: " + t.Name + "." : "Desfeito: " + t.Name + ". Voltou ao padrão do Windows.") +
                      (t.Reboot ? " Reinicie o PC para o Windows aplicar." : "") +
                      (on ? " Para desfazer, clique em Desfazer neste mesmo ajuste." : "");
                Log.Write((on ? "Ajuste aplicado: " : "Ajuste desfeito: ") + t.Name);
                return true;
            }
            catch (Exception e)
            {
                msg = "Não foi possível alterar: " + e.Message + (t.Admin && !SysUtil.IsAdmin() ? " (é preciso executar o DiagIT como administrador)" : "");
                return false;
            }
        }
    }
}
