using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace DiagIT
{
    public enum TestKind { Cpu, Ram, Gpu, Disk, Combined }

    /// <summary>Resultado de um teste. Persistido em %LOCALAPPDATA%\DiagIT\testes\*.test</summary>
    public class TestResult
    {
        public string Id = "";
        public TestKind Kind;
        public DateTime Start;
        public DateTime? End;
        public int PlannedSec;
        public string Status = "running";   // running | passed | failed | aborted | crashed | error
        public int Errors;
        public string Summary = "";
        public string SessionId = "";
        public List<string> Details = new List<string>();
        public Dictionary<string, double> M = new Dictionary<string, double>();
        readonly object gate = new object();

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public string FilePath { get { return Path.Combine(Paths.Tests, Id + ".test"); } }

        public static string KindLabel(TestKind k)
        {
            switch (k)
            {
                case TestKind.Cpu: return "Processador (CPU)";
                case TestKind.Ram: return "Memória RAM";
                case TestKind.Gpu: return "Placa de vídeo e VRAM";
                case TestKind.Disk: return "Armazenamento (SSD/HD)";
                default: return "Fonte de energia (carga máxima)";
            }
        }

        public string StatusLabel
        {
            get
            {
                switch (Status)
                {
                    case "running": return "Em andamento";
                    case "passed": return "APROVADO";
                    case "failed": return "REPROVADO";
                    case "aborted": return "Interrompido";
                    case "crashed": return "PC CAIU DURANTE O TESTE";
                    default: return "Erro ao executar";
                }
            }
        }

        public double Get(string k) { double v; lock (gate) return M.TryGetValue(k, out v) ? v : double.NaN; }
        public void Set(string k, double v) { lock (gate) M[k] = v; }
        public void Detail(string s) { lock (gate) { if (Details.Count < 400) Details.Add(s); } }
        public List<string> DetailsCopy() { lock (gate) return new List<string>(Details); }

        public void Save()
        {
            try
            {
                Paths.Ensure();
                List<string> l = new List<string>();
                l.Add("id=" + Id);
                l.Add("kind=" + Kind);
                l.Add("start=" + SessionInfo.ToUnix(Start));
                if (End.HasValue) l.Add("end=" + SessionInfo.ToUnix(End.Value));
                l.Add("planned=" + PlannedSec);
                l.Add("status=" + Status);
                l.Add("errors=" + Errors);
                l.Add("session=" + SessionId);
                l.Add("summary=" + Esc(Summary));
                lock (gate)
                {
                    foreach (KeyValuePair<string, double> kv in M)
                        if (!double.IsNaN(kv.Value)) l.Add("m." + kv.Key + "=" + kv.Value.ToString("R", Inv));
                    foreach (string d in Details) l.Add("d=" + Esc(d));
                }
                File.WriteAllLines(FilePath, l.ToArray(), Encoding.UTF8);
            }
            catch (Exception e) { Log.Write("Salvar teste: " + e.Message); }
        }

        static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"); }
        static string Unesc(string s) { return s.Replace("\\n", "\n").Replace("\\\\", "\\"); }

        public static TestResult Load(string path)
        {
            try
            {
                TestResult r = new TestResult();
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    double d;
                    if (k.StartsWith("m."))
                    {
                        if (double.TryParse(v, NumberStyles.Float, Inv, out d)) r.M[k.Substring(2)] = d;
                        continue;
                    }
                    switch (k)
                    {
                        case "id": r.Id = v; break;
                        case "kind": try { r.Kind = (TestKind)Enum.Parse(typeof(TestKind), v); } catch { } break;
                        case "start": if (double.TryParse(v, NumberStyles.Float, Inv, out d)) r.Start = SessionInfo.FromUnix(d); break;
                        case "end": if (double.TryParse(v, NumberStyles.Float, Inv, out d)) r.End = SessionInfo.FromUnix(d); break;
                        case "planned": int.TryParse(v, out r.PlannedSec); break;
                        case "status": r.Status = v; break;
                        case "errors": int.TryParse(v, out r.Errors); break;
                        case "session": r.SessionId = v; break;
                        case "summary": r.Summary = Unesc(v); break;
                        case "d": r.Details.Add(Unesc(v)); break;
                    }
                }
                return r.Id.Length > 0 ? r : null;
            }
            catch { return null; }
        }
    }

    public static class TestStore
    {
        public static string MarkerPath { get { return Path.Combine(Paths.Root, "teste_em_andamento.txt"); } }

        public static List<TestResult> ListAll()
        {
            List<TestResult> r = new List<TestResult>();
            try
            {
                Paths.Ensure();
                foreach (string f in Directory.GetFiles(Paths.Tests, "*.test"))
                {
                    TestResult t = TestResult.Load(f);
                    if (t != null) r.Add(t);
                }
            }
            catch { }
            r.Sort(delegate(TestResult a, TestResult b) { return b.Start.CompareTo(a.Start); });
            return r;
        }

        /// <summary>Teste marcado como "em andamento" na inicialização = o PC caiu/travou durante o teste.</summary>
        public static TestResult RecoverCrashed()
        {
            try
            {
                if (!File.Exists(MarkerPath)) return null;
                string id = File.ReadAllText(MarkerPath).Trim();
                File.Delete(MarkerPath);
                string f = Path.Combine(Paths.Tests, id + ".test");
                TestResult r = File.Exists(f) ? TestResult.Load(f) : null;
                if (r == null || r.Status != "running") return null;
                // últimas leituras da telemetria gravada durante o teste
                SessionInfo si = SessionInfo.LoadMeta(Path.Combine(Paths.Sessions, r.SessionId + ".meta"));
                List<Sample> smp = si != null ? SessionStore.LoadSamples(si) : new List<Sample>();
                r.End = smp.Count > 0 ? smp[smp.Count - 1].Time : r.Start;
                TimeSpan el = r.End.Value - r.Start;
                TestRunner.FillTelemetry(r, smp);
                if (SysUtil.BootTime() < r.Start)
                {
                    // o Windows não reiniciou: foi o DiagIT que foi fechado à força durante o teste
                    r.Status = "aborted";
                    r.Summary = "Teste interrompido após " + ((int)el.TotalMinutes) + " min " + el.Seconds + " s porque o DiagIT foi fechado (o PC não reiniciou).";
                    r.Save();
                    return null;
                }
                r.Status = "crashed";
                StringBuilder sb = new StringBuilder();
                sb.Append("O PC desligou, reiniciou ou travou durante o teste, após " + ((int)el.TotalMinutes) + " min " + el.Seconds + " s.");
                if (smp.Count > 0)
                {
                    List<Sample> tail = smp.GetRange(Math.Max(0, smp.Count - 5), Math.Min(5, smp.Count));
                    sb.Append(" Últimas leituras: " + TestRunner.Snapshot(tail));
                }
                r.Summary = sb.ToString();
                r.Errors = Math.Max(1, r.Errors);
                r.Save();
                return r;
            }
            catch (Exception e) { Log.Write("RecoverCrashed: " + e.Message); return null; }
        }

        public static void Delete(TestResult t)
        {
            try { if (File.Exists(t.FilePath)) File.Delete(t.FilePath); } catch { }
        }
    }

    /// <summary>Estado compartilhado entre o teste em execução e a interface.</summary>
    public class TestContext
    {
        public volatile bool Cancel;
        public double Progress;          // 0..1
        public string Phase = "";
        public int Errors;
        public Watcher W;
        public object Options;

        public void AddError() { Interlocked.Increment(ref Errors); }
        public bool Sleep(int ms)
        {
            int left = ms;
            while (left > 0 && !Cancel) { Thread.Sleep(Math.Min(200, left)); left -= 200; }
            return !Cancel;
        }
    }

    public abstract class HwTest
    {
        public abstract TestKind Kind { get; }
        /// <summary>Executa o teste. Deve preencher r.Summary, r.Errors, métricas e decidir aprovado/reprovado.</summary>
        public abstract void Run(TestContext ctx, TestResult r, int seconds);
    }

    public class TestRunner
    {
        public TestResult Current;
        public TestContext Ctx;
        readonly Watcher w;
        Thread th;
        public event Action<TestResult> Finished;

        public TestRunner(Watcher w) { this.w = w; }

        public bool Busy { get { return Current != null; } }

        public static HwTest Create(TestKind k)
        {
            switch (k)
            {
                case TestKind.Cpu: return new CpuTest();
                case TestKind.Ram: return new RamTest();
                case TestKind.Gpu: return new GpuTest();
                case TestKind.Disk: return new DiskTest();
                default: return new CombinedTest();
            }
        }

        /// <summary>Inicia um teste. Recusa (retorna null e explica em "refuse") se houver jogo aberto, a menos que force=true.</summary>
        public TestResult Start(TestKind kind, int seconds, object options, bool force, out string refuse)
        {
            refuse = null;
            if (Busy) { refuse = "Já existe um teste em andamento."; return null; }
            string game;
            if (!force && w.GameActive(out game))
            {
                refuse = "Há um jogo aberto (" + game + "). Os testes usam o hardware no máximo e derrubariam o desempenho do jogo — feche o jogo antes de testar.";
                return null;
            }
            return Start(kind, seconds, options);
        }

        public TestResult Start(TestKind kind, int seconds, object options)
        {
            if (Busy) return null;
            TestResult r = new TestResult();
            r.Kind = kind;
            r.Start = DateTime.Now;
            r.Id = r.Start.ToString("yyyyMMdd_HHmmss") + "_" + kind.ToString().ToLowerInvariant();
            r.PlannedSec = seconds;
            Current = r;
            Ctx = new TestContext();
            Ctx.W = w;
            Ctx.Options = options;
            TestContext ctx = Ctx;
            HwTest test = Create(kind);

            th = new Thread(delegate() { RunInner(test, ctx, r, seconds); });
            th.IsBackground = true;
            th.Name = "DiagIT.Teste";
            th.Start();
            return r;
        }

        public void Stop()
        {
            if (Ctx != null) Ctx.Cancel = true;
        }

        void RunInner(HwTest test, TestContext ctx, TestResult r, int seconds)
        {
            SessionInfo si = null;
            try
            {
                Paths.Ensure();
                if (w.Current != null) w.EndSession("Encerrada para iniciar um teste");
                si = w.StartTestSession("[Teste] " + TestResult.KindLabel(r.Kind));
                r.SessionId = si.Id;
                r.Save();
                File.WriteAllText(TestStore.MarkerPath, r.Id);
                w.FastMode = true;

                Exception fail = null;
                Thread worker = new Thread(delegate()
                {
                    try { test.Run(ctx, r, seconds); }
                    catch (Exception e) { fail = e; }
                });
                worker.IsBackground = true;
                worker.Priority = ThreadPriority.BelowNormal;
                worker.Start();

                // vigia térmica enquanto o teste roda
                int cpuHot = 0, gpuHot = 0;
                while (!worker.Join(1000))
                {
                    Sample s = w.Last;
                    if (s == null) continue;
                    double ct = s[F.CpuTemp], gh = s[F.GpuHot], gt = s[F.GpuTemp];
                    cpuHot = (!double.IsNaN(ct) && ct >= 95) ? cpuHot + 1 : 0;
                    gpuHot = ((!double.IsNaN(gh) && gh >= 110) || (!double.IsNaN(gt) && gt >= 100)) ? gpuHot + 1 : 0;
                    if (cpuHot >= 8 || gpuHot >= 8)
                    {
                        r.Set("thermal_abort", 1);
                        r.Status = "failed";
                        r.Detail(cpuHot >= 8 ? "Teste interrompido por segurança: CPU acima de 95 °C por 8 s." : "Teste interrompido por segurança: GPU em temperatura crítica por 8 s.");
                        ctx.Cancel = true;
                    }
                }
                if (fail != null)
                {
                    r.Status = "error";
                    r.Summary = "Erro ao executar o teste: " + fail.Message;
                    Log.Write("Teste " + r.Kind + ": " + fail);
                }
            }
            catch (Exception e)
            {
                r.Status = "error";
                r.Summary = "Erro ao preparar o teste: " + e.Message;
                Log.Write("Teste: " + e);
            }
            finally
            {
                w.FastMode = false;
                r.End = DateTime.Now;
                if (r.Status == "running") r.Status = ctx.Cancel ? "aborted" : "passed";
                if (si != null)
                {
                    List<Sample> smp = SessionStore.LoadSamples(si);
                    FillTelemetry(r, smp);
                    w.EndSession("Teste " + r.StatusLabel.ToLowerInvariant());
                }
                CheckWindowsEvents(r);
                if (r.Get("thermal_abort") == 1 && r.Summary.Length == 0)
                    r.Summary = "Temperatura crítica atingida durante o teste — a refrigeração não dá conta da carga.";
                r.Errors = Math.Max(r.Errors, ctx.Errors);
                r.Save();
                try { if (File.Exists(TestStore.MarkerPath)) File.Delete(TestStore.MarkerPath); } catch { }
                Current = null;
                if (Finished != null) Finished(r);
            }
        }

        /// <summary>Erros de hardware registrados pelo Windows durante o teste também reprovam o teste.</summary>
        static void CheckWindowsEvents(TestResult r)
        {
            try
            {
                List<WinEvent> ev = EventScanner.ScanAll(1, null);
                DateTime a = r.Start.AddSeconds(-5), b = (r.End ?? DateTime.Now).AddSeconds(60);
                int whea = 0, tdr = 0, lke = 0;
                foreach (WinEvent e in ev)
                {
                    if (e.Time < a || e.Time > b) continue;
                    if (e.Provider.IndexOf("WHEA", StringComparison.OrdinalIgnoreCase) >= 0) { whea++; r.Detail("Windows registrou erro de hardware (WHEA " + e.Id + ") às " + e.Time.ToString("HH:mm:ss") + "."); }
                    else if ((e.Provider == "Display" && e.Id == 4101) || e.Provider.IndexOf("amdkmdag", StringComparison.OrdinalIgnoreCase) >= 0 || e.Provider.IndexOf("nvlddmkm", StringComparison.OrdinalIgnoreCase) >= 0)
                    { tdr++; r.Detail("O driver de vídeo parou de responder às " + e.Time.ToString("HH:mm:ss") + "."); }
                    else if (e.Provider == "Windows Error Reporting" && e.Prop(2) == "LiveKernelEvent")
                    { lke++; r.Detail("LiveKernelEvent 0x" + e.Prop(5) + " às " + e.Time.ToString("HH:mm:ss") + "."); }
                }
                r.Set("win_whea", whea);
                r.Set("win_tdr", tdr);
                r.Set("win_lke", lke);
                if ((whea > 0 || tdr > 0) && (r.Status == "passed"))
                {
                    r.Status = "failed";
                    r.Errors += whea + tdr;
                    r.Summary = (r.Summary.Length > 0 ? r.Summary + " " : "") + "Porém o Windows registrou " +
                        (whea > 0 ? whea + " erro(s) de hardware (WHEA)" : "") + (whea > 0 && tdr > 0 ? " e " : "") +
                        (tdr > 0 ? tdr + " travamento(s) do driver de vídeo" : "") + " durante o teste.";
                }
            }
            catch (Exception e) { Log.Write("CheckWindowsEvents: " + e.Message); }
        }

        public static string Snapshot(List<Sample> tail)
        {
            List<string> p = new List<string>();
            Add(p, tail, F.Cpu, "CPU {0:0}%");
            Add(p, tail, F.Gpu, "GPU {0:0}%");
            AddMax(p, tail, F.CpuTemp, "temp. CPU {0:0} °C");
            AddMax(p, tail, F.GpuTemp, "temp. GPU {0:0} °C");
            AddMax(p, tail, F.GpuHot, "hotspot {0:0} °C");
            Add(p, tail, F.CpuPower, "CPU {0:0} W");
            Add(p, tail, F.GpuPower, "GPU {0:0} W");
            AddMin(p, tail, F.V12, "+12 V {0:0.00} V");
            return p.Count == 0 ? "(sem leituras)" : string.Join(", ", p.ToArray()) + ".";
        }

        static void Add(List<string> p, List<Sample> l, int f, string fmt)
        {
            double v = Stat.Avg(Stat.Vals(l, f));
            if (!double.IsNaN(v)) p.Add(string.Format(CultureInfo.CurrentCulture, fmt, v));
        }
        static void AddMax(List<string> p, List<Sample> l, int f, string fmt)
        {
            double v = Stat.Max(Stat.Vals(l, f));
            if (!double.IsNaN(v)) p.Add(string.Format(CultureInfo.CurrentCulture, fmt, v));
        }
        static void AddMin(List<string> p, List<Sample> l, int f, string fmt)
        {
            double v = Stat.Min(Stat.Vals(l, f));
            if (!double.IsNaN(v)) p.Add(string.Format(CultureInfo.CurrentCulture, fmt, v));
        }

        /// <summary>Métricas de telemetria do período do teste (temperaturas, consumo, tensões, clocks).</summary>
        public static void FillTelemetry(TestResult r, List<Sample> smp)
        {
            if (smp.Count == 0) return;
            r.Set("t_cpu_temp_max", Stat.Max(Stat.Vals(smp, F.CpuTemp)));
            r.Set("t_gpu_temp_max", Stat.Max(Stat.Vals(smp, F.GpuTemp)));
            r.Set("t_gpu_hot_max", Stat.Max(Stat.Vals(smp, F.GpuHot)));
            r.Set("t_gpu_mem_temp_max", Stat.Max(Stat.Vals(smp, F.GpuMemTemp)));
            r.Set("t_cpu_power_max", Stat.Max(Stat.Vals(smp, F.CpuPower)));
            r.Set("t_gpu_power_max", Stat.Max(Stat.Vals(smp, F.GpuPower)));
            r.Set("t_v12_min", Stat.Min(Stat.Vals(smp, F.V12)));
            r.Set("t_v12_avg", Stat.Avg(Stat.Vals(smp, F.V12)));
            r.Set("t_v5_min", Stat.Min(Stat.Vals(smp, F.V5)));
            r.Set("t_board_temp_max", Stat.Max(Stat.Vals(smp, F.BoardTemp)));
            r.Set("t_cpu_fan_min", Stat.Min(Stat.Vals(smp, F.CpuFan)));
            r.Set("t_gpu_fan_max", Stat.Max(Stat.Vals(smp, F.GpuFan)));
            r.Set("t_pcie_gen_max", Stat.Max(Stat.Vals(smp, F.PcieGen)));
            r.Set("t_pcie_lanes_max", Stat.Max(Stat.Vals(smp, F.PcieLanes)));
            r.Set("t_cpu_load_avg", Stat.Avg(Stat.Vals(smp, F.Cpu)));
            r.Set("t_gpu_load_avg", Stat.Avg(Stat.Vals(smp, F.Gpu)));
            // clock no começo x fim (queda = throttling)
            int q = Math.Max(1, smp.Count / 4);
            if (smp.Count >= 12)
            {
                r.Set("t_cpu_clock_early", Stat.Avg(Stat.Vals(smp.GetRange(1, q), F.CpuClock)));
                r.Set("t_cpu_clock_late", Stat.Avg(Stat.Vals(smp.GetRange(smp.Count - q, q), F.CpuClock)));
                r.Set("t_gpu_clock_early", Stat.Avg(Stat.Vals(smp.GetRange(1, q), F.GpuClock)));
                r.Set("t_gpu_clock_late", Stat.Avg(Stat.Vals(smp.GetRange(smp.Count - q, q), F.GpuClock)));
            }
            int stalls = 0;
            foreach (Sample s in smp) { double g = s[F.Gap]; if (!double.IsNaN(g) && g > 5 && g < 120) stalls++; }
            r.Set("t_stalls", stalls);
            r.Set("t_samples", smp.Count);
        }
    }
}
