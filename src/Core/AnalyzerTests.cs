using System;
using System.Collections.Generic;
using System.Text;

namespace DiagIT
{
    public static partial class Analyzer
    {
        public static bool TestFor(Comp c, out TestKind k)
        {
            switch (c)
            {
                case Comp.Cpu: k = TestKind.Cpu; return true;
                case Comp.Gpu: k = TestKind.Gpu; return true;
                case Comp.Ram: k = TestKind.Ram; return true;
                case Comp.Disk: k = TestKind.Disk; return true;
                case Comp.Psu: case Comp.Cooling: case Comp.Board: k = TestKind.Combined; return true;
                default: k = TestKind.Cpu; return false;
            }
        }

        static Finding New(Sev s, string title, string ev, string adv, bool confirmed, Comp primary)
        {
            Finding f = new Finding();
            f.Sev = s; f.Source = "Teste de hardware"; f.Title = title; f.Evidence = ev; f.Advice = adv;
            f.Confirmed = confirmed; f.Primary = (int)primary;
            return f;
        }

        static string Dt(TestResult t) { return t.Start.ToString("dd/MM HH:mm", Pt); }

        // ================================================================ RESULTADOS DOS TESTES
        static void TestRules(Diagnosis d, int days)
        {
            DateTime cut = DateTime.Now.AddDays(-days);
            Dictionary<TestKind, TestResult> latest = new Dictionary<TestKind, TestResult>();
            List<TestResult> recent = new List<TestResult>();
            foreach (TestResult t in d.Tests)
            {
                if (t.Start < cut || t.Status == "running") continue;
                recent.Add(t);
                if (!latest.ContainsKey(t.Kind) && t.Status != "aborted" && t.Status != "error") latest[t.Kind] = t;
            }

            // falhas: cada teste reprovado/que derrubou o PC é evidência concreta
            HashSet<TestKind> reported = new HashSet<TestKind>();
            foreach (TestResult t in recent)
            {
                if (t.Status != "failed" && t.Status != "crashed") continue;
                if (reported.Contains(t.Kind)) continue;
                reported.Add(t.Kind);
                int failures = 0;
                foreach (TestResult x in recent) if (x.Kind == t.Kind && (x.Status == "failed" || x.Status == "crashed")) failures++;
                d.Findings.Add(FailureFinding(t, failures));
            }

            // aprovações e medições feitas sob carga controlada
            foreach (KeyValuePair<TestKind, TestResult> kv in latest)
            {
                TestResult t = kv.Value;
                if (t.Status == "passed")
                {
                    Comp c = t.Kind == TestKind.Cpu ? Comp.Cpu : t.Kind == TestKind.Gpu ? Comp.Gpu : t.Kind == TestKind.Ram ? Comp.Ram :
                        t.Kind == TestKind.Disk ? Comp.Disk : Comp.Psu;
                    Finding ok = New(Sev.Ok, "Teste de " + TestResult.KindLabel(t.Kind) + " APROVADO (" + Dt(t) + ")", t.Summary, "", false, c);
                    d.Findings.Add(ok);
                }
                LoadMeasurements(d, t);
            }
        }

        /// <summary>Potência de fonte recomendada a partir do que o próprio teste mediu (CPU + GPU + resto do PC, com folga de 50%).</summary>
        public static string PsuAdvice(TestResult t)
        {
            double cpu = t.Get("t_cpu_power_max"), gpu = t.Get("t_gpu_power_max");
            if (double.IsNaN(cpu) || double.IsNaN(gpu) || cpu + gpu < 30)
                return "Confira se a fonte tem potência e qualidade adequadas: some o consumo da placa de vídeo e do processador (está no site de cada fabricante), mais uns 80 W do resto do PC, e use uma fonte de boa marca com pelo menos 50% de folga.";
            int need = (int)(Math.Ceiling((cpu + gpu + 80) * 1.5 / 50.0) * 50);
            return "O teste mediu até " + N0(cpu + gpu) + " W só no processador e na placa de vídeo; para esse conjunto, use uma fonte de boa marca com pelo menos " + need + " W reais.";
        }

