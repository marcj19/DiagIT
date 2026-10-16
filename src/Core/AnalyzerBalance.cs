using System;
using System.Collections.Generic;
using System.Text;

namespace DiagIT
{
    /// <summary>
    /// Página "Desempenho": o que segura o PC e onde um ajuste ou upgrade rende. Cada dica só aparece com dado do próprio PC por trás
    /// (monitor, memória, link PCIe, disco, uso medido de processador e placa nas partidas); o que já está bom vira um "OK" verde.
    /// Não é diagnóstico de defeito: nada aqui mexe no veredito de saúde.
    /// </summary>
    public static partial class Analyzer
    {
        const string BDraw = "DE GRAÇA", BCheap = "BARATO", BWhere = "ONDE O UPGRADE RENDE", BLimit = "LIMITE DO CONJUNTO", BDaily = "MAIOR GANHO NO DIA A DIA", BBefore = "ANTES DE UM UPGRADE", BPlatform = "SUA PLATAFORMA";

        static Finding Tip(Sev sev, string badge, string title, string evidence, string advice, string guide, string actionLabel)
        {
            Finding f = new Finding();
            f.Tip = true; f.Sev = sev; f.Source = "Desempenho"; f.Badge = sev == Sev.Ok ? "" : badge;
            f.Title = title; f.Evidence = evidence; f.Advice = advice; f.Guide = guide; f.ActionLabel = actionLabel;
            return f;
        }

        /// <summary>Taxa de memória em MT/s: algumas versões do Windows informam em MHz (metade).</summary>
        /// <summary>Velocidade oficial de memória do processador para o tipo de memória instalado (DDR4 pode ter limite diferente do DDR5).</summary>
        static int OfficialMem(CompatFamily f, int ramType) { return ramType == 26 && f.MemMaxDdr4 > 0 ? f.MemMaxDdr4 : f.MemMaxMts; }

        static int Mts(int v, int smType) { return (smType == 26 || smType == 34) && v > 0 && v < 1300 ? v * 2 : v; }

