using System;
using System.Collections.Generic;

namespace DiagIT
{
    /// <summary>Resultado do acompanhamento de uma correção.</summary>
    public class FixStatus
    {
        public FixEntry Fix;
        public int State;          // 0 acompanhando, 1 resolvido, 2 voltou (não resolveu), 3 sem como comparar
        public int Before, After;  // ocorrências do problema antes e depois da correção
        public double BeforeDays, AfterDays;
        public DateTime FirstAfter = DateTime.MinValue;
        public string Headline = "", Text = "";
    }

    /// <summary>
    /// Ciclo "corrigiu?": compara o problema antes e depois de cada correção. Resolvido = nenhuma ocorrência por tempo suficiente
    /// (pelo menos 7 dias e 2× o intervalo médio que havia entre as ocorrências, no máximo 21); voltou = qualquer ocorrência depois da correção.
    /// </summary>
    public static partial class Analyzer
    {
        public static readonly string[] Scopes = { "telas-azuis", "video", "desligamentos", "engasgos", "geral" };

        public static string ScopeName(string scope)
        {
            switch (scope)
            {
                case "telas-azuis": return "telas azuis";
                case "video": return "travamentos de vídeo";
                case "desligamentos": return "desligamentos sozinho";
                case "engasgos": return "congelamentos e engasgos";
                default: return "problemas registrados";
            }
        }

        /// <summary>Momentos em que cada tipo de problema aconteceu (eventos do Windows + partidas gravadas), mais recentes primeiro.</summary>
        static List<DateTime> ProblemTimes(string scope, List<WinEvent> ev, List<SessionData> sessions)
        {
            List<DateTime> r = new List<DateTime>();
            bool all = scope == "geral";
            if (scope == "telas-azuis" || all)
                foreach (WinEvent e in ev)
                    if (BugcodeOf(e) > 0 && !r.Exists(delegate(DateTime t) { return Math.Abs((t - e.Time).TotalMinutes) < 10; })) r.Add(e.Time);
            if (scope == "video" || all)
                foreach (DateTime t in GpuCrashTimes(ev)) if (!r.Exists(delegate(DateTime x) { return Math.Abs((x - t).TotalMinutes) < 10; })) r.Add(t);
            if (scope == "desligamentos" || all)
            {
                foreach (WinEvent e in ev)
                    if (e.Provider == "Microsoft-Windows-Kernel-Power" && e.Id == 41 && ParseCode(e.Prop(0)) == 0 &&
                        !r.Exists(delegate(DateTime t) { return Math.Abs((t - e.Time).TotalMinutes) < 10; })) r.Add(e.Time);
            }
            if (scope == "engasgos" || all)
                foreach (SessionData sd in sessions)
                {
                    if (!GameSession(sd)) continue;
                    double sph = StuttersPerHour(sd.Info);
                    if (!double.IsNaN(sph) && sph >= 30) { r.Add(sd.Info.Start); continue; }
                    foreach (Sample s in sd.S) { double g = s.V[F.Gap]; if (!double.IsNaN(g) && g > 6 && g < 120) { r.Add(s.Time); break; } }
                }
            if (all)
                foreach (SessionData sd in sessions)
                    if (GameSession(sd) && (sd.Info.Status == "interrupted" || sd.Info.Note.StartsWith("O jogo fechou com erro", StringComparison.Ordinal)))
                        r.Add(sd.Info.End ?? sd.Info.Start);
            r.Sort(delegate(DateTime a, DateTime b) { return b.CompareTo(a); });
            return r;
        }

        /// <summary>Que tipo de problema um achado representa (para ligar achados às correções).</summary>
        public static string ScopeOf(Finding f)
        {
            if (f.BugCode >= 0) return Array.IndexOf(VideoBugchecks, f.BugCode) >= 0 ? "video" : "telas-azuis";
            string t = f.Title;
            if (t.StartsWith("O driver de vídeo parou", StringComparison.Ordinal) || t.StartsWith("Travamentos de GPU", StringComparison.Ordinal) ||
                t.StartsWith("Jogos/apps travaram dentro do driver", StringComparison.Ordinal) || t.StartsWith("Os travamentos de vídeo", StringComparison.Ordinal) ||
                t.StartsWith("A GPU trabalha acima", StringComparison.Ordinal) || t.StartsWith("O Windows Update instalou", StringComparison.Ordinal)) return "video";
            if (t.StartsWith("O PC desligou", StringComparison.Ordinal) || t.StartsWith("Partida interrompida", StringComparison.Ordinal)) return "desligamentos";
            if (t.StartsWith("O sistema 'congelou'", StringComparison.Ordinal) || t.StartsWith("Tempo excessivo em drivers", StringComparison.Ordinal) ||
                t.StartsWith("Disco com picos", StringComparison.Ordinal) || t.StartsWith("Memória virtual também no HD", StringComparison.Ordinal) ||
                t.StartsWith("Partidas com", StringComparison.Ordinal) || t.StartsWith("Placa de rede", StringComparison.Ordinal)) return "engasgos";
            return "geral";
        }

