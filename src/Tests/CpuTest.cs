using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DiagIT
{
    /// <summary>
    /// Estresse com verificação: cada núcleo lógico calcula pacotes determinísticos (SIMD, inteiros, raiz, divisão, cache)
    /// e compara o resultado bit a bit com a referência. CPU estável nunca erra; qualquer divergência é erro real de cálculo.
    /// </summary>
    public class CpuTest : HwTest
    {
        public override TestKind Kind { get { return TestKind.Cpu; } }

        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] static extern UIntPtr SetThreadAffinityMask(IntPtr h, UIntPtr mask);

        const int UNITS = 48;
        const int SCRATCH = 32768;     // 256 KB de doubles + 256 KB de inteiros (cabe na L2/L3)
        const int ROUNDS = 600;
        const int HOT = 40000;

        public static void Pin(int core)
        {
            Thread.BeginThreadAffinity();
            SetThreadAffinityMask(GetCurrentThread(), new UIntPtr(1UL << core));
        }

        public static void Unpin()
        {
            SetThreadAffinityMask(GetCurrentThread(), new UIntPtr(ulong.MaxValue >> (64 - Math.Min(64, Environment.ProcessorCount))));
            Thread.EndThreadAffinity();
        }

        /// <summary>Um pacote de trabalho determinístico. O mesmo seed sempre produz o mesmo resultado numa CPU estável.</summary>
        public static ulong Unit(int seed, double[] d, ulong[] u)
        {
            ulong h = (ulong)(seed + 1) * 0x9E3779B97F4A7C15UL;
            for (int i = 0; i < d.Length; i++)
            {
                h ^= h << 13; h ^= h >> 7; h ^= h << 17;
                d[i] = (h >> 11) * (1.0 / 9007199254740992.0) + 0.5;
                u[i] = h;
            }
            int vc = Vector<double>.Count;
            Vector<double> a1 = Vector<double>.One, a2 = new Vector<double>(0.25), a3 = new Vector<double>(0.5);
            Vector<double> mul = new Vector<double>(0.999999), add = new Vector<double>(1e-7), half = new Vector<double>(0.5);
            double x = 0.3;
            ulong mask = (ulong)(u.Length - 1);
            for (int rep = 0; rep < ROUNDS; rep++)
            {
                for (int i = 0; i + vc <= d.Length; i += vc * 4)
                {
                    Vector<double> v = new Vector<double>(d, i);
                    a1 = a1 * mul + v * add;
                    a2 = a2 * mul + a1 * v * add;
                    a3 = Vector.SquareRoot(a3 * a3 + v) * half;
                }
                // bloco "quente": 8 cadeias vetoriais independentes, só em registradores (máximo de operações por ciclo)
                Vector<double> h0 = a1, h1 = a2, h2 = a3, h3 = a1 + a2, h4 = a2 + a3, h5 = a1 * half, h6 = a2 * half, h7 = a3 * half;
                for (int k = 0; k < HOT; k++)
                {
                    h0 = h0 * mul + add; h1 = h1 * mul + add; h2 = h2 * mul + add; h3 = h3 * mul + add;
                    h4 = h4 * mul + add; h5 = h5 * mul + add; h6 = h6 * mul + add; h7 = h7 * mul + add;
                }
                a1 = a1 + (h0 + h1 + h2 + h3) * add;
                a2 = a2 + (h4 + h5 + h6 + h7) * add;

                // inteiros + acesso aleatório à cache
                for (int k = 0; k < 2048; k++)
                {
                    h = h * 6364136223846793005UL + 1442695040888963407UL;
                    int idx = (int)((h >> 29) & mask);
                    u[idx] = (u[idx] ^ h) + (ulong)rep;
                    h ^= u[(idx * 7 + 3) & (int)mask] >> 3;
                }
                // escalar com divisão
                for (int k = 0; k < 64; k++) x = x / 1.0000001 + 0.3 / (1.0 + x);
            }
            ulong r = h ^ (ulong)BitConverter.DoubleToInt64Bits(x);
            for (int i = 0; i < vc; i++)
            {
                r = r * 31 + (ulong)BitConverter.DoubleToInt64Bits(a1[i]);
                r = r * 31 + (ulong)BitConverter.DoubleToInt64Bits(a2[i]);
                r = r * 31 + (ulong)BitConverter.DoubleToInt64Bits(a3[i]);
            }
            for (int i = 0; i < u.Length; i += 64) r = r * 1099511628211UL ^ u[i];
            return r;
        }

        public override void Run(TestContext ctx, TestResult r, int seconds)
        {
            int n = Math.Min(64, Environment.ProcessorCount);
            r.Set("threads", n);
            r.Detail("Núcleos lógicos testados: " + n + ". Instruções vetoriais (SIMD): " +
                (Vector.IsHardwareAccelerated ? Vector<double>.Count * 64 + " bits" : "não aceleradas") + ".");

            // ---- referência, calculada duas vezes em núcleos diferentes
            ctx.Phase = "Calculando a referência";
            ulong[] refA = new ulong[UNITS], refB = new ulong[UNITS];
            Stopwatch sw = Stopwatch.StartNew();
            RunOn(0, delegate { double[] d = new double[SCRATCH]; ulong[] u = new ulong[SCRATCH]; for (int i = 0; i < UNITS && !ctx.Cancel; i++) refA[i] = Unit(i, d, u); });
            double unitMs = sw.Elapsed.TotalMilliseconds / UNITS;
            RunOn(n - 1, delegate { double[] d = new double[SCRATCH]; ulong[] u = new ulong[SCRATCH]; for (int i = 0; i < UNITS && !ctx.Cancel; i++) refB[i] = Unit(i, d, u); });
            r.Set("unit_ms", unitMs);
            if (ctx.Cancel) return;   // parado no meio da referência: os cálculos ficaram pela metade, comparar daria erro falso
            int refMismatch = 0;
            for (int i = 0; i < UNITS; i++) if (refA[i] != refB[i]) refMismatch++;
            if (refMismatch > 0)
            {
                r.Detail("Os núcleos 0 e " + (n - 1) + " chegaram a resultados diferentes em " + refMismatch + " de " + UNITS + " cálculos de referência.");
                ctx.AddError();
            }
            if (ctx.Cancel) return;

            // ---- estresse com verificação em todos os núcleos
            ctx.Phase = "Estressando todos os núcleos";
            long[] done = new long[n];
            int[] errs = new int[n];
            DateTime start = DateTime.Now, end = start.AddSeconds(seconds);
            Thread[] ths = new Thread[n];
            for (int t = 0; t < n; t++)
            {
                int core = t;
                ths[t] = new Thread(delegate()
                {
                    Pin(core);
                    try
                    {
                        double[] d = new double[SCRATCH]; ulong[] u = new ulong[SCRATCH];
                        int k = core * 7;
                        while (!ctx.Cancel && DateTime.Now < end)
                        {
                            int seed = k % UNITS; k++;
                            ulong got = Unit(seed, d, u);
                            Interlocked.Increment(ref done[core]);
                            if (got != refA[seed])
                            {
                                int e = Interlocked.Increment(ref errs[core]);
                                ctx.AddError();
                                if (e <= 3) r.Detail("ERRO DE CÁLCULO no núcleo lógico " + core + " às " + DateTime.Now.ToString("HH:mm:ss") +
                                    " (pacote " + seed + ": esperado " + refA[seed].ToString("X16") + ", obtido " + got.ToString("X16") + ").");
                            }
                        }
                    }
                    catch (Exception ex) { r.Detail("Núcleo " + core + " falhou: " + ex.Message); ctx.AddError(); }
                    finally { Unpin(); }
                });
                ths[t].IsBackground = true;
                ths[t].Priority = ThreadPriority.BelowNormal;
                ths[t].Start();
            }
            while (true)
            {
                bool alive = false;
                foreach (Thread t in ths) if (t.IsAlive) alive = true;
                if (!alive) break;
                ctx.Progress = Math.Min(1, (DateTime.Now - start).TotalSeconds / seconds);
                Thread.Sleep(250);
            }

            // ---- resultado
            long total = 0; long minDone = long.MaxValue, maxDone = 0;
            List<string> bad = new List<string>();
            for (int t = 0; t < n; t++)
            {
                total += done[t];
                minDone = Math.Min(minDone, done[t]); maxDone = Math.Max(maxDone, done[t]);
                if (errs[t] > 0) bad.Add(t + " (" + errs[t] + ")");
            }
            r.Set("units_total", total);
            r.Set("units_min_core", minDone);
            r.Set("units_max_core", maxDone);
            double elapsed = (DateTime.Now - start).TotalSeconds;
            r.Detail("Cálculos verificados: " + total.ToString("N0") + " em " + elapsed.ToString("0") + " s (" + (total / Math.Max(1, elapsed)).ToString("0.0") + " por segundo).");
            if (maxDone > 0 && minDone < maxDone * 0.35)
                r.Detail("Um núcleo lógico processou menos que os outros (" + minDone + " x " + maxDone + " cálculos), normalmente porque outro programa estava usando esse núcleo. Não é erro.");

            if (ctx.Errors > 0)
            {
                r.Status = "failed";
                r.Summary = ctx.Errors + " erro(s) de cálculo detectado(s)" + (bad.Count > 0 ? " nos núcleos lógicos " + string.Join(", ", bad.ToArray()) : "") +
                    ". Um processador estável nunca erra esses cálculos: a CPU (ou a tensão/overclock/memória que a alimenta) está instável.";
            }
            else if (!ctx.Cancel)
            {
                r.Status = "passed";
                r.Summary = "Nenhum erro em " + total.ToString("N0") + " cálculos verificados em " + n + " núcleos lógicos durante " + (elapsed / 60).ToString("0.#") + " min.";
            }
        }

        static void RunOn(int core, ThreadStart work)
        {
            Thread t = new Thread(delegate() { Pin(core); try { work(); } finally { Unpin(); } });
            t.IsBackground = true;
            t.Start();
            t.Join();
        }
    }
}