        static Finding FailureFinding(TestResult t, int count)
        {
            double cpuT = t.Get("t_cpu_temp_max"), gpuT = t.Get("t_gpu_temp_max"), hot = t.Get("t_gpu_hot_max");
            bool hotCpu = !double.IsNaN(cpuT) && cpuT >= 94;
            bool hotGpu = (!double.IsNaN(hot) && hot >= 105) || (!double.IsNaN(gpuT) && gpuT >= 95);
            string times = count > 1 ? " (" + count + " vezes nos últimos testes)" : "";
            string ev = t.Summary + (t.Details.Count > 0 ? "\n" + string.Join("\n", t.Details.GetRange(0, Math.Min(6, t.Details.Count)).ToArray()) : "");

            if (t.Get("thermal_abort") == 1 || (t.Status == "crashed" && (hotCpu || hotGpu)))
            {
                return New(Sev.Crit, "Superaquecimento comprovado no teste de " + TestResult.KindLabel(t.Kind) + times, ev,
                    "A refrigeração não segura a carga. Limpe a poeira, confira se as ventoinhas giram, reaplique pasta térmica (CPU) e melhore o fluxo de ar do gabinete; depois repita o teste.",
                    true, Comp.Cooling);
            }
            if (t.Status == "crashed")
            {
                switch (t.Kind)
                {
                    case TestKind.Combined:
                        return New(Sev.Crit, "O PC DESLIGOU/REINICIOU com CPU e GPU em carga máxima" + times, ev,
                            "Com temperaturas normais no momento da queda, isso é o comportamento típico de fonte que não entrega a potência (ou com proteção disparando). " +
                            PsuAdvice(t) + " Confira também se os cabos de energia da placa de vídeo estão firmes (um cabo por conector, sem divisor) e, se possível, teste com outra fonte.",
                            true, Comp.Psu);
                    case TestKind.Ram:
                        return New(Sev.Crit, "O PC travou/reiniciou durante o teste de memória" + times, ev,
                            "Uma queda do sistema enquanto a RAM é exercitada aponta para pente defeituoso ou instável. Teste um pente por vez e desative {perfil_memoria}.", true, Comp.Ram);
                    case TestKind.Gpu:
                        return New(Sev.Crit, "O PC travou/reiniciou durante o teste da placa de vídeo" + times, ev,
                            "Queda completa com a GPU em carga: fonte (picos de consumo da GPU), a própria GPU ou o slot. Teste com o driver reinstalado (DDU) e, se repetir, com outra fonte.",
                            true, Comp.Gpu);
                    case TestKind.Cpu:
                        return New(Sev.Crit, "O PC travou/reiniciou durante o teste do processador" + times, ev,
                            "Remova overclock e {ajustes_cpu}, carregue os padrões da BIOS e repita. Persistindo, suspeite de fonte ou dos reguladores de tensão da placa-mãe.",
                            true, Comp.Cpu);
                    default:
                        return New(Sev.Crit, "O PC travou durante o teste de armazenamento" + times, ev, "Verifique o disco e os cabos; faça backup.", true, Comp.Disk);
                }
            }
            switch (t.Kind)
            {
                case TestKind.Cpu:
                    return New(Sev.Crit, "Processador ERRA CÁLCULOS sob carga" + times, ev,
                        "CPU estável nunca erra. Carregue os padrões da BIOS (sem overclock e sem {ajustes_cpu}), atualize a BIOS e repita. Se continuar errando com tudo padrão, a CPU (ou a placa-mãe) está com defeito.",
                        true, Comp.Cpu);
                case TestKind.Ram:
                    return New(Sev.Crit, "Memória RAM com ERROS confirmados" + times, ev,
                        "Teste cada pente sozinho (e em cada slot) para achar o defeituoso; desative {perfil_memoria}. Pente com erro precisa ser trocado (geralmente coberto pela garantia).",
                        true, Comp.Ram);
                case TestKind.Gpu:
                    if (t.Get("device_removed") == 1)
                        return New(Sev.Crit, "A placa de vídeo TRAVA sob carga (o Windows precisou reiniciá-la)" + times, ev,
                            "Mesmo tipo de falha dos travamentos em jogo. 1) Reinstale o driver limpo com DDU; 2) remova overclock/undervolt no {painel_video}; 3) confira cabos de energia da GPU. " +
                            "Se travar de novo com driver limpo e temperatura normal, a placa está com defeito (acione a garantia).", true, Comp.Gpu);
                    return New(Sev.Crit, "Placa de vídeo com ERROS de cálculo/VRAM" + times, ev,
                        "Remova overclock/undervolt e repita. Erros com tudo no padrão indicam GPU ou memória de vídeo com defeito.", true, Comp.Gpu);
                case TestKind.Disk:
                    return New(Sev.Crit, "Armazenamento com problemas confirmados" + times, ev,
                        "Faça backup imediatamente. Setores ilegíveis/alertas do SMART indicam disco falhando; em SATA, troque também o cabo.", true, Comp.Disk);
                default:
                    return New(Sev.Crit, "Falha sob carga máxima (CPU + GPU)" + times, ev,
                        "Veja os detalhes: o componente que errou aparece marcado. Se as temperaturas estavam normais, a fonte é a principal suspeita.", true,
                        t.Get("device_removed") == 1 ? Comp.Gpu : Comp.Psu);
            }
        }

