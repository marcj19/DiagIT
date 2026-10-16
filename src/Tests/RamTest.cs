using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace DiagIT
{
    /// <summary>
    /// Teste de memória: aloca e trava na RAM física a maior quantidade livre possível e grava/confere padrões
    /// (endereço próprio, inverso, xadrez, aleatório e "march" de inversões). Qualquer bit diferente do gravado é defeito
    /// ou instabilidade real da memória (ou do controlador/tensão).
    /// </summary>
    public unsafe class RamTest : HwTest
    {
        public override TestKind Kind { get { return TestKind.Ram; } }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr VirtualAlloc(IntPtr addr, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualFree(IntPtr addr, UIntPtr size, uint type);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualLock(IntPtr addr, UIntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetProcessWorkingSetSizeEx(IntPtr proc, UIntPtr min, UIntPtr max, uint flags);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();

        [StructLayout(LayoutKind.Sequential)]
        struct MEMSTAT
        {
            public uint dwLength, dwMemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtVirtual;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);

        const long CHUNK = 128L * 1024 * 1024;
        const int MAXREC = 40;

        class Chunk { public IntPtr P; public long Words; }

        public class Options { public long MaxMB = 0; }

        List<Chunk> chunks = new List<Chunk>();
        long errors;
        readonly object recGate = new object();
        readonly List<string> records = new List<string>();
        readonly Dictionary<int, int> bitHits = new Dictionary<int, int>();

        /// <summary>Os programas que mais ocupam memória agora (somando todos os processos de cada um), para a pessoa saber o que fechar.</summary>
        /// <summary>Quanto o teste de memória conseguiria usar agora (MB); abaixo de 256 ele não roda.</summary>
        public static long TestableMB()
        {
            MEMSTAT m = new MEMSTAT(); m.dwLength = (uint)Marshal.SizeOf(typeof(MEMSTAT));
            if (!GlobalMemoryStatusEx(ref m)) return -1;
            long t = Math.Min((long)m.AvailPhys - 1536L * 1024 * 1024, (long)m.AvailPageFile - 1024L * 1024 * 1024);
            return Math.Max(0, t >> 20);
        }

        /// <summary>Partes do próprio Windows (e o antivírus dele): ocupam memória, mas não são "programas para fechar".</summary>
        static readonly HashSet<string> SystemProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "System", "Idle", "Registry", "Memory Compression", "svchost", "explorer", "dwm", "csrss", "lsass", "services", "wininit", "winlogon", "smss",
            "MsMpEng", "MpDefenderCoreService", "NisSrv", "SecurityHealthService", "SearchHost", "SearchIndexer", "StartMenuExperienceHost",
            "ShellExperienceHost", "RuntimeBroker", "sihost", "ctfmon", "fontdrvhost", "audiodg", "spoolsv", "WmiPrvSE", "TextInputHost",
            "WidgetService", "Widgets", "dllhost", "conhost", "taskhostw", "LockApp", "ApplicationFrameHost", "SystemSettings", "Taskmgr", "DiagIT", "PresentMon" };

        public static long TotalMB()
        {
            MEMSTAT m = new MEMSTAT(); m.dwLength = (uint)Marshal.SizeOf(typeof(MEMSTAT));
            return GlobalMemoryStatusEx(ref m) ? (long)(m.TotalPhys >> 20) : -1;
        }

        /// <summary>Programas que a pessoa pode fechar e que ocupam bastante memória (todos os processos de cada um somados), com o nome que ela reconhece.</summary>
        public static List<KeyValuePair<string, long>> ClosableApps()
        {
            Dictionary<string, long> by = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> label = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
                    using (p)
                    {
                        try
                        {
                            string n = p.ProcessName;
                            if (SystemProcs.Contains(n)) continue;
                            long v; by.TryGetValue(n, out v); by[n] = v + p.WorkingSet64;
                            if (!label.ContainsKey(n))
                            {
                                string desc = null;
                                try { desc = p.MainModule.FileVersionInfo.FileDescription; } catch { }
                                label[n] = string.IsNullOrEmpty(desc) || desc.Trim().Length < 3 ? n : desc.Trim();
                            }
                        }
                        catch { }
                    }
            }
            catch { }
            List<KeyValuePair<string, long>> l = new List<KeyValuePair<string, long>>();
            foreach (KeyValuePair<string, long> kv in by)
                if (kv.Value >= 300L * 1024 * 1024) l.Add(new KeyValuePair<string, long>(label.ContainsKey(kv.Key) ? label[kv.Key] : kv.Key, kv.Value));
            l.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b) { return b.Value.CompareTo(a.Value); });
            if (l.Count > 4) l.RemoveRange(4, l.Count - 4);
            return l;
        }

        public static string TopMemoryUsers()
        {
            List<KeyValuePair<string, long>> l = ClosableApps();
            if (l.Count == 0) return "";
            List<string> t = new List<string>();
            for (int i = 0; i < Math.Min(3, l.Count); i++) t.Add(l[i].Key + " " + (l[i].Value / 1073741824.0).ToString("0.0", new System.Globalization.CultureInfo("pt-BR")) + " GB");
            return " (os que mais usam: " + string.Join(", ", t.ToArray()) + ")";
        }

        public override void Run(TestContext ctx, TestResult r, int seconds)
        {
            MEMSTAT m = new MEMSTAT(); m.dwLength = (uint)Marshal.SizeOf(typeof(MEMSTAT));
            GlobalMemoryStatusEx(ref m);
            long availPhys = (long)m.AvailPhys, availCommit = (long)m.AvailPageFile;
            long reserve = 1536L * 1024 * 1024;
            long target = Math.Min(availPhys - reserve, availCommit - 1024L * 1024 * 1024);
            Options opt = ctx.Options as Options;
            r.Set("ram_total_mb", m.TotalPhys / 1048576.0);
            r.Set("ram_free_mb", availPhys / 1048576.0);
            r.Set("commit_free_mb", availCommit / 1048576.0);

            if (target < 256L * 1024 * 1024)
            {
                r.Status = "error";
                r.Summary = "Memória livre insuficiente para testar (RAM livre " + (availPhys >> 20) + " MB, memória virtual livre " + (availCommit >> 20) + " MB). " +
                    (availPhys - reserve < 256L * 1024 * 1024 ? "Os programas abertos estão ocupando a memória" + TopMemoryUsers() + ": feche-os e rode de novo." :
                     "Feche programas e, se a memória virtual estiver quase esgotada, aumente o arquivo de paginação na página Resolver.");
                return;
            }
            if (opt != null && opt.MaxMB > 0) target = Math.Min(target, opt.MaxMB * 1024 * 1024);
            if (availCommit - 1024L * 1024 * 1024 < availPhys - reserve)
                r.Detail("A memória virtual livre (" + (availCommit >> 20) + " MB) limita o teste a menos do que a RAM física livre (" + (availPhys >> 20) +
                    " MB). Ajustar a memória virtual (página Resolver) permite testar mais RAM.");

            ctx.Phase = "Reservando memória";
            try
            {
                SetProcessWorkingSetSizeEx(GetCurrentProcess(), new UIntPtr((ulong)(target + 256L * 1024 * 1024)),
                    new UIntPtr((ulong)(target + 512L * 1024 * 1024)), 0);
                long got = 0; int locked = 0;
                while (got + CHUNK <= target && !ctx.Cancel)
                {
                    IntPtr p = VirtualAlloc(IntPtr.Zero, new UIntPtr((ulong)CHUNK), 0x1000 | 0x2000, 0x04);
                    if (p == IntPtr.Zero) break;
                    if (VirtualLock(p, new UIntPtr((ulong)CHUNK))) locked++;
                    Chunk c = new Chunk(); c.P = p; c.Words = CHUNK / 8;
                    chunks.Add(c);
                    got += CHUNK;
                }
                double testedMb = got / 1048576.0;
                r.Set("tested_mb", testedMb);
                r.Set("locked_pct", chunks.Count == 0 ? 0 : 100.0 * locked / chunks.Count);
                r.Detail("Memória sob teste: " + testedMb.ToString("N0") + " MB de " + (m.TotalPhys / 1048576.0).ToString("N0") + " MB instalados (" +
                    (100.0 * got / m.TotalPhys).ToString("0") + "%). " + (locked == chunks.Count ? "Toda travada na RAM física." :
                    "Parte não pôde ser travada (" + locked + "/" + chunks.Count + " blocos) — o Windows pode mover parte dela para o disco."));
                if (chunks.Count == 0) { r.Status = "error"; r.Summary = "Não foi possível reservar memória para o teste."; return; }

                int threads = Math.Max(1, Math.Min(Environment.ProcessorCount, chunks.Count));
                DateTime start = DateTime.Now, end = start.AddSeconds(seconds);
                int pass = 0;
                string[] names = { "endereço próprio", "endereço invertido", "xadrez", "aleatório", "march (inversões)" };
                while (!ctx.Cancel && (DateTime.Now < end || pass == 0))
                {
                    for (int pat = 0; pat < 5 && !ctx.Cancel; pat++)
                    {
                        ctx.Phase = "Passada " + (pass + 1) + ": padrão " + names[pat];
                        ulong key = (ulong)(pass * 5 + pat + 1) * 0x9E3779B97F4A7C15UL;
                        int patc = pat;
                        Parallel(threads, delegate(Chunk c, int ci) { RunPattern(patc, c, ci, key); }, ctx);
                        ctx.Progress = Math.Min(1, (DateTime.Now - start).TotalSeconds / seconds);
                        if (DateTime.Now >= end && pass > 0) break;
                    }
                    pass++;
                }
                r.Set("passes", pass);
                r.Set("bit_errors", errors);
                foreach (string s in records) r.Detail(s);
                if (bitHits.Count > 0)
                {
                    List<string> bits = new List<string>();
                    foreach (KeyValuePair<int, int> kv in bitHits) bits.Add("bit " + kv.Key + " (" + kv.Value + "×)");
                    r.Detail("Bits afetados: " + string.Join(", ", bits.ToArray()) + ".");
                }

                double gb = got / 1073741824.0;
                if (errors > 0)
                {
                    r.Status = "failed";
                    r.Errors = (int)Math.Min(int.MaxValue, errors);
                    string pattern = bitHits.Count == 1 ? " Sempre o mesmo bit falha — típico de chip de memória com defeito físico." :
                        " Bits diferentes falhando — típico de instabilidade (frequência/tensão/XMP) ou pente com defeito.";
                    r.Summary = errors.ToString("N0") + " erro(s) de memória em " + gb.ToString("0.0") + " GB testados (" + pass + " passada(s))." + pattern;
                }
                else if (!ctx.Cancel || pass > 0)
                {
                    r.Status = ctx.Cancel ? "aborted" : "passed";
                    r.Summary = "Nenhum erro em " + gb.ToString("0.0") + " GB (" + (100.0 * got / m.TotalPhys).ToString("0") + "% da RAM) após " + pass +
                        " passada(s) de 5 padrões. A parte da RAM em uso pelo Windows não pôde ser testada; para cobertura total use o teste de memória do Windows (página Resolver).";
                }
            }
            finally
            {
                foreach (Chunk c in chunks) VirtualFree(c.P, UIntPtr.Zero, 0x8000);
                chunks.Clear();
                SetProcessWorkingSetSizeEx(GetCurrentProcess(), new UIntPtr(ulong.MaxValue), new UIntPtr(ulong.MaxValue), 0);
            }
        }

        delegate void ChunkWork(Chunk c, int index);

        void Parallel(int threads, ChunkWork work, TestContext ctx)
        {
            int next = -1;
            Thread[] ths = new Thread[threads];
            for (int t = 0; t < threads; t++)
            {
                ths[t] = new Thread(delegate()
                {
                    int i;
                    while (!ctx.Cancel && (i = Interlocked.Increment(ref next)) < chunks.Count) work(chunks[i], i);
                });
                ths[t].IsBackground = true;
                ths[t].Priority = ThreadPriority.BelowNormal;
                ths[t].Start();
            }
            foreach (Thread th in ths) th.Join();
        }

        void Report(Chunk c, long i, ulong expected, ulong got)
        {
            Interlocked.Increment(ref errors);
            ulong diff = expected ^ got;
            lock (recGate)
            {
                for (int b = 0; b < 64; b++)
                    if (((diff >> b) & 1) != 0) { int n; bitHits.TryGetValue(b, out n); bitHits[b] = n + 1; }
                if (records.Count < MAXREC)
                    records.Add("ERRO no endereço 0x" + (c.P.ToInt64() + i * 8).ToString("X") + ": gravado " + expected.ToString("X16") +
                        ", lido " + got.ToString("X16") + " (bits diferentes: " + diff.ToString("X16") + ")");
            }
        }

        static ulong Rnd(ref ulong s) { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return s; }

        void RunPattern(int pat, Chunk c, int ci, ulong key)
        {
            ulong* p = (ulong*)c.P.ToPointer();
            long n = c.Words;
            switch (pat)
            {
                case 0: // endereço próprio
                    for (long i = 0; i < n; i++) p[i] = (ulong)(p + i) ^ key;
                    for (long i = 0; i < n; i++) { ulong e = (ulong)(p + i) ^ key; if (p[i] != e) Report(c, i, e, p[i]); }
                    break;
                case 1: // endereço invertido
                    for (long i = 0; i < n; i++) p[i] = ~((ulong)(p + i) ^ key);
                    for (long i = 0; i < n; i++) { ulong e = ~((ulong)(p + i) ^ key); if (p[i] != e) Report(c, i, e, p[i]); }
                    break;
                case 2: // xadrez e o inverso
                    for (int inv = 0; inv < 2; inv++)
                    {
                        ulong a = inv == 0 ? 0xAAAAAAAAAAAAAAAAUL : 0x5555555555555555UL, b = ~a;
                        for (long i = 0; i < n; i++) p[i] = (i & 1) == 0 ? a : b;
                        for (long i = 0; i < n; i++) { ulong e = (i & 1) == 0 ? a : b; if (p[i] != e) Report(c, i, e, p[i]); }
                    }
                    break;
                case 3: // aleatório (regerado para conferir)
                    {
                        ulong s = key ^ ((ulong)ci * 0xD1B54A32D192ED03UL) | 1;
                        ulong s0 = s;
                        for (long i = 0; i < n; i++) p[i] = Rnd(ref s);
                        s = s0;
                        for (long i = 0; i < n; i++) { ulong e = Rnd(ref s); if (p[i] != e) Report(c, i, e, p[i]); }
                    }
                    break;
                default: // march: escreve P; sobe lendo P e escrevendo ~P; desce lendo ~P e escrevendo P
                    {
                        ulong P = key | 0x8000000000000001UL;
                        for (long i = 0; i < n; i++) p[i] = P;
                        for (long i = 0; i < n; i++) { if (p[i] != P) Report(c, i, P, p[i]); p[i] = ~P; }
                        for (long i = n - 1; i >= 0; i--) { if (p[i] != ~P) Report(c, i, ~P, p[i]); p[i] = P; }
                        for (long i = 0; i < n; i++) { if (p[i] != P) Report(c, i, P, p[i]); }
                    }
                    break;
            }
        }
    }
}
