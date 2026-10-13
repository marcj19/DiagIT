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
        static readonly int[] MemCodes = { 0x1A, 0x4E, 0x50 }, CpuCodes = { 0x124, 0x9C, 0x101, 0x7F },
            DiskCodes = { 0x7A, 0x7B, 0xF4, 0xEF }, DrvCodes = { 0xA, 0xD1, 0xC5, 0x133, 0x3B, 0x1E, 0x7E, 0xC2, 0x139 };

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
        public static string BsodFamily(int code)
        {
            if (Array.IndexOf(VideoBugchecks, code) >= 0) return "ligada à placa de vídeo";
            if (Array.IndexOf(MemCodes, code) >= 0) return "ligada à memória";
            if (Array.IndexOf(CpuCodes, code) >= 0) return "de erro de hardware";
            if (Array.IndexOf(DiskCodes, code) >= 0) return "ligada ao disco";
            if (Array.IndexOf(DrvCodes, code) >= 0) return "causada por um driver";
            return "";
        }

        // prefixo do título técnico -> título simples ({n} = número de ocorrências no título)
        static readonly string[][] PlainMap =
        {
            new string[] { "Erros FATAIS de hardware", "O próprio hardware registrou erros graves" },
            new string[] { "Erros de hardware corrigidos", "O hardware corrigiu erros sozinho várias vezes (sinal de instabilidade)" },
            new string[] { "O driver de vídeo parou de responder", "A tela travou ou ficou preta e voltou ({n}): o driver de vídeo reiniciou" },
            new string[] { "Travamentos de GPU registrados", "A placa de vídeo parou de responder ({n})" },
            new string[] { "Watchdog do kernel disparou", "O Windows ficou travado por alguns segundos" },
            new string[] { "O PC desligou/reiniciou sem aviso", "O PC desligou ou reiniciou sozinho ({n}), sem tela azul" },
            new string[] { "A BIOS/firmware limitou a velocidade da CPU", "O processador foi freado para não esquentar ou gastar energia demais" },
            new string[] { "O diagnóstico de memória do Windows encontrou ERROS", "O teste de memória do Windows encontrou defeito na RAM" },
            new string[] { "Teste de memória do Windows sem erros", "O teste de memória do Windows não achou problemas" },
            new string[] { "Vários programas diferentes travaram com erro de memória", "Vários programas fecharam sozinhos com erro de memória" },
            new string[] { "Jogos/apps travaram dentro do driver de vídeo", "Jogos fecharam por erro no driver de vídeo ({n})" },
            new string[] { "Memória virtual (commit) quase", "A memória do PC está quase cheia" },
            new string[] { "Pouca memória RAM livre durante o jogo", "Falta memória RAM durante os jogos" },
            new string[] { "Erros de armazenamento registrados", "O Windows teve erros ao ler ou gravar no disco" },
            new string[] { "Disco com problemas registrados por ele mesmo", "Um disco está começando a falhar" },
            new string[] { "Disco com problemas", "Um disco está com problemas" },
            new string[] { "Discos reportam estado saudável", "Os discos estão saudáveis" },
            new string[] { "Discos saudáveis", "Os discos estão saudáveis" },
            new string[] { "Sem leitura de temperatura", "O DiagIT não consegue ler a temperatura" },
            new string[] { "CPU chegou ao limite térmico", "O processador esquentou demais" },
            new string[] { "CPU muito quente em jogo", "O processador fica muito quente nos jogos" },
            new string[] { "CPU passa ", "O processador fica muito quente nos jogos" },
            new string[] { "Temperatura da CPU normal", "A temperatura do processador está normal" },
            new string[] { "GPU em temperatura crítica", "A placa de vídeo esquentou demais" },
            new string[] { "GPU muito quente", "A placa de vídeo fica muito quente" },
            new string[] { "GPU quente, mas dentro do limite", "A placa de vídeo esquenta, mas dentro do limite" },
            new string[] { "Temperatura da GPU normal", "A temperatura da placa de vídeo está normal" },
            new string[] { "Ventoinha da GPU parada", "A ventoinha da placa de vídeo não está girando" },
            new string[] { "Ventoinhas da GPU NÃO giraram", "A ventoinha da placa de vídeo não girou no teste" },
            new string[] { "Ventoinha da CPU parada", "A ventoinha do processador parece parada" },
            new string[] { "A CPU perde desempenho", "O processador fica mais lento quando esquenta" },
            new string[] { "CPU reduz a velocidade por temperatura", "O processador fica mais lento quando esquenta" },
            new string[] { "CPU chegou a ", "O processador chegou perto do limite de temperatura no teste" },
            new string[] { "O clock da GPU cai", "A placa de vídeo fica mais lenta com o tempo de jogo" },
            new string[] { "Tensão de +12 V caiu", "A fonte não mantém a energia estável" },
            new string[] { "Tensão de +12 V fora", "A fonte não mantém a energia estável" },
            new string[] { "Tensão de +12 V estável", "A fonte entrega energia estável" },
            new string[] { "+12 V estável", "A fonte entregou energia estável no máximo do PC" },
            new string[] { "O sistema 'congelou'", "O PC congelou por alguns segundos durante os jogos" },
            new string[] { "O sistema congelou", "O PC congelou por alguns segundos durante o teste" },
            new string[] { "Tempo excessivo em drivers", "Algum driver está atrasando o PC (causa travadinhas)" },
            new string[] { "Muita leitura de disco por falta de memória", "Falta memória e o PC usa o disco no lugar (causa travadinhas)" },
            new string[] { "Disco com picos de latência", "O disco às vezes demora para responder (causa travadinhas)" },
            new string[] { "SSD/HD quente", "O disco está esquentando" },
            new string[] { "VRAM da GPU quase cheia", "A memória da placa de vídeo fica cheia (baixe a qualidade das texturas)" },
            new string[] { "Gargalo de CPU", "O processador limita o FPS nos jogos (não é defeito)" },
            new string[] { "Partida interrompida abruptamente", "Uma partida terminou de repente" },
            new string[] { "O PC DESLIGOU/REINICIOU com CPU e GPU", "O PC desligou no teste de energia: a fonte é a principal suspeita" },
            new string[] { "Processador ERRA CÁLCULOS", "O processador errou cálculos no teste" },
            new string[] { "Memória RAM com ERROS", "A memória RAM errou no teste: defeito comprovado" },
            new string[] { "A placa de vídeo TRAVA sob carga", "A placa de vídeo travou no teste" },
            new string[] { "Placa de vídeo com ERROS", "A placa de vídeo errou cálculos no teste" },
            new string[] { "Armazenamento com problemas confirmados", "O disco falhou no teste" },
            new string[] { "Falha sob carga máxima", "O PC falhou com tudo no máximo" },
            new string[] { "GPU conectada com menos pistas", "A placa de vídeo está mal encaixada ou o slot tem problema" },
            new string[] { "Placa de vídeo usando x", "A placa de vídeo está mal encaixada ou o slot tem problema" },
            new string[] { "Banda PCIe da placa de vídeo abaixo", "A conexão da placa de vídeo está mais lenta que o normal" },
            new string[] { "Banda PCIe normal", "A conexão da placa de vídeo está normal" },
            new string[] { "REPRODUZIDO: o watchdog", "O travamento se repetiu no teste sem erro de hardware: o driver é o suspeito" },
            new string[] { "O hardware reportou erro (WHEA) durante o teste", "O hardware registrou erro durante o teste" },
            new string[] { "Erros de comunicação no cabo", "O cabo do disco está com mau contato" },
            new string[] { "Memória em single channel", "A memória está instalada de um jeito que deixa os jogos mais lentos" },
            new string[] { "Os travamentos de vídeo começaram depois", "Os travamentos começaram depois que o Windows Update trocou o driver de vídeo" },
            new string[] { "O Windows Update instalou o driver de vídeo", "O driver de vídeo em uso foi instalado pelo Windows Update" },
            new string[] { "A GPU trabalha acima do clock oficial", "A placa de vídeo trabalha acima da velocidade oficial" },
            new string[] { "Placa de rede ", "A placa de rede Realtek pode estar causando travadinhas" },
            new string[] { "Memória virtual também no HD", "Parte da memória virtual está no HD, que é lento" },
        };

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
                    f.Simple = "O PC teve " + Times(n, "tela azul", "telas azuis") + (fam.Length > 0 ? " " + fam : "");
                    continue;
                }
                if (f.Sev == Sev.Ok && f.Title.StartsWith("Teste de ", StringComparison.Ordinal) && f.Title.Contains("APROVADO"))
                {
                    Match m = Regex.Match(f.Title, @"^Teste de (.+) APROVADO \((.+)\)$");
                    if (m.Success) { f.Simple = m.Groups[1].Value + " passou no teste (" + m.Groups[2].Value + ")"; continue; }
                }
                foreach (string[] p in PlainMap)
                {
                    if (!f.Title.StartsWith(p[0], StringComparison.Ordinal)) continue;
                    int n = CountIn(f.Title);
                    f.Simple = p[1].Replace("({n})", n > 0 ? "(" + n + "×)" : "").Replace("  ", " ").Replace(" :", ":").Trim();
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
                sub = "As peças passaram nos testes e não há sinais de problema nos registros do Windows.";
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
            if (f.Sev == Sev.Info && f.W.Count == 0) return "INFORMAÇÃO";
            return "INDÍCIO";
        }
    }
}
