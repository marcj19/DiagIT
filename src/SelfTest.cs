using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace DiagIT
{
    /// <summary>Teste de integração do núcleo (sem interface). Uso: DiagIT.exe --selftest saida.txt</summary>
    public static class SelfTest
    {
        static StreamWriter o;
        static int fails;

        static void Say(string s) { o.WriteLine(s); o.Flush(); }
        static void Check(bool ok, string what) { Say((ok ? "[OK]   " : "[FALHA] ") + what); if (!ok) fails++; }

        /// <summary>Relatório (e opcionalmente o pacote .zip) com os dados reais deste PC, sem abrir a interface.</summary>
        public static int ReportReal(string htmlPath, string zipPath)
        {
            Paths.Ensure();
            List<WinEvent> ev = EventScanner.ScanAll(30, null);
            HwSnapshot hw = HwFacts.Collect(null);
            List<SessionData> sds = new List<SessionData>();
            foreach (SessionInfo si in SessionStore.ListAll())
                if (si.Start > DateTime.Now.AddDays(-30)) { SessionData sd = new SessionData(); sd.Info = si; sd.S = SessionStore.LoadSamples(si); sds.Add(sd); }
            Diagnosis d = Analyzer.Run(sds, ev, DiskInfo.Read(), 30, TestStore.ListAll(), hw, FixLog.Load());
            ReportExtras x = ReportExtras.Collect(ev);
            string html = Report.Build(d, MachineInfo.Read(double.NaN), sds, x);
            File.WriteAllText(htmlPath, html, new UTF8Encoding(true));
            if (zipPath != null) TechPackage.Build(zipPath, html, d, x, sds);
            return 0;
        }

        /// <summary>Gera um relatório HTML com dados sintéticos para conferir a exportação.</summary>
        public static int ReportTest(string htmlPath)
        {
            Paths.Ensure();
            List<SessionData> list = new List<SessionData>();
            SessionData sd = new SessionData();
            sd.Info = new SessionInfo(); sd.Info.Id = "t"; sd.Info.Game = "jogo_teste.exe"; sd.Info.Start = DateTime.Now.AddMinutes(-30); sd.Info.End = DateTime.Now; sd.Info.Status = "interrupted";
            for (int i = 0; i < 400; i++)
            {
                Sample x = new Sample(); x.Time = sd.Info.Start.AddSeconds(i * 4);
                x.V[F.Cpu] = 70 + (i % 20); x.V[F.Gpu] = 98; x.V[F.Ram] = 66; x.V[F.CpuTemp] = 60 + i * 0.08; x.V[F.GpuTemp] = 65 + i * 0.05; x.V[F.GpuHot] = 80 + i * 0.07; x.V[F.Gap] = 4;
                sd.S.Add(x);
            }
            list.Add(sd);
            Diagnosis d = Analyzer.Run(list, new List<WinEvent>(), new List<DiskHealth>(), 30);
            File.WriteAllText(htmlPath, Report.Build(d, MachineInfo.Read(8176), list), new UTF8Encoding(true));
            return 0;
        }

        /// <summary>Cenários de diagnóstico sintéticos: tela azul x teste aprovado (ordem no tempo, sem contar a mesma falha duas vezes).</summary>
        public static int DiagScenarios(string outFile)
        {
            o = new StreamWriter(outFile, false, new UTF8Encoding(false));
            try
            {
                Paths.Ensure();
                Annotations.UseMemory(null);   // o autoteste nunca lê nem grava as anotações reais do usuário
                // 2 telas azuis 0x1A (MEMORY_MANAGEMENT) há 5 dias, também gravadas em Minidump
                Func<int, List<WinEvent>> bsods = delegate(int daysAgo)
                {
                    List<WinEvent> l = new List<WinEvent>();
                    for (int k = 0; k < 2; k++)
                    {
                        WinEvent e = new WinEvent();
                        e.Provider = "Microsoft-Windows-WER-SystemErrorReporting"; e.Id = 1001; e.Time = DateTime.Now.AddDays(-daysAgo).AddHours(-k * 3);
                        e.P = new string[] { "0x0000001a" };
                        l.Add(e);
                    }
                    return l;
                };
                Func<int, HwSnapshot> dumps = delegate(int daysAgo)
                {
                    HwSnapshot h = new HwSnapshot();
                    h.CrashDumpEnabled = true;
                    for (int k = 0; k < 2; k++)
                    {
                        CrashDump cd = new CrashDump(); cd.Code = 0x1A; cd.Time = DateTime.Now.AddDays(-daysAgo).AddHours(-k * 3); cd.P = new ulong[] { 0x41793, 0, 0, 0 };
                        h.Dumps.Add(cd);
                    }
                    return h;
                };
                Func<int, List<TestResult>> ramPass = delegate(int daysAgo)
                {
                    TestResult t = new TestResult(); t.Id = "x"; t.Kind = TestKind.Ram; t.Status = "passed"; t.Start = DateTime.Now.AddDays(-daysAgo); t.Summary = "Sem erros.";
                    return new List<TestResult> { t };
                };
                int ram = (int)Comp.Ram, sw = (int)Comp.Software;

                Say("== 1. Tela azul há 5 dias, teste de RAM aprovado há 1 dia");
                Diagnosis d = Analyzer.Run(new List<SessionData>(), bsods(5), new List<DiskHealth>(), 30, ramPass(1), dumps(5));
                Say("  RAM: " + d.States[ram].Status + " — " + d.States[ram].Headline + " | " + d.States[ram].Detail);
                Say("  Software: " + d.States[sw].Status + " — " + d.States[sw].Headline);
                Check(d.Findings.FindAll(delegate(Finding f) { return f.BugCode == 0x1A; }).Count == 1, "registro do Windows + Minidump viram UM achado (sem peso em dobro)");
                Check(d.States[ram].Status == CompStatus.Ok, "RAM fica OK: o teste depois da tela azul não reproduziu o problema");
                Check(d.States[sw].Status == CompStatus.Suspect && d.States[sw].Cause != null && d.States[sw].Cause.BugCode == 0x1A, "a tela azul sem explicação passa para drivers/BIOS");
                Finding fb1 = d.Findings.Find(delegate(Finding f) { return f.BugCode == 0x1A; });
                string vt, vsub;
                Analyzer.PlainVerdict(d, out vt, out vsub);
                Say("  Simples: " + (fb1 == null ? "-" : fb1.Plain) + " | " + vt + " | " + vsub);
                Check(fb1 != null && fb1.Plain == "O PC teve 2 telas azuis ligadas à memória", "achado em linguagem simples (camada para leigos)");
                Check(vt == "Seu PC teve 2 telas azuis, mas as peças testadas estão boas" && vsub.Contains("um driver ou um ajuste da BIOS"), "veredito simples aponta driver/BIOS");
                Check(Analyzer.PlainNote(d.States[ram], ram).StartsWith("Passou no teste em"), "nota simples da RAM: passou no teste");

                Say("== 2. Teste de RAM aprovado há 5 dias, tela azul há 1 dia");
                d = Analyzer.Run(new List<SessionData>(), bsods(1), new List<DiskHealth>(), 30, ramPass(5), dumps(1));
                Say("  RAM: " + d.States[ram].Status + " — " + d.States[ram].Detail);
                Check(d.States[ram].Status == CompStatus.Suspect && d.States[ram].Retest, "RAM suspeita e pede para REPETIR o teste (problema depois da aprovação)");

                Say("== 3. Tela azul sem nenhum teste");
                d = Analyzer.Run(new List<SessionData>(), bsods(1), new List<DiskHealth>(), 30, new List<TestResult>(), dumps(1));
                Say("  RAM: " + d.States[ram].Status + " — " + d.States[ram].Detail);
                Check(d.States[ram].Status == CompStatus.Suspect && d.States[ram].Passed == null, "RAM suspeita e indica rodar o teste");

                // ---- itens do guia da comunidade
                Func<int, WinEvent> tdr = delegate(int daysAgo)
                {
                    WinEvent e = new WinEvent(); e.Provider = "Display"; e.Id = 4101; e.Time = DateTime.Now.AddDays(-daysAgo); return e;
                };
                Func<List<WinEvent>, List<WinEvent>> sorted = delegate(List<WinEvent> l) { l.Sort(delegate(WinEvent a, WinEvent b) { return b.Time.CompareTo(a.Time); }); return l; };
                Predicate<Finding> wuF = delegate(Finding f) { return f.Title.Contains("Windows Update"); };

                Say("== 4. Windows Update instalou o driver de vídeo em uso e os travamentos começaram depois");
                WinEvent wu = new WinEvent(); wu.Provider = "Microsoft-Windows-WindowsUpdateClient"; wu.Id = 19; wu.Time = DateTime.Now.AddDays(-10);
                wu.P = new string[] { "Advanced Micro Devices, Inc. - Display - 31.0.21921.1000" };
                HwSnapshot hw = new HwSnapshot(); hw.GpuName = "AMD Radeon RX 7600"; hw.GpuDriver = "31.0.21921.1000";
                d = Analyzer.Run(new List<SessionData>(), sorted(new List<WinEvent> { wu, tdr(2), tdr(4), tdr(6) }), new List<DiskHealth>(), 30, new List<TestResult>(), hw);
                Finding fw = d.Findings.Find(wuF);
                Say("  " + (fw == null ? "(nenhum achado)" : fw.Title));
                Check(fw != null && fw.Sev == Sev.Warn && fw.Title.StartsWith("Os travamentos de vídeo começaram depois"), "liga os travamentos à troca de driver pelo Windows Update");
                hw.GpuDriver = "32.0.11000.1";   // usuário já instalou outro driver depois
                d = Analyzer.Run(new List<SessionData>(), sorted(new List<WinEvent> { wu, tdr(2) }), new List<DiskHealth>(), 30, new List<TestResult>(), hw);
                Check(d.Findings.Find(wuF) == null, "não acusa quando o driver do Windows Update já não está em uso");

                Say("== 4b. Troca de driver (DDU + instalador) não conta como GPU travando");
                Func<DateTime, WinEvent> lke193 = delegate(DateTime t)
                {
                    WinEvent e = new WinEvent(); e.Provider = "Windows Error Reporting"; e.Id = 1001; e.Time = t;
                    e.P = new string[] { "", "", "LiveKernelEvent", "", "", "193" }; return e;
                };
                WinEvent msi = new WinEvent(); msi.Provider = "MsiInstaller"; msi.Id = 1033; msi.Time = DateTime.Now.AddHours(-3);
                msi.Msg = "O Windows Installer instalou o produto. Nome do Produto: AMD Software.";
                List<WinEvent> swap = new List<WinEvent> { msi, lke193(msi.Time.AddMinutes(-12)), lke193(msi.Time.AddMinutes(-9)), lke193(msi.Time.AddMinutes(-2)) };
                d = Analyzer.Run(new List<SessionData>(), sorted(swap), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Check(d.Findings.Find(delegate(Finding f) { return f.Title.StartsWith("Travamentos de GPU registrados"); }) == null, "travamentos durante a troca do driver não viram achado");
                swap.Add(lke193(DateTime.Now.AddHours(-30)));
                d = Analyzer.Run(new List<SessionData>(), sorted(swap), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Finding fs = d.Findings.Find(delegate(Finding f) { return f.Title.StartsWith("Travamentos de GPU registrados"); });
                Say("  " + (fs == null ? "(nenhum)" : fs.Title + " | " + fs.Evidence.Replace("\n", " / ")));
                Check(fs != null && fs.Title.EndsWith("1×") && fs.Evidence.Contains("durante a instalação"), "só conta o travamento fora da troca de driver e explica os que ignorou");

                Say("== 4d. Driver de vídeo trocado: o que veio antes não pesa, e o veredito diz que está acompanhando");
                FixEntry swapFix = new FixEntry(); swapFix.Id = "t1"; swapFix.Kind = "guia:driver-video-limpo"; swapFix.Scope = "video"; swapFix.Title = "Reinstalei o driver de vídeo do jeito certo";
                swapFix.When = DateTime.Now.AddHours(-2);
                List<WinEvent> before = sorted(new List<WinEvent> { lke193(DateTime.Now.AddHours(-30)), lke193(DateTime.Now.AddHours(-26)), lke193(DateTime.Now.AddHours(-20)) });
                Func<Diagnosis, Finding> gpuF = delegate(Diagnosis dd) { return dd.Findings.Find(delegate(Finding f) { return f.Title.StartsWith("Travamentos de GPU registrados"); }); };
                d = Analyzer.Run(new List<SessionData>(), before, new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                double wBefore = gpuF(d).W[Comp.Gpu];
                d = Analyzer.Run(new List<SessionData>(), before, new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot(), new List<FixEntry> { swapFix });
                string swapT, swapS; Analyzer.PlainVerdict(d, out swapT, out swapS);
                Say("  " + swapT + " | " + swapS);
                Check(wBefore > 0 && gpuF(d) == null, "troca de driver sem nada novo: os resets de antes da troca ficam no histórico e não entram na conta (o achado some da lista atual)");
                HwSnapshot hwCooler = new HwSnapshot();
                ProblemDevice cooler = new ProblemDevice(); cooler.Name = "Corsair H100i Platinum"; cooler.Class = "USB"; cooler.Code = 28; hwCooler.Problems.Add(cooler);
                Diagnosis dCooler = Analyzer.Run(new List<SessionData>(), new List<WinEvent>(), new List<DiskHealth>(), 30, new List<TestResult>(), hwCooler);
                Finding fCooler = dCooler.Findings.Find(delegate(Finding f) { return f.Title.Contains("dispositivo(s) com problema"); });
                Check(fCooler != null && fCooler.Sev == Sev.Info && fCooler.W.Count == 0, "conexão USB do cooler sem driver só informa, não vira suspeita de driver");
                Check(d.Fixes.Count == 1 && d.Fixes[0].State != 2 && swapS.Contains("Você trocou o driver de vídeo"), "o veredito diz que a troca está em acompanhamento");
                List<WinEvent> again = new List<WinEvent>(before); again.Add(lke193(DateTime.Now.AddMinutes(-20)));
                WinEvent crashAgain = new WinEvent(); crashAgain.Provider = "Application Error"; crashAgain.Id = 1000; crashAgain.Time = DateTime.Now.AddMinutes(-20).AddSeconds(1);
                crashAgain.P = new string[] { "jogo.exe", "1.0.0.0", "0x1", "atidxx64.dll" }; again.Add(crashAgain);   // com programa fechando junto: é sintoma de verdade
                sorted(again);
                d = Analyzer.Run(new List<SessionData>(), again, new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot(), new List<FixEntry> { swapFix });
                Analyzer.PlainVerdict(d, out swapT, out swapS);
                Check(d.Fixes[0].State == 2 && !swapS.Contains("Você trocou o driver de vídeo") && !(gpuF(d).Evidence.Contains("antes do driver novo")), "travamento depois da troca: não esconde nada e avisa que não resolveu");

                Say("== 4f. Reset de vídeo isolado x com sintoma; o problema voltou depois da troca do driver");
                // appCrash: o programa caiu no módulo dele mesmo (sintoma fraco); appCrashGpu: caiu dentro do driver de vídeo (sintoma forte)
                Func<DateTime, string, string, WinEvent> crashIn = delegate(DateTime t, string app, string module)
                {
                    WinEvent e = new WinEvent(); e.Provider = "Application Error"; e.Id = 1000; e.Time = t;
                    e.P = new string[] { app, "1.0.0.0", "0x1", module, "1.0.0.0", "0x1", "0xc0000005" }; return e;
                };
                Func<DateTime, string, WinEvent> appCrash = delegate(DateTime t, string app) { return crashIn(t, app, app); };
                Func<DateTime, string, WinEvent> appCrashGpu = delegate(DateTime t, string app) { return crashIn(t, app, "atidxx64.dll"); };
                DateTime tSym = DateTime.Now.AddHours(-20);
                d = Analyzer.Run(new List<SessionData>(), sorted(new List<WinEvent> { lke193(tSym), appCrashGpu(tSym.AddSeconds(2), "jogo.exe") }), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Finding fSym = gpuF(d);
                Say("  " + (fSym == null ? "(nenhum)" : fSym.Sev + " | " + fSym.Evidence.Replace("\n", " / ")));
                Check(fSym != null && fSym.Sev == Sev.Warn && fSym.Evidence.Contains("Algo fechou ou travou na mesma hora") && fSym.Evidence.Contains("jogo.exe fechou com erro dentro do driver de vídeo"),
                      "reset de vídeo com o programa caindo DENTRO do driver de vídeo: sintoma forte, alerta");
                d = Analyzer.Run(new List<SessionData>(), sorted(new List<WinEvent> { lke193(tSym), appCrash(tSym.AddSeconds(2), "jogo.exe") }), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Check(gpuF(d) != null && gpuF(d).Sev == Sev.Info && gpuF(d).Evidence.Contains("só um defeito do programa") && gpuF(d).Evidence.Contains("do próprio programa"),
                      "reset de vídeo com o programa caindo no módulo DELE, uma vez só: pode ser defeito do programa, só informa");
                d = Analyzer.Run(new List<SessionData>(), sorted(new List<WinEvent> { lke193(tSym), appCrash(tSym.AddSeconds(2), "jogo.exe"), lke193(tSym.AddHours(3)), appCrash(tSym.AddHours(3).AddSeconds(2), "jogo.exe") }),
                    new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Check(gpuF(d) != null && gpuF(d).Sev == Sev.Crit, "o mesmo programa caindo junto com o reset duas vezes: passa a contar (crítico)");
                d = Analyzer.Run(new List<SessionData>(), sorted(new List<WinEvent> { lke193(tSym) }), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Check(gpuF(d) != null && gpuF(d).Sev == Sev.Info && gpuF(d).Evidence.Contains("isolado"), "reset de vídeo isolado (nada fechou): só informação");

                // muitos resets isolados (o seu caso): continua informação, nunca "pede atenção", e não manda refazer o DDU
                List<WinEvent> manyIso = new List<WinEvent>();
                for (int mi = 0; mi < 8; mi++) manyIso.Add(lke193(DateTime.Now.AddHours(-40 - mi * 7)));
                d = Analyzer.Run(new List<SessionData>(), sorted(manyIso), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Finding fMany = gpuF(d);
                Check(fMany != null && fMany.Sev == Sev.Info && fMany.Advice.StartsWith("Nada a fazer agora") && fMany.Advice.Contains("frequentes (") && !fMany.Advice.Contains("DDU"),
                      "muitos resets de vídeo isolados: continuam só informação (sem 'pede atenção'), avisam que estão frequentes e não mandam refazer o DDU");

                FixEntry swap2 = new FixEntry(); swap2.Id = "t2"; swap2.Kind = "guia:driver-video-limpo"; swap2.Scope = "video"; swap2.Title = "Reinstalei o driver de vídeo do jeito certo"; swap2.When = DateTime.Now.AddHours(-3);
                List<WinEvent> oldOnes = new List<WinEvent> { lke193(DateTime.Now.AddHours(-30)), lke193(DateTime.Now.AddHours(-26)) };
                List<WinEvent> isoAfter = new List<WinEvent>(oldOnes); isoAfter.Add(lke193(DateTime.Now.AddHours(-1)));
                d = Analyzer.Run(new List<SessionData>(), sorted(isoAfter), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot(), new List<FixEntry> { swap2 });
                Check(d.Fixes.Count == 1 && d.Fixes[0].State != 2 && d.Fixes[0].State != 1 && d.Fixes[0].Isolated == 1 && d.Fixes[0].NextGuide.Length == 0 && d.Fixes[0].Text.Contains("isolado"),
                      "reset de vídeo isolado depois da troca: continua acompanhando e explica que não conta como 'voltou'");
                List<WinEvent> symAfter = new List<WinEvent>(oldOnes); DateTime tBack = DateTime.Now.AddHours(-1);
                // um programa fechando uma vez no módulo dele, junto com o reset: ainda não é "voltou"
                List<WinEvent> weakAfter = new List<WinEvent>(oldOnes); weakAfter.Add(lke193(tBack)); weakAfter.Add(appCrash(tBack.AddSeconds(1), "RainbowSixHelper.exe"));
                d = Analyzer.Run(new List<SessionData>(), sorted(weakAfter), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot(), new List<FixEntry> { swap2 });
                Check(d.Fixes[0].State != 2 && d.Fixes[0].State != 1 && d.Fixes[0].Isolated == 1, "um programa caindo uma vez no módulo dele junto com o reset: continua acompanhando, não é 'voltou'");
                symAfter.Add(lke193(tBack)); symAfter.Add(appCrashGpu(tBack.AddSeconds(1), "RainbowSixHelper.exe"));
                d = Analyzer.Run(new List<SessionData>(), sorted(symAfter), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot(), new List<FixEntry> { swap2 });
                Finding fBack = gpuF(d);
                string backT, backS; Analyzer.PlainVerdict(d, out backT, out backS);
                Say("  " + d.Fixes[0].Headline + ": " + d.Fixes[0].Text);
                Check(d.Fixes[0].State == 2 && d.Fixes[0].NextGuide == "video-voltou" && d.Fixes[0].Context.Contains("RainbowSixHelper.exe fechou com erro") && d.Fixes[0].Headline == "O problema voltou",
                      "programa fechou junto com o reset depois da troca: 'o problema voltou', com o contexto e o plano como próximo passo");
                Check(fBack != null && fBack.Guide == "video-voltou" && backS.Contains("o problema voltou"), "o achado deixa de mandar refazer o DDU e aponta o plano de testes; o veredito diz que voltou");
                Check(Rules.GuideById("video-voltou") != null && Rules.GuideById("cpu-instavel") != null && Rules.GuideById("cpu-quente") != null, "guias novos carregados (vídeo voltou, CPU instável, CPU quente)");

                Say("== 4h. PC saudável com ruído: tudo aprovado nos testes + sinais fracos = verde, e os sinais continuam visíveis como informação");
                Func<TestKind, TestResult> okTest = delegate(TestKind kind)
                {
                    TestResult tr = new TestResult(); tr.Id = "ok" + kind; tr.Kind = kind; tr.Status = "passed"; tr.Start = DateTime.Now.AddHours(-2); tr.End = tr.Start.AddMinutes(3); tr.Summary = "Nenhum erro.";
                    tr.Set("t_cpu_temp_max", 62); tr.Set("t_gpu_hot_max", 88); tr.Set("t_gpu_temp_max", 66); tr.Set("t_v12_min", 11.9); tr.Set("pcie_up_gbs", 12);
                    return tr;
                };
                TestResult combo = okTest(TestKind.Combined); combo.Set("t_cpu_fan_min", 0);   // bomba de water cooler: não usa o conector CPU_FAN
                List<TestResult> allOk = new List<TestResult> { okTest(TestKind.Cpu), okTest(TestKind.Ram), okTest(TestKind.Gpu), okTest(TestKind.Disk), combo };
                HwSnapshot hwNoise = new HwSnapshot(); hwNoise.GpuName = "AMD Radeon RX 7600"; hwNoise.GpuDriver = "31.0.21921.1000"; hwNoise.PagefileAuto = true;
                ProblemDevice cooler2 = new ProblemDevice(); cooler2.Name = "Corsair H100i Platinum"; cooler2.Class = "USB"; cooler2.Code = 28; hwNoise.Problems.Add(cooler2);
                WinEvent wuNoise = new WinEvent(); wuNoise.Provider = "Microsoft-Windows-WindowsUpdateClient"; wuNoise.Id = 19; wuNoise.Time = DateTime.Now.AddDays(-5);
                wuNoise.P = new string[] { "Advanced Micro Devices, Inc. - Display - 31.0.21921.1000" };
                List<WinEvent> noise = sorted(new List<WinEvent> { wuNoise, lke193(DateTime.Now.AddDays(-3)), lke193(DateTime.Now.AddDays(-2)) });   // dois resets de vídeo isolados
                d = Analyzer.Run(new List<SessionData>(), noise, new List<DiskHealth>(), 30, allOk, hwNoise);
                string nt, ns; Analyzer.PlainVerdict(d, out nt, out ns);
                int suspectOrFailed = 0; foreach (CompState cs in d.States) if (cs != null && (cs.Status == CompStatus.Suspect || cs.Status == CompStatus.Failed)) suspectOrFailed++;
                int indicios = d.Findings.FindAll(delegate(Finding f) { return Analyzer.Confidence(f) == "INDÍCIO"; }).Count;
                Say("  " + nt + " | " + ns + " | suspeitas: " + suspectOrFailed + " | indícios: " + indicios);
                Check(suspectOrFailed == 0 && indicios == 0 && nt == "Seu PC está saudável", "tudo aprovado + ruído (reset isolado, driver do Windows Update, ventoinha 0 RPM em water cooler, USB do cooler): saudável, zero indícios");
                Check(d.Findings.Exists(delegate(Finding f) { return Analyzer.Confidence(f) == "INFORMAÇÃO" && f.Title.StartsWith("Travamentos de GPU"); }), "o reset isolado continua visível, como informação");
                // atestado de saúde: uma página com a prova de cada peça, o resumo de 30 dias e o que NÃO foi verificado
                Diagnosis dCert = Analyzer.Run(new List<SessionData>(), noise, new List<DiskHealth>(), 30, allOk, hwNoise);
                MachineInfo miCert = new MachineInfo(); miCert.Cpu = "AMD Ryzen 5 5500"; miCert.Gpu = "AMD Radeon RX 7600"; miCert.Board = "Gigabyte A520M K V2"; miCert.Os = "Windows 11 Pro";
                string cert = System.Net.WebUtility.HtmlDecode(Report.BuildCertificate(dCert, miCert, new ReportExtras()));   // o HTML guarda acentos como entidades
                foreach (string needle in new string[] { "Atestado de saúde do PC", "Seu PC está saudável", "Teste de Processador (CPU) aprovado em", "+12 V mínimo 11,90 V", "Últimos 30 dias", "O que este atestado não cobre", "Não é laudo técnico", "AMD Radeon RX 7600" }) if (!cert.Contains(needle)) Say("  falta no atestado: " + needle);
                Check(cert.Contains("Atestado de saúde do PC") && cert.Contains("Seu PC está saudável") && cert.Contains("Teste de Processador (CPU) aprovado em") && cert.Contains("+12 V mínimo 11,90 V") &&
                      cert.Contains("Últimos 30 dias") && cert.Contains("O que este atestado não cobre") && cert.Contains("Não é laudo técnico") && cert.Contains("AMD Radeon RX 7600"),
                      "atestado de saúde: veredito, configuração, prova de cada peça com data, 30 dias e o que não cobre");

                // e o problema real não some: 3 resets com programa fechando junto + tela azul de vídeo continuam alarmando
                List<WinEvent> real = new List<WinEvent>(); DateTime tr0 = DateTime.Now.AddMinutes(-30);
                for (int k2 = 0; k2 < 3; k2++) { DateTime tk = tr0.AddMinutes(-k2 * 10); real.Add(lke193(tk)); real.Add(appCrash(tk.AddSeconds(1), "jogo.exe")); }
                d = Analyzer.Run(new List<SessionData>(), sorted(real), new List<DiskHealth>(), 30, allOk, hwNoise);
                Finding fReal = gpuF(d);
                Check(fReal != null && fReal.Sev == Sev.Crit && Analyzer.Confidence(fReal) == "INDÍCIO", "3 resets com programa fechando junto, depois dos testes: continua alarmando (crítico)");

                Say("== 4i. Programas que não são jogo (TeamSpeak, Discord...) não viram 'jogo que trava'");
                Func<string, SessionData> chat = delegate(string name)
                {
                    SessionData sd = new SessionData(); sd.Info = new SessionInfo(); sd.Info.Id = "c" + name; sd.Info.Game = name; sd.Info.Status = "ok";
                    sd.Info.Start = DateTime.Now.AddHours(-3); sd.Info.End = sd.Info.Start.AddMinutes(30);
                    sd.Info.FpsAvg = 70; sd.Info.FpsLow = 12; sd.Info.Stutters = 130; sd.Info.Frames = 22000;
                    return sd;
                };
                Predicate<Finding> fpsAny = delegate(Finding f) { return f.Source.StartsWith("Medição de FPS"); };
                d = Analyzer.Run(new List<SessionData> { chat("TeamSpeak") }, new List<WinEvent>(), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Diagnosis dGameFps = Analyzer.Run(new List<SessionData> { chat("jogo.exe") }, new List<WinEvent>(), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Check(d.Findings.Find(fpsAny) == null && dGameFps.Findings.Find(fpsAny) != null && AppSettings.IsNeverGame("TeamSpeak") && AppSettings.IsNeverGame("Discord.exe") && !AppSettings.IsNeverGame("cs2"),
                      "TeamSpeak/Discord/navegadores nunca são jogo; um jogo de verdade com os mesmos números continua sendo analisado");

                Say("== 4j. Desempenho: limites e upgrades só com dado por trás; o que está bom vira OK");
                Func<double, double, int, SessionData> playSess = delegate(double gpuPct, double corePct, int nSamples)
                {
                    SessionData sd = new SessionData(); sd.Info = new SessionInfo(); sd.Info.Id = "p" + gpuPct; sd.Info.Game = "jogo.exe"; sd.Info.Status = "ok";
                    sd.Info.Start = DateTime.Now.AddDays(-1); sd.Info.End = sd.Info.Start.AddHours(1); sd.Info.FpsAvg = 140; sd.Info.FpsLow = 90; sd.Info.Frames = 100000;
                    for (int i = 0; i < nSamples; i++) { Sample s = new Sample(); s.Time = sd.Info.Start.AddSeconds(i * 2); s.V[F.Gpu] = gpuPct; s.V[F.CoreMax] = corePct; sd.S.Add(s); }
                    return sd;
                };
                DisplayMode dmLow = new DisplayMode(); dmLow.Width = 1920; dmLow.Height = 1080; dmLow.CurrentHz = 60; dmLow.MaxHz = 165; dmLow.Rates.AddRange(new int[] { 60, 144, 165 });
                HwSnapshot hBad = new HwSnapshot(); hBad.CpuName = "AMD Ryzen 5 5500"; hBad.GpuName = "AMD Radeon RX 7600";
                hBad.RamModules = 1; hBad.DualChannel = false; hBad.RamTotalGb = 8; hBad.RamConfMts = 2400; hBad.RamRatedMts = 3200; hBad.RamType = 26;
                VolumeInfo vHdd = new VolumeInfo(); vHdd.Letter = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:"; vHdd.Media = "HDD"; vHdd.SizeGB = 500; hBad.Volumes.Add(vHdd);
                List<Finding> tBad = Analyzer.BalanceTips(hBad, new List<SessionData> { playSess(40, 95, 400) }, new List<TestResult>(), dmLow);
                foreach (Finding tf in tBad) Say("  [" + (tf.Badge.Length > 0 ? tf.Badge : tf.Sev == Sev.Ok ? "OK" : "-") + "] " + tf.Title);
                Check(tBad.Exists(delegate(Finding f) { return f.Title.Contains("60 Hz, mas ela aceita 165 Hz") && f.Badge == "DE GRAÇA" && f.Guide == "monitor-hz" && f.Evidence.Contains("140 FPS"); }), "tela em 60 Hz com monitor de 165 Hz: dica de graça, com os FPS reais do jogador");
                Check(tBad.Exists(delegate(Finding f) { return f.Title.Contains("roda a 2400 MT/s, mas o pente suporta 3200") && f.Guide == "ram-perfil"; }), "perfil de memória desligado: dica de graça com guia");
                Check(tBad.Exists(delegate(Finding f) { return f.Title.Contains("canal único"); }) && tBad.Exists(delegate(Finding f) { return f.Title.Contains("Só 8 GB"); }), "um pente de 8 GB: canal único e pouca memória");
                Check(tBad.Exists(delegate(Finding f) { return f.Title.Contains("Windows está em um HD"); }), "Windows em HD: maior ganho no dia a dia");
                Check(tBad.Exists(delegate(Finding f) { return f.Title.Contains("o processador é o limite") && f.Evidence.Contains("100% do tempo"); }), "partidas limitadas pelo processador: diz que a placa está folgada, com a medição");
                Check(tBad[0].Title.StartsWith("Por onde começar: 4 passos"), "'por onde começar' lista no máximo 4 passos, do mais barato ao mais caro");
                int idxFree = tBad[0].Advice.IndexOf("[de graça]"), idxWhere = tBad[0].Advice.IndexOf("[onde o upgrade rende]");
                Check(idxFree >= 0 && (idxWhere < 0 || idxFree < idxWhere), "a ordem põe o que é de graça antes do que custa dinheiro");

                // compatibilidade: a plataforma vem de data/compatibilidade.json
                Func<string, CompatFamily> fam0 = delegate(string cpu) { return Rules.CompatFor(cpu); };
                Check(fam0("AMD Ryzen 5 5500 6-Core Processor").Pcie == 3 && fam0("AMD Ryzen 5 5600G with Radeon Graphics").Pcie == 3 && fam0("AMD Ryzen 7 5700 8-Core Processor").Pcie == 3 &&
                      fam0("AMD Ryzen 5 5600X 6-Core Processor").Pcie == 4 && fam0("AMD Ryzen 5 5600 6-Core Processor").Pcie == 4 && fam0("AMD Ryzen 7 5700X 8-Core Processor").Pcie == 4 &&
                      fam0("AMD Ryzen 7 5800X3D 8-Core Processor").Socket == "AM4",
                      "compatibilidade (AM4 5000): 5500, 5600G e 5700 sem X são PCIe 3.0; 5600, 5600X e 5700X são PCIe 4.0 (conferido na AMD)");
                Check(fam0("AMD Ryzen 5 5500F 6-Core Processor").Pcie == 4 && fam0("AMD Ryzen 5 5500X3D 6-Core Processor").Pcie == 4 && fam0("AMD Ryzen 5 3400G with Radeon Vega Graphics").Pcie == 3 &&
                      fam0("AMD Ryzen 5 3600 6-Core Processor").Pcie == 4 && fam0("AMD Ryzen 7 8700G w/ Radeon 780M Graphics").Pcie == 4 && fam0("AMD Ryzen 7 8700G w/ Radeon 780M Graphics").Socket == "AM5",
                      "compatibilidade (casos que eu tinha errado): 5500F e 5500X3D são PCIe 4.0, Ryzen 3000 G é PCIe 3.0, Ryzen 8000 G é PCIe 4.0");
                Check(fam0("AMD Ryzen 7 7800X3D 8-Core Processor").MemMaxMts == 5200 && fam0("AMD Ryzen 7 9800X3D 8-Core Processor").MemMaxMts == 5600 && fam0("12th Gen Intel(R) Core(TM) i5-12400F").Socket == "LGA1700" &&
                      fam0("12th Gen Intel(R) Core(TM) i5-12400F").MemMaxDdr4 == 3200 && fam0("13th Gen Intel(R) Core(TM) i7-13700K").Note.Contains("0x12F") && fam0("10th Gen Intel(R) Core(TM) i7-10700K").MemMaxMts == 2933 &&
                      fam0("Processador Desconhecido X9") == null,
                      "compatibilidade (AM5 e Intel): memória oficial por geração, DDR4 separado do DDR5, microcódigo recomendado pela Intel; o desconhecido não inventa nada");
                Check(Rules.NeverGames.Contains("teamspeak") && Rules.NeverGames.Contains("discord") && Rules.BenignNames.Contains("corsair") && Rules.BenignClasses.Contains("usb") &&
                      !Rules.BenignClasses.Contains("display") && !Rules.BenignClasses.Contains("system") && !Rules.BenignClasses.Contains("net"),
                      "normal.json: lista do que nunca é jogo e dos dispositivos inofensivos; vídeo, sistema e rede nunca entram");
                bool compatOk = Rules.Compat.Count >= 8;
                foreach (CompatFamily cfm in Rules.Compat)
                    if (cfm.Name.Length == 0 || cfm.Detect.Length == 0 || cfm.Socket.Length == 0 || cfm.MemMaxMts <= 0 || cfm.Pcie < 3 || cfm.Note.Length == 0 ||
                        (cfm.Verified && !cfm.Source.StartsWith("http", StringComparison.Ordinal))) compatOk = false;   // "verificado" exige a fonte oficial
                Check(compatOk, "compatibilidade.json: toda família tem nome, trechos para detectar, soquete, memória, PCIe e nota (" + Rules.Compat.Count + " famílias)");
                List<Finding> tPlat = Analyzer.BalanceTips(hBad, new List<SessionData> { playSess(40, 95, 400) }, new List<TestResult>(), dmLow);
                Finding platTip = tPlat.Find(delegate(Finding f) { return f.Title.StartsWith("Sua plataforma"); });
                Check(platTip != null && platTip.Title.Contains("AM4 com DDR4") && platTip.Advice.Contains("Ryzen 7 5700X3D") && platTip.Advice.Contains("lista de processadores suportados") && platTip.Evidence.Contains("Fonte dos números: https://www.amd.com") && platTip.Advice.Contains("o upgrade que mais rende") && platTip.Badge == "ONDE O UPGRADE RENDE" &&
                      tPlat[0].Advice.IndexOf("Sua plataforma") < 0,
                      "plataforma AM4: diz o que encaixa sem trocar placa-mãe, manda conferir a lista do fabricante e liga ao processador ser o limite");
                hBad.RamRatedMts = 2400;   // sem perfil para ativar: cai na dica de memória lenta, que cita o máximo oficial do processador
                Check(Analyzer.BalanceTips(hBad, new List<SessionData>(), new List<TestResult>(), dmLow).Exists(delegate(Finding f) { return f.Evidence.Contains("O seu processador aceita oficialmente até 3200 MT/s"); }),
                      "memória lenta: cita o máximo oficial que o processador aceita (3200 MT/s no Ryzen 5 5500)");
                hBad.RamRatedMts = 3200;
                hBad.IsLaptop = true;
                Check(Analyzer.BalanceTips(hBad, new List<SessionData>(), new List<TestResult>(), dmLow).TrueForAll(delegate(Finding f) { return !f.Title.StartsWith("Sua plataforma"); }), "notebook: não sugere upgrade de plataforma");
                hBad.IsLaptop = false;

                // notebook: sem conselho de "coloque outro pente" ou "troque o processador", e a tela avisa que o Windows baixa a taxa de propósito
                hBad.IsLaptop = true;
                List<Finding> tLap = Analyzer.BalanceTips(hBad, new List<SessionData> { playSess(40, 95, 400) }, new List<TestResult>(), dmLow);
                Check(tLap.Exists(delegate(Finding f) { return f.Title.Contains("canal único") && f.Advice.Contains("soldada") && !f.Advice.Contains("Colocar um segundo pente"); }) &&
                      tLap.Exists(delegate(Finding f) { return f.Title.Contains("o processador é o limite") && f.Advice.Contains("costumam ser trocáveis") && !f.Advice.Contains("um processador mais forte"); }) &&
                      tLap.Exists(delegate(Finding f) { return f.Title.Contains("60 Hz, mas ela aceita 165 Hz") && f.Advice.Contains("baixa a taxa de propósito"); }),
                      "notebook: não manda trocar memória nem processador e explica que o Windows baixa a taxa da tela de propósito");
                hBad.IsLaptop = false;

                DisplayMode dmOk = new DisplayMode(); dmOk.Width = 1920; dmOk.Height = 1080; dmOk.CurrentHz = 165; dmOk.MaxHz = 165; dmOk.Rates.AddRange(new int[] { 60, 165 });
                HwSnapshot hGood = new HwSnapshot(); hGood.RamModules = 2; hGood.DualChannel = true; hGood.RamTotalGb = 16; hGood.RamConfMts = 3600; hGood.RamRatedMts = 3600; hGood.RamType = 26;
                VolumeInfo vSsd = new VolumeInfo(); vSsd.Letter = vHdd.Letter; vSsd.Media = "SSD"; hGood.Volumes.Add(vSsd);
                List<Finding> tGood = Analyzer.BalanceTips(hGood, new List<SessionData> { playSess(70, 60, 400) }, new List<TestResult>(), dmOk);
                Check(tGood[0].Sev == Sev.Ok && tGood[0].Title.StartsWith("Nenhum ajuste ou upgrade se destaca") && tGood.TrueForAll(delegate(Finding f) { return f.Sev == Sev.Ok; }),
                      "tudo bom (tela, memória, disco, equilíbrio): nenhuma dica de gasto, tudo OK");
                MonthSummary ms = Analyzer.BuildMonth(new List<WinEvent>(), new List<SessionData> { playSess(70, 60, 10) }, allOk);
                Check(ms.BlueScreens == 0 && ms.UnexpectedShutdowns == 0 && ms.Sessions == 1 && ms.HoursPlayed > 0.9 && ms.TestsTotal == 5 && ms.TestsPassed == 5, "resumo de 30 dias: zeros de verdade, partidas, horas e testes aprovados");

                Say("== 4k. 'Fui eu que desliguei' e desligamento limpo");
                Func<DateTime, WinEvent> kp41 = delegate(DateTime t)
                {
                    WinEvent e = new WinEvent(); e.Provider = "Microsoft-Windows-Kernel-Power"; e.Id = 41; e.Time = t; e.P = new string[] { "0" }; return e;
                };
                Predicate<Finding> offF = delegate(Finding f) { return f.Title.StartsWith("O PC desligou/reiniciou sem aviso"); };
                DateTime off1 = DateTime.Now.AddDays(-3), off2 = DateTime.Now.AddDays(-2);
                d = Analyzer.Run(new List<SessionData>(), sorted(new List<WinEvent> { kp41(off1), kp41(off2) }), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Finding fOff = d.Findings.Find(offF);
                Check(fOff != null && fOff.AnnotKind == "desligamento" && fOff.Times.Count == 2 && fOff.Sev == Sev.Warn, "dois desligamentos sem aviso: alerta com o botão 'Fui eu que desliguei'");
                Annotations.Add("desligamento", fOff.Times);
                d = Analyzer.Run(new List<SessionData>(), sorted(new List<WinEvent> { kp41(off1), kp41(off2) }), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Check(d.Findings.Find(offF) == null && d.Findings.Exists(delegate(Finding f) { return f.Title.StartsWith("Você marcou 2 desligamentos"); }) &&
                      Analyzer.BuildMonth(sorted(new List<WinEvent> { kp41(off1), kp41(off2) }), new List<SessionData>(), new List<TestResult>()).UnexpectedShutdowns == 0,
                      "marcados como seus: saem da conta e do resumo de 30 dias, mas continuam visíveis como informação");
                d = Analyzer.Run(new List<SessionData>(), sorted(new List<WinEvent> { kp41(off1), kp41(off2), kp41(DateTime.Now.AddHours(-5)) }), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Check(d.Findings.Find(offF) != null && d.Findings.Find(offF).Title.EndsWith("1×"), "um desligamento NOVO, sem marcação: o alerta volta (só ele conta)");

                Func<SessionData> cutSession = delegate()
                {
                    SessionData sd = new SessionData(); sd.Info = new SessionInfo(); sd.Info.Id = "cut"; sd.Info.Game = "jogo.exe"; sd.Info.Status = "interrupted";
                    sd.Info.Start = DateTime.Now.AddHours(-6); sd.Info.End = DateTime.Now.AddHours(-5);
                    for (int i = 0; i < 30; i++) { Sample s = new Sample(); s.Time = sd.Info.Start.AddSeconds(i * 2); s.V[F.Cpu] = 50; s.V[F.Gpu] = 60; sd.S.Add(s); }
                    return sd;
                };
                SessionData cutS = cutSession();
                WinEvent shut = new WinEvent(); shut.Provider = "User32"; shut.Id = 1074; shut.Time = cutS.Info.End.Value.AddMinutes(1);
                Predicate<Finding> cutF = delegate(Finding f) { return f.Title.StartsWith("Partida interrompida"); };
                d = Analyzer.Run(new List<SessionData> { cutSession() }, new List<WinEvent>(), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Diagnosis dClean = Analyzer.Run(new List<SessionData> { cutS }, new List<WinEvent> { shut }, new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Check(d.Findings.Find(cutF) != null && dClean.Findings.Find(cutF) == null, "gravação que parou junto com um desligamento normal do Windows (1074): não é queda");

                Say("== 4g. Ajustes reversíveis de Windows (só leitura aqui)");
                bool tweaksOk = Tweaks.All.Count >= 5;
                foreach (Tweak tw in Tweaks.All) { try { bool on = tw.IsOn(); Say("  " + tw.Id + ": " + (on ? "ligado" : "padrão")); } catch (Exception ex) { tweaksOk = false; Say("  " + tw.Id + " falhou: " + ex.Message); } }
                int cpuMax = Tweaks.CpuMaxPercent();
                Check(tweaksOk && cpuMax >= 1 && cpuMax <= 100, "todos os ajustes sabem dizer se estão ligados (estado máximo do processador: " + cpuMax + "%)");

                bool readsOk = true;
                try
                {
                    string pg, pn; Tweaks.ActivePlan(out pg, out pn);
                    int tf; long tb; Tweaks.TempInfo(out tf, out tb);
                    List<string> su = Tweaks.StartupPrograms();
                    Say("  plano de energia: " + pn + "; programas ao iniciar: " + su.Count + "; temporários > 3 dias: " + tf + " arquivos; modo de jogo: " + Tweaks.GameMode());
                    readsOk = tf >= 0 && tb >= 0 && su != null;
                }
                catch (Exception ex) { readsOk = false; Say("  leitura falhou: " + ex.Message); }
                Check(readsOk, "plano de energia, programas de inicialização, temporários e modo de jogo são lidos sem alterar nada");
                bool groupsOk = false, perfNoTrack = true;
                foreach (Tweak tw in Tweaks.All) { if (tw.Group == "desempenho") { groupsOk = true; if (tw.Id == "energia" && tw.Track) perfNoTrack = false; } }
                Check(groupsOk && perfNoTrack, "ajustes de desempenho existem e o plano de energia não entra no \"Resolveu?\"");

                Say("== 4e. Testes do próprio DiagIT não viram 'pouca RAM' nem 'picos de disco'");
                Func<string, SessionData> stress = delegate(string name)
                {
                    SessionData sd = new SessionData(); sd.Info = new SessionInfo(); sd.Info.Id = "x"; sd.Info.Game = name; sd.Info.Start = DateTime.Now.AddHours(-5); sd.Info.End = DateTime.Now.AddHours(-4);
                    for (int i = 0; i < 60; i++)
                    {
                        Sample x = new Sample(); x.Time = sd.Info.Start.AddSeconds(i * 2);
                        x.V[F.Cpu] = 20; x.V[F.Avail] = 200; x.V[F.Commit] = 97; x.V[F.DiskLat] = i == 30 ? 1500 : 2;   // RAM travada de propósito e leitura total do disco
                        sd.S.Add(x);
                    }
                    return sd;
                };
                Predicate<Finding> memDisk = delegate(Finding f) { return f.Title.StartsWith("Pouca memória") || f.Title.StartsWith("Memória virtual (commit)") || f.Title.StartsWith("Disco com picos"); };
                Diagnosis dTest = Analyzer.Run(new List<SessionData> { stress("[Teste] Memória RAM") }, new List<WinEvent>(), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Diagnosis dGame = Analyzer.Run(new List<SessionData> { stress("jogo.exe") }, new List<WinEvent>(), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Check(dTest.Findings.Find(memDisk) == null && dGame.Findings.Find(memDisk) != null, "RAM/disco no limite só contam em partida de jogo, não durante os testes");

                Say("== 4c. Adaptador de vídeo básico em qualquer idioma");
                Check(VideoAdapters.IsGeneric("Adaptador de Vídeo Básico da Microsoft", "", "") && VideoAdapters.IsGeneric("Microsoft Basic Display Adapter", "", "") &&
                      VideoAdapters.IsGeneric("Qualquer nome", "Microsoft", "") && VideoAdapters.IsGeneric("Qualquer nome", "", "BasicDisplay.sys") &&
                      !VideoAdapters.IsGeneric("AMD Radeon RX 7600", "Advanced Micro Devices, Inc.", "amdkmdag.sys"),
                      "reconhece o adaptador básico em português, inglês, pelo fabricante e pelo arquivo do driver");
                Check(VideoAdapters.VendorFromPnp(@"PCI\VEN_1002&DEV_7480&SUBSYS_53131849") == "AMD Radeon" && VideoAdapters.VendorFromPnp(@"PCI\VEN_10DE&DEV_2504") == "NVIDIA",
                      "marca da placa pelo código PCI quando ainda não há driver");

                Say("== 5. GPU acima do clock oficial");
                string model;
                Check(Analyzer.OfficialBoost("AMD Radeon RX 7600", out model) == 2655 && Analyzer.OfficialBoost("AMD Radeon RX 7600 XT", out model) == 2755 &&
                      Analyzer.OfficialBoost("AMD Radeon RX 9070 XT", out model) == 2970 && Analyzer.OfficialBoost("AMD Radeon RX 9070", out model) == 2520,
                      "boost oficial pelo nome (RX 7600 x RX 7600 XT, RX 9070 x RX 9070 XT)");
                TestResult gt = new TestResult(); gt.Id = "g"; gt.Kind = TestKind.Gpu; gt.Status = "passed"; gt.Start = DateTime.Now.AddDays(-1); gt.Summary = "ok";
                gt.Set("t_gpu_clock_p95", 2790);
                hw = new HwSnapshot(); hw.GpuName = "AMD Radeon RX 7600";
                Predicate<Finding> clkF = delegate(Finding f) { return f.Title.StartsWith("A GPU trabalha acima do clock oficial"); };
                d = Analyzer.Run(new List<SessionData>(), sorted(new List<WinEvent> { tdr(3) }), new List<DiskHealth>(), 30, new List<TestResult> { gt }, hw);
                Finding fc = d.Findings.Find(clkF);
                Say("  " + (fc == null ? "(nenhum achado)" : fc.Title));
                Check(fc != null && fc.Sev == Sev.Warn && fc.Advice.Contains("2655"), "com travamentos: pista + limitar a 2655 MHz no Adrenalin");
                d = Analyzer.Run(new List<SessionData>(), new List<WinEvent>(), new List<DiskHealth>(), 30, new List<TestResult> { gt }, hw);
                fc = d.Findings.Find(clkF);
                Check(fc != null && fc.Sev == Sev.Info && fc.W.Count == 0, "sem travamentos: só informa, sem peso");

                Say("== 6. Programa em segundo plano x partidas com problema");
                List<SessionData> ss = new List<SessionData>();
                for (int k = 0; k < 6; k++)
                {
                    SessionData sd = new SessionData(); sd.Info = new SessionInfo(); sd.Info.Id = "s" + k; sd.Info.Game = "jogo.exe";
                    sd.Info.Start = DateTime.Now.AddDays(-k - 1); sd.Info.End = sd.Info.Start.AddHours(1);
                    bool we = k < 3;
                    if (we) sd.Info.Bg.Add("Wallpaper Engine");
                    sd.Info.Status = we && k < 2 ? "interrupted" : "ok";
                    ss.Add(sd);
                }
                d = Analyzer.Run(ss, new List<WinEvent>(), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Finding fb = d.Findings.Find(delegate(Finding f) { return f.Title.Contains("Wallpaper Engine"); });
                Say("  " + (fb == null ? "(nenhum achado)" : fb.Title));
                Check(fb != null && fb.Sev == Sev.Warn, "partidas com Wallpaper Engine caem mais (2 de 3 x 0 de 3)");
                foreach (SessionData sd in ss) sd.Info.Status = sd.Info.Id == "s0" || sd.Info.Id == "s4" ? "interrupted" : "ok";
                d = Analyzer.Run(ss, new List<WinEvent>(), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Check(d.Findings.Find(delegate(Finding f) { return f.Title.Contains("Wallpaper Engine"); }) == null, "sem diferença entre com e sem: não acusa");

                Say("== 7. Arquivo de paginação no HD com o Windows em SSD");
                string sysL = Path.GetPathRoot(Environment.SystemDirectory).Substring(0, 1);
                string other = sysL == "D" ? "E" : "D";
                hw = new HwSnapshot();
                VolumeInfo vs = new VolumeInfo(); vs.Letter = sysL; vs.Media = "SSD"; vs.Health = "saudável"; hw.Volumes.Add(vs);
                VolumeInfo vh = new VolumeInfo(); vh.Letter = other; vh.Media = "HDD"; vh.Health = "saudável"; hw.Volumes.Add(vh);
                hw.PagefileFiles.Add(sysL + ":\\pagefile.sys"); hw.PagefileFiles.Add(other + ":\\pagefile.sys");
                d = Analyzer.Run(new List<SessionData>(), new List<WinEvent>(), new List<DiskHealth>(), 30, new List<TestResult>(), hw);
                Finding fp = d.Findings.Find(delegate(Finding f) { return f.Title.StartsWith("Memória virtual também no HD"); });
                Say("  " + (fp == null ? "(nenhum achado)" : fp.Title));
                Check(fp != null, "aponta o arquivo de paginação no HD");
                hw.PagefileFiles.RemoveAt(1);
                d = Analyzer.Run(new List<SessionData>(), new List<WinEvent>(), new List<DiskHealth>(), 30, new List<TestResult>(), hw);
                Check(d.Findings.Find(delegate(Finding f) { return f.Title.StartsWith("Memória virtual também no HD"); }) == null, "só no SSD: nada a apontar");

                Say("== 8. Regras em dados (data/*.json, fonte: " + Rules.Source + ")");
                Rules.Ensure();
                Say("  " + Rules.Bugs.Count + " códigos de tela azul, " + Rules.Video.Count + " marcas de vídeo, " + Rules.Cpus.Count + " de processador, " +
                    Rules.Boosts.Count + " placas, " + Rules.Programs.Count + " programas, " + Rules.Plain.Count + " frases simples");
                Check(Rules.Bugs.Count >= 20 && Rules.Plain.Count >= 60 && Rules.Boosts.Count >= 20 && Rules.Programs.Count >= 10 && Rules.Video.Count >= 3, "todas as regras carregadas");
                bool pesosOk = true, codigosUnicos = true;
                List<int> vistos = new List<int>();
                foreach (BugRule b in Rules.Bugs)
                {
                    foreach (double v in b.W.Values) if (v <= 0 || v > 1) pesosOk = false;
                    if (b.Codes.Length == 0 || b.Name.Length == 0 || b.Advice.Length == 0) pesosOk = false;
                    foreach (int c in b.Codes) { if (vistos.Contains(c)) codigosUnicos = false; vistos.Add(c); }
                    foreach (int a in b.AddrParams) if (a < 1 || a > 4) pesosOk = false;
                }
                Check(pesosOk, "telas azuis: todo código tem nome, conselho, pesos entre 0 e 1 e parâmetros de 1 a 4");
                Check(codigosUnicos, "telas azuis: nenhum código repetido");
                bool tiposOk = true;
                foreach (BgProgram bp in Rules.Programs) if (!Rules.BgTypes.ContainsKey(bp.Type)) tiposOk = false;
                Check(tiposOk, "programas: todo programa aponta para um tipo existente");
                bool drvOk = Rules.Drivers.Count >= 50;
                foreach (DriverRule dr in Rules.Drivers) if (!Rules.DriverTypes.ContainsKey(dr.Type) || dr.File.Length < 2 || dr.Name.Length == 0) drvOk = false;
                Check(drvOk, "drivers: todo driver tem nome e aponta para um tipo existente (" + Rules.Drivers.Count + " drivers)");
                bool guiasOk = Rules.Guides.Count >= 5;
                string[] acoesValidas = { "bloquear-wu", "tirar-hd", "reiniciar-avancado", "reiniciar-bios", "teste-gpu", "teste-memoria", "teste-cpu", "verificar-driver", "ver-resultados", "ver-hardware", "ver-aovivo" };
                foreach (Guide gu in Rules.Guides)
                {
                    if (gu.Id.Length == 0 || gu.Title.Length == 0 || gu.Done.Length == 0 || gu.Steps.Count == 0 || Array.IndexOf(Analyzer.Scopes, gu.Scope) < 0) guiasOk = false;
                    foreach (GuideStep st in gu.Steps)
                    {
                        if (st.Title.Length == 0 || st.Text.Length == 0 || Rules.Fill(st.Text).Contains("{")) guiasOk = false;
                        foreach (GuideAction ga in st.Actions)
                        {
                            bool tweakOk = ga.Target.StartsWith("tweak-", StringComparison.Ordinal) && (ga.Target.EndsWith("-on", StringComparison.Ordinal) || ga.Target.EndsWith("-off", StringComparison.Ordinal)) &&
                                Tweaks.ById(ga.Target.Substring(6, ga.Target.Length - 6 - (ga.Target.EndsWith("-on", StringComparison.Ordinal) ? 3 : 4))) != null;
                            if (ga.Label.Length == 0 || (ga.Type != "abrir" && ga.Type != "diagit" && ga.Type != "guia") ||
                                (ga.Type == "diagit" && Array.IndexOf(acoesValidas, ga.Target) < 0 && !tweakOk) || (ga.Type == "guia" && Rules.GuideById(ga.Target) == null)) guiasOk = false;
                        }
                    }
                }
                foreach (KeyValuePair<string, string> gl in Rules.GuideLinks) if (Rules.GuideById(gl.Value) == null) guiasOk = false;
                Check(guiasOk, "guias: todo guia tem passos, escopo válido e ações conhecidas; todo achado aponta para um guia existente (" + Rules.Guides.Count + " guias)");
                bool marcadores = true;
                foreach (BugRule b in Rules.Bugs) if (Rules.Fill(b.Advice).Contains("{")) marcadores = false;
                Check(marcadores, "nenhum {marcador} desconhecido nos conselhos");

                Say("== 9. Textos conforme a marca do PC");
                Func<string, string, string> advice124 = delegate(string gpuN, string cpuN)
                {
                    HwSnapshot h = new HwSnapshot(); h.GpuName = gpuN; h.CpuName = cpuN;
                    WinEvent e = new WinEvent(); e.Provider = "Microsoft-Windows-WER-SystemErrorReporting"; e.Id = 1001; e.Time = DateTime.Now.AddDays(-1); e.P = new string[] { "0x00000124" };
                    Diagnosis dd = Analyzer.Run(new List<SessionData>(), new List<WinEvent> { e }, new List<DiskHealth>(), 30, new List<TestResult>(), h);
                    Finding f124 = dd.Findings.Find(delegate(Finding f) { return f.BugCode == 0x124; });
                    return f124 == null ? "" : f124.Advice;
                };
                string aAmd = advice124("AMD Radeon RX 7600", "AMD Ryzen 5 5500 6-Core Processor"), aIntel = advice124("NVIDIA GeForce RTX 4060", "13th Gen Intel(R) Core(TM) i5-13400F");
                Say("  AMD: " + aAmd);
                Say("  Intel/NVIDIA: " + aIntel);
                Check(aAmd.Contains("EXPO") && aAmd.Contains("PBO"), "PC AMD: fala de EXPO e PBO");
                Check(aIntel.Contains("XMP") && !aIntel.Contains("EXPO") && !aIntel.Contains("PBO"), "PC Intel: fala de XMP, sem EXPO/PBO");
                Check(Rules.VideoFor("NVIDIA GeForce GTX 750 Ti").Brand == "NVIDIA" && Rules.VideoFor("AMD Radeon RX 7600").Brand == "AMD" &&
                      Rules.VideoFor("Intel(R) Arc(TM) A770").Brand == "Intel" && Rules.VideoFor("Microsoft Basic Display Adapter").Brand == "", "marca da placa de vídeo pelo nome");
                TestResult psu = new TestResult(); psu.Set("t_cpu_power_max", 70); psu.Set("t_gpu_power_max", 160);
                string pa = Analyzer.PsuAdvice(psu);
                Say("  Fonte: " + pa);
                Check(pa.Contains("500 W"), "fonte recomendada a partir do consumo medido (230 W → 500 W)");

                Say("== 10. Driver culpado da tela azul (evento 1019 do Windows)");
                Func<int, string, int, WinEvent[]> bsodWith = delegate(int code, string drv, int hoursAgo)
                {
                    WinEvent e = new WinEvent(); e.Provider = "Microsoft-Windows-WER-SystemErrorReporting"; e.Id = 1001; e.Time = DateTime.Now.AddHours(-hoursAgo);
                    e.P = new string[] { "0x" + code.ToString("x8") };
                    WinEvent r = new WinEvent(); r.Provider = "Microsoft-Windows-WER-SystemErrorReporting"; r.Id = 1019; r.Time = e.Time.AddSeconds(2); r.P = new string[] { drv };
                    return new WinEvent[] { e, r };
                };
                Func<WinEvent[][], List<WinEvent>> join = delegate(WinEvent[][] parts) { List<WinEvent> l = new List<WinEvent>(); foreach (WinEvent[] x in parts) l.AddRange(x); return sorted(l); };
                HwSnapshot nv = new HwSnapshot(); nv.GpuName = "NVIDIA GeForce GTX 750 Ti";
                d = Analyzer.Run(new List<SessionData>(), join(new WinEvent[][] { bsodWith(0x116, "nvlddmkm.sys", 30), bsodWith(0x116, "nvlddmkm.sys", 100) }), new List<DiskHealth>(), 30, new List<TestResult>(), nv);
                Finding fv = d.Findings.Find(delegate(Finding f) { return f.BugCode == 0x116; });
                Say("  " + (fv == null ? "-" : fv.Plain + " | " + fv.Advice));
                Check(fv != null && fv.Culprit == "nvlddmkm.sys" && fv.CulpritCount == 2, "pareia cada tela azul com o driver do evento 1019");
                Check(fv != null && fv.Plain == "O PC teve 2 telas azuis causadas pelo driver de vídeo da NVIDIA", "frase simples nomeia o driver culpado");
                Check(fv != null && fv.Advice.StartsWith("O Windows aponta o driver de vídeo da NVIDIA") && fv.Advice.Contains("NVIDIA App"), "conselho do tipo de driver, com o painel da marca");
                Check(fv != null && fv.W[Comp.Software] > fv.W[Comp.Gpu], "driver culpado consistente: peso passa para Drivers e Windows");
                Check(fv != null && fv.Guide == "driver-video-limpo", "tela azul do driver de vídeo ganha o guia de reinstalação limpa");
                string vt2, vs2;
                Analyzer.PlainVerdict(d, out vt2, out vs2);
                Say("  Veredito: " + vt2 + " | " + vs2);
                Check(vs2.StartsWith("A causa mais provável é o driver de vídeo da NVIDIA (nvlddmkm.sys)"), "veredito do Início nomeia o driver apontado pelo Windows");

                d = Analyzer.Run(new List<SessionData>(), join(new WinEvent[][] { bsodWith(0x1A, "ntoskrnl.exe", 30), bsodWith(0x1A, "ntoskrnl.exe", 60) }), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Finding fk = d.Findings.Find(delegate(Finding f) { return f.BugCode == 0x1A; });
                Check(fk != null && fk.W[Comp.Ram] > fk.W[Comp.Software] && fk.Evidence.Contains("núcleo do Windows") && fk.Plain.Contains("ligadas à memória"),
                    "núcleo do Windows apontado: não tira peso da memória");

                d = Analyzer.Run(new List<SessionData>(), join(new WinEvent[][] { bsodWith(0x3B, "rtwlane.sys", 30), bsodWith(0x3B, "vgk.sys", 60), bsodWith(0x3B, "nvlddmkm.sys", 90) }), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Finding fm = d.Findings.Find(delegate(Finding f) { return f.BugCode == 0x3B; });
                Check(fm != null && fm.Culprit != null && fm.Plain.Contains("causadas por um driver") && fm.Evidence.Contains("Outros apontados"),
                    "um driver diferente em cada tela azul: não acusa um só (aponta memória/hardware)");
                Check(Rules.Driver("RTKVHD64.sys") != null && Rules.Driver("RTKVHD64.sys").Type == "audio" && Rules.Driver("rtk_wifi.sys").Type == "rede" &&
                      Rules.Driver("desconhecido123.sys") == null && Rules.Driver("ntoskrnl.exe").Core, "drivers.json: prefixo mais longo vence, desconhecido = null, núcleo marcado");

                Say("== 11. Leitura do arquivo da tela azul (minidump sintético no formato do Windows 11)");
                Func<int, ulong[], CrashDump> fakeDump = delegate(int code, ulong[] stack)
                {
                    byte[] b = new byte[0x8000];
                    Encoding.ASCII.GetBytes("PAGEDU64").CopyTo(b, 0);
                    string[] names = { "ntoskrnl.exe", "Wdf01000.sys", "foo.sys", "bar.sys" };
                    ulong[] bases = { 0xFFFFF80000000000UL, 0xFFFFF80002000000UL, 0xFFFFF80003000000UL, 0xFFFFF80004000000UL };
                    uint[] sizes = { 0x1000000, 0x100000, 0x10000, 0x10000 };
                    Action<int, uint> w32 = delegate(int at, uint v) { BitConverter.GetBytes(v).CopyTo(b, at); };
                    Action<int, ulong> w64 = delegate(int at, ulong v) { BitConverter.GetBytes(v).CopyTo(b, at); };
                    int t = 0x2000;
                    w32(t + 0x0C, 0x3000);                         // contexto: Rip no núcleo (ignorado)
                    w64(0x3000 + 0xF8, bases[0] + 0x500);
                    w32(t + 0x28, 0x4000); w32(t + 0x2C, (uint)(stack.Length * 8));
                    for (int i = 0; i < stack.Length; i++) w64(0x4000 + i * 8, stack[i]);
                    w32(t + 0x30, 0x5000); w32(t + 0x34, (uint)names.Length); w32(t + 0x38, 0x6000);
                    int pool = 0x6000;
                    for (int i = 0; i < names.Length; i++)
                    {
                        int e = 0x5000 + i * 0x90;
                        w32(e, (uint)pool); w64(e + 0x38, bases[i]); w32(e + 0x48, sizes[i]);
                        w32(pool, (uint)names[i].Length); Encoding.Unicode.GetBytes(names[i]).CopyTo(b, pool + 4);
                        pool += 4 + names[i].Length * 2 + 2;
                    }
                    CrashDump cd = new CrashDump(); cd.Code = (uint)code;
                    cd.P = new ulong[] { 0, 2, 0, 0xFFFFF80003000123UL };
                    Minidump.Analyze(cd, b);
                    return cd;
                };
                CrashDump dp = fakeDump(0xD1, new ulong[] { 0xFFFFF80000000777UL, 0x1234, 0xFFFFF80004000010UL });
                Say("  0xD1: " + dp.Culprit + " (" + dp.CulpritHow + "), " + dp.ModulesRead + " drivers, layout " + Minidump.LastLayout);
                Check(dp.ModulesRead == 4 && dp.Culprit == "foo.sys" && dp.CulpritHow.StartsWith("parâmetro 4"), "0xD1: culpado pelo endereço no parâmetro 4");
                CrashDump ds = fakeDump(0x1A, new ulong[] { 0xFFFFF80000000777UL, 0xFFFFF80002000050UL, 0x1234, 0xFFFFF80004000010UL });
                Say("  0x1A: " + ds.Culprit + " (" + ds.CulpritHow + "), pilha " + string.Join(" → ", ds.StackModules.ToArray()));
                Check(ds.Culprit == "bar.sys" && ds.CulpritHow.StartsWith("pilha") && ds.StackModules.Count == 3, "0x1A: culpado pela pilha, pulando núcleo e Wdf01000");
                CrashDump dk = fakeDump(0x1A, new ulong[] { 0xFFFFF80000000777UL, 0x1234 });
                Check(dk.Culprit == "ntoskrnl.exe", "só o núcleo na pilha: aponta o núcleo (memória/hardware)");

                Say("== 12. Ciclo \"corrigiu?\"");
                Func<double, FixEntry> fixAt = delegate(double daysAgo)
                {
                    FixEntry fe = new FixEntry(); fe.Id = "t" + daysAgo; fe.When = DateTime.Now.AddDays(-daysAgo); fe.Kind = "guia:driver-video-limpo";
                    fe.Title = "Reinstalei o driver de vídeo do jeito certo"; fe.Scope = "video"; return fe;
                };
                Func<int[], double, Diagnosis> runFix = delegate(int[] bsodDaysAgo, double fixDaysAgo)
                {
                    List<WinEvent[]> parts = new List<WinEvent[]>();
                    foreach (int dd in bsodDaysAgo) parts.Add(bsodWith(0x116, "nvlddmkm.sys", dd * 24));
                    return Analyzer.Run(new List<SessionData>(), join(parts.ToArray()), new List<DiskHealth>(), 30, new List<TestResult>(), nv, new List<FixEntry> { fixAt(fixDaysAgo) });
                };
                d = runFix(new int[] { 10, 8 }, 5);
                Say("  " + d.Fixes[0].Headline + ": " + d.Fixes[0].Text);
                Check(d.Fixes.Count == 1 && d.Fixes[0].State == 0 && d.Fixes[0].Before == 2 && d.Fixes[0].After == 0, "sem volta há 5 dias (antes a cada ~1 dia): acompanhando");
                d = runFix(new int[] { 25, 24 }, 20);
                Say("  " + d.Fixes[0].Headline + ": " + d.Fixes[0].Text);
                Finding old = d.Findings.Find(delegate(Finding f) { return f.BugCode == 0x116; });
                Check(d.Fixes[0].State == 1, "20 dias sem tela azul depois da correção: resolvido");
                Check(old != null && old.Sev == Sev.Info && old.W.Count == 0 && old.Evidence.Contains("antes da correção"), "resolvido: telas azuis antigas viram histórico (informação, sem peso)");
                d = runFix(new int[] { 10, 2 }, 5);
                Say("  " + d.Fixes[0].Headline + ": " + d.Fixes[0].Text);
                Check(d.Fixes[0].State == 2 && d.Fixes[0].After == 1, "voltou depois da correção: não resolveu");
                d = runFix(new int[] { }, 5);
                Check(d.Fixes[0].State == 3, "nada registrado antes: sem como comparar");

                Say("== 13. Desempenho medido (FPS)");
                Func<int, double, double, int, SessionData> game = delegate(int daysAgo, double fps, double low, int stutters)
                {
                    SessionData sd = new SessionData(); sd.Info = new SessionInfo(); sd.Info.Id = "g" + daysAgo; sd.Info.Game = "cs2";
                    sd.Info.Start = DateTime.Now.AddDays(-daysAgo); sd.Info.End = sd.Info.Start.AddHours(1); sd.Info.Status = "ok";
                    sd.Info.FpsAvg = fps; sd.Info.FpsLow = low; sd.Info.Stutters = stutters; sd.Info.Frames = (int)(fps * 3600);
                    return sd;
                };
                List<SessionData> perf = new List<SessionData> { game(9, 140, 41, 80), game(8, 138, 43, 70), game(2, 150, 58, 5), game(1, 152, 60, 4) };
                FixEntry pf = new FixEntry(); pf.Id = "pf"; pf.When = DateTime.Now.AddDays(-5); pf.Title = "Desliguei o Instant Replay"; pf.Scope = "engasgos";
                d = Analyzer.Run(perf, new List<WinEvent>(), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot(), new List<FixEntry> { pf });
                Finding fps1 = d.Findings.Find(delegate(Finding f) { return f.Source.StartsWith("Medição de FPS"); });
                Say("  " + (fps1 == null ? "-" : fps1.Plain + " | " + fps1.Evidence.Split('\n')[0]));
                Say("  " + d.Fixes[0].Headline + ": " + d.Fixes[0].Text);
                Check(fps1 != null && fps1.Sev == Sev.Ok, "achado reflete as partidas recentes (depois da correção: sem engasgos)");
                Diagnosis dBad = Analyzer.Run(new List<SessionData> { game(3, 140, 41, 80), game(2, 138, 43, 70) }, new List<WinEvent>(), new List<DiskHealth>(), 30, new List<TestResult>(), new HwSnapshot());
                Finding fps2 = dBad.Findings.Find(delegate(Finding f) { return f.Source.StartsWith("Medição de FPS"); });
                Say("  " + (fps2 == null ? "-" : fps2.Plain));
                Check(fps2 != null && fps2.Sev == Sev.Warn && fps2.Title.Contains("75 engasgos por hora") && fps2.W[Comp.Software] > 0, "engasgos medidos frequentes viram achado");
                Check(d.Fixes[0].State == 2 || d.Fixes[0].State == 0 || d.Fixes[0].State == 1, "acompanhamento de engasgos usa as partidas medidas");
                Check(d.Fixes[0].Text.Contains("1% low) em cs2: 42 → 59"), "correção mostra o FPS mínimo antes e depois no mesmo jogo");
                Check(double.IsNaN(Analyzer.StuttersPerHour(new SessionInfo())), "partida sem medição de FPS não conta");

                Say("== 14. Relatório para o técnico");
                List<WinEvent> evR = join(new WinEvent[][] { bsodWith(0x116, "nvlddmkm.sys", 30), bsodWith(0x116, "nvlddmkm.sys", 100) });
                Diagnosis dRep = Analyzer.Run(perf, evR, new List<DiskHealth>(), 30, new List<TestResult>(), nv, new List<FixEntry> { pf });
                List<TimelineItem> tlr = Analyzer.Timeline(evR, nv, perf, dRep.Tests, new List<FixEntry> { pf }, 30);
                Check(tlr.Exists(delegate(TimelineItem i) { return i.Kind == "Tela azul" && i.Text.Contains("nvlddmkm.sys"); }) &&
                      tlr.Exists(delegate(TimelineItem i) { return i.Kind == "Correção"; }), "linha do tempo: telas azuis com o driver culpado e as correções");
                ReportExtras rx = new ReportExtras(); rx.Events = evR; rx.Fixes = new List<FixEntry> { pf }; rx.Windows = "Windows de teste";
                string html = Report.Build(dRep, new MachineInfo(), perf, rx);
                Check(html.Contains("Detalhes para o técnico") && html.Contains("Linha do tempo") && html.Contains("Suas correções") && html.Contains("1% low"),
                    "relatório: resumo para o dono, parte técnica, correções e FPS");

                Say("== 15. Sensores de placa-mãe MSI (Nuvoton NCT6687D, lidos num i7-11700 + GTX 750 Ti)");
                Func<STyp, string, double, SensorItem> bi = delegate(STyp ty, string name, double v)
                { SensorItem it = new SensorItem(); it.Cat = SCat.Board; it.Typ = ty; it.Name = name; it.Val = v; it.Hw = "Nuvoton NCT6687D"; it.Unit = ty == STyp.Fan ? "RPM" : "°C"; return it; };
                List<SensorItem> msiSens = new List<SensorItem> {
                    bi(STyp.Temp, "CPU", 86), bi(STyp.Temp, "System", 37), bi(STyp.Temp, "VRM MOS", 58), bi(STyp.Temp, "PCH", 45), bi(STyp.Temp, "CPU Socket", 52),
                    bi(STyp.Fan, "CPU Fan", 0), bi(STyp.Fan, "Pump Fan", 0), bi(STyp.Fan, "System Fan #1", 2139), bi(STyp.Fan, "System Fan #2", 0) };
                Sample sm = new Sample();
                SensorHub.Map(msiSens, sm);
                Check(sm[F.BoardTemp] == 58, "temperatura da placa-mãe não usa o sensor \"CPU\" do chip da placa (" + sm[F.BoardTemp] + " °C)");
                Check(sm[F.CpuFan] == 0 && SensorHub.OtherFanRpm == 2139, "CPU_FAN em 0 RPM com outra ventoinha girando é reconhecido");
                Say("");
                Say(fails == 0 ? "TUDO OK" : fails + " FALHA(S)");
                return fails == 0 ? 0 : 1;
            }
            catch (Exception e) { Say("EXCEÇÃO: " + e); return 2; }
            finally { o.Close(); }
        }

        /// <summary>Mede o FPS de um processo (nome ou PID) por alguns segundos com o PresentMon embutido. Precisa de administrador.</summary>
        public static int FpsTest(string target, int seconds, string outFile)
        {
            o = new StreamWriter(outFile, false, new UTF8Encoding(false));
            try
            {
                int pid;
                if (!int.TryParse(target, out pid))
                {
                    Process[] ps = Process.GetProcessesByName(target);
                    if (ps.Length == 0) { Say("Processo não encontrado: " + target); return 1; }
                    pid = ps[0].Id;
                }
                Say("PresentMon: " + FrameMonitor.ExePath + " (existe: " + FrameMonitor.Available + ")  administrador: " + SysUtil.IsAdmin());
                FrameMonitor fm = new FrameMonitor();
                if (!fm.Start(pid)) { Say("FALHOU ao iniciar: " + fm.Error); return 1; }
                for (int i = 0; i < seconds; i++)
                {
                    Thread.Sleep(1000);
                    fm.Tick();
                    double fps, low, ms; int st;
                    fm.Snapshot(out fps, out low, out ms, out st);
                    Say(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  {0,2}s  FPS {1,6:0.0}  1% low {2,6:0.0}  quadro {3,6:0.00} ms  engasgos {4}", i + 1, fps, low, ms, st));
                }
                double avg, l1; int stt, fr;
                fm.Summary(out avg, out l1, out stt, out fr);
                fm.Stop();
                Say(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Resumo: {0} quadros, FPS médio {1:0.0}, 1% low {2:0.0}, {3} engasgos. Erro: {4}", fr, avg, l1, stt, fm.Error.Length > 0 ? fm.Error : "nenhum"));
                return fr > 0 ? 0 : 1;
            }
            catch (Exception e) { Say("EXCEÇÃO: " + e); return 2; }
            finally { o.Close(); }
        }

        /// <summary>O que o DiagIT extrai de arquivos de tela azul (um .dmp ou uma pasta): código, parâmetros, driver culpado e pilha.</summary>
        public static int DumpInfo(string input, string outFile)
        {
            o = new StreamWriter(outFile, false, new UTF8Encoding(false));
            try
            {
                string[] files = Directory.Exists(input) ? Directory.GetFiles(input, "*.dmp") : new string[] { input };
                foreach (string f in files)
                {
                    CrashDump d = HwFacts.Parse(f);
                    if (d == null) { Say(Path.GetFileName(f) + ": formato não reconhecido"); continue; }
                    Say(Path.GetFileName(f) + "  (" + d.Time.ToString("dd/MM/yyyy HH:mm") + ")");
                    Say("  código " + d.CodeHex + " " + Rules.Bug((int)(d.Code & 0xFFFF)).Name + "  parâmetros 0x" + d.P[0].ToString("X") + ", 0x" + d.P[1].ToString("X") + ", 0x" + d.P[2].ToString("X") + ", 0x" + d.P[3].ToString("X"));
                    Say("  drivers lidos: " + d.ModulesRead + " (layout: " + Minidump.LastLayout + ")");
                    DriverRule r = Rules.Driver(d.Culprit);
                    Say("  culpado: " + (d.Culprit ?? "não identificado") + (d.Culprit != null ? " (" + (r != null ? r.Name : "driver desconhecido") + ", por " + d.CulpritHow + ")" : ""));
                    Say("  pilha: " + string.Join(" → ", d.StackModules.ToArray()));
                }
                return 0;
            }
            catch (Exception e) { Say("EXCEÇÃO: " + e); return 2; }
            finally { o.Close(); }
        }

        /// <summary>Sonda as fontes de sensores reais e escreve tudo que foi lido.</summary>
        public static int Probe(string outFile)
        {
            o = new StreamWriter(outFile, false, new UTF8Encoding(false));
            try
            {
                Say("Administrador: " + SysUtil.IsAdmin());
                AdlSource adl = new AdlSource();
                bool ok = adl.Init();
                Say("ADL: " + ok + "  adaptador: " + adl.AdapterName);
                if (ok)
                {
                    Say(adl.Dump());
                    foreach (SensorItem it in adl.Read()) Say("  " + it.Name + " = " + it.Val + " " + it.Unit);
                    int g, w; adl.PcieLink(out g, out w); Say("  PCIe: gen=" + g + " lanes=" + w);
                    adl.Close();
                }
                LhmDllSource lhm = new LhmDllSource();
                lhm.EnableGpu = false;
                bool lok = lhm.Init();
                Say("LHM: " + lok + " " + lhm.Error + "  dll=" + LhmDllSource.FindDll());
                if (lok)
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        List<SensorItem> items = lhm.Read();
                        if (pass == 1) foreach (SensorItem it in items) Say("  [" + it.Cat + "/" + it.Typ + "] " + it.Hw + " | " + it.Name + " = " + it.Val.ToString("0.###") + " " + it.Unit);
                        Thread.Sleep(1000);
                    }
                    lhm.Close();
                }
            }
            catch (Exception e) { Say("EXCEÇÃO: " + e); }
            o.Dispose();
            return 0;
        }

        /// <summary>Executa um teste de hardware sem interface: --run-test cpu|ram|gpu|disk|combined segundos saida.txt</summary>
        public static int RunHw(string kind, int secs, string outFile, long limitMb)
        {
            o = new StreamWriter(outFile, false, new UTF8Encoding(false));
            Paths.Ensure();
            Watcher w = new Watcher(new AppSettings());
            w.Start();
            Thread.Sleep(3000);
            TestRunner tr = new TestRunner(w);
            TestKind k = (TestKind)Enum.Parse(typeof(TestKind), kind, true);
            object opt = null;
            if (k == TestKind.Ram && limitMb > 0) { RamTest.Options ro = new RamTest.Options(); ro.MaxMB = limitMb; opt = ro; }
            if (k == TestKind.Gpu && limitMb > 0) { GpuTest.Options go = new GpuTest.Options(); go.VramMaxMB = limitMb; opt = go; }
            ManualResetEvent done = new ManualResetEvent(false);
            TestResult fin = null;
            tr.Finished += delegate(TestResult r) { fin = r; done.Set(); };
            Stopwatch sw = Stopwatch.StartNew();
            tr.Start(k, secs, opt);
            while (!done.WaitOne(5000))
            {
                Sample s = w.Last;
                Say(string.Format("  {0,5:0}s  {1,-40} {2,4:0}%  erros={3}  cpu={4:0}°C {5:0}W  gpu={6:0}°C hot={7:0}°C {8:0}W clk={9:0}  pcie=g{10} x{11}  12V={12:0.00}",
                    sw.Elapsed.TotalSeconds, tr.Ctx.Phase, tr.Ctx.Progress * 100, tr.Ctx.Errors,
                    s == null ? double.NaN : s[F.CpuTemp], s == null ? double.NaN : s[F.CpuPower], s == null ? double.NaN : s[F.GpuTemp],
                    s == null ? double.NaN : s[F.GpuHot], s == null ? double.NaN : s[F.GpuPower], s == null ? double.NaN : s[F.GpuClock],
                    s == null ? double.NaN : s[F.PcieGen], s == null ? double.NaN : s[F.PcieLanes], s == null ? double.NaN : s[F.V12]));
            }
            Say("== " + TestResult.KindLabel(fin.Kind) + ": " + fin.StatusLabel + " (erros " + fin.Errors + ")");
            Say(fin.Summary);
            foreach (string d in fin.Details) Say("  - " + d);
            foreach (KeyValuePair<string, double> kv in fin.M) Say("  m." + kv.Key + " = " + kv.Value.ToString("0.###"));
            w.Stop();
            o.Dispose();
            return fin.Status == "passed" ? 0 : 1;
        }

        static void Progress(Watcher w, TestRunner tr, Stopwatch sw)
        {
            Sample s = w.Last;
            if (tr.Ctx == null) return;
            Say(string.Format("  {0,5:0}s  {1,-38} {2,4:0}%  erros={3}  CPU {4:0}°C {5:0}W fan {6:0}  GPU {7:0}°C hot {8:0}°C {9:0}W  +12V {10:0.00}",
                sw.Elapsed.TotalSeconds, tr.Ctx.Phase, tr.Ctx.Progress * 100, tr.Ctx.Errors,
                s == null ? double.NaN : s[F.CpuTemp], s == null ? double.NaN : s[F.CpuPower], s == null ? double.NaN : s[F.CpuFan],
                s == null ? double.NaN : s[F.GpuTemp], s == null ? double.NaN : s[F.GpuHot], s == null ? double.NaN : s[F.GpuPower],
                s == null ? double.NaN : s[F.V12]));
        }

        /// <summary>Validação completa como administrador: sensores, SMART, telas azuis e bateria de testes (dados reais do app).</summary>
        public static int ValidateAll(string outFile, int scale)
        {
            o = new StreamWriter(outFile, false, new UTF8Encoding(false));
            try
            {
                Paths.Ensure();
                Say("Administrador: " + SysUtil.IsAdmin() + "   início " + DateTime.Now.ToString("HH:mm:ss"));
                Watcher w = new Watcher(AppSettings.Load());
                w.Start();
                Thread.Sleep(9000);
                Sample s = w.Last;
                Say("== Sensores: " + w.Sensors.ActiveName);
                Say("   " + w.Sensors.Coverage);
                if (s != null)
                    for (int i = 0; i < F.Count; i++)
                        if (!double.IsNaN(s.V[i])) Say("   " + F.Labels[i].PadRight(26) + s.V[i].ToString("0.###") + " " + F.Units[i]);
                Say("== Fatos de hardware");
                HwSnapshot h = HwFacts.Collect(w.Sensors);
                foreach (Fact f in h.Facts) Say("   [" + f.Level + "] " + f.Group + " | " + f.Name + " = " + f.Value + (f.Note.Length > 0 ? "  (" + f.Note + ")" : ""));
                foreach (DriveHealth d in h.Drives)
                    Say("   disco " + d.Number + " " + d.Model + " " + d.Bus + " lido=" + d.Read + " " + d.Error + " | temp " + d.TempC + " desgaste " + d.UsedPct + "% horas " + d.PowerOnHours +
                        " ciclos " + d.PowerCycles + " insegs " + d.UnsafeShutdowns + " midia " + d.MediaErrors + " realoc " + d.Reallocated + " pend " + d.Pending + " crc " + d.CrcErrors +
                        " TBw " + d.TbWritten + " aviso " + d.CriticalWarning + " reserva " + d.SparePct);
                Say("   dumps legíveis=" + h.DumpsReadable + " total=" + h.Dumps.Count);
                foreach (CrashDump cd in h.Dumps) Say("     " + (cd.Live ? "[live] " : "[BSOD] ") + cd.Time.ToString("dd/MM HH:mm") + " " + cd.CodeHex + " " + cd.File);

                TestRunner tr = new TestRunner(w);
                object[][] plan =
                {
                    new object[] { TestKind.Cpu, 120 * scale, null },
                    new object[] { TestKind.Ram, 300 * scale, null },
                    new object[] { TestKind.Gpu, 180 * scale, null },
                    new object[] { TestKind.Disk, 120 * scale, new DiskTest.Options() },
                    new object[] { TestKind.Combined, 300 * scale, null }
                };
                List<TestResult> results = new List<TestResult>();
                foreach (object[] step in plan)
                {
                    ManualResetEvent done = new ManualResetEvent(false);
                    TestResult fin = null;
                    Action<TestResult> h2 = delegate(TestResult r) { fin = r; done.Set(); };
                    tr.Finished += h2;
                    string refuse;
                    Say("");
                    Say("== Teste: " + TestResult.KindLabel((TestKind)step[0]) + " (" + step[1] + " s)   " + DateTime.Now.ToString("HH:mm:ss"));
                    Stopwatch sw = Stopwatch.StartNew();
                    if (tr.Start((TestKind)step[0], (int)step[1], step[2], false, out refuse) == null) { Say("RECUSADO: " + refuse); tr.Finished -= h2; break; }
                    while (!done.WaitOne(10000)) Progress(w, tr, sw);
                    tr.Finished -= h2;
                    results.Add(fin);
                    Say("-> " + fin.StatusLabel + " (erros " + fin.Errors + "): " + fin.Summary);
                    foreach (string d in fin.Details) Say("     - " + d);
                    foreach (KeyValuePair<string, double> kv in fin.M) Say("     m." + kv.Key + " = " + kv.Value.ToString("0.###"));
                    if (fin.Get("thermal_abort") == 1) { Say("Bateria interrompida por temperatura."); break; }
                    Thread.Sleep(15000);   // resfriamento entre testes
                }

                Say("");
                Say("== Diagnóstico final");
                List<WinEvent> ev = EventScanner.ScanAll(30, null);
                Diagnosis dg = Analyzer.Run(new List<SessionData>(), ev, new List<DiskHealth>(), 30, TestStore.ListAll(), h);
                Say(dg.Verdict);
                for (int i = 0; i < 8; i++) Say("   " + CompInfo.Names[i].PadRight(28) + dg.States[i].Status + " — " + dg.States[i].Headline);
                foreach (Finding f in dg.Findings) Say("   [" + f.Label + "] " + f.Title);
                w.Stop();
                Say("fim " + DateTime.Now.ToString("HH:mm:ss"));
            }
            catch (Exception e) { Say("EXCEÇÃO: " + e); }
            o.Dispose();
            return 0;
        }

        /// <summary>Somente leitura: fatos de hardware/sistema e diagnóstico atual (sem testes de carga).</summary>
        public static int Facts(string outFile)
        {
            o = new StreamWriter(outFile, false, new UTF8Encoding(false));
            try
            {
                Paths.Ensure();
                Say("Administrador: " + SysUtil.IsAdmin());
                SensorHub hub = new SensorHub();
                hub.Poll(); Thread.Sleep(1500); hub.Poll();
                HwSnapshot h = HwFacts.Collect(hub);
                foreach (Fact f in h.Facts) Say("[" + f.Level + "] " + f.Group + " | " + f.Name + " = " + f.Value + (f.Note.Length > 0 ? "\n      (" + f.Note + ")" : ""));
                if (h.Boot != null)
                {
                    Say("boot: lido=" + h.Boot.Read + " default=" + h.Boot.Default + " ordem=" + string.Join(",", h.Boot.DisplayOrder.ToArray()) + " efi=" + string.Join(",", h.Boot.EfiDisks.ConvertAll<string>(delegate(int x) { return x.ToString(); }).ToArray()) + " windows=" + h.Boot.WindowsDisk);
                    foreach (BootEntry e in h.Boot.Entries) if (e.IsLoader || e.IsBootMgr) Say("   " + e.Id + " | " + e.Description + " | " + e.OsDevice + " | " + e.Path + (h.Boot.Stale().Contains(e) ? "  <- ANTIGA" : ""));
                }
                List<WinEvent> ev = EventScanner.ScanAll(30, null);
                Diagnosis dg = Analyzer.Run(new List<SessionData>(), ev, new List<DiskHealth>(), 30, TestStore.ListAll(), h);
                Say("");
                Say(dg.Verdict);
                for (int i = 0; i < 8; i++) Say("   " + CompInfo.Names[i].PadRight(28) + dg.States[i].Status + " — " + dg.States[i].Headline);
                foreach (Finding f in dg.Findings) Say("   [" + f.Label + "] " + f.Title);
                hub.Close();
            }
            catch (Exception e) { Say("EXCEÇÃO: " + e); }
            o.Dispose();
            return 0;
        }

        public static int Run(string outFile)
        {
            o = new StreamWriter(outFile, false, new UTF8Encoding(false));
            try
            {
                Paths.Ensure();

                // ---- 1. coletor PDH
                Say("== Coletor PDH ==");
                PerfCollector pc = new PerfCollector();
                Say("Contadores indisponíveis: " + pc.Missing + " | RAM total MB: " + pc.TotalRamMB.ToString("F0") + " | VRAM MB: " + pc.TotalVramMB.ToString("F0"));
                Sample s = null;
                Dictionary<int, double> gpu = null;
                Stopwatch sw = new Stopwatch();
                long worst = 0; List<long> times = new List<long>();
                for (int i = 0; i < 6; i++)
                {
                    Thread.Sleep(1000);
                    s = new Sample();
                    sw.Restart();
                    gpu = pc.Collect(s);
                    times.Add(sw.ElapsedMilliseconds); if (sw.ElapsedMilliseconds > worst) worst = sw.ElapsedMilliseconds;
                }
                for (int i = 0; i < F.Count; i++)
                    Say("  " + F.Names[i].PadRight(12) + " = " + (double.IsNaN(s.V[i]) ? "(n/d)" : s.V[i].ToString("F2")));
                Say("  maior custo de uma coleta: " + worst + " ms");
                Check(!double.IsNaN(s.V[F.Cpu]), "uso de CPU lido");
                Check(!double.IsNaN(s.V[F.Ram]), "uso de RAM lido");
                Check(!double.IsNaN(s.V[F.Gpu]), "uso de GPU lido");
                Check(!double.IsNaN(s.V[F.GpuMem]), "VRAM lida");
                Check(!double.IsNaN(s.V[F.DiskLat]), "latência de disco lida");
                times.Sort(); Say("  tempos (ms): " + string.Join(", ", times.ConvertAll<string>(delegate(long x) { return x.ToString(); }).ToArray())); Check(times[times.Count / 2] < 100, "coleta barata (mediana <100 ms)");
                Say("  processos com GPU 3D: " + gpu.Count);
                pc.Dispose();

                // ---- 2. HWiNFO sintético
                Say("== Sensores (parser HWiNFO sintético) ==");
                string[] sn = { "CPU [#0]: AMD Ryzen 5 5500: Enhanced", "GPU [#0]: AMD Radeon RX 7600", "Gigabyte A520M K V2 (ITE IT8688E)", "S.M.A.R.T.: ADATA LEGEND 710" };
                object[][] rd =
                {
                    new object[] { 1, 0, "CPU (Tctl/Tdie)", "°C", 71.5 },
                    new object[] { 1, 0, "CPU IOD Hotspot", "°C", 80.0 },
                    new object[] { 5, 0, "CPU Package Power", "W", 55.2 },
                    new object[] { 6, 0, "Core Clocks (avg)", "MHz", 4200.0 },
                    new object[] { 1, 1, "GPU Temperature", "°C", 66.0 },
                    new object[] { 1, 1, "GPU Hot Spot Temperature", "°C", 82.0 },
                    new object[] { 1, 1, "GPU Memory Junction Temperature", "°C", 90.0 },
                    new object[] { 5, 1, "GPU ASIC Power", "W", 120.0 },
                    new object[] { 6, 1, "GPU Clock", "MHz", 2500.0 },
                    new object[] { 3, 1, "GPU Fan", "RPM", 1450.0 },
                    new object[] { 2, 2, "+12V", "V", 11.9 },
                    new object[] { 2, 2, "+3.3V", "V", 3.3 },
                    new object[] { 1, 3, "Drive Temperature", "°C", 44.0 }
                };
                byte[] buf = HwinfoSource.BuildTestBuffer(sn, rd);
                using (MemoryMappedFile mm = MemoryMappedFile.CreateNew("DiagITTestHw", buf.Length))
                {
                    using (MemoryMappedViewAccessor va = mm.CreateViewAccessor()) va.WriteArray(0, buf, 0, buf.Length);
                    HwinfoSource hw = new HwinfoSource();
                    hw.MapName = "DiagITTestHw";
                    Check(hw.Init(), "HWiNFO: abriu memória compartilhada");
                    List<SensorItem> items = hw.Read();
                    Check(items.Count == rd.Length, "HWiNFO: leu " + items.Count + " de " + rd.Length + " leituras");
                    Sample t = new Sample();
                    SensorHub.Map(items, t);
                    Check(Math.Abs(t.V[F.CpuTemp] - 71.5) < 0.01, "CPU temp = " + t.V[F.CpuTemp]);
                    Check(Math.Abs(t.V[F.CpuPower] - 55.2) < 0.01, "CPU power = " + t.V[F.CpuPower]);
                    Check(Math.Abs(t.V[F.CpuClock] - 4200) < 0.01, "CPU clock = " + t.V[F.CpuClock]);
                    Check(Math.Abs(t.V[F.GpuTemp] - 66) < 0.01, "GPU temp = " + t.V[F.GpuTemp] + " (não pode pegar a junção da VRAM)");
                    Check(Math.Abs(t.V[F.GpuHot] - 82) < 0.01, "GPU hotspot = " + t.V[F.GpuHot]);
                    Check(Math.Abs(t.V[F.GpuPower] - 120) < 0.01, "GPU power = " + t.V[F.GpuPower]);
                    Check(Math.Abs(t.V[F.GpuClock] - 2500) < 0.01, "GPU clock = " + t.V[F.GpuClock]);
                    Check(Math.Abs(t.V[F.GpuFan] - 1450) < 0.01, "GPU fan = " + t.V[F.GpuFan]);
                    Check(Math.Abs(t.V[F.V12] - 11.9) < 0.01, "+12V = " + t.V[F.V12] + " (não pode pegar +3.3V)");
                    Check(Math.Abs(t.V[F.DiskTemp] - 44) < 0.01, "Disk temp = " + t.V[F.DiskTemp]);
                    hw.Close();
                }
                SensorHub realHub = new SensorHub();
                realHub.Poll();
                Say("Fonte de sensores real encontrada: " + (realHub.ActiveName ?? "nenhuma (esperado se HWiNFO/LHM não estiver aberto)"));
                realHub.Close();

                // ---- 3. eventos do Windows
                Say("== Eventos do Windows (30 dias) ==");
                sw.Restart();
                List<WinEvent> ev = EventScanner.ScanAll(30, null);
                Say("  " + ev.Count + " eventos relevantes em " + sw.ElapsedMilliseconds + " ms");
                Dictionary<string, int> cnt = new Dictionary<string, int>();
                foreach (WinEvent e in ev) { string k = e.Provider + " #" + e.Id; int n; cnt.TryGetValue(k, out n); cnt[k] = n + 1; }
                foreach (KeyValuePair<string, int> kv in cnt) Say("    " + kv.Key + " ×" + kv.Value);

                Say("== Análise REAL só com eventos do seu PC =="); Diagnosis real = Analyzer.Run(new List<SessionData>(), ev, new List<DiskHealth>(), 30); Say("  " + real.Verdict); for (int q = 0; q < 8; q++) Say("  " + CompInfo.Names[q].PadRight(28) + real.Score[q] + "%"); foreach (Finding rf in real.Findings) Say("  [" + rf.Sev + "] " + rf.Title + "\n      " + rf.Evidence.Replace("\n", "\n      "));
                // ---- 4. discos e máquina
                Say("== Discos / máquina ==");
                List<DiskHealth> dh = DiskInfo.Read();
                foreach (DiskHealth h in dh) Say("  " + h.Name + " [" + h.MediaType + "] saúde=" + h.Health + " temp=" + h.Temp + " desgaste=" + h.Wear + " horas=" + h.PowerOnHours);
                Check(dh.Count > 0, "discos lidos");
                MachineInfo mi = MachineInfo.Read(double.NaN);
                Say("  CPU: " + mi.Cpu + " | GPU: " + mi.Gpu + " " + mi.GpuDriver + " | Placa: " + mi.Board + " | " + mi.Os + " | RAM " + mi.RamGB.ToString("F1") + " GB");

                // ---- 5. gravação e leitura de sessão
                Say("== Armazenamento ==");
                SessionInfo si = new SessionInfo();
                si.Id = "selftest_" + DateTime.Now.ToString("HHmmss");
                si.Game = "jogo_teste.exe"; si.Start = DateTime.Now.AddMinutes(-10);
                si.Save();
                List<Sample> synth = new List<Sample>();
                using (SessionWriter w = new SessionWriter(si.CsvPath))
                {
                    for (int i = 0; i < 300; i++)
                    {
                        Sample x = new Sample();
                        x.Time = si.Start.AddSeconds(i * 2);
                        x.V[F.Cpu] = 90; x.V[F.Gpu] = 99;
                        x.V[F.CpuTemp] = 70 + i * 0.1; x.V[F.GpuTemp] = 70 + i * 0.12; x.V[F.GpuHot] = 85 + i * 0.1;
                        x.V[F.GpuClock] = 2600 - i * 1.2; x.V[F.CpuClock] = 4300 - i * 1.5;
                        x.V[F.V12] = 11.2;
                        x.V[F.Gap] = 2;
                        synth.Add(x);
                        w.Append(x);
                    }
                }
                List<Sample> back = SessionStore.LoadSamples(si);
                Check(back.Count == 300, "CSV: gravou e releu 300 amostras (" + back.Count + ")");
                Check(double.IsNaN(back[0].V[F.DiskLat]), "CSV: NaN preservado");
                Check(Math.Abs(back[10].V[F.CpuTemp] - synth[10].V[F.CpuTemp]) < 0.01, "CSV: valores preservados");
                List<SessionInfo> rec = SessionStore.RecoverInterrupted();
                SessionInfo after = SessionInfo.LoadMeta(si.MetaPath);
                Check(after.Status != "running", "sessão 'running' foi recuperada (" + after.Status + ": sem reboot = app fechado, não queda)");
                after.Status = "interrupted"; after.Save();

                // ---- 6. análise sintética
                Say("== Análise sintética (sessão interrompida sob carga, 12V baixo, GPU esquentando) ==");
                SessionInfo loaded = SessionInfo.LoadMeta(si.MetaPath);
                SessionData sd = new SessionData(); sd.Info = loaded; sd.S = back;
                List<SessionData> list = new List<SessionData>(); list.Add(sd);
                Diagnosis dg = Analyzer.Run(list, ev, dh, 30);
                Say("  Veredito: " + dg.Verdict);
                for (int i = 0; i < 8; i++) Say("  " + CompInfo.Names[i].PadRight(28) + dg.Score[i] + "%");
                foreach (Finding f in dg.Findings) Say("  [" + f.Sev + "] " + f.Title);
                Check(dg.Score[(int)Comp.Psu] >= 40, "fonte aparece como suspeita (>=40%)");
                Check(dg.Findings.Exists(delegate(Finding f) { return f.Title.Contains("interrompida"); }), "sessão interrompida detectada");

                // ---- 7. diagnóstico a partir de testes (simulados) + fatos de hardware reais
                Say("== Diagnóstico com resultados de teste simulados ==");
                List<TestResult> tests = new List<TestResult>();
                TestResult tc = new TestResult(); tc.Id = "a"; tc.Kind = TestKind.Cpu; tc.Start = DateTime.Now.AddHours(-3); tc.End = tc.Start.AddMinutes(10); tc.Status = "passed";
                tc.Summary = "Nenhum erro em 20.000 cálculos."; tc.Set("t_cpu_temp_max", 78); tc.Set("t_cpu_clock_early", 4200); tc.Set("t_cpu_clock_late", 4150);
                TestResult tg = new TestResult(); tg.Id = "b"; tg.Kind = TestKind.Gpu; tg.Start = DateTime.Now.AddHours(-2); tg.End = tg.Start.AddMinutes(4); tg.Status = "failed";
                tg.Summary = "A GPU TRAVOU durante o teste."; tg.Set("device_removed", 1); tg.Set("t_gpu_temp_max", 74); tg.Set("t_gpu_hot_max", 88); tg.Set("t_gpu_fan_max", 1600);
                TestResult tb = new TestResult(); tb.Id = "c"; tb.Kind = TestKind.Combined; tb.Start = DateTime.Now.AddHours(-1); tb.End = tb.Start.AddMinutes(3); tb.Status = "crashed";
                tb.Summary = "O PC desligou durante o teste."; tb.Set("t_cpu_temp_max", 80); tb.Set("t_gpu_hot_max", 90); tb.Set("t_v12_min", 11.2);
                tests.Add(tb); tests.Add(tg); tests.Add(tc);
                HwSnapshot hs = HwFacts.Collect(null);
                Say("  fatos de hardware lidos: " + hs.Facts.Count);
                foreach (Fact fct in hs.Facts) Say("    [" + fct.Level + "] " + fct.Group + " | " + fct.Name + " = " + fct.Value + (fct.Note.Length > 0 ? "  (" + fct.Note + ")" : ""));
                Diagnosis dt = Analyzer.Run(new List<SessionData>(), ev, new List<DiskHealth>(), 30, tests, hs);
                Say("  Veredito:\n" + dt.Verdict);
                for (int i = 0; i < 8; i++) Say("  " + CompInfo.Names[i].PadRight(28) + dt.States[i].Status + " — " + dt.States[i].Headline);
                foreach (Finding f in dt.Findings) Say("  [" + f.Label + "] " + f.Title);
                Check(dt.States[(int)Comp.Gpu].Status == CompStatus.Failed, "GPU marcada como PROBLEMA (travou no teste)");
                Check(dt.States[(int)Comp.Psu].Status == CompStatus.Failed, "Fonte marcada como PROBLEMA (PC desligou no teste combinado)");
                Check(dt.States[(int)Comp.Cpu].Status == CompStatus.Ok, "CPU marcada como OK (teste aprovado)");
                Check(dt.Verdict.StartsWith("PROBLEMA CONFIRMADO"), "veredito concreto");
                string html = Report.Build(dt, MachineInfo.Read(double.NaN), new List<SessionData>());
                Check(html.Contains("Suas peças") && html.Contains("Com defeito"), "relatório HTML com estado por componente");

                SessionStore.Delete(si);
                Say(fails == 0 ? "\nTODOS OS TESTES PASSARAM" : "\n" + fails + " FALHA(S)");
            }
            catch (Exception e)
            {
                Say("EXCEÇÃO: " + e);
                fails++;
            }
            o.Dispose();
            return fails;
        }
    }
}
