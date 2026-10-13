using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DiagIT
{
    /// <summary>
    /// Verificações vindas do guia da comunidade (r/AMDHelp, "Ultimate AMD Performance Fix Guide"), mantidas só onde dá
    /// para medir: cada uma vira achado apenas quando cruza com problemas registrados (travamentos, partidas que caíram).
    /// </summary>
    public static partial class Analyzer
    {
        static void GamingRules(Diagnosis d, List<SessionData> sessions, List<WinEvent> ev)
        {
            List<DateTime> gpuCrashes = GpuCrashTimes(ev);
            WuDriverRule(d, ev, gpuCrashes);
            GpuClockRule(d, sessions, gpuCrashes);
            BackgroundAppRules(d, sessions, ev, gpuCrashes);
            PagefileRule(d);
        }

        // ================================================================ travamentos de vídeo (base das correlações)
        static readonly int[] VideoBugchecks = { 0x116, 0x117, 0x119, 0x141, 0x10E };

        /// <summary>Código da tela azul de um evento (16 bits baixos), ou -1.</summary>
        static int BugcodeOf(WinEvent e)
        {
            if (e.Provider == "Microsoft-Windows-WER-SystemErrorReporting" && e.Id == 1001)
            {
                Match m = Regex.Match(e.Prop(0) + " " + e.Msg, @"0x([0-9a-fA-F]{1,8})");
                if (m.Success) return (int)(Convert.ToInt64(m.Groups[1].Value, 16) & 0xFFFF);
            }
            else if (e.Provider == "Microsoft-Windows-Kernel-Power" && e.Id == 41)
            {
                long c = ParseCode(e.Prop(0));
                if (c > 0) return (int)(c & 0xFFFF);
            }
            return -1;
        }

        static bool GpuModule(string m) { return Has(m, "atiu9p", "atidxx", "amdxx", "aticfx", "amdvlk", "nvwgf2", "nvd3d", "igd"); }

        /// <summary>Momentos em que a GPU/driver de vídeo falhou (TDR, LiveKernelEvent de vídeo, tela azul de vídeo, jogo caindo no driver), mais recentes primeiro.</summary>
        static List<DateTime> GpuCrashTimes(List<WinEvent> ev)
        {
            List<DateTime[]> installs = InstallWindows(ev);
            List<DateTime> r = new List<DateTime>();
            foreach (WinEvent e in ev)
            {
                bool hit = ((e.Provider == "Display" && e.Id == 4101) || Has(e.Provider, "amdkmdag", "amdwddmg", "atikmdag", "nvlddmkm", "igfx")) && !DuringInstall(installs, e.Time);
                if (!hit && e.Provider == "Windows Error Reporting" && e.Id == 1001 && e.Prop(2) == "LiveKernelEvent")
                {
                    string c = e.Prop(5).ToLowerInvariant();
                    hit = c == "141" || c == "117" || c == "193" || c == "10e";
                }
                if (!hit && Array.IndexOf(VideoBugchecks, BugcodeOf(e)) >= 0) hit = true;
                if (!hit && e.Provider == "Application Error" && e.Id == 1000 && GpuModule(e.Prop(3))) hit = true;
                // eventos do mesmo episódio (TDR + LiveKernelEvent + jogo fechando) contam uma vez
                if (hit && (r.Count == 0 || Math.Abs((r[r.Count - 1] - e.Time).TotalMinutes) > 5)) r.Add(e.Time);
            }
            return r;
        }

        static int CountBetween(List<DateTime> l, DateTime a, DateTime b)
        {
            int n = 0;
            foreach (DateTime t in l) if (t >= a && t < b) n++;
            return n;
        }

        // ================================================================ 1. Windows Update trocou o driver de vídeo
        static void WuDriverRule(Diagnosis d, List<WinEvent> ev, List<DateTime> crashes)
        {
            WinEvent wu = null; string vendor = "", ver = "";
            foreach (WinEvent e in ev)   // mais recente primeiro
            {
                if (e.Provider != "Microsoft-Windows-WindowsUpdateClient" || e.Id != 19) continue;
                Match m = Regex.Match(e.Prop(0) + "\n" + e.Msg, @"([^\n:]+?)\s+-\s+Display\s+-\s+([0-9]+(?:\.[0-9]+)+)");
                if (!m.Success) continue;
                wu = e; vendor = m.Groups[1].Value.Trim(); ver = m.Groups[2].Value;
                break;
            }
            if (wu == null) return;
            // só interessa se o driver instalado pelo Windows Update é o que está em uso
            bool inUse = d.Hw != null && d.Hw.GpuDriver == ver;
            if (!inUse) return;

            DateTime t = wu.Time, settled = t.AddMinutes(30);   // a própria instalação reinicia o vídeo
            TimeSpan span = DateTime.Now - settled;
            int after = CountBetween(crashes, settled, DateTime.Now.AddMinutes(1));
            int before = CountBetween(crashes, t - span, t);
            bool blocked = d.Hw.WuDriversBlocked;

            Finding f = new Finding();
            f.Source = "Eventos do Windows";
            f.Title = "O Windows Update instalou o driver de vídeo em uso (" + vendor + " " + ver + ", " + D(t) + ")";
            StringBuilder evd = new StringBuilder();
            evd.Append("O driver ativo veio do Windows Update, não do instalador do fabricante (o painel do Adrenalin pode reclamar de versão diferente ou perder recursos). ");
            evd.Append("Travamentos de vídeo: " + after + " depois da troca, " + before + " no mesmo período antes dela.");
            if (blocked) evd.Append(" A política que impede o Windows Update de trocar drivers já está ativa.");
            f.Evidence = evd.ToString();
            f.Advice = "Instalação limpa: baixe o driver no site do fabricante (sem o detector automático), desconecte a internet, remova o atual com DDU em modo de segurança e instale o baixado. " +
                (blocked ? "" : "Depois, em Resolver, impeça o Windows Update de trocar drivers para não voltar a acontecer.");
            if (after == 0)
            {
                f.Sev = Sev.Info;
                f.Advice = "Sem travamentos de vídeo desde então, não precisa mexer. Se começarem, " + char.ToLowerInvariant(f.Advice[0]) + f.Advice.Substring(1);
            }
            else
            {
                bool pattern = after >= 2 && after > 2 * before;
                f.Sev = Sev.Warn;
                if (pattern) f.Title = "Os travamentos de vídeo começaram depois que o Windows Update trocou o driver (" + D(t) + ")";
                f.Last = crashes[0];
                f.Weigh(Comp.Software, pattern ? 0.40 : 0.20).Weigh(Comp.Gpu, 0.05);
            }
            d.Findings.Add(f);
        }

        // ================================================================ 2. GPU acima do clock oficial
        // boost oficial (MHz) dos modelos de referência AMD; placas com overclock de fábrica passam disso por projeto
        static readonly string[][] AmdBoost =
        {
            new string[] { "RX 6400", "2321" }, new string[] { "RX 6500 XT", "2815" }, new string[] { "RX 6600", "2491" }, new string[] { "RX 6600 XT", "2589" },
            new string[] { "RX 6650 XT", "2635" }, new string[] { "RX 6700", "2450" }, new string[] { "RX 6700 XT", "2581" }, new string[] { "RX 6750 XT", "2600" },
            new string[] { "RX 6800", "2105" }, new string[] { "RX 6800 XT", "2250" }, new string[] { "RX 6900 XT", "2250" }, new string[] { "RX 6950 XT", "2310" },
            new string[] { "RX 7600", "2655" }, new string[] { "RX 7600 XT", "2755" }, new string[] { "RX 7700 XT", "2544" }, new string[] { "RX 7800 XT", "2430" },
            new string[] { "RX 7900 GRE", "2245" }, new string[] { "RX 7900 XT", "2400" }, new string[] { "RX 7900 XTX", "2500" },
            new string[] { "RX 9060 XT", "3130" }, new string[] { "RX 9070", "2520" }, new string[] { "RX 9070 GRE", "2790" }, new string[] { "RX 9070 XT", "2970" },
        };

        /// <summary>Boost oficial pelo nome da placa (a chave mais longa contida no nome vence: "RX 7600 XT" antes de "RX 7600").</summary>
        public static int OfficialBoost(string gpuName, out string model)
        {
            model = ""; int boost = 0;
            string n = " " + Regex.Replace((gpuName ?? "").ToUpperInvariant(), @"\s+", " ") + " ";
            foreach (string[] k in AmdBoost)
                if (n.Contains(" " + k[0] + " ") && k[0].Length > model.Length) { model = k[0]; boost = int.Parse(k[1]); }
            return boost;
        }

        static void GpuClockRule(Diagnosis d, List<SessionData> sessions, List<DateTime> crashes)
        {
            if (d.Hw == null || d.Hw.GpuName.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) < 0) return;
            string model;
            int boost = OfficialBoost(d.Hw.GpuName, out model);
            if (boost <= 0) return;

            // clock sustentado (p95) com a GPU realmente em carga: partidas e testes de GPU
            List<double> clk = new List<double>();
            foreach (SessionData sd in sessions)
                foreach (Sample s in sd.S)
                    if (s.V[F.Gpu] >= 80 && !double.IsNaN(s.V[F.GpuClock]) && s.V[F.GpuClock] > 100) clk.Add(s.V[F.GpuClock]);
            double measured = clk.Count >= 30 ? Stat.Percentile(clk.ToArray(), 0.95) : double.NaN;
            string where = "nas partidas gravadas";
            bool deviceRemoved = false;
            foreach (TestResult t in d.Tests)
            {
                if (t.Kind != TestKind.Gpu && t.Kind != TestKind.Combined) continue;
                if (t.Get("device_removed") == 1) deviceRemoved = true;
                double tc = t.Get("t_gpu_clock_p95");
                if (!double.IsNaN(tc) && (double.IsNaN(measured) || tc > measured)) { measured = tc; where = "no teste de " + TestResult.KindLabel(t.Kind) + " (" + Dt(t) + ")"; }
            }
            if (double.IsNaN(measured) || measured <= boost + 30) return;

            bool rdna4 = model.StartsWith("RX 9", StringComparison.Ordinal);
            bool problems = crashes.Count > 0 || deviceRemoved;
            Finding f = new Finding();
            f.Source = "Medição da placa de vídeo";
            f.Title = "A GPU trabalha acima do clock oficial: " + N0(measured) + " MHz medidos, oficial da " + model + " é " + boost + " MHz" +
                (problems ? " — e há travamentos de vídeo" : "");
            f.Evidence = "Clock sustentado (95% do tempo abaixo disso) com a GPU acima de 80% de uso, " + where + ". " +
                "O ajuste automático do driver (e o overclock de fábrica) leva placas Radeon além do boost anunciado; em algumas unidades isso não é estável." +
                (problems ? " Travamentos de vídeo registrados: " + crashes.Count + (deviceRemoved ? ", incluindo travamento da GPU no teste do DiagIT" : "") + "." : "");
            f.Advice = "No Adrenalin: Desempenho → Ajuste → Ajuste manual → Ajuste da GPU + Controle avançado, " +
                (rdna4 ? "aplique um deslocamento negativo na frequência máxima até o clock em jogo ficar em ~" + boost + " MHz"
                       : "coloque a frequência máxima em " + boost + " MHz") +
                ". Desligue o HYPR-RX (inclusive nos perfis de cada jogo, que sobrepõem o ajuste global). " +
                (problems ? "Use assim por alguns dias: se os travamentos pararem, a causa era o clock; se continuarem, volte ao padrão."
                          : "Sem travamentos, não é preciso mexer: isso é o comportamento normal do driver.");
            if (problems)
            {
                f.Sev = Sev.Warn;
                f.Last = crashes.Count > 0 ? crashes[0] : DateTime.Now;
                f.Weigh(Comp.Software, 0.20).Weigh(Comp.Gpu, 0.15);
            }
            else f.Sev = Sev.Info;
            d.Findings.Add(f);
        }

        // ================================================================ 3. Programas em segundo plano e placa de rede
        static bool GameSession(SessionData sd) { return sd.Info.Note != "teste" && !sd.Info.Game.StartsWith("[Teste]"); }

        static int Stalls(SessionData sd)
        {
            int n = 0;
            foreach (Sample s in sd.S) { double g = s.V[F.Gap]; if (!double.IsNaN(g) && g > 6 && g < 120) n++; }
            return n;
        }

        /// <summary>Partida com problema concreto: PC caiu, jogo fechou com erro ou o sistema congelou.</summary>
        static bool BadSession(SessionData sd)
        {
            return sd.Info.Status == "interrupted" || sd.Info.Note.StartsWith("O jogo fechou com erro", StringComparison.Ordinal) || Stalls(sd) > 0;
        }

        static void BackgroundAppRules(Diagnosis d, List<SessionData> sessions, List<WinEvent> ev, List<DateTime> crashes)
        {
            List<SessionData> games = sessions.FindAll(GameSession);
            List<SessionData> tracked = games.FindAll(delegate(SessionData s) { return s.Info.Bg.Count > 0; });
            int bad = games.FindAll(BadSession).Count;

            if (tracked.Count > 0)
            {
                // compara as partidas com e sem cada programa aberto
                List<string> labels = new List<string>();
                foreach (SessionData s in games) foreach (string l in s.Info.Bg) if (!labels.Contains(l)) labels.Add(l);
                foreach (string label in labels)
                {
                    List<SessionData> with = games.FindAll(delegate(SessionData s) { return s.Info.Bg.Contains(label); });
                    List<SessionData> without = games.FindAll(delegate(SessionData s) { return !s.Info.Bg.Contains(label); });
                    List<SessionData> withBad = with.FindAll(BadSession);
                    int withoutBad = without.FindAll(BadSession).Count;
                    if (withBad.Count == 0) continue;
                    double rw = withBad.Count / (double)with.Count, rwo = without.Count > 0 ? withoutBad / (double)without.Count : double.NaN;
                    bool contrast = with.Count >= 2 && without.Count >= 2 && rw >= 0.5 && rw >= 2 * rwo;
                    bool always = without.Count == 0;
                    if (!contrast && !always) continue;   // aparece igual com e sem: não é ele

                    Finding f = new Finding();
                    f.Source = "Partidas gravadas";
                    f.Sev = contrast ? Sev.Warn : Sev.Info;
                    f.Title = contrast
                        ? "Partidas com " + label + " aberto tiveram mais problemas (" + withBad.Count + " de " + with.Count + ", contra " + withoutBad + " de " + without.Count + " sem ele)"
                        : label + " estava aberto em todas as partidas gravadas (" + withBad.Count + " de " + with.Count + " com problema)";
                    f.Evidence = "Problema = o PC caiu, o jogo fechou com erro ou o sistema congelou. A comunidade relata " +
                        (label.StartsWith("Wallpaper", StringComparison.Ordinal) ? "engasgos e quedas de FPS com o Wallpaper Engine rodando, mesmo pausado."
                         : label.StartsWith("Gravação do Adrenalin", StringComparison.Ordinal) ? "que o Instant Replay do Adrenalin causa engasgos, quedas de FPS e travamentos do driver."
                         : "que softwares de RGB causam engasgos (1% low), quedas de FPS e travamentos, mesmo em PCs topo de linha.") +
                        (contrast ? "" : " Sem partidas sem ele não dá para comparar.");
                    f.Advice = label.StartsWith("Wallpaper", StringComparison.Ordinal) ? "Feche o Wallpaper Engine pela bandeja (Sair) antes de jogar e confira no Gerenciador de Tarefas que ele não ficou aberto."
                        : label.StartsWith("Gravação do Adrenalin", StringComparison.Ordinal) ? "No Adrenalin, aba Gravar e transmitir: desligue Instant Replay e as demais gravações; em Configurações → Sistema, desligue também a Detecção de problemas."
                        : "Desinstale (fechar nem sempre basta, o serviço continua rodando) e jogue alguns dias sem ele para comparar.";
                    f.Last = withBad[0].Info.Start;
                    foreach (SessionData s in withBad) if (s.Info.Start > f.Last) f.Last = s.Info.Start;
                    if (contrast) f.Weigh(Comp.Software, 0.30); else f.Weigh(Comp.Software, 0.08);
                    d.Findings.Add(f);
                }
            }
            else if (d.Hw != null && d.Hw.Background.Count > 0 && (bad > 0 || crashes.Count > 0))
            {
                Finding f = new Finding();
                f.Sev = Sev.Info; f.Source = "Programas abertos";
                f.Title = "Abertos agora, citados pela comunidade como causa de engasgos: " + string.Join(", ", d.Hw.Background.ToArray());
                f.Evidence = "As partidas gravadas até aqui não registravam os programas abertos; as próximas vão registrar, e os Resultados comparam as partidas com e sem cada um.";
                f.Advice = "Para testar já: feche-os (ou desinstale os de RGB) e jogue algumas partidas.";
                d.Findings.Add(f);
            }

            // placa de rede Realtek 2.5GbE: só pesa se há congelamentos ou watchdog do kernel em uso normal
            if (d.Hw != null && d.Hw.RealtekNic != null)
            {
                int stallSessions = games.FindAll(delegate(SessionData s) { return Stalls(s) > 0; }).Count;
                List<WinEvent> wd = Where(ev, delegate(WinEvent e)
                {
                    if (e.Provider != "Windows Error Reporting" || e.Id != 1001 || e.Prop(2) != "LiveKernelEvent") return false;
                    string c = e.Prop(5).ToLowerInvariant();
                    return c == "1a8" || c == "1b8";
                });
                if (stallSessions > 0 || wd.Count > 0)
                {
                    Finding f = new Finding();
                    f.Sev = Sev.Info; f.Source = "Dispositivos";
                    f.Title = "Placa de rede " + d.Hw.RealtekNic + " presente, com congelamentos registrados";
                    f.Evidence = (stallSessions > 0 ? stallSessions + " partida(s) com o sistema congelado. " : "") +
                        (wd.Count > 0 ? "Watchdog do kernel (LiveKernelEvent 0x1a8/0x1b8) " + wd.Count + "×, último em " + D(wd[0].Time) + ". " : "") +
                        "A comunidade relata engasgos, estalos no áudio e picos de ping com essa placa, mesmo usando só Wi-Fi" +
                        (d.Hw.RealtekDriver.Length > 0 ? " (driver atual " + d.Hw.RealtekDriver + ")." : ".");
                    f.Advice = "Teste: desative a placa no Gerenciador de Dispositivos (Adaptadores de rede) e jogue pelo Wi-Fi. Se os congelamentos sumirem, " +
                        "deixe desativada ou instale o driver 10.54.1111.2021 indicado pela comunidade e impeça o Windows de trocá-lo (Resolver).";
                    f.Last = wd.Count > 0 ? wd[0].Time : DateTime.Now;
                    f.Weigh(Comp.Software, 0.12);
                    d.Findings.Add(f);
                }
            }
        }

        // ================================================================ 4. Arquivo de paginação no HD
        static void PagefileRule(Diagnosis d)
        {
            HwSnapshot h = d.Hw;
            if (h == null || h.PagefileFiles.Count == 0) return;
            string sysL = System.IO.Path.GetPathRoot(Environment.SystemDirectory).Substring(0, 1);
            VolumeInfo sys = h.Volumes.Find(delegate(VolumeInfo v) { return v.Letter.Equals(sysL, StringComparison.OrdinalIgnoreCase); });
            if (sys == null || sys.Media != "SSD") return;
            List<string> onHdd = new List<string>();
            foreach (string pf in h.PagefileFiles)
            {
                VolumeInfo v = pf.Length == 0 ? null : h.Volumes.Find(delegate(VolumeInfo x) { return x.Letter.Equals(pf.Substring(0, 1), StringComparison.OrdinalIgnoreCase); });
                if (v != null && v.Media == "HDD") onHdd.Add(pf);
            }
            if (onHdd.Count == 0) return;
            Finding f = new Finding();
            f.Sev = Sev.Warn; f.Source = "Configuração do Windows";
            f.Title = "Memória virtual também no HD (" + string.Join(", ", onHdd.ToArray()) + ") com o Windows em SSD";
            f.Evidence = "Quando o jogo precisa trocar memória com o disco, a parte que cai no HD é dezenas de vezes mais lenta e trava o jogo por instantes, mesmo com RAM livre.";
            f.Advice = "Resolver → Memória virtual → \"Tirar do HD\": remove o arquivo do HD e mantém no SSD com tamanho gerenciado pelo Windows (requer reiniciar).";
            f.Weigh(Comp.Software, 0.08);
            d.Findings.Add(f);
        }
    }
}