        /// <summary>Medições objetivas feitas durante os testes (temperatura, clock, tensão, ventoinhas, PCIe).</summary>
        static void LoadMeasurements(Diagnosis d, TestResult t)
        {
            double cpuT = t.Get("t_cpu_temp_max"), gpuT = t.Get("t_gpu_temp_max"), hot = t.Get("t_gpu_hot_max");
            double ce = t.Get("t_cpu_clock_early"), cl = t.Get("t_cpu_clock_late");
            string where = " (teste de " + TestResult.KindLabel(t.Kind) + ", " + Dt(t) + ")";

            if ((t.Kind == TestKind.Cpu || t.Kind == TestKind.Combined) && !double.IsNaN(ce) && !double.IsNaN(cl) && ce > 0 && cl / ce < 0.88 && !double.IsNaN(cpuT) && cpuT >= 85)
                d.Findings.Add(New(Sev.Warn, "CPU reduz a velocidade por temperatura sob carga" + where,
                    "Clock médio caiu de " + N0(ce) + " MHz para " + N0(cl) + " MHz enquanto a temperatura chegou a " + N0(cpuT) + " °C.",
                    "Isso derruba o FPS em jogos pesados. Melhore a refrigeração da CPU (pasta térmica, cooler, fluxo de ar).", false, Comp.Cooling).Weigh(Comp.Cooling, 0.35));
            else if (!double.IsNaN(cpuT) && cpuT >= 96 && (t.Kind == TestKind.Cpu || t.Kind == TestKind.Combined))
                // Ryzen 7000/9000 trabalham a ~95 °C por projeto e Intel recentes chegam a 100 °C: abaixo disso não é defeito. Superaquecimento de verdade
                // (o teste abortou por temperatura ou o PC caiu) continua comprovado em outro ponto.
                d.Findings.Add(New(Sev.Warn, "CPU chegou a " + N0(cpuT) + " °C" + where, "Valor medido sob carga máxima controlada.",
                    "Está no limite térmico do processador; melhore a refrigeração.", false, Comp.Cooling).Weigh(Comp.Cooling, 0.35));

            // GPU: hotspot de placas Radeon e GeForce sobe até ~110 °C por projeto
            if ((t.Kind == TestKind.Gpu || t.Kind == TestKind.Combined) && ((!double.IsNaN(hot) && hot >= 110) || (!double.IsNaN(gpuT) && gpuT >= 95)))
                d.Findings.Add(New(Sev.Warn, "GPU muito quente sob carga" + where,
                    "Núcleo máx " + N0(gpuT) + " °C, hotspot máx " + N0(hot) + " °C.", "Limpe a placa, ajuste a curva das ventoinhas e melhore o fluxo de ar.", false, Comp.Cooling).Weigh(Comp.Cooling, 0.35));

            // sensor de ventoinha sem suporte devolve sempre 0 RPM: por isso é indício (não comprovado) e só com a placa realmente quente
            double gpuFanMax = t.Get("t_gpu_fan_max");
            if ((t.Kind == TestKind.Gpu || t.Kind == TestKind.Combined) && !double.IsNaN(hot) && hot >= 95 && !double.IsNaN(gpuT) && gpuT >= 80 && !double.IsNaN(gpuFanMax) && gpuFanMax < 100)
                d.Findings.Add(New(Sev.Warn, "Ventoinhas da GPU não apareceram girando com a placa quente" + where,
                    "Hotspot chegou a " + N0(hot) + " °C e a leitura da ventoinha ficou em " + N0(gpuFanMax) + " RPM.",
                    "Pode ser ventoinha travada/defeituosa, curva zerada no {painel_video}, ou apenas um sensor de ventoinha que o app não consegue ler nesta placa. Olhe a placa durante um jogo: se as ventoinhas giram, ignore.", false, Comp.Cooling)
                    .Weigh(Comp.Cooling, 0.35).Weigh(Comp.Gpu, 0.10));

            // bomba de water cooler e hubs não usam o conector CPU_FAN: só importa se a CPU esquentou de verdade
            double cpuFanMin = t.Get("t_cpu_fan_min");
            if ((t.Kind == TestKind.Cpu || t.Kind == TestKind.Combined) && !double.IsNaN(cpuFanMin) && cpuFanMin < 150 && !double.IsNaN(cpuT) && cpuT >= 75)
            {
                bool hotCpu = cpuT >= 90;
                Finding ff = New(hotCpu ? Sev.Warn : Sev.Info, "Ventoinha da CPU parada ou desconectada durante a carga" + where,
                    "Leitura mínima de " + N0(cpuFanMin) + " RPM no conector CPU_FAN com a CPU a " + N0(cpuT) + " °C.",
                    "Confirme se o cooler está ligado no conector CPU_FAN e girando. " + (hotCpu ? "" : "Com water cooler ou cooler em outro conector, essa leitura é normal e a temperatura estava boa: pode ignorar."), false, Comp.Cooling);
                if (hotCpu) ff.Weigh(Comp.Cooling, 0.3);
                d.Findings.Add(ff);
            }

            double v12 = t.Get("t_v12_min");
            if (!double.IsNaN(v12) && (t.Kind == TestKind.Combined || t.Kind == TestKind.Gpu || t.Kind == TestKind.Cpu))
            {
                if (v12 < 11.4)
                    d.Findings.Add(New(v12 < 11.0 ? Sev.Crit : Sev.Warn, "Tensão de +12 V caiu para " + v12.ToString("0.00", Pt) + " V sob carga" + where,
                        "O padrão ATX permite no mínimo 11,40 V. Medida pelo sensor da placa-mãe (precisão de alguns %).",
                        "Indica fonte com dificuldade sob carga. Confirme com multímetro em um conector Molex/PCIe durante o teste combinado.", false, Comp.Psu)
                        .Weigh(Comp.Psu, v12 < 11.0 ? 0.6 : 0.35));
                else if (t.Kind == TestKind.Combined)
                    d.Findings.Add(New(Sev.Ok, "+12 V estável sob carga máxima: mínimo " + v12.ToString("0.00", Pt) + " V" + where, "Dentro do padrão ATX (11,40–12,60 V).", "", false, Comp.Psu));
            }

            double lanes = t.Get("t_pcie_lanes_max");
            if (d.Hw != null && !d.Hw.IsLaptop && !d.Hw.GpuIntegrated && t.Get("integrated") != 1 && d.Hw.PcieMaxLanes > 0 && !double.IsNaN(lanes) && lanes > 0 && lanes < d.Hw.PcieMaxLanes)
                // compara com o máximo da PLACA DE VÍDEO, não com o do slot: em slots ligados em x4 (segundo slot, algumas placas-mãe) isso é normal, por isso é indício
                d.Findings.Add(New(Sev.Warn, "GPU conectada com menos pistas PCIe (x" + N0(lanes) + " de x" + d.Hw.PcieMaxLanes + ")" + where, "Medido pela própria GPU sob carga.",
                    "Veja se a placa está no slot principal. Se estiver, desligue o PC, reencaixe a placa de vídeo, limpe o slot e não use riser. Persistindo, o slot da placa-mãe pode estar com defeito. (Em slots secundários ligados em x4, isso é normal.)", false, Comp.Board)
                    .Weigh(Comp.Board, 0.45));

            double up = t.Get("pcie_up_gbs");
            if (!double.IsNaN(up) && t.Kind == TestKind.Gpu)
            {
                if (up < 4.5)
                    d.Findings.Add(New(Sev.Warn, "Banda PCIe da placa de vídeo abaixo do normal: " + up.ToString("0.0", Pt) + " GB/s" + where,
                        "Medido copiando dados da RAM para a VRAM. Um link PCIe 3.0 x8 entrega ~6–7 GB/s; valores baixos indicam o slot operando em geração menor (PCIe 2.0/1.0) ou com menos pistas.",
                        "Na BIOS, deixe a velocidade do slot PCIe em Auto/Gen3 (ou maior); reencaixe a placa de vídeo e limpe o slot.", false, Comp.Board).Weigh(Comp.Board, 0.35));
                else
                    d.Findings.Add(New(Sev.Ok, "Banda PCIe normal: " + up.ToString("0.0", Pt) + " GB/s RAM→GPU" + where, "Compatível com o link esperado para a placa.", "", false, Comp.Board));
            }

            double stalls = t.Get("t_stalls");
            if (!double.IsNaN(stalls) && stalls >= 2)
                d.Findings.Add(New(Sev.Warn, "O sistema congelou " + N0(stalls) + "× durante o teste" + where,
                    "O monitor ficou mais de 5 s sem conseguir ler os sensores — o Windows inteiro parou por alguns segundos.",
                    "Congelamentos sob carga vêm de driver (vídeo/chipset/armazenamento) ou de hardware instável.", false, Comp.Software).Weigh(Comp.Software, 0.3));

            double lke = t.Get("win_lke");
            if (!double.IsNaN(lke) && lke > 0)
            {
                bool gpuTest = t.Kind == TestKind.Gpu || t.Kind == TestKind.Combined;
                Finding f = New(Sev.Warn, "REPRODUZIDO: o watchdog do kernel disparou durante o teste de " + TestResult.KindLabel(t.Kind) + where,
                    string.Join("\n", t.Details.FindAll(delegate(string x) { return x.StartsWith("LiveKernelEvent"); }).ToArray()) +
                    "\nO evento aconteceu sob carga controlada, mas o hardware não errou nenhum cálculo e não travou" + (gpuTest ? " (GPU e VRAM conferidas sem erro)" : "") + ".",
                    gpuTest ? "Como o hardware passou nas verificações e o evento aparece com a GPU em uso, o driver de vídeo é o principal suspeito: faça a instalação limpa (DDU) do driver mais recente — ou do anterior, se o atual for novo — e repita o teste de GPU. Se o evento sumir, a causa era o driver."
                            : "Atualize os drivers de chipset e de rede (Wi-Fi) e repita o teste.",
                    false, Comp.Software);
                f.Weigh(Comp.Software, 0.45).Weigh(Comp.Gpu, gpuTest ? 0.15 : 0.05);
                d.Findings.Add(f);
            }

            if (t.Get("win_whea") > 0)
                d.Findings.Add(New(Sev.Crit, "O hardware reportou erro (WHEA) durante o teste" + where, string.Join("\n", t.Details.ToArray()),
                    "Erro reportado pelo próprio processador/chipset sob carga. Volte a BIOS para o padrão e repita.", true, t.Kind == TestKind.Ram ? Comp.Ram : Comp.Cpu));
        }

