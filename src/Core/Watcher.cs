using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DiagIT
{
    public class Watcher
    {
        public AppSettings Cfg;
        public readonly PerfCollector Perf;
        public readonly SensorHub Sensors = new SensorHub();

        readonly object gate = new object();
        readonly List<Sample> live = new List<Sample>();
        const int MaxLive = 3600;

        Thread th;
        volatile bool stop;
        readonly ManualResetEvent wake = new ManualResetEvent(false);

        public SessionInfo Current;
        SessionWriter writer;
        int gamePid = -1;
        TimeSpan lastGameCpu;
        DateTime lastGameCpuAt;
        DateTime lastTick = DateTime.MinValue;

        int pendingPid = -1;
        DateTime pendingSeen;
        DateTime pendingSince;
        readonly Dictionary<int, string> names = new Dictionary<int, string>();

        public string TopApp = "";
        public int TopPid = -1;
        public double TopScore;
        public volatile bool WindowVisible = true;
        public Sample Last;
        public int TickCostMs;
        public int SamplesTotal;

        public event Action<Sample> SampleReady;
        public event Action<SessionInfo> SessionStarted;
        public event Action<SessionInfo> SessionEnded;

        public Watcher(AppSettings cfg)
        {
            Cfg = cfg;
            Perf = new PerfCollector();
        }

        public void Start()
        {
            th = new Thread(Run);
            th.IsBackground = true;
            th.Name = "DiagIT.Monitor";
            th.Priority = ThreadPriority.BelowNormal;
            th.Start();
        }

        public void Stop()
        {
            stop = true;
            wake.Set();
            if (th != null) th.Join(3000);
            EndSession("Aplicativo encerrado durante a sessão");
            Sensors.Close();
            Perf.Dispose();
        }

        public List<Sample> LiveCopy(int lastSeconds)
        {
            lock (gate)
            {
                if (lastSeconds <= 0 || live.Count == 0) return new List<Sample>(live);
                DateTime from = live[live.Count - 1].Time.AddSeconds(-lastSeconds);
                int i = live.Count - 1;
                while (i > 0 && live[i - 1].Time >= from) i--;
                return live.GetRange(i, live.Count - i);
            }
        }

        void Run()
        {
            while (!stop)
            {
                Stopwatch sw = Stopwatch.StartNew();
                try { Tick(); }
                catch (Exception e) { Log.Write("Tick: " + e); }
                TickCostMs = (int)sw.ElapsedMilliseconds;

                double interval = FastMode ? 1 : Cfg.IntervalSec;
                if (Current == null && !WindowVisible) interval = Math.Max(interval, 5);
                int wait = (int)(interval * 1000) - TickCostMs;
                if (wait < 100) wait = 100;
                wake.WaitOne(wait);
                wake.Reset();
            }
        }

        string NameOf(int pid)
        {
            string n;
            if (names.TryGetValue(pid, out n)) return n;
            try { n = Process.GetProcessById(pid).ProcessName; }
            catch { n = ""; }
            if (names.Count > 500) names.Clear();
            names[pid] = n;
            return n;
        }

        void Tick()
        {
            DateTime now = DateTime.Now;
            Sample s = new Sample();
            s.Time = now;
            if (lastTick != DateTime.MinValue) s.V[F.Gap] = (now - lastTick).TotalSeconds;
            lastTick = now;

            Dictionary<int, double> pidGpu = Perf.Collect(s);
            Sensors.Poll();
            Sensors.Apply(s);
            // sem sensor de clock: estima pela frequência nominal x desempenho do processador
            if (double.IsNaN(s.V[F.CpuClock]) && !double.IsNaN(s.V[F.CpuMhz]) && !double.IsNaN(s.V[F.CpuPerf]))
                s.V[F.CpuClock] = s.V[F.CpuMhz] * s.V[F.CpuPerf] / 100.0;

            // escolhe o candidato a "jogo": lista de jogos conhecidos, app em tela cheia em primeiro plano,
            // ou o maior consumidor de GPU que não esteja na lista de ignorados
            int fgPid = ForegroundPid();
            bool fgFull = fgPid > 0 && IsForegroundFullscreen();
            int cand = -1; double candEff = 0, candRaw = 0; string candName = "";
            foreach (KeyValuePair<int, double> kv in pidGpu)
            {
                string n = NameOf(kv.Key);
                if (n.Length == 0) continue;
                double eff = kv.Value;
                if (Cfg.Games.Contains(n)) { if (kv.Value >= 1) eff = Math.Max(eff, Cfg.MinGpuForGame); }
                else if (Cfg.Ignored.Contains(n)) continue;
                else if (kv.Key == fgPid && fgFull && kv.Value >= 3) eff = Math.Max(eff, Cfg.MinGpuForGame);
                if (eff > candEff) { cand = kv.Key; candEff = eff; candRaw = kv.Value; candName = n; }
            }
            TopPid = cand; TopScore = candRaw; TopApp = candName;

            // detecção automática (com tolerância: oscilações curtas de uso não zeram a contagem)
            SessionInfo started = null, ended = null;
            if (Current == null)
            {
                if (Cfg.AutoDetect && cand > 0 && candEff >= Cfg.MinGpuForGame)
                {
                    pendingSeen = now;
                    if (cand != pendingPid) { pendingPid = cand; pendingSince = now; }
                    else if ((now - pendingSince).TotalSeconds >= Cfg.StartDelaySec)
                    {
                        started = BeginSession(candName, cand, false);
                        pendingPid = -1;
                    }
                }
                else if (pendingPid > 0 && (now - pendingSeen).TotalSeconds > 8) pendingPid = -1;
            }
            else if (gamePid > 0 && !Current.Manual)
            {
                if (!Alive(gamePid)) ended = Current;
            }
            // métricas do processo do jogo
            if (Current != null && gamePid > 0)
            {
                double g;
                if (pidGpu.TryGetValue(gamePid, out g)) s.V[F.GameGpu] = g; else s.V[F.GameGpu] = 0;
                try
                {
                    Process p = Process.GetProcessById(gamePid);
                    TimeSpan cpu = p.TotalProcessorTime;
                    if (lastGameCpuAt != DateTime.MinValue)
                    {
                        double el = (now - lastGameCpuAt).TotalSeconds;
                        if (el > 0.2)
                            s.V[F.GameCpu] = Math.Max(0, Math.Min(100,
                                (cpu - lastGameCpu).TotalSeconds / (el * Environment.ProcessorCount) * 100.0));
                    }
                    lastGameCpu = cpu; lastGameCpuAt = now;
                }
                catch { }
            }

            lock (gate)
            {
                live.Add(s);
                if (live.Count > MaxLive) live.RemoveRange(0, live.Count - MaxLive);
            }
            Last = s;
            SamplesTotal++;

            if (Current != null && writer != null)
            {
                try { writer.Append(s); } catch (Exception e) { Log.Write("Gravação: " + e.Message); }
            }

            if (started != null && SessionStarted != null) SessionStarted(started);
            if (SampleReady != null) SampleReady(s);
            if (ended != null) EndSession(null);
        }

        static bool Alive(int pid)
        {
            try { Process p = Process.GetProcessById(pid); return !p.HasExited; }
            catch { return false; }
        }

        SessionInfo BeginSession(string game, int pid, bool manual)
        {
            SessionInfo si = new SessionInfo();
            si.Start = DateTime.Now;
            si.Id = si.Start.ToString("yyyyMMdd_HHmmss");
            si.Game = game;
            si.Manual = manual;
            si.Status = "running";
            si.GamePid = pid;
            if (!manual || pid > 0) si.Bg = Interference.Running();
            Paths.Ensure();
            si.Save();
            writer = new SessionWriter(si.CsvPath);
            gamePid = pid;
            lastGameCpuAt = DateTime.MinValue;
            Current = si;
            Log.Write("Sessão iniciada: " + si.Id + " (" + game + ")");
            return si;
        }

        public volatile bool FastMode;

        /// <summary>Há um jogo aberto? (sessão de jogo em gravação ou processo da lista de jogos conhecidos rodando)</summary>
        public bool GameActive(out string name)
        {
            name = "";
            SessionInfo cur = Current;
            if (cur != null && cur.Note != "teste" && !cur.Game.StartsWith("[Teste]")) { name = cur.Game; return true; }
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    string n = p.ProcessName;
                    p.Dispose();
                    if (Cfg.Games.Contains(n)) { name = n; return true; }
                }
            }
            catch { }
            return false;
        }

        /// <summary>Sessão de telemetria de um teste de hardware (não é encerrada pela detecção de jogos).</summary>
        public SessionInfo StartTestSession(string label)
        {
            if (Current != null) EndSession("Encerrada para iniciar um teste");
            SessionInfo si = BeginSession(label, -1, true);
            si.Note = "teste";
            try { si.Save(); } catch { }
            if (SessionStarted != null) SessionStarted(si);
            return si;
        }

        public SessionInfo StartManual()
        {
            if (Current != null) return Current;
            string label = TopApp.Length > 0 ? TopApp : "Sistema";
            SessionInfo si = BeginSession(label, TopPid, true);
            if (SessionStarted != null) SessionStarted(si);
            return si;
        }

        public void EndSession(string note)
        {
            SessionInfo si = Current;
            if (si == null) return;
            Current = null;
            try { if (writer != null) writer.Dispose(); } catch { }
            writer = null;
            gamePid = -1;
            si.End = DateTime.Now;
            si.Status = "ok";
            if (!string.IsNullOrEmpty(note)) si.Note = note;
            try { si.Save(); } catch (Exception e) { Log.Write("Salvar sessão: " + e.Message); }
            Log.Write("Sessão encerrada: " + si.Id);
            if (SessionEnded != null) SessionEnded(si);

            // verifica (depois de alguns segundos) se o jogo fechou com erro
            if (!si.Manual || si.Game.Length > 0)
            {
                string exe = si.Game;
                DateTime from = si.End.Value.AddMinutes(-2), to = si.End.Value.AddMinutes(3);
                ThreadPool.QueueUserWorkItem(delegate(object o)
                {
                    Thread.Sleep(20000);
                    string crash = EventScanner.FindCrash(exe, from, to);
                    if (crash != null)
                    {
                        si.Note = crash;
                        try { si.Save(); } catch { }
                        if (SessionEnded != null) SessionEnded(si);
                    }
                });
            }
        }

        // -------- utilidades de janela
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>A janela em primeiro plano cobre o monitor inteiro (jogo em tela cheia ou sem bordas)?</summary>
        public static bool IsForegroundFullscreen()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero || h == GetShellWindow()) return false;
                System.Text.StringBuilder cls = new System.Text.StringBuilder(64);
                GetClassName(h, cls, 64);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;
                RECT r;
                if (!GetWindowRect(h, out r)) return false;
                System.Drawing.Rectangle b = System.Windows.Forms.Screen.FromHandle(h).Bounds;
                return r.Left <= b.Left + 1 && r.Top <= b.Top + 1 && r.Right >= b.Right - 1 && r.Bottom >= b.Bottom - 1;
            }
            catch { return false; }
        }

        public static int ForegroundPid()
        {
            uint pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            return (int)pid;
        }
    }
}

