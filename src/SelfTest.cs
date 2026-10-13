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
                Say("");
                Say(fails == 0 ? "TUDO OK" : fails + " FALHA(S)");
                return fails == 0 ? 0 : 1;
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
                Check(html.Contains("Estado de cada componente") && html.Contains("PROBLEMA"), "relatório HTML com estado por componente");

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