        // ================================================================ FATOS DE HARDWARE
        static void FactRules(Diagnosis d)
        {
            HwSnapshot h = d.Hw;
            if (h == null) return;

            bool anyRead = false, anyBad = false;
            foreach (DriveHealth dr in h.Drives)
            {
                if (!dr.Read) continue;
                anyRead = true;
                List<string> p = dr.Problems();
                if (p.Count == 0) continue;
                anyBad = true;
                bool hard = dr.CriticalWarning > 0 || (!double.IsNaN(dr.MediaErrors) && dr.MediaErrors > 0) || (!double.IsNaN(dr.Pending) && dr.Pending > 0) ||
                            (!double.IsNaN(dr.Uncorrectable) && dr.Uncorrectable > 0) || dr.Failing.Count > 0 || (!double.IsNaN(dr.Reallocated) && dr.Reallocated > 0);
                bool cableOnly = !hard && !double.IsNaN(dr.CrcErrors) && dr.CrcErrors > 0;
                Finding f = new Finding();
                f.Sev = hard ? Sev.Crit : Sev.Warn;
                f.Source = "Saúde lida do próprio disco";
                f.Title = (cableOnly ? "Erros de comunicação no cabo/porta do disco " : "Disco com problemas registrados por ele mesmo: ") + dr.Model;
                f.Evidence = string.Join(".\n", p.ToArray()) + ".";
                f.Advice = cableOnly ? "Troque o cabo SATA e use outra porta da placa-mãe." : "Faça backup agora e planeje a troca do disco.";
                f.Confirmed = hard || cableOnly;
                f.Primary = cableOnly ? (int)Comp.Board : (int)Comp.Disk;
                f.Weigh(Comp.Disk, hard ? 0.8 : 0.3);
                d.Findings.Add(f);
            }
            if (anyRead && !anyBad)
            {
                List<string> names = new List<string>();
                foreach (DriveHealth dr in h.Drives)
                    if (dr.Read) names.Add(dr.Model + (double.IsNaN(dr.UsedPct) ? "" : " (desgaste " + N0(dr.UsedPct) + "%)"));
                Finding f = new Finding();
                f.Sev = Sev.Ok; f.Source = "Saúde lida do próprio disco"; f.Primary = (int)Comp.Disk;
                f.Title = "Discos saudáveis segundo os próprios dados de saúde (SMART/NVMe)";
                f.Evidence = string.Join("; ", names.ToArray()) + ". Sem setores defeituosos, erros de mídia ou alertas.";
                d.Findings.Add(f);
            }

            // telas azuis registradas em arquivo (prova de que ocorreram, com o código exato)
            Dictionary<uint, List<CrashDump>> byCode = new Dictionary<uint, List<CrashDump>>();
            foreach (CrashDump cd in h.Dumps)
            {
                if (cd.Live || cd.Time < DateTime.Now.AddDays(-60)) continue;
                List<CrashDump> l;
                if (!byCode.TryGetValue(cd.Code, out l)) { l = new List<CrashDump>(); byCode[cd.Code] = l; }
                l.Add(cd);
            }
            foreach (KeyValuePair<uint, List<CrashDump>> kv in byCode)
            {
                int code = (int)(kv.Key & 0xFFFF);
                string name, advice; Dictionary<Comp, double> w;
                Bugcheck(code, out name, out advice, out w);
                List<string> lines = new List<string>();
                DateTime last = DateTime.MinValue;
                foreach (CrashDump cd in kv.Value)
                {
                    lines.Add("  • " + cd.Time.ToString("dd/MM/yyyy HH:mm", Pt) + " — parâmetros 0x" + cd.P[0].ToString("X") + ", 0x" + cd.P[1].ToString("X") +
                        (cd.Culprit != null ? " — culpado: " + cd.Culprit + " (" + cd.CulpritHow + ")" : "") +
                        (cd.StackModules.Count > 0 ? "\n    pilha: " + string.Join(" → ", cd.StackModules.ToArray()) : ""));
                    if (cd.Time > last) last = cd.Time;
                }
                // a mesma tela azul já apareceu no registro do Windows: só acrescenta a prova do arquivo, sem somar o peso de novo
                Finding ev = d.Findings.Find(delegate(Finding x) { return x.BugCode == code; });
                if (ev != null)
                {
                    ev.Source = "Eventos do Windows + arquivos de tela azul";
                    ev.Evidence += "\nConfirmado nos arquivos de despejo (Minidump):\n" + string.Join("\n", lines.ToArray());
                    if (last > ev.Last) ev.Last = last;
                    if (ev.Culprit == null)
                    {
                        List<string> fromDumps = new List<string>();
                        foreach (CrashDump cd in kv.Value) if (cd.Culprit != null) fromDumps.Add(cd.Culprit);
                        ApplyCulprit(ev, fromDumps, Math.Max(ev.Count, kv.Value.Count), Math.Min(1, (ev.Count + 1) / 4.0), "arquivo da tela azul");
                    }
                    continue;
                }
                double s = Math.Min(1, (kv.Value.Count + 1) / 4.0);
                Finding f = new Finding();
                f.Sev = kv.Value.Count >= 2 ? Sev.Crit : Sev.Warn; f.Source = "Arquivos de tela azul (Minidump)";
                f.Title = "Tela azul 0x" + kv.Key.ToString("X") + " (" + name + ") registrada " + kv.Value.Count + "×";
                f.Evidence = "Lido diretamente dos arquivos de despejo do Windows.\n" + string.Join("\n", lines.ToArray());
                f.Advice = advice;
                f.Last = last; f.BugCode = code; f.Count = kv.Value.Count;
                foreach (KeyValuePair<Comp, double> c in w) f.Weigh(c.Key, c.Value * s);
                List<string> culprits = new List<string>();
                foreach (CrashDump cd in kv.Value) if (cd.Culprit != null) culprits.Add(cd.Culprit);
                ApplyCulprit(f, culprits, kv.Value.Count, s, "arquivo da tela azul");
                d.Findings.Add(f);
            }
            if (!h.CrashDumpEnabled)
            {
                Finding f = new Finding();
                f.Sev = Sev.Info; f.Source = "Configuração do Windows";
                f.Title = "O Windows não está guardando o motivo das telas azuis";
                f.Evidence = "O registro de despejo de memória (minidump) está desativado.";
                f.Advice = "Ative em Resolver para que a próxima tela azul deixe o código exato do problema.";
                d.Findings.Add(f);
            }

            // leitura de agora, com a placa possivelmente em repouso (ela pode baixar o link para economizar): é indício; a prova vem do teste de placa de vídeo, sob carga.
            // Em notebook o número de pistas é escolha do fabricante.
            if (!h.IsLaptop && !h.GpuIntegrated && h.PcieLanes > 0 && h.PcieMaxLanes > 0 && h.PcieLanes < h.PcieMaxLanes)
            {
                Finding f = new Finding();
                f.Sev = Sev.Warn; f.Source = "Medição da placa de vídeo"; f.Primary = (int)Comp.Board;
                f.Title = "Placa de vídeo usando x" + h.PcieLanes + " de x" + h.PcieMaxLanes + " pistas PCIe";
                f.Evidence = "O link entre a placa de vídeo e o slot estava reduzido no momento da leitura.";
                f.Advice = "Rode o teste de placa de vídeo: ele mede as pistas com a placa em carga. Se continuar reduzido, reencaixe a placa, limpe o slot e evite risers. (Em slots secundários ligados em x4, isso é normal.)";
                f.Weigh(Comp.Board, 0.3).Weigh(Comp.Gpu, 0.1);
                d.Findings.Add(f);
            }
            // ---------- dispositivos sem driver / com falha
            List<ProblemDevice> pd = h.Problems.FindAll(delegate(ProblemDevice x) { return x.Code != 22; });
            if (pd.Count > 0)
            {
                Finding f = new Finding();
                bool benign = pd.TrueForAll(delegate(ProblemDevice x) { return HwFacts.IsBenignDevice(x); });
                f.Sev = benign ? Sev.Info : Sev.Warn; f.Source = "Gerenciador de dispositivos";
                f.Title = pd.Count + " dispositivo(s) com problema ou sem driver";
                List<string> l = new List<string>();
                foreach (ProblemDevice x in pd) l.Add("• " + x.Name + " — " + x.Meaning + " (código " + x.Code + ")");
                f.Evidence = string.Join("\n", l.ToArray());
                f.Advice = HwFacts.DeviceHint(pd[0]) + (pd.Count > 1 ? " Para os demais, veja Avançado → Hardware." : "");
                bool critical = pd.Exists(delegate(ProblemDevice x) { return x.Class == "Display" || x.Class == "System" || x.Class == "SCSIAdapter" || x.Class == "HDC"; });
                if (!benign) f.Weigh(Comp.Software, critical ? 0.35 : 0.1);   // só informa; não faz "Drivers e Windows" virar suspeito
                d.Findings.Add(f);
            }

            // ---------- instalação do Windows e inicialização
            if (h.Boot != null && h.Boot.WindowsDisk >= 0 && h.Boot.EfiDisks.Count > 0 && !h.Boot.EfiDisks.Contains(h.Boot.WindowsDisk))
            {
                Finding f = new Finding();
                f.Sev = Sev.Info; f.Source = "Configuração de inicialização";
                f.Title = "O Windows (disco " + h.Boot.WindowsDisk + ") depende do disco " + h.Boot.EfiDisks[0] + " para dar a partida";
                f.Evidence = "A partição de boot (EFI) ficou no outro disco, normalmente porque ele estava conectado durante a instalação do Windows.";
                f.Advice = "Não remova nem formate o disco " + h.Boot.EfiDisks[0] + ". Na próxima instalação limpa, deixe apenas o disco de destino conectado (ou apague todas as partições dele) para o Windows ficar independente.";
                d.Findings.Add(f);
            }
            if (h.Boot != null && h.Boot.Read && h.Boot.Stale().Count > 0)
            {
                List<string> n = new List<string>();
                bool inMenu = false;
                foreach (BootEntry e in h.Boot.Stale())
                {
                    bool m = h.Boot.DisplayOrder.Contains(e.Id);
                    inMenu |= m;
                    n.Add("• \"" + e.Description + "\" apontando para " + e.OsDevice.Replace("partition=", "") + (BootInfo.Broken(e) ? " — esse Windows não existe mais" : " — duplicada") +
                        (m ? " — APARECE NO MENU DE INICIALIZAÇÃO" : ""));
                }
                Finding f = new Finding();
                f.Sev = inMenu ? Sev.Warn : Sev.Info; f.Source = "Configuração de inicialização";
                f.Title = inMenu ? "O menu de inicialização oferece um Windows que não existe mais" : "Restos de instalações anteriores do Windows no gerenciador de boot (" + h.Boot.Stale().Count + ")";
                f.Evidence = string.Join("\n", n.ToArray()) + (inMenu ? "\nPor isso o PC pergunta qual Windows iniciar: a entrada antiga não funciona se escolhida." : "\nNão aparecem no menu, mas são restos da instalação anterior.");
                f.Advice = "Inofensivo. Para deixar só o Windows atual, use Resolver → Limpar entradas antigas do boot (faz backup antes).";
                d.Findings.Add(f);
            }
            if (h.WindowsOld != null)
            {
                Finding f = new Finding();
                f.Sev = Sev.Info; f.Source = "Instalação do Windows";
                f.Title = "A instalação anterior do Windows ficou guardada em " + h.WindowsOld;
                f.Evidence = "Criada em " + h.WindowsOldCreated.ToString("dd/MM/yyyy HH:mm", Pt) + ": o Windows foi reinstalado sem formatar a partição. Drivers e programas antigos não estão ativos, mas ocupam espaço.";
                f.Advice = "Copie o que precisar de Windows.old\\Users e depois remova pela página Resolver (Limpeza de Disco → Instalações anteriores do Windows).";
                d.Findings.Add(f);
            }
            foreach (VolumeInfo v in h.Volumes)
            {
                if (v.Health != "saudável" && v.Health != "desconhecido")
                {
                    Finding f = new Finding();
                    f.Sev = Sev.Crit; f.Source = "Volumes"; f.Confirmed = true; f.Primary = (int)Comp.Disk;
                    f.Title = "Volume " + v.Letter + ": com sistema de arquivos em estado " + v.Health;
                    f.Evidence = "O Windows detectou problemas na estrutura do volume.";
                    f.Advice = "Rode Resolver → Verificar disco e faça backup.";
                    f.Weigh(Comp.Disk, 0.5);
                    d.Findings.Add(f);
                }
                else if (v.SizeGB > 0 && (v.FreeGB / v.SizeGB < 0.10 || v.FreeGB < 15))
                {
                    Finding f = new Finding();
                    f.Sev = Sev.Warn; f.Source = "Volumes";
                    f.Title = "Pouco espaço livre em " + v.Letter + ": (" + v.FreeGB.ToString("0", Pt) + " GB)";
                    f.Evidence = "SSD cheio fica lento e impede o arquivo de paginação de crescer.";
                    f.Advice = "Libere espaço (Limpeza de Disco, desinstalar jogos não usados).";
                    f.Weigh(Comp.Software, 0.1).Weigh(Comp.Disk, 0.05);
                    d.Findings.Add(f);
                }
            }

            if (h.MixedRam)
            {
                Finding f = new Finding();
                f.Sev = Sev.Warn; f.Source = "Configuração de hardware";
                f.Title = "Pentes de memória de modelos diferentes";
                f.Evidence = "Kits misturados podem não funcionar juntos nas mesmas temporizações.";
                f.Advice = "Rode o teste de RAM; se houver erros, teste um pente por vez.";
                f.Weigh(Comp.Ram, 0.2);
                d.Findings.Add(f);
            }
            if (h.RamModules > 0 && !h.DualChannel)
            {
                Finding f = new Finding();
                f.Sev = Sev.Info; f.Source = "Configuração de hardware";
                f.Title = "Memória em single channel";
                f.Evidence = "Só um canal de memória em uso.";
                f.Advice = "Não é defeito, mas reduz muito o desempenho em jogos com Ryzen. Use dois pentes nos slots indicados no manual.";
                d.Findings.Add(f);
            }
        }

