using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DiagIT
{
    /// <summary>
    /// Um ajuste de Windows que ajuda a descobrir a causa de travamentos: liga e desliga com um clique, sempre reversível.
    /// São experimentos (um de cada vez), não "otimizações": o DiagIT acompanha se o problema parou depois de cada um.
    /// </summary>
    public class Tweak
    {
        public string Id = "", Name = "", Done = "", Scope = "video", What = "";
        /// <summary>O Windows só aplica depois de reiniciar.</summary>
        public bool Reboot;
        public Func<bool> IsOn;
        public Action Apply, Undo;
    }

    public static class Tweaks
    {
        const string Gfx = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        const string Dwm = @"SOFTWARE\Microsoft\Windows\Dwm";

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

        public static readonly List<Tweak> All = Build();

        static List<Tweak> Build()
        {
            List<Tweak> l = new List<Tweak>();

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

            Tweak dvr = new Tweak();
            dvr.Id = "gamedvr"; dvr.Reboot = false;
            dvr.Name = "Gravação de jogo em segundo plano do Windows (Game DVR) desligada";
            dvr.Done = "Desliguei a gravação de jogo em segundo plano do Windows (Game DVR)";
            dvr.What = "O Windows pode gravar o jogo em segundo plano (Xbox Game Bar). Isso usa a placa de vídeo e já foi ligado a quedas de jogo; desligar não perde nada que você use.";
            dvr.IsOn = delegate { return HkcuInt(@"System\GameConfigStore", "GameDVR_Enabled", 1) == 0 && HkcuInt(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1) == 0; };
            dvr.Apply = delegate { HkcuSet(@"System\GameConfigStore", "GameDVR_Enabled", 0); HkcuSet(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0); };
            dvr.Undo = delegate { HkcuSet(@"System\GameConfigStore", "GameDVR_Enabled", 1); HkcuSet(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1); };
            l.Add(dvr);

            Tweak boost = new Tweak();
            boost.Id = "cpu-boost"; boost.Reboot = false; boost.Scope = "geral";
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

        /// <summary>
        /// Liga (on) ou desfaz o ajuste. Ligar entra no acompanhamento (o DiagIT mostra se o problema parou); desfazer sai dele.
        /// </summary>
        public static bool Set(string id, bool on, out string msg)
        {
            Tweak t = ById(id);
            if (t == null) { msg = "Ajuste desconhecido: " + id; return false; }
            try
            {
                if (on) t.Apply(); else t.Undo();
                if (on) FixLog.Add("exp:" + t.Id, t.Done, t.Scope);
                else FixLog.RemoveKind("exp:" + t.Id);
                msg = (on ? "Pronto: " + t.Name + "." : "Desfeito: " + t.Name + ". Voltou ao padrão do Windows.") +
                      (t.Reboot ? " Reinicie o PC para o Windows aplicar." : "") +
                      (on ? " Para desfazer, volte a este passo e clique em Desfazer." : "");
                Log.Write((on ? "Ajuste aplicado: " : "Ajuste desfeito: ") + t.Name);
                return true;
            }
            catch (Exception e)
            {
                msg = "Não foi possível alterar: " + e.Message + (SysUtil.IsAdmin() ? "" : " (é preciso executar o DiagIT como administrador)");
                return false;
            }
        }
    }
}
