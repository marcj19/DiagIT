using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DiagIT
{
    /// <summary>
    /// Camada em linguagem simples: o que a interface mostra primeiro para quem é leigo.
    /// Os títulos técnicos continuam no achado (Title/Evidence) e aparecem em "Ver detalhes técnicos".
    /// </summary>
    public static partial class Analyzer
    {

        static string Times(int n, string one, string many) { return n == 1 ? "1 " + one : n + " " + many; }

        /// <summary>Quantas vezes o achado aconteceu (telas azuis, travamentos...), 0 se não se aplica.</summary>
        public static int Occurrences(Finding f) { return f.Count > 0 ? f.Count : CountIn(f.Title); }

        static int CountIn(string title)
        {
            Match m = Regex.Match(title, @"(\d+)\s*×");
            int n;
            return m.Success && int.TryParse(m.Groups[1].Value, out n) ? n : 0;
        }

        /// <summary>Do que a tela azul costuma ser, dito para leigo.</summary>
        public static string BsodFamily(int code) { return Rules.Bug(code).Family; }

        static void PlainTitles(Diagnosis d)
        {
            foreach (Finding f in d.Findings)
            {
                if (f.Simple.Length > 0) continue;
                if (f.BugCode >= 0)
                {
                    int n = f.Count > 0 ? f.Count : Math.Max(1, CountIn(f.Title));
                    string fam = BsodFamily(f.BugCode);
                    if (n > 1) fam = fam.Replace("ligada", "ligadas").Replace("causada", "causadas");
                    string who = CulpritPhrase(f);
                    if (who != null) fam = (n > 1 ? "causadas pelo " : "causada pelo ") + who;
                    f.Simple = "O PC teve " + Times(n, "tela azul", "telas azuis") + (fam.Length > 0 ? " " + fam : "");
                    continue;
                }
                if (f.Sev == Sev.Ok && f.Title.StartsWith("Teste de ", StringComparison.Ordinal) && f.Title.Contains("APROVADO"))
                {
                    Match m = Regex.Match(f.Title, @"^Teste de (.+) APROVADO \((.+)\)$");
                    if (m.Success) { f.Simple = m.Groups[1].Value + " passou no teste (" + m.Groups[2].Value + ")"; continue; }
                }
                foreach (PlainRule p in Rules.Plain)   // data/linguagem-simples.json
                {
                    if (!f.Title.StartsWith(p.Prefix, StringComparison.Ordinal)) continue;
                    int n = CountIn(f.Title);
                    f.Simple = p.Simple.Replace("({n})", n > 0 ? "(" + n + "×)" : "").Replace("  ", " ").Replace(" :", ":").Trim();
                    break;
                }
            }
        }

        /// <summary>Primeira frase de um texto (para resumos curtos).</summary>
        public static string FirstSentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf(". ", StringComparison.Ordinal);
            return i > 0 ? s.Substring(0, i + 1) : s;
        }

        static readonly string[] CauseNames =
        {
            "o processador", "a placa de vídeo", "a memória RAM", "o armazenamento (SSD/HD)",
            "a fonte de energia", "a refrigeração (temperatura alta)", "um driver ou um ajuste da BIOS", "a placa-mãe"
        };

        /// <summary>Veredito em linguagem simples para o topo do Início e dos Resultados.</summary>
        public static void PlainVerdict(Diagnosis d, out string title, out string sub)
        {
            PlainVerdictCore(d, out title, out sub);
            // troca de driver de vídeo em acompanhamento: diz claramente em que pé está, em vez de deixar o usuário na dúvida
            foreach (FixStatus fs in d.Fixes)
            {
                if (IsDriverSwap(fs.Fix) && fs.State == 2)
                {
                    sub = (sub.Length > 0 ? sub + " " : "") + "Depois da troca do driver de vídeo, o problema voltou em " + fs.FirstAfter.ToString("dd/MM 'às' HH:mm") +
                        (fs.Context.Length > 0 ? " (" + fs.Context + ")" : "") + ". Veja o plano de testes em \"Suas correções\": um passo de cada vez, sempre com o DiagIT acompanhando.";
                    break;
                }
                if (!IsDriverSwap(fs.Fix) || fs.State == 2) continue;
                sub = (sub.Length > 0 ? sub + " " : "") + (fs.State == 1
                    ? "O driver de vídeo foi trocado em " + fs.Fix.When.ToString("dd/MM") + " e os travamentos pararam."
                    : "Você trocou o driver de vídeo em " + fs.Fix.When.ToString("dd/MM 'às' HH:mm") + " e nada de errado apareceu desde então. " +
                      "Falta usar o PC por alguns dias (jogue normalmente) para confirmar; o DiagIT acompanha e avisa.");
                break;
            }
        }

        static void PlainVerdictCore(Diagnosis d, out string title, out string sub)
        {
            List<int> failed = new List<int>(), suspect = new List<int>(), untested = new List<int>();
            int ok = 0;
            for (int i = 0; i < 8; i++)
            {
                CompState s = d.States[i];
                if (s == null) continue;
                if (s.Status == CompStatus.Failed) failed.Add(i);
                else if (s.Status == CompStatus.Suspect) suspect.Add(i);
                else if (s.Status == CompStatus.Ok) ok++;
                else if (s.HasTest) untested.Add(i);
            }
            int bsod = 0;
            foreach (Finding f in d.Findings) if (f.BugCode >= 0) bsod += Math.Max(1, f.Count);

            if (failed.Count > 0)
            {
                title = "Encontramos um defeito: " + JoinPlain(failed).ToLowerInvariant().Replace("ram", "RAM");
                CompState s = d.States[failed[0]];
                sub = s.Cause != null ? FirstSentence(s.Cause.Advice) : "Veja abaixo o que fazer.";
                return;
            }
            if (suspect.Count > 0)
            {
                // principal suspeito: o de maior pontuação
                int top = suspect[0];
                foreach (int i in suspect) if (d.Score[i] > d.Score[top]) top = i;
                string cause = CauseNames[top];
                // driver apontado pelo próprio Windows na maioria das telas azuis: é a evidência mais forte que existe
                Finding byDriver = null;
                foreach (Finding f in d.Findings)
                    if (f.BugCode >= 0 && CulpritPhrase(f) != null && (byDriver == null || f.Count > byDriver.Count)) byDriver = f;
                if (byDriver != null && byDriver.CulpritCount * 2 >= bsod) cause = "o " + CulpritPhrase(byDriver) + " (" + byDriver.Culprit + "), apontado pelo próprio Windows";
                if (bsod > 0) title = "Seu PC teve " + Times(bsod, "tela azul", "telas azuis") + (ok > 0 ? ", mas as peças testadas estão boas" : "");
                else title = suspect.Count == 1 ? "Há 1 ponto para verificar no seu PC" : "Há " + suspect.Count + " pontos para verificar no seu PC";
                sub = "A causa mais provável é " + cause + ".";
                List<int> rest = untested.FindAll(delegate(int i) { return !suspect.Contains(i); });
                if (rest.Count > 0) sub += " Falta testar " + JoinTests(d, rest) + " para ter certeza.";
                return;
            }
            if (ok >= 4 && untested.Count == 0)
            {
                title = "Seu PC está saudável";
                int routine = d.Findings.FindAll(delegate(Finding f) { return f.Sev == Sev.Info && !f.Confirmed; }).Count;
                sub = routine == 0
                    ? "As peças passaram nos testes e não há sinais de problema nos registros do Windows."
                    : "As peças passaram nos testes e nada pede ação. Há " + routine + (routine == 1 ? " aviso de rotina" : " avisos de rotina") + " em Resultados, só para você saber.";
                return;
            }
            if (ok >= 4)
            {
                title = "Nenhum problema encontrado até agora";
                sub = "Falta testar " + JoinTests(d, untested) + " para confirmar a saúde de todas as peças.";
                return;
            }
            title = "Nenhum problema registrado até agora";
            sub = "Faça o check-up para confirmar a saúde de cada peça. Leva cerca de 20 minutos, com os jogos fechados.";
        }

        /// <summary>Nomes dos testes que faltam (fonte, temperatura e placa-mãe saem do mesmo teste com tudo no máximo).</summary>
        static string JoinTests(Diagnosis d, List<int> comps)
        {
            List<string> n = new List<string>();
            foreach (int i in comps)
            {
                CompState s = d.States[i];
                if (s == null || !s.HasTest) continue;
                string t = s.Test == TestKind.Cpu ? "o processador" : s.Test == TestKind.Ram ? "a memória RAM" : s.Test == TestKind.Gpu ? "a placa de vídeo" :
                    s.Test == TestKind.Disk ? "o armazenamento" : "a fonte de energia";
                if (!n.Contains(t)) n.Add(t);
            }
            if (n.Count <= 1) return n.Count == 1 ? n[0] : "";
            return string.Join(", ", n.GetRange(0, n.Count - 1).ToArray()) + " e " + n[n.Count - 1];
        }

        static string JoinPlain(List<int> comps)
        {
            List<string> n = new List<string>();
            foreach (int i in comps) n.Add(CompInfo.Plain[i]);
            if (n.Count <= 1) return n.Count == 1 ? n[0] : "";
            return string.Join(", ", n.GetRange(0, n.Count - 1).ToArray()) + " e " + n[n.Count - 1];
        }

        /// <summary>Uma linha simples sobre o estado da peça (cartões do Início).</summary>
        public static string PlainNote(CompState s, int comp)
        {
            if (s == null) return "";
            switch (s.Status)
            {
                case CompStatus.Failed:
                    return s.Cause != null ? s.Cause.Plain + "." : "Defeito comprovado.";
                case CompStatus.Suspect:
                    if (s.Retest) return "O problema voltou depois do último teste.";
                    if (comp == (int)Comp.Software && s.Cause != null && s.Cause.BugCode >= 0) return "Principal suspeito das telas azuis.";
                    return s.Cause != null ? s.Cause.Plain + "." : "Há sinais de problema.";
                case CompStatus.Ok:
                    if (s.Passed != null) return "Passou no teste em " + s.Passed.Start.ToString("dd/MM") + (s.Cause != null ? ", depois do problema." : ".");
                    return s.Basis != null ? s.Basis.Plain + "." : "Sem sinais de problema.";
                default:
                    return s.HasTest ? "Ainda não foi testado com o PC no máximo." : "Sem sinais de problema.";
            }
        }

        public static string StatusLabel(CompStatus s)
        {
            switch (s)
            {
                case CompStatus.Failed: return "Com defeito";
                case CompStatus.Suspect: return "Precisa verificar";
                case CompStatus.Ok: return "Saudável";
                default: return "Não verificado";
            }
        }

        /// <summary>Rótulo de confiança de um achado: Comprovado, Indício, Informação ou OK.</summary>
        public static string Confidence(Finding f)
        {
            if (f.Confirmed) return "COMPROVADO";
            if (f.Sev == Sev.Ok) return "OK";
            if (f.Sev == Sev.Info) return "INFORMAÇÃO";   // informação nunca é "indício", mesmo com peso interno
            return "INDÍCIO";
        }
    }
}