        // ================================================================ ESTADO POR COMPONENTE + VEREDITO
        static void BuildStates(Diagnosis d)
        {
            List<Finding> cleared = new List<Finding>();   // indícios que os testes de hardware não reproduziram
            for (int i = 0; i < 8; i++)
            {
                Comp c = (Comp)i;
                CompState s = new CompState();
                TestKind k;
                s.HasTest = TestFor(c, out k);
                s.Test = k;
                d.States[i] = s;

                Finding conf = null, ok = null, top = null;
                double topW = 0;
                foreach (Finding f in d.Findings)
                {
                    if (f.Confirmed && f.Primary == i && conf == null) conf = f;
                    if (f.Sev == Sev.Ok && f.Primary == i && ok == null) ok = f;
                    double w;
                    if (!f.Confirmed && f.Sev != Sev.Ok && f.Sev != Sev.Info && f.W.TryGetValue(c, out w) && w > topW) { topW = w; top = f; }
                }
                // refrigeração "OK" quando os testes de carga passaram sem temperatura crítica
                if (c == Comp.Cooling && ok == null)
                    foreach (TestResult t in d.Tests)
                        if (t.Status == "passed" && (t.Kind == TestKind.Cpu || t.Kind == TestKind.Gpu || t.Kind == TestKind.Combined) && t.Start > DateTime.Now.AddDays(-30))
                        {
                            double ct = t.Get("t_cpu_temp_max"), gh = t.Get("t_gpu_hot_max");
                            if ((double.IsNaN(ct) || ct < 90) && (double.IsNaN(gh) || gh < 100) && (!double.IsNaN(ct) || !double.IsNaN(gh)))
                            {
                                ok = new Finding();
                                ok.Title = "Temperaturas normais sob carga máxima (" + t.Start.ToString("dd/MM", Pt) + ")";
                                ok.Evidence = "CPU máx " + (double.IsNaN(ct) ? "—" : N0(ct) + " °C") + ", GPU hotspot máx " + (double.IsNaN(gh) ? "—" : N0(gh) + " °C") + ".";
                                break;
                            }
                        }

                // último teste aprovado deste componente e o indício mais recente que pesa contra ele
                if (s.HasTest)
                    foreach (TestResult t in d.Tests)
                        if (t.Kind == k && t.Status == "passed" && t.Start > DateTime.Now.AddDays(-Math.Max(d.Days, 30)) && (s.Passed == null || t.Start > s.Passed.Start)) s.Passed = t;
                DateTime lastEv = DateTime.MinValue; bool undated = false;
                foreach (Finding f in d.Findings)
                {
                    double w;
                    if (f.Confirmed || f.Sev == Sev.Ok || !f.W.TryGetValue(c, out w) || w < 0.05) continue;
                    if (f.Last == DateTime.MinValue) undated = true;
                    else if (f.Last > lastEv) lastEv = f.Last;
                }
                bool suspicious = d.Score[i] >= 20 && top != null && topW >= 0.15;   // várias pistas fracas não bastam: precisa de pelo menos uma evidência de peso
                // o teste aprovado veio depois de todos os indícios datados: sob carga controlada o problema não se reproduziu
                bool clearedByTest = ok != null && s.Passed != null && suspicious && !undated && s.Passed.Start > lastEv;
                s.Retest = suspicious && s.Passed != null && lastEv > s.Passed.Start;

                if (conf != null)
                {
                    s.Status = CompStatus.Failed;
                    s.Headline = conf.Title;
                    s.Detail = conf.Advice;
                    s.Cause = conf;
                }
                else if (ok != null && (clearedByTest || (d.Score[i] < 45 && !s.Retest)))
                {
                    s.Status = CompStatus.Ok;
                    s.Basis = ok;
                    s.Headline = ok.Title;
                    s.Detail = ok.Evidence + (suspicious ? " Indício anterior (" + top.Title + ") não se reproduziu no teste." : "");
                    if (suspicious) { s.Cause = top; cleared.Add(top); }
                }
                else if (suspicious)
                {
                    s.Status = CompStatus.Suspect;
                    s.Headline = top.Title;
                    s.Cause = top;
                    if (s.Retest)
                        s.Detail = "O teste de " + TestResult.KindLabel(k) + " passou em " + Dt(s.Passed) + ", mas o problema voltou a acontecer depois (" +
                            lastEv.ToString("dd/MM HH:mm", Pt) + "). Repita o teste para ver se agora reproduz; se passar de novo, siga a recomendação do achado.";
                    else if (s.Passed != null)
                        s.Detail = "O teste de " + TestResult.KindLabel(k) + " já passou (" + Dt(s.Passed) + ") e não reproduziu o problema. Próximo passo: " + top.Advice;
                    else
                        s.Detail = s.HasTest ? "Para confirmar ou descartar: rode o teste de " + TestResult.KindLabel(k) + "." : "Sem teste automático para este item; siga a recomendação do achado.";
                }
                else
                {
                    s.Status = CompStatus.Untested;
                    s.Headline = "Nenhum problema observado";
                    s.Detail = s.HasTest ? "Ainda não testado sob carga. Teste recomendado: " + TestResult.KindLabel(k) + "." : "Sem sinais de problema nos registros do Windows.";
                }
            }

            // Telas azuis/travamentos que nenhum teste de hardware reproduziu continuam sem explicação: com as peças
            // aprovadas, a causa mais provável passa a ser driver, BIOS ou ajustes (EXPO/PBO/undervolt), que o DiagIT não testa.
            CompState sw = d.States[(int)Comp.Software];
            Finding unexplained = cleared.Find(delegate(Finding f) { return f.BugCode >= 0; }) ?? cleared.Find(delegate(Finding f) { return f.Symptom; });   // só sintoma real passa a suspeita para drivers; indício fraco que o teste descartou fica descartado
            if (unexplained != null && sw.Status == CompStatus.Untested)
            {
                sw.Status = CompStatus.Suspect;
                sw.Cause = unexplained;
                sw.Headline = "Sem defeito de hardware nos testes: " + unexplained.Title;
                sw.Detail = "As peças apontadas passaram nos testes, então o próximo suspeito é driver, BIOS ou ajuste instável. " +
                    "Volte a BIOS para o padrão (sem {perfil_memoria}, {ajustes_cpu} ou undervolt) por alguns dias; se parar, reative um ajuste por vez. " +
                    "Reinstale o driver de vídeo limpo (DDU), atualize chipset e BIOS. " + unexplained.Advice;
            }

            // veredito concreto
            List<string> failed = new List<string>(), suspect = new List<string>(), okList = new List<string>();
            for (int i = 0; i < 8; i++)
            {
                CompState s = d.States[i];
                if (s.Status == CompStatus.Failed) failed.Add(CompInfo.Names[i] + ": " + s.Headline);
                else if (s.Status == CompStatus.Suspect) suspect.Add(CompInfo.Names[i] + (s.HasTest && s.Passed == null ? " → teste de " + TestResult.KindLabel(s.Test) :
                    s.Retest ? " → repetir o teste de " + TestResult.KindLabel(s.Test) : s.Cause != null && s.Cause.BugCode >= 0 && i == (int)Comp.Software ? " → drivers/BIOS" : ""));
                else if (s.Status == CompStatus.Ok) okList.Add(CompInfo.Names[i]);
            }
            StringBuilder v = new StringBuilder();
            if (failed.Count > 0)
            {
                v.Append("PROBLEMA CONFIRMADO:\n• " + string.Join("\n• ", failed.ToArray()));
                if (suspect.Count > 0) v.Append("\n\nAinda a verificar: " + string.Join(", ", suspect.ToArray()) + ".");
            }
            else if (suspect.Count > 0)
            {
                v.Append("Nenhum defeito confirmado até agora.\nSuspeitas a verificar: " + string.Join("; ", suspect.ToArray()) + ".");
            }
            else if (okList.Count >= 4)
            {
                v.Append("Nenhum defeito encontrado: " + string.Join(", ", okList.ToArray()) + " passaram nos testes.");
            }
            else
            {
                v.Append("Nenhum problema encontrado nos registros. Para confirmar a saúde de cada peça, rode os testes em Verificar peças (feche os jogos antes).");
            }
            if (okList.Count > 0 && (failed.Count > 0 || suspect.Count > 0)) v.Append("\nAprovados nos testes: " + string.Join(", ", okList.ToArray()) + ".");
            d.Verdict = v.ToString();
        }
    }
}
