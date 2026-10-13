using System;
using System.Collections.Generic;

namespace DiagIT
{
    /// <summary>
    /// Driver culpado de cada tela azul. Fontes: o evento 1019 do Windows ("Possibly related driver", lido sem administrador)
    /// e o próprio arquivo da tela azul (Minidump, com administrador). O conhecimento sobre cada driver fica em data/drivers.json.
    /// </summary>
    public static partial class Analyzer
    {
        /// <summary>Driver apontado para a tela azul que aconteceu em t (evento 1019 ou arquivo de despejo, até 10 min de diferença).</summary>
        static string CulpritNear(List<WinEvent> ev, HwSnapshot hw, DateTime t, out string how)
        {
            how = null;
            string best = null; double bestMin = 10;
            foreach (WinEvent e in ev)
            {
                if (e.Provider != "Microsoft-Windows-WER-SystemErrorReporting" || e.Id != 1019 || e.Prop(0).Length == 0) continue;
                double m = Math.Abs((e.Time - t).TotalMinutes);
                if (m <= bestMin) { bestMin = m; best = e.Prop(0); how = "Windows"; }
            }
            if (hw != null)
                foreach (CrashDump cd in hw.Dumps)
                {
                    if (cd.Live || cd.Culprit == null) continue;
                    double m = Math.Abs((cd.Time - t).TotalMinutes);
                    if (m <= bestMin) { bestMin = m; best = cd.Culprit; how = "arquivo da tela azul"; }
                }
            return best;
        }

        /// <summary>
        /// Registra o driver culpado no achado. Se ele aparece na maioria das telas azuis e não é o núcleo do Windows,
        /// o peso passa para "Drivers e Windows" (e o do hardware cai pela metade): peças boas com driver ruim também dão tela azul.
        /// </summary>
        static void ApplyCulprit(Finding f, List<string> found, int total, double s, string sources)
        {
            if (f.Culprit != null || found.Count == 0) return;
            Dictionary<string, int> n = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string x in found) { int c; n.TryGetValue(x, out c); n[x] = c + 1; }
            string top = null; int topN = 0;
            foreach (KeyValuePair<string, int> kv in n) if (kv.Value > topN) { top = kv.Key; topN = kv.Value; }
            f.Culprit = top; f.CulpritCount = topN; f.CulpritOf = total;

            DriverRule r = Rules.Driver(top);
            DriverType ty = Rules.DriverTypeOf(r);
            string who = r != null ? r.Name : "um driver de terceiros";
            f.Evidence += "\nDriver apontado (" + sources + "): " + top + " — " + who + ", em " + topN + " de " + total + (total == 1 ? " tela azul." : " telas azuis.");
            if (n.Count > 1)
            {
                List<string> others = new List<string>();
                foreach (KeyValuePair<string, int> kv in n) if (kv.Key != top) others.Add(kv.Key + " ×" + kv.Value);
                f.Evidence += " Outros apontados: " + string.Join(", ", others.ToArray()) + ".";
            }
            if (r != null && r.Core)
            {
                f.Evidence += " É o próprio núcleo do Windows: a causa real costuma ser memória, hardware ou outro driver que corrompeu a memória.";
                return;
            }
            if (topN * 2 < total) return;   // cada tela azul num driver diferente: aponta mais para memória/hardware

            List<Comp> keys = new List<Comp>(f.W.Keys);
            foreach (Comp c in keys) if (c != Comp.Software) f.W[c] = f.W[c] * 0.5;
            Dictionary<Comp, double> tw = ty != null ? ty.W : new Dictionary<Comp, double> { { Comp.Software, 0.45 } };
            foreach (KeyValuePair<Comp, double> kv in tw)
            {
                double cur;
                f.W.TryGetValue(kv.Key, out cur);
                f.W[kv.Key] = Math.Max(cur, kv.Value * Math.Max(0.6, s));
            }
            f.Advice = "O Windows aponta " + (r != null ? "o " + r.Name : "o driver " + top) + " (" + top + ") como causa. " +
                (ty != null ? ty.Advice : "Descubra de qual programa é esse driver (pesquise o nome do arquivo) e atualize ou desinstale esse programa.");
        }

        /// <summary>Liga cada achado ao guia passo a passo que o resolve (data/guias.json).</summary>
        static void AssignGuides(Diagnosis d)
        {
            foreach (Finding f in d.Findings)
            {
                if (f.Sev == Sev.Ok) continue;
                if (f.BugCode >= 0)
                {
                    DriverType ty = Rules.DriverTypeOf(Rules.Driver(f.Culprit));
                    if (CulpritPhrase(f) != null && ty != null && ty.Key == "video") f.Guide = "driver-video-limpo";
                    else if (CulpritPhrase(f) == null)
                    {
                        string fam = Rules.Bug(f.BugCode).Family;
                        if (fam == "ligada à memória" || fam == "de erro de hardware") f.Guide = "bios-padrao";
                        else if (fam == "ligada à placa de vídeo") f.Guide = "driver-video-limpo";
                    }
                    continue;
                }
                foreach (KeyValuePair<string, string> kv in Rules.GuideLinks)
                    if (f.Title.StartsWith(kv.Key, StringComparison.Ordinal)) { f.Guide = kv.Value; break; }
                if (f.Guide != null && Rules.GuideById(f.Guide) == null) f.Guide = null;
            }
        }

        /// <summary>Nome do culpado para a frase simples ("causadas pelo driver de vídeo da NVIDIA"), ou null.</summary>
        static string CulpritPhrase(Finding f)
        {
            if (f.Culprit == null || f.CulpritCount * 2 < f.CulpritOf) return null;
            DriverRule r = Rules.Driver(f.Culprit);
            if (r != null && r.Core) return null;
            return r != null ? r.Name : "driver " + f.Culprit;
        }
    }
}