        public static List<Finding> BalanceTips(HwSnapshot h, List<SessionData> sessions, List<TestResult> tests, DisplayMode dm)
        {
            List<Finding> tips = new List<Finding>();
            if (sessions == null) sessions = new List<SessionData>();
            if (tests == null) tests = new List<TestResult>();
            if (h != null) Rules.SetContext(h.GpuName, h.CpuName);
            bool lap = h != null && h.IsLaptop;   // notebook: memória e processador costumam não ser trocáveis, e a tela troca de taxa para poupar bateria

            CompatFamily fam = h != null ? Rules.CompatFor(h.CpuName) : null;   // plataforma do processador (data/compatibilidade.json)
            bool cpuLimited = false;

            // FPS médio real dos jogos (ponderado pelos quadros medidos)
            double fpsSum = 0, fpsFrames = 0;
            foreach (SessionData sd in sessions)
                if (GameSession(sd) && sd.Info.Frames > 600 && !double.IsNaN(sd.Info.FpsAvg)) { fpsSum += sd.Info.FpsAvg * sd.Info.Frames; fpsFrames += sd.Info.Frames; }
            double fpsAvg = fpsFrames > 0 ? fpsSum / fpsFrames : double.NaN;

            // ---------------------------------------------------------------- 1. Taxa de atualização do monitor
            if (dm != null && dm.MaxHz > 0)
            {
                string rates = string.Join(", ", dm.Rates.ConvertAll<string>(delegate(int r) { return r + " Hz"; }).ToArray());
                if (dm.CurrentHz + 5 < dm.MaxHz)
                {
                    string seen = !double.IsNaN(fpsAvg) && fpsAvg > dm.CurrentHz + 10
                        ? " Seus jogos rodam em média a " + N0(fpsAvg) + " FPS, mas com a tela em " + dm.CurrentHz + " Hz você só vê " + dm.CurrentHz + " quadros por segundo: o resto é desperdiçado." : "";
                    tips.Add(Tip(Sev.Info, BDraw, "Sua tela está em " + dm.CurrentHz + " Hz, mas ela aceita " + dm.MaxHz + " Hz",
                        "Resolução atual " + dm.Width + "×" + dm.Height + ". Taxas oferecidas nessa resolução: " + rates + "." + seen,
                        "É de graça e é a mudança que mais se sente: imagem bem mais fluida. Em Configurações > Sistema > Tela > Tela avançada, escolha a maior taxa. Depois confira também dentro do jogo. Se a taxa maior não aparecer ou a imagem piscar, troque o cabo (DisplayPort costuma ser o melhor)." +
                        (lap ? " Em notebook, o Windows baixa a taxa de propósito na bateria e em tarefas leves para economizar energia: isso é normal. Conecte na tomada e confira durante um jogo." : ""),
                        "monitor-hz", "Como fazer"));
                }
                else if (dm.MaxHz >= 100)
                    tips.Add(Tip(Sev.Ok, "", "Tela na taxa máxima: " + dm.CurrentHz + " Hz", "Resolução " + dm.Width + "×" + dm.Height + ", taxas oferecidas: " + rates + ".", "", null, null));
                else
                    tips.Add(Tip(Sev.Ok, "", "Tela em " + dm.CurrentHz + " Hz (o máximo que ela oferece nessa resolução)", "Resolução " + dm.Width + "×" + dm.Height + ".", "", null, null));
            }

            // ---------------------------------------------------------------- 2. Memória RAM
            if (h != null && h.RamModules > 0)
            {
                if (!h.DualChannel)
                    tips.Add(Tip(Sev.Info, BCheap, "A memória está em canal único (" + h.RamModules + " pente)",
                        "Capacidade total " + N0(h.RamTotalGb) + " GB.",
                        "Com um só pente, o processador usa metade da banda de memória: costuma custar bastante FPS e deixa o PC mais lento. " +
                        (lap ? "Em notebook, a memória muitas vezes é soldada ou o aparelho tem só um slot: veja no manual ou no site do fabricante se existe um segundo slot livre antes de comprar qualquer coisa."
                             : "Colocar um segundo pente igual (nos slots indicados no manual da placa-mãe) é um dos upgrades mais baratos e de maior efeito."), null, null));
                else
                    tips.Add(Tip(Sev.Ok, "", "Memória em dual channel (" + h.RamModules + " pentes)", "Capacidade total " + N0(h.RamTotalGb) + " GB.", "", null, null));

                int conf = Mts(h.RamConfMts, h.RamType), rated = Mts(h.RamRatedMts, h.RamType);
                if (conf > 0 && rated > conf * 1.05 && lap)
                    // notebook não tem XMP/EXPO na BIOS: quem define a velocidade é o fabricante (e o processador móvel costuma aceitar menos que o pente)
                    tips.Add(Tip(Sev.Ok, "", "Memória a " + conf + " MT/s (em notebook, a velocidade é definida pelo fabricante)",
                        "O pente suporta " + rated + " MT/s, mas notebooks não têm opção para mudar isso na BIOS.", "", null, null));
                else if (conf > 0 && rated > conf * 1.05)
                    tips.Add(Tip(Sev.Info, BDraw, "A memória roda a " + conf + " MT/s, mas o pente suporta " + rated + " MT/s",
                        "O perfil de velocidade da memória (XMP, DOCP ou EXPO) está desligado na BIOS.",
                        "Ativar {perfil_memoria} na BIOS é de graça e costuma dar um ganho real em jogos" + (h.CpuName.IndexOf("Ryzen", StringComparison.OrdinalIgnoreCase) >= 0 ? ", principalmente em processadores Ryzen" : "") + ". Depois de ativar, use o PC normalmente e rode o teste de memória do DiagIT; se aparecer instabilidade, volte ao padrão.",
                        "ram-perfil", "Como fazer"));
                else if (conf > 0 && h.RamType == 26 && conf <= 2700)
                    tips.Add(Tip(Sev.Info, BCheap, "A memória é lenta para um processador atual: DDR4 a " + conf + " MT/s",
                        "Não há perfil mais rápido para ativar: " + conf + " MT/s é o que o pente foi feito para entregar." + (fam != null && OfficialMem(fam, h.RamType) > conf ? " O seu processador aceita oficialmente até " + OfficialMem(fam, h.RamType) + " MT/s." : ""),
                        "Kits DDR4-3200 ou 3600 custam pouco. Testes de imprensa costumam mostrar ganho em jogos que dependem do processador, mas o tamanho varia muito por jogo e o DiagIT não mediu isso no seu PC. Antes de comprar, confira no site do fabricante da placa-mãe a velocidade máxima de memória que ela e o processador aceitam." +
                        (lap ? " Em notebook, confira antes se a memória é trocável." : ""), null, null));
                else if (conf > 0)
                    tips.Add(Tip(Sev.Ok, "", "Memória rodando na velocidade do pente: " + conf + " MT/s", "", "", null, null));

                if (h.RamTotalGb > 0 && h.RamTotalGb <= 8.5)
                    tips.Add(Tip(Sev.Info, BWhere, "Só " + N0(h.RamTotalGb) + " GB de memória é pouco para jogos de hoje",
                        "Muitos jogos recentes e o próprio Windows já usam perto disso.",
                        lap ? "Passar para 16 GB resolve engasgos de memória, mas muitos notebooks têm a memória soldada: confira no manual ou no site do fabricante se ela é trocável antes de comprar."
                            : "Passar para 16 GB (dois pentes de 8 GB, em dual channel) resolve engasgos de memória e deixa o PC mais fluido. É um upgrade barato e seguro.", null, null));
                else if (h.RamTotalGb > 0)
                    tips.Add(Tip(Sev.Ok, "", N0(h.RamTotalGb) + " GB de memória: bom para jogos", "", "", null, null));
            }

            // ---------------------------------------------------------------- 3. Link PCIe da placa de vídeo (medido no teste)
            TestResult gpuTest = null;
            foreach (TestResult t in tests)
                if (t.Kind == TestKind.Gpu && t.Status == "passed" && !double.IsNaN(t.Get("pcie_up_gbs")) && (gpuTest == null || t.Start > gpuTest.Start)) gpuTest = t;
            if (gpuTest != null && h != null && h.PcieMaxGen >= 3)
            {
                double lanes = gpuTest.Get("t_pcie_lanes_max"); if (double.IsNaN(lanes) || lanes <= 0) lanes = h.PcieLanes;
                double up = gpuTest.Get("pcie_up_gbs");
                if (lanes > 0 && up > 0)
                {
                    double perLane = up / lanes;   // ~0,4 GB/s por pista em PCIe 2.0, ~0,85 em 3.0, ~1,7 em 4.0
                    int gen = perLane < 0.6 ? 2 : perLane < 1.2 ? 3 : 4;
                    if (gen < h.PcieMaxGen && gen < 4)
                        tips.Add(Tip(Sev.Info, BLimit, "A placa de vídeo suporta PCIe " + h.PcieMaxGen + ".0, mas o conjunto entrega PCIe " + gen + ".0",
                            "O teste de placa mediu " + up.ToString("0.0", Pt) + " GB/s com " + N0(lanes) + " pistas (" + perLane.ToString("0.0", Pt) + " GB/s por pista, que é PCIe " + gen + ".0). Medido em " + Dt(gpuTest) + ".",
                            (fam != null && fam.Pcie == gen ? "O limite é do seu processador (" + fam.Name + "): ele só entrega PCIe " + gen + ".0 para a placa de vídeo, não é defeito. " : "O limite vem do processador ou da placa-mãe, não de defeito. ") +
                            "Testes de imprensa costumam mostrar efeito pequeno em placas de 8 pistas como esta na maioria dos jogos, e maior quando o jogo passa da memória de vídeo; varia por jogo e o DiagIT não mediu o efeito no seu PC. Não vale trocar peças só por isso; considere apenas se for trocar processador e placa-mãe por outro motivo." +
                            (fam == null ? " (Alguns processadores, como as séries com G e o Ryzen 5 5500, só têm PCIe 3.0.)" : ""), null, null));
                    else
                        tips.Add(Tip(Sev.Ok, "", "Placa de vídeo com o link PCIe no máximo do conjunto", "Banda medida: " + up.ToString("0.0", Pt) + " GB/s com " + N0(lanes) + " pistas.", "", null, null));
                }
            }

            // ---------------------------------------------------------------- 3b. Memória de vídeo pequena
            if (h != null && !double.IsNaN(h.VramMB) && h.VramMB > 0 && h.VramMB <= 4200)
            {
                // quanto da VRAM os jogos usaram de verdade (95% do tempo abaixo disso)
                List<double> vu = new List<double>();
                foreach (SessionData sd in sessions)
                    if (GameSession(sd)) foreach (Sample s in sd.S) if (!double.IsNaN(s.V[F.GpuMemPct])) vu.Add(s.V[F.GpuMemPct]);
                double p95 = double.NaN;
                if (vu.Count >= 150) { vu.Sort(); p95 = vu[(int)(vu.Count * 0.95)]; }
                string gb = (h.VramMB / 1024).ToString("0.#", Pt);
                tips.Add(Tip(Sev.Info, BWhere, "A placa de vídeo tem só " + gb + " GB de memória (VRAM)",
                    (h.GpuName.Length > 0 ? h.GpuName.Trim() + " com " + gb + " GB." : gb + " GB de memória de vídeo.") +
                    (!double.IsNaN(p95) ? " Nas suas partidas, a memória de vídeo ficou em até " + N0(p95) + "% de uso" + (p95 >= 90 ? ": está no limite." : ".") : ""),
                    "Quando a memória de vídeo acaba, o jogo engasga, as texturas demoram a aparecer e o driver pode travar. De graça: texturas no baixo ou médio e navegador e Discord fechados durante o jogo (eles também usam memória de vídeo). " +
                    (lap ? "Em notebook a placa de vídeo não se troca." : "Se quiser gastar, uma placa de vídeo com 8 GB ou mais é o upgrade que mais rende aqui; confira antes a fonte de energia."), null, null));
            }

            // ---------------------------------------------------------------- 4. Quem limita quem, medido nas partidas
            int n = 0, cpuB = 0, gpuB = 0; double gpuSum = 0;
            foreach (SessionData sd in sessions)
            {
                if (!GameSession(sd)) continue;
                foreach (Sample s in sd.S)
                {
                    double g = s.V[F.Gpu], c = s.V[F.CoreMax];
                    if (double.IsNaN(g) || double.IsNaN(c)) continue;
                    n++; gpuSum += g;
                    if (g >= 92) gpuB++; else if (g < 80 && c >= 85) cpuB++;
                }
            }
            if (n >= 300)
            {
                double cpuPct = cpuB * 100.0 / n, gpuPct = gpuB * 100.0 / n, gpuAvg = gpuSum / n;
                string basis = "Medido em " + (n * 2 / 60) + " minutos de partidas: " + N0(cpuPct) + "% do tempo o processador (um núcleo no limite) segurou a placa, " + N0(gpuPct) + "% do tempo a placa foi o limite. Uso médio da placa de vídeo: " + N0(gpuAvg) + "%.";
                if (cpuPct >= 40 && gpuPct < 30 && (cpuLimited = true))
                    tips.Add(Tip(Sev.Info, BWhere, "Nos seus jogos, o processador é o limite; a placa de vídeo está folgada",
                        basis,
                        lap ? "Mesmo assim, em notebook nem processador nem placa de vídeo costumam ser trocáveis. O que rende: aumentar resolução e qualidade gráfica nos jogos (de graça, a placa tem folga), manter o notebook na tomada e com o plano de energia de desempenho, e deixar as saídas de ar desobstruídas."
                            : "Trocar a placa de vídeo renderia pouco: ela passa a maior parte do tempo esperando o processador. Rendem mais, nesta ordem: aumentar resolução e qualidade gráfica nos jogos (de graça, a placa tem folga), memória mais rápida e, se quiser gastar, um processador mais forte. Antes de comprar um processador, confira na lista do fabricante da placa-mãe quais ele aceita.", null, null));
                else if (gpuPct >= 50)
                    tips.Add(Tip(Sev.Info, BWhere, "Nos seus jogos, a placa de vídeo é o limite",
                        basis,
                        "Baixar a qualidade gráfica ou a resolução, ou ligar o upscaling do jogo (FSR, DLSS ou XeSS), dá FPS de graça. " +
                        (lap ? "Em notebook a placa de vídeo normalmente não é trocável; manter o aparelho na tomada e com boa ventilação ajuda a placa a trabalhar no máximo."
                             : "Se quiser gastar, uma placa de vídeo mais forte é o upgrade que mais rende aqui; o processador está dando conta."), null, null));
                else
                    tips.Add(Tip(Sev.Ok, "", "Processador e placa de vídeo trabalham equilibrados nos seus jogos", basis, "", null, null));
            }

            // ---------------------------------------------------------------- 4b. Plataforma: o que o processador aceita e o que encaixa sem trocar a placa-mãe
            if (fam != null && !lap)
            {
                StringBuilder ev = new StringBuilder();
                string memSpeed = fam.MemMaxDdr4 > 0 ? "até " + fam.MemMaxMts + " MT/s em DDR5 e até " + fam.MemMaxDdr4 + " MT/s em DDR4" : "até " + fam.MemMaxMts + " MT/s";
                ev.Append("Processador detectado: " + h.CpuName.Trim() + ". Plataforma: " + fam.Name + ". Soquete " + fam.Socket + ". Memória aceita: " + fam.MemType + ", oficialmente " + memSpeed + " (com dois pentes). ");
                ev.Append("O processador entrega PCIe " + fam.Pcie + ".0 para a placa de vídeo. ");
                // de onde vem cada número: a página oficial do fabricante, ou o aviso de que ainda não foi conferido
                ev.Append(fam.Verified && fam.Source.Length > 0 ? "Fonte dos números: " + fam.Source + "." : "Atenção: estes números desta plataforma ainda não foram conferidos em fonte oficial; confirme no site do fabricante.");
                StringBuilder adv = new StringBuilder();
                if (fam.Upgrades.Length > 0)
                    adv.Append("Para ganhar desempenho SEM trocar a placa-mãe nem a memória, os processadores mais fortes que encaixam no mesmo soquete são, por exemplo: " + string.Join(", ", fam.Upgrades) + ". ");
                adv.Append(fam.Note + " Antes de comprar qualquer coisa, confira a lista de processadores suportados da sua placa-mãe e a versão da BIOS (em Hardware aparece o modelo da placa e a data da BIOS): é a lista do fabricante que manda, não esta dica.");
                if (cpuLimited) adv.Append(" Como o seu processador é o limite nos jogos (veja a dica de equilíbrio), este é o upgrade que mais rende.");
                tips.Add(Tip(Sev.Info, cpuLimited ? BWhere : BPlatform, "Sua plataforma: " + fam.Socket + " com " + fam.MemType + " (" + fam.Name + ")", ev.ToString(), adv.ToString(), null, null));
            }

            // ---------------------------------------------------------------- 5. Disco do Windows
            if (h != null && h.Volumes != null)
            {
                VolumeInfo sys = h.Volumes.Find(delegate(VolumeInfo v) { return v.Letter.StartsWith(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", StringComparison.OrdinalIgnoreCase); });
                if (sys != null && sys.Media == "HDD")
                    tips.Add(Tip(Sev.Info, BDaily, "O Windows está em um HD, não em um SSD",
                        "Unidade " + sys.Letter + " (" + N0(sys.SizeGB) + " GB) detectada como HD mecânico.",
                        "Passar o Windows e os jogos para um SSD é o upgrade que mais muda o dia a dia: o PC liga em segundos, os programas abrem na hora e os jogos carregam muito mais rápido. Um SSD de 500 GB já resolve.", null, null));
                else if (sys != null && sys.Media == "SSD")
                    tips.Add(Tip(Sev.Ok, "", "Windows em SSD", "Unidade " + sys.Letter + ".", "", null, null));
            }

            // ---------------------------------------------------------------- 6. Fonte, antes de um upgrade de placa de vídeo
            TestResult combo = null;
            foreach (TestResult t in tests)
                if (t.Kind == TestKind.Combined && t.Status == "passed" && !double.IsNaN(t.Get("t_cpu_power_max")) && !double.IsNaN(t.Get("t_gpu_power_max")) && (combo == null || t.Start > combo.Start)) combo = t;
            if (combo != null)
                tips.Add(Tip(Sev.Info, BBefore, "Se for trocar a placa de vídeo, confira antes a fonte de energia",
                    "No teste de carga máxima de " + Dt(combo) + " o processador e a placa de vídeo somaram " + N0(combo.Get("t_cpu_power_max") + combo.Get("t_gpu_power_max")) + " W.",
                    lap ? "Em notebook a fonte é o carregador original e a placa de vídeo não se troca: use sempre o carregador do próprio notebook, com a potência indicada nele."
                        : PsuAdvice(combo) + " Uma placa de vídeo nova que consuma mais de 100 W além da atual pede fonte nova junto: some o consumo dela (está no site do fabricante) com o do processador e uns 80 W do resto do PC.", null, null));

            // ---------------------------------------------------------------- 7. Por onde começar (só o que tem dado por trás, do mais barato ao mais caro)
            List<Finding> start = new List<Finding>();
            string[] order = { BDraw, BCheap, BDaily, BWhere, BLimit };
            foreach (string b in order) foreach (Finding f in tips) if (f.Badge == b && f.Sev == Sev.Info && start.Count < 4 && !f.Title.StartsWith("Sua plataforma")) start.Add(f);
            Finding head;
            if (start.Count == 0)
                head = Tip(Sev.Ok, "", "Nenhum ajuste ou upgrade se destaca: seu PC está bem equilibrado", "Tudo o que o DiagIT consegue medir (tela, memória, link da placa, disco, equilíbrio entre processador e placa) está bom para o que o conjunto entrega.", "", null, null);
            else
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < start.Count; i++) sb.Append((i + 1) + ". " + start[i].Title + (start[i].Badge.Length > 0 ? "  [" + start[i].Badge.ToLowerInvariant() + "]" : "") + "\n");
                head = Tip(Sev.Info, "POR ONDE COMEÇAR", "Por onde começar: " + start.Count + (start.Count == 1 ? " passo" : " passos") + ", do mais barato ao mais caro",
                    "Ordem sugerida a partir das medições do seu PC.", "Faça nesta ordem, parando quando estiver satisfeito:\n" + sb.ToString().TrimEnd(), null, null);
            }
            // o que tem ação vem primeiro (na ordem do mais barato ao mais caro); os "OK", que provam que está bom, vêm depois
            List<Finding> sorted = new List<Finding>();
            sorted.Add(head);
            string[] full = { BDraw, BCheap, BDaily, BWhere, BLimit, BPlatform, BBefore };
            foreach (string b in full) foreach (Finding f in tips) if (f.Sev == Sev.Info && f.Badge == b) sorted.Add(f);
            foreach (Finding f in tips) if (f.Sev == Sev.Ok) sorted.Add(f);
            tips = sorted;
            foreach (Finding f in tips) { f.Advice = Rules.Fill(f.Advice); f.Evidence = Rules.Fill(f.Evidence); f.Title = Rules.Fill(f.Title); }
            return tips;
        }
    }
}

namespace DiagIT
{
    /// <summary>Resumo dos últimos 30 dias com números de verdade: o "verde com prova" do Início.</summary>
    public class MonthSummary
    {
        public int BlueScreens, UnexpectedShutdowns, VideoSymptoms, Sessions, TestsPassed, TestsTotal;
        public double HoursPlayed;
        /// <summary>Um item por dia dos ultimos 30 dias (do mais antigo ao de hoje): horas jogadas e problemas com sintoma.</summary>
        public List<DayStat> Days = new List<DayStat>();
    }