        /// <summary>FPS medido no mesmo jogo antes e depois da correção (" 1% low em CS2: 41 → 58 (+41%).").</summary>
        static string PerfChange(List<SessionData> sessions, DateTime when)
        {
            Dictionary<string, double[]> acc = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);   // jogo → [lowAntes, framesAntes, lowDepois, framesDepois]
            foreach (SessionData sd in sessions)
            {
                SessionInfo si = sd.Info;
                if (!GameSession(sd) || si.Frames < 600 || double.IsNaN(si.FpsLow)) continue;
                double[] a;
                if (!acc.TryGetValue(si.Game, out a)) { a = new double[4]; acc[si.Game] = a; }
                int k = si.Start < when ? 0 : 2;
                a[k] += si.FpsLow * si.Frames; a[k + 1] += si.Frames;
            }
            string best = ""; double bestFrames = 0;
            foreach (KeyValuePair<string, double[]> kv in acc)
            {
                double[] a = kv.Value;
                if (a[1] == 0 || a[3] == 0 || a[1] + a[3] <= bestFrames) continue;
                double before = a[0] / a[1], after = a[2] / a[3];
                double pct = (after - before) / before * 100;
                best = " FPS mínimo (1% low) em " + kv.Key + ": " + N0(before) + " → " + N0(after) + " (" + (pct >= 0 ? "+" : "") + N0(pct) + "%).";
                bestFrames = a[1] + a[3];
            }
            return best;
        }

        static void FixRules(Diagnosis d, List<WinEvent> ev, List<SessionData> sessions, List<FixEntry> fixes)
        {
            if (fixes == null) return;
            DateTime now = DateTime.Now, horizon = now.AddDays(-d.Days);
            foreach (FixEntry fx in fixes)
            {
                FixStatus st = new FixStatus();
                st.Fix = fx;
                List<DateTime> times = ProblemTimes(fx.Scope, ev, sessions);
                DateTime from = fx.When.AddDays(-30) > horizon ? fx.When.AddDays(-30) : horizon;
                DateTime settled = fx.When.AddMinutes(30);   // reinícios e reinstalações da própria correção não contam
                DateTime earliest = fx.When;
                foreach (DateTime t in times)
                {
                    if (t >= from && t < fx.When) { st.Before++; if (t < earliest) earliest = t; }
                    if (t > settled) { st.After++; if (st.FirstAfter == DateTime.MinValue || t < st.FirstAfter) st.FirstAfter = t; }
                }
                st.BeforeDays = Math.Max(1, (fx.When - (st.Before > 0 ? earliest : from)).TotalDays);   // do primeiro registro até a correção
                st.AfterDays = (now - fx.When).TotalDays;
                string what = ScopeName(fx.Scope);
                string beforeTxt = st.Before == 0 ? "" : " Antes: " + st.Before + " em " + Math.Round(st.BeforeDays) + " dias.";
                if (st.After > 0)
                {
                    st.State = 2;
                    st.Headline = "Ainda não resolveu";
                    st.Text = "Os " + what + " voltaram em " + st.FirstAfter.ToString("dd/MM") + " (" + st.After + (st.After == 1 ? " vez" : " vezes") + " desde a correção)." + beforeTxt +
                        " Veja os Resultados: o próximo passo já considera isso.";
                }
                else if (st.Before == 0)
                {
                    st.State = 3;
                    st.Headline = "Acompanhando";
                    st.Text = "Não havia " + what + " registrados antes, então não dá para comparar. O DiagIT avisa se algo aparecer.";
                }
                else
                {
                    double interval = st.BeforeDays / st.Before;                       // a cada quantos dias acontecia
                    double need = Math.Max(7, Math.Min(21, interval * 2));
                    if (st.AfterDays >= need)
                    {
                        st.State = 1;
                        st.Headline = "Resolvido";
                        st.Text = "Nenhum registro de " + what + " em " + Math.Floor(st.AfterDays) + " dias desde a correção." + beforeTxt;
                    }
                    else
                    {
                        st.State = 0;
                        st.Headline = "Acompanhando";
                        int left = (int)Math.Ceiling(need - st.AfterDays);
                        st.Text = "Nenhum registro de " + what + " desde " + fx.When.ToString("dd/MM") + "." + beforeTxt +
                            " Mais " + left + (left == 1 ? " dia" : " dias") + " sem problemas para confirmar.";
                    }
                }
                st.Text += PerfChange(sessions, fx.When);
                d.Fixes.Add(st);

                // correção que resolveu: os indícios de antes dela pesam bem menos (o problema daquela época foi tratado)
                if (st.State == 1)
                    foreach (Finding f in d.Findings)
                    {
                        if (f.Confirmed || f.Sev == Sev.Ok || f.Last == DateTime.MinValue || f.Last > fx.When) continue;
                        string sc = ScopeOf(f);
                        if (fx.Scope != "geral" && sc != fx.Scope && !(fx.Scope == "telas-azuis" && sc == "video") && !(fx.Scope == "video" && sc == "telas-azuis" && f.BugCode >= 0 && Array.IndexOf(VideoBugchecks, f.BugCode) >= 0)) continue;
                        List<Comp> keys = new List<Comp>(f.W.Keys);
                        foreach (Comp c in keys) f.W[c] = f.W[c] * 0.3;
                        f.Evidence += "\nAconteceu antes da correção de " + fx.When.ToString("dd/MM") + " (\"" + fx.Title + "\"), e não voltou desde então.";
                    }
            }
            d.Fixes.Sort(delegate(FixStatus a, FixStatus b) { return b.Fix.When.CompareTo(a.Fix.When); });
        }
    }
}
