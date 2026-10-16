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
            List<DateTime> gpuCrashes = GpuCrashTimes(ev, true);   // só com sintoma: reset de vídeo isolado não conta como "travamento" nas regras de driver e de clock
            WuDriverRule(d, ev, gpuCrashes);
            GpuClockRule(d, sessions, gpuCrashes);
            BackgroundAppRules(d, sessions, ev, gpuCrashes);
            PerformanceRules(d, sessions);
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

        /// <summary>Algo visível que aconteceu até 90 s de t: programa fechou com erro, TDR, tela azul ou desligamento sem aviso. null = nada.</summary>
        static WinEvent SymptomNear(List<WinEvent> ev, DateTime t)
        {
            WinEvent weak = null;
            foreach (WinEvent e in ev)
            {
                if (Math.Abs((e.Time - t).TotalSeconds) > 90) continue;
                if (e.Provider == "Application Error" && e.Id == 1000 && !e.Prop(0).StartsWith("DiagIT", StringComparison.OrdinalIgnoreCase))
                {
                    if (GpuModule(e.Prop(3))) return e;   // caiu dentro do driver de vídeo: sintoma forte
                    if (weak == null) weak = e;           // caiu no módulo do próprio programa: pode ser só um bug dele
                    continue;
                }
                if (e.Provider == "Display" && e.Id == 4101) return e;
                if (BugcodeOf(e) > 0) return e;
                if (e.Provider == "Microsoft-Windows-Kernel-Power" && e.Id == 41) return e;
            }
            return weak;
        }

        /// <summary>
        /// Sintoma fraco: um programa fechou com erro no módulo dele mesmo (não no driver de vídeo). Isso sozinho pode ser só um defeito do programa;
        /// só passa a pesar contra o driver quando se repete.
        /// </summary>
        static bool IsWeakSymptom(WinEvent e)
        {
            return e != null && e.Provider == "Application Error" && !GpuModule(e.Prop(3));
        }

        /// <summary>Quantos resets de vídeo (LiveKernelEvent) vieram só com um sintoma fraco ao lado, fora de instalações.</summary>
        static int WeakResetCount(List<WinEvent> ev, List<DateTime[]> installs)
        {
            int n = 0;
            foreach (WinEvent e in ev)
            {
                if (e.Provider != "Windows Error Reporting" || e.Id != 1001 || e.Prop(2) != "LiveKernelEvent") continue;
                string c = e.Prop(5).ToLowerInvariant();
                if (!(c == "141" || c == "117" || c == "193" || c == "10e") || DuringInstall(installs, e.Time)) continue;
                if (IsWeakSymptom(SymptomNear(ev, e.Time))) n++;
            }
            return n;
        }

        /// <summary>Em palavras: "RainbowSixHelper.exe fechou com erro (o módulo que falhou é do próprio programa, não do driver de vídeo)".</summary>
        public static string SymptomText(WinEvent e)
        {
            if (e == null) return "";
            if (e.Provider == "Application Error")
                return e.Prop(0) + " fechou com erro" + (e.Prop(3).Length > 0 ? (GpuModule(e.Prop(3)) ? " dentro do driver de vídeo (" + e.Prop(3) + ")" : " (o módulo que falhou é " + e.Prop(3) + ", do próprio programa, não do driver de vídeo)") : "");
            if (e.Provider == "Display") return "o driver de vídeo parou de responder (TDR)";
            if (e.Provider == "Microsoft-Windows-Kernel-Power") return "o PC desligou ou reiniciou sem aviso";
            return "tela azul";
        }

        static bool GpuModule(string m) { return Has(m, "atiu9p", "atidxx", "amdxx", "aticfx", "amdvlk", "nvwgf2", "nvd3d", "igd"); }

        /// <summary>Momentos em que a GPU/driver de vídeo falhou (TDR, LiveKernelEvent de vídeo, tela azul de vídeo, jogo caindo no driver), mais recentes primeiro.</summary>
        static List<DateTime> GpuCrashTimes(List<WinEvent> ev) { return GpuCrashTimes(ev, false); }

        /// <summary>
        /// O LiveKernelEvent de vídeo (141/117/193) só registra que o Windows reiniciou o vídeo; sozinho, ele costuma aparecer ao abrir um jogo em tela cheia
        /// ou trocar a resolução, sem nada visível. "Com sintoma" = na mesma hora (90 s) um programa fechou com erro, o vídeo travou (TDR), houve tela azul
        /// ou o PC desligou. Com symptomOnly, só esses entram.
        /// </summary>
        static List<DateTime> GpuCrashTimes(List<WinEvent> ev, bool symptomOnly)
        {
            List<DateTime[]> installs = InstallWindows(ev);
            List<DateTime> r = new List<DateTime>();
            bool weakCounts = symptomOnly && WeakResetCount(ev, installs) >= 2;   // um programa que fecha uma vez junto com um reset pode ser só o programa; repetido, conta
            foreach (WinEvent e in ev)
            {
                bool hit = ((e.Provider == "Display" && e.Id == 4101) || Has(e.Provider, "amdkmdag", "amdwddmg", "atikmdag", "nvlddmkm", "igfx")) && !DuringInstall(installs, e.Time);
                if (!hit && e.Provider == "Windows Error Reporting" && e.Id == 1001 && e.Prop(2) == "LiveKernelEvent")
                {
                    string c = e.Prop(5).ToLowerInvariant();
                    hit = (c == "141" || c == "117" || c == "193" || c == "10e") && !DuringInstall(installs, e.Time);
                    if (hit && symptomOnly) { WinEvent near = SymptomNear(ev, e.Time); hit = near != null && (!IsWeakSymptom(near) || weakCounts); }
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
            evd.Append("O driver ativo veio do Windows Update, não do instalador do fabricante (o {painel_video} pode reclamar de versão diferente ou perder recursos). ");
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
        /// <summary>Boost oficial pelo nome da placa (a chave mais longa contida no nome vence: "RX 7600 XT" antes de "RX 7600").</summary>
        public static int OfficialBoost(string gpuName, out string model)
        {
            Rules.Ensure();
            model = ""; int boost = 0;
            string n = " " + Regex.Replace((gpuName ?? "").ToUpperInvariant(), @"\s+", " ") + " ";
            foreach (GpuBoost k in Rules.Boosts)
                if (n.Contains(" " + k.Model + " ") && k.Model.Length > model.Length) { model = k.Model; boost = k.Boost; }
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

            GpuBoost rule = Rules.Boosts.Find(delegate(GpuBoost b) { return b.Model == model; });
            bool offset = rule != null && rule.Offset;
            bool problems = crashes.Count > 0 || deviceRemoved;
            Finding f = new Finding();
            f.Source = "Medição da placa de vídeo";
            f.Title = "A GPU trabalha acima do clock oficial: " + N0(measured) + " MHz medidos, oficial da " + model + " é " + boost + " MHz" +
                (problems ? " — e há travamentos de vídeo" : "");
            f.Evidence = "Clock sustentado (95% do tempo abaixo disso) com a GPU acima de 80% de uso, " + where + ". " +
                "O ajuste automático do driver (e o overclock de fábrica) leva placas Radeon além do boost anunciado; em algumas unidades isso não é estável." +
                (problems ? " Travamentos de vídeo registrados: " + crashes.Count + (deviceRemoved ? ", incluindo travamento da GPU no teste do DiagIT" : "") + "." : "");
            f.Advice = (offset ? Rules.BoostAdjustOffset : Rules.BoostAdjust).Replace("{boost}", boost.ToString()) +
                " Desligue o HYPR-RX (inclusive nos perfis de cada jogo, que sobrepõem o ajuste global). " +
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
        static bool GameSession(SessionData sd) { return sd.Info.Note != "teste" && !sd.Info.Game.StartsWith("[Teste]") && !AppSettings.IsNeverGame(sd.Info.Game); }

        static int Stalls(SessionData sd)
        {
            int n = 0;
            foreach (Sample s in sd.S) { double g = s.V[F.Gap]; if (!double.IsNaN(g) && g > 6 && g < 120) n++; }
            return n;
        }

        /// <summary>Engasgos por hora medidos pelo PresentMon (NaN = partida sem medição de FPS).</summary>
        public static double StuttersPerHour(SessionInfo si)
        {
            if (si.Frames < 600 || double.IsNaN(si.FpsAvg) || si.FpsAvg <= 0) return double.NaN;
            double hours = si.Frames / si.FpsAvg / 3600.0;
            return hours < 0.05 ? double.NaN : si.Stutters / hours;
        }

        /// <summary>Partida com problema concreto: PC caiu, jogo fechou com erro, o sistema congelou ou o jogo engasgou muito (medido).</summary>
        static bool BadSession(SessionData sd)
        {
            double sph = StuttersPerHour(sd.Info);
            return sd.Info.Status == "interrupted" || sd.Info.Note.StartsWith("O jogo fechou com erro", StringComparison.Ordinal) || Stalls(sd) > 0 ||
                (!double.IsNaN(sph) && sph >= 30);
        }

        // ================================================================ 5. Desempenho medido (FPS, 1% low, engasgos)
        static void PerformanceRules(Diagnosis d, List<SessionData> sessions)
        {
            Dictionary<string, List<SessionInfo>> byGame = new Dictionary<string, List<SessionInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (SessionData sd in sessions)
            {
                if (!GameSession(sd) || double.IsNaN(StuttersPerHour(sd.Info))) continue;
                List<SessionInfo> l;
                if (!byGame.TryGetValue(sd.Info.Game, out l)) { l = new List<SessionInfo>(); byGame[sd.Info.Game] = l; }
                l.Add(sd.Info);
            }
            if (byGame.Count == 0) return;
            // o estado atual: as 3 partidas mais recentes de cada jogo (correções antigas não se misturam com o agora)
            foreach (List<SessionInfo> l in byGame.Values)
            {
                l.Sort(delegate(SessionInfo a, SessionInfo b) { return b.Start.CompareTo(a.Start); });
                if (l.Count > 3) l.RemoveRange(3, l.Count - 3);
            }
            List<string> lines = new List<string>();
            double worst = 0; string worstGame = "";
            foreach (KeyValuePair<string, List<SessionInfo>> kv in byGame)
            {
                double fps = 0, low = 0, frames = 0, st = 0, hours = 0;
                foreach (SessionInfo si in kv.Value)
                {
                    fps += si.FpsAvg * si.Frames; low += si.FpsLow * si.Frames; frames += si.Frames;
                    st += si.Stutters; hours += si.Frames / si.FpsAvg / 3600.0;
                }
                double sph = st / Math.Max(0.05, hours);
                if (sph > worst) { worst = sph; worstGame = kv.Key; }
                lines.Add("• " + kv.Key + ": " + N0(fps / frames) + " FPS em média, 1% low " + N0(low / frames) + ", " + N0(sph) + " engasgos por hora (" +
                    (kv.Value.Count == 1 ? "última partida)" : kv.Value.Count + " últimas partidas)"));
            }
            Finding f = new Finding();
            f.Source = "Medição de FPS (PresentMon)";
            f.Evidence = string.Join("\n", lines.ToArray()) + "\nEngasgo = quadro mais de 2,5× mais lento que o ritmo do momento e acima de 50 ms (o que se sente como travadinha).";
            if (worst >= 30)
            {
                f.Sev = Sev.Warn;
                f.Title = "Os jogos engasgam: " + N0(worst) + " engasgos por hora em " + worstGame + " (medido)";
                f.Simple = "O jogo trava por instantes com frequência (" + N0(worst) + " vezes por hora em " + worstGame + ")";
                f.Advice = "Veja os outros achados sobre programas abertos, memória virtual e drivers: os Resultados comparam as partidas com e sem cada um. " +
                    "Depois de cada ajuste, jogue de novo: o DiagIT compara o FPS e os engasgos antes e depois em \"Suas correções\".";
                f.Weigh(Comp.Software, 0.15);
            }
            else
            {
                f.Sev = Sev.Ok;
                f.Title = "Desempenho medido nos jogos";
                f.Simple = "Os jogos rodam sem travadinhas frequentes";
            }
            d.Findings.Add(f);
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
                    BgProgram prog = Rules.ProgramByName(label);
                    BgType kind;
                    if (prog == null || !Rules.BgTypes.TryGetValue(prog.Type, out kind)) { kind = new BgType(); kind.Report = "que esse programa causa engasgos."; kind.Advice = "Feche ou desinstale e jogue alguns dias sem ele para comparar."; }
                    f.Evidence = "Problema = o PC caiu, o jogo fechou com erro ou o sistema congelou. A comunidade relata " + kind.Report +
                        (contrast ? "" : " Sem partidas sem ele não dá para comparar.");
                    f.Advice = kind.Advice;
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