    public class DayStat
    {
        public DateTime Day;
        public double Hours;
        public int BlueScreens, Shutdowns, VideoSymptoms;
        public int Problems { get { return BlueScreens + Shutdowns + VideoSymptoms; } }
    }

    public static partial class Analyzer
    {
        public static MonthSummary BuildMonth(List<WinEvent> events, List<SessionData> sessions, List<TestResult> tests)
        {
            MonthSummary m = new MonthSummary();
            if (events == null) events = new List<WinEvent>();
            if (sessions == null) sessions = new List<SessionData>();
            m.BlueScreens = ProblemTimes("telas-azuis", events, sessions).Count;
            m.UnexpectedShutdowns = ProblemTimes("desligamentos", events, new List<SessionData>()).Count;
            m.VideoSymptoms = GpuCrashTimes(events, true).Count;
            foreach (SessionData sd in sessions)
                if (GameSession(sd)) { m.Sessions++; m.HoursPlayed += sd.Info.Duration.TotalHours; }
            // calendario: um item por dia, do mais antigo ao de hoje
            DateTime today = DateTime.Now.Date;
            for (int i = 29; i >= 0; i--) { DayStat ds = new DayStat(); ds.Day = today.AddDays(-i); m.Days.Add(ds); }
            Action<List<DateTime>, int> mark = delegate(List<DateTime> times, int kind)
            {
                foreach (DateTime t in times)
                {
                    int idx = 29 - (int)(today - t.Date).TotalDays;
                    if (idx < 0 || idx > 29) continue;
                    if (kind == 0) m.Days[idx].BlueScreens++; else if (kind == 1) m.Days[idx].Shutdowns++; else m.Days[idx].VideoSymptoms++;
                }
            };
            mark(ProblemTimes("telas-azuis", events, sessions), 0);
            mark(ProblemTimes("desligamentos", events, new List<SessionData>()), 1);
            mark(GpuCrashTimes(events, true), 2);
            foreach (SessionData sd in sessions)
            {
                if (!GameSession(sd)) continue;
                int idx = 29 - (int)(today - sd.Info.Start.Date).TotalDays;
                if (idx >= 0 && idx <= 29) m.Days[idx].Hours += sd.Info.Duration.TotalHours;
            }
            DateTime cut = DateTime.Now.AddDays(-30);
            if (tests != null)
                foreach (TestResult t in tests)
                    if (t.Start >= cut) { m.TestsTotal++; if (t.Status == "passed") m.TestsPassed++; }
            return m;
        }
    }
}
