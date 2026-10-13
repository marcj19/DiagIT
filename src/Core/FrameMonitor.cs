using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace DiagIT
{
    /// <summary>
    /// FPS e engasgos do jogo via PresentMon (Intel, MIT; vendor/presentmon). O PresentMon lê os eventos de apresentação de quadros
    /// do próprio Windows (ETW) — não injeta nada no jogo, então é seguro com anti-cheat. Precisa de administrador.
    /// </summary>
    public class FrameMonitor
    {
        Process proc;
        Thread reader;
        int pid = -1;
        readonly object gate = new object();
        // (instante, duração do quadro em ms) dos últimos ~12 s
        readonly Queue<KeyValuePair<DateTime, double>> recent = new Queue<KeyValuePair<DateTime, double>>();
        readonly List<float> all = new List<float>();   // todos os quadros da partida (para 1% low da partida)
        int stuttersTotal, stuttersSinceSnap;
        public string Error = "";

        public static string ExePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PresentMon", "PresentMon.exe"); } }
        public static bool Available { get { return File.Exists(ExePath); } }

        public bool Running { get { Process p = proc; try { return p != null && !p.HasExited; } catch { return false; } } }
        public int Pid { get { return pid; } }

        public bool Start(int targetPid)
        {
            Stop();
            Error = "";
            if (!Available) { Error = "PresentMon não encontrado ao lado do DiagIT."; return false; }
            if (!SysUtil.IsAdmin()) { Error = "Medir FPS precisa do DiagIT aberto como administrador."; return false; }
            lock (gate) { recent.Clear(); all.Clear(); stuttersTotal = stuttersSinceSnap = 0; }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(ExePath,
                    "--process_id " + targetPid + " --output_stdout --no_console_stats --session_name DiagIT_FPS --stop_existing_session --terminate_on_proc_exit");
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                proc = Process.Start(psi);
                pid = targetPid;
                Process p = proc;
                reader = new Thread(delegate() { Read(p); });
                reader.IsBackground = true;
                reader.Start();
                Log.Write("FPS: PresentMon medindo o processo " + targetPid);
                return true;
            }
            catch (Exception e) { Error = "Não foi possível iniciar o PresentMon: " + e.Message; Log.Write("FPS: " + Error); return false; }
        }

        public void Stop()
        {
            Process p = proc;
            proc = null; pid = -1;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.Dispose(); } catch { }
        }

        void Read(Process p)
        {
            try
            {
                StreamReader so = p.StandardOutput;
                string header = so.ReadLine();
                if (header == null) { Error = "PresentMon não retornou dados: " + Trim(p.StandardError.ReadToEnd()); return; }
                string[] cols = header.Split(',');
                int ix = Array.IndexOf(cols, "MsBetweenPresents");
                if (ix < 0) ix = Array.IndexOf(cols, "FrameTime");
                if (ix < 0) { Error = "Formato do PresentMon não reconhecido."; Log.Write("FPS: cabeçalho " + header); return; }
                List<double> win = new List<double>();
                string line;
                while ((line = so.ReadLine()) != null)
                {
                    string[] f = line.Split(',');
                    double ms;
                    if (f.Length <= ix || !double.TryParse(f[ix], NumberStyles.Float, CultureInfo.InvariantCulture, out ms) || ms <= 0) continue;
                    // mais de 1 s sem quadro = pausa (tela de carregamento, menu, alt-tab), não um quadro lento nem um engasgo
                    if (ms > 1000) { win.Clear(); continue; }
                    DateTime now = DateTime.Now;
                    // engasgo: quadro bem mais lento que o ritmo do momento (mediana dos últimos 120) e perceptível (> 50 ms)
                    win.Add(ms); if (win.Count > 120) win.RemoveAt(0);
                    bool stutter = false;
                    if (win.Count >= 30 && ms > 50)
                    {
                        double[] c = win.ToArray(); Array.Sort(c);
                        stutter = ms > 2.5 * c[c.Length / 2];
                    }
                    lock (gate)
                    {
                        recent.Enqueue(new KeyValuePair<DateTime, double>(now, ms));
                        while (recent.Count > 0 && (now - recent.Peek().Key).TotalSeconds > 12) recent.Dequeue();
                        if (all.Count < 4000000) all.Add((float)ms);
                        if (stutter) { stuttersTotal++; stuttersSinceSnap++; }
                    }
                }
            }
            catch (Exception e) { Log.Write("FPS (leitura): " + e.Message); }
        }

        static string Trim(string s) { s = (s ?? "").Trim(); return s.Length > 200 ? s.Substring(0, 200) : s; }

        /// <summary>1% low = FPS médio do 1% de quadros mais lentos.</summary>
        static double Low1(List<double> ms)
        {
            if (ms.Count < 20) return double.NaN;
            double[] c = ms.ToArray(); Array.Sort(c);
            int n = Math.Max(1, c.Length / 100);
            double sum = 0;
            for (int i = c.Length - n; i < c.Length; i++) sum += c[i];
            return 1000.0 / (sum / n);
        }

        /// <summary>Leitura para a amostra atual: FPS do último segundo, 1% low dos últimos 10 s, tempo médio de quadro e engasgos desde a última leitura.</summary>
        public void Snapshot(out double fps, out double low1, out double frameMs, out int stutters)
        {
            fps = low1 = frameMs = double.NaN; stutters = 0;
            lock (gate)
            {
                if (recent.Count == 0) return;
                DateTime now = DateTime.Now;
                List<double> last1 = new List<double>(), last10 = new List<double>();
                foreach (KeyValuePair<DateTime, double> kv in recent)
                {
                    double age = (now - kv.Key).TotalSeconds;
                    if (age <= 10) last10.Add(kv.Value);
                    if (age <= 1) last1.Add(kv.Value);
                }
                if (last10.Count == 0) return;   // jogo minimizado ou parado
                double sum = 0; foreach (double v in last10) sum += v;
                frameMs = sum / last10.Count;
                fps = 1000.0 / frameMs;
                if (last1.Count > 0) { double s1 = 0; foreach (double v in last1) s1 += v; fps = 1000.0 / (s1 / last1.Count); }
                low1 = Low1(last10);
                stutters = stuttersSinceSnap; stuttersSinceSnap = 0;
            }
        }

        /// <summary>FPS do último segundo e 1% low dos últimos 10 s, sem mexer no contador de engasgos (painel no jogo).</summary>
        public void Peek(out double fps, out double low1)
        {
            fps = low1 = double.NaN;
            List<double> l10 = RecentFrames(10), l1 = RecentFrames(1);
            if (l10.Count == 0) return;
            List<double> src = l1.Count > 0 ? l1 : l10;
            double sum = 0; foreach (double v in src) sum += v;
            fps = 1000.0 / (sum / src.Count);
            low1 = Low1(l10);
        }

        /// <summary>Só para a imagem de prévia do painel (--screenshots): quadros de exemplo.</summary>
        public void InjectPreview(IList<double> frameMs)
        {
            lock (gate)
            {
                DateTime t = DateTime.Now;
                for (int i = frameMs.Count - 1; i >= 0; i--) { recent.Enqueue(new KeyValuePair<DateTime, double>(t, frameMs[i])); t = t.AddMilliseconds(-frameMs[i]); }
                List<KeyValuePair<DateTime, double>> l = new List<KeyValuePair<DateTime, double>>(recent);
                l.Sort(delegate(KeyValuePair<DateTime, double> a, KeyValuePair<DateTime, double> b) { return a.Key.CompareTo(b.Key); });
                recent.Clear(); foreach (KeyValuePair<DateTime, double> kv in l) recent.Enqueue(kv);
            }
        }

        /// <summary>Tempos de quadro dos últimos segundos (gráfico do painel no jogo).</summary>
        public List<double> RecentFrames(double seconds)
        {
            List<double> r = new List<double>();
            lock (gate)
            {
                DateTime now = DateTime.Now;
                foreach (KeyValuePair<DateTime, double> kv in recent) if ((now - kv.Key).TotalSeconds <= seconds) r.Add(kv.Value);
            }
            return r;
        }

        /// <summary>Resumo da partida inteira: FPS médio, 1% low e engasgos.</summary>
        public void Summary(out double avgFps, out double low1, out int stutters, out int frames)
        {
            lock (gate)
            {
                frames = all.Count; stutters = stuttersTotal; avgFps = low1 = double.NaN;
                if (all.Count < 20) return;
                double sum = 0; foreach (float v in all) sum += v;
                avgFps = 1000.0 / (sum / all.Count);
                List<double> l = new List<double>(all.Count); foreach (float v in all) l.Add(v);
                low1 = Low1(l);
            }
        }
    }
}
