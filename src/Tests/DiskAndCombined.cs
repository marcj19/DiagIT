using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace DiagIT
{
    /// <summary>
    /// Armazenamento: lê a saúde SMART/NVMe direto de cada disco e faz leituras espalhadas pelo disco inteiro
    /// (somente leitura) medindo erros e latência. Setor ilegível = erro concreto.
    /// </summary>
    public class DiskTest : HwTest
    {
        public override TestKind Kind { get { return TestKind.Disk; } }
        public class Options { public bool FullScan; }

        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(SafeFileHandle h, IntPtr buf, int n, out int read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetFilePointerEx(SafeFileHandle h, long dist, out long newPos, int method);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr VirtualAlloc(IntPtr a, UIntPtr s, uint t, uint p);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualFree(IntPtr a, UIntPtr s, uint t);

        public override void Run(TestContext ctx, TestResult r, int seconds)
        {
            Options opt = ctx.Options as Options ?? new Options();
            ctx.Phase = "Lendo a saúde (SMART) dos discos";
            List<DriveHealth> drives = Smart.ReadAll();
            int problems = 0;
            foreach (DriveHealth d in drives)
            {
                string head = "Disco " + d.Number + " — " + d.Model + " (" + d.Bus + ", " + (d.SizeBytes / 1e9).ToString("0") + " GB): ";
                if (!d.Read) { r.Detail(head + "saúde não lida (" + d.Error + ")."); continue; }
                List<string> pr = d.Problems();
                List<string> info = new List<string>();
                if (!double.IsNaN(d.UsedPct)) info.Add("desgaste " + d.UsedPct.ToString("0") + "%");
                if (!double.IsNaN(d.TempC)) info.Add(d.TempC.ToString("0") + " °C");
                if (!double.IsNaN(d.PowerOnHours)) info.Add(d.PowerOnHours.ToString("N0") + " h ligado");
                if (!double.IsNaN(d.TbWritten)) info.Add(d.TbWritten.ToString("0.0") + " TB gravados");
                if (!double.IsNaN(d.UnsafeShutdowns)) info.Add(d.UnsafeShutdowns.ToString("N0") + " desligamentos inseguros");
                if (!double.IsNaN(d.MediaErrors)) info.Add(d.MediaErrors.ToString("N0") + " erros de mídia");
                if (!double.IsNaN(d.Reallocated)) info.Add(d.Reallocated.ToString("N0") + " realocados");
                if (!double.IsNaN(d.Pending)) info.Add(d.Pending.ToString("N0") + " pendentes");
                if (!double.IsNaN(d.CrcErrors)) info.Add(d.CrcErrors.ToString("N0") + " erros CRC");
                r.Detail(head + string.Join(", ", info.ToArray()) + ".");
                foreach (string p in pr) { r.Detail("  PROBLEMA: " + p + "."); problems++; }
                r.Set("disk" + d.Number + "_used_pct", d.UsedPct);
                r.Set("disk" + d.Number + "_media_errors", double.IsNaN(d.MediaErrors) ? d.Uncorrectable : d.MediaErrors);
            }
            r.Set("smart_problems", problems);

            // ---- leituras espalhadas (ou completas) em cada disco
            int n = Math.Max(1, drives.Count);
            int perDrive = Math.Max(20, seconds / n);
            long readErrors = 0;
            int idx = 0;
            foreach (DriveHealth d in drives)
            {
                if (ctx.Cancel) break;
                ctx.Phase = "Lendo o disco " + d.Number + " (" + d.Model + ")";
                readErrors += Surface(ctx, r, d, perDrive, opt.FullScan, idx, n);
                idx++;
            }
            r.Set("read_errors", readErrors);

            // sem saúde lida e sem nenhuma leitura feita, o teste não verificou nada: não pode "passar"
            bool anyHealth = drives.Exists(delegate(DriveHealth d) { return d.Read; });
            double totalReads = 0;
            foreach (DriveHealth d in drives) { double x = r.Get("disk" + d.Number + "_reads"); if (!double.IsNaN(x)) totalReads += x; }

            if (problems > 0 || readErrors > 0)
            {
                r.Status = "failed";
                r.Errors = (int)(problems + readErrors);
                r.Summary = (readErrors > 0 ? readErrors + " setor(es) ilegível(is) encontrado(s)" : "") + (readErrors > 0 && problems > 0 ? " e " : "") +
                    (problems > 0 ? problems + " problema(s) reportado(s) pelo próprio disco (SMART)" : "") + ". Faça backup dos seus arquivos.";
            }
            else if (!ctx.Cancel && !anyHealth && totalReads == 0)
            {
                r.Status = "error";
                r.Summary = "Não foi possível ler os discos (é preciso abrir o DiagIT como administrador). Nada foi verificado.";
            }
            else if (!ctx.Cancel)
            {
                r.Status = "passed";
                r.Summary = "Discos sem erros: SMART/saúde sem alertas e nenhuma falha de leitura nas amostras de todo o disco.";
            }
        }

        long Surface(TestContext ctx, TestResult r, DriveHealth d, int seconds, bool full, int idx, int count)
        {
            const int BLOCK = 1 << 20;
            IntPtr buf = VirtualAlloc(IntPtr.Zero, new UIntPtr(BLOCK), 0x3000, 0x04);
            long errs = 0, reads = 0, slow = 0;
            List<double> lat = new List<double>();
            try
            {
                using (SafeFileHandle h = Smart.CreateFile(@"\\.\PhysicalDrive" + d.Number, 0x80000000u, 3, IntPtr.Zero, 3, 0x20000000u, IntPtr.Zero))
                {
                    if (h.IsInvalid) { r.Detail("Disco " + d.Number + ": não foi possível abrir para leitura (requer administrador)."); return 0; }
                    long len = Smart.DiskLength(h);
                    if (len <= BLOCK) return 0;
                    long blocks = len / BLOCK;
                    DateTime start = DateTime.Now, end = start.AddSeconds(seconds);
                    long planned = full ? blocks : Math.Min(blocks, 4000);
                    long step = Math.Max(1, blocks / planned);
                    Stopwatch sw = new Stopwatch();
                    for (long i = 0; i < planned && !ctx.Cancel; i++)
                    {
                        if (!full && DateTime.Now > end) break;
                        long pos = (i * step) * (long)BLOCK;
                        long np;
                        SetFilePointerEx(h, pos, out np, 0);
                        int read;
                        sw.Restart();
                        bool ok = ReadFile(h, buf, BLOCK, out read, IntPtr.Zero);
                        double ms = sw.Elapsed.TotalMilliseconds;
                        reads++;
                        lat.Add(ms);
                        if (ms > 500) slow++;
                        if (!ok)
                        {
                            int e = Marshal.GetLastWin32Error();
                            errs++;
                            ctx.AddError();
                            if (errs <= 10) r.Detail("ERRO DE LEITURA no disco " + d.Number + " na posição " + (pos / 1e9).ToString("0.000") + " GB (código " + e +
                                (e == 23 ? " — dados ilegíveis/CRC" : e == 1117 ? " — falha de E/S do dispositivo" : "") + ").");
                        }
                        ctx.Progress = (idx + (full ? (double)i / planned : Math.Min(1, (DateTime.Now - start).TotalSeconds / seconds))) / count;
                    }
                    double[] la = lat.ToArray();
                    r.Detail("Disco " + d.Number + ": " + reads.ToString("N0") + " leituras de 1 MB " + (full ? "cobrindo o disco inteiro" : "espalhadas por todo o disco") +
                        ", " + errs + " erro(s); latência média " + Stat.Avg(la).ToString("0.0") + " ms, p99 " + Stat.Percentile(la, 0.99).ToString("0.0") +
                        " ms, máx " + Stat.Max(la).ToString("0") + " ms" + (slow > 0 ? "; " + slow + " leitura(s) acima de 500 ms" : "") + ".");
                    r.Set("disk" + d.Number + "_reads", reads);
                    r.Set("disk" + d.Number + "_lat_max", Stat.Max(la));
                    r.Set("disk" + d.Number + "_lat_p99", Stat.Percentile(la, 0.99));
                    r.Set("disk" + d.Number + "_slow", slow);
                }
            }
            finally { VirtualFree(buf, UIntPtr.Zero, 0x8000); }
            return errs;
        }
    }

    /// <summary>
    /// Teste de fonte: CPU e GPU em carga máxima ao mesmo tempo (consumo máximo do PC). Se o PC desligar/reiniciar,
    /// o DiagIT detecta na próxima abertura e registra o consumo e as temperaturas do momento da queda.
    /// </summary>
    public class CombinedTest : HwTest
    {
        public override TestKind Kind { get { return TestKind.Combined; } }

        public override void Run(TestContext ctx, TestResult r, int seconds)
        {
            TestResult rc = new TestResult(), rg = new TestResult();
            rc.Kind = TestKind.Cpu; rg.Kind = TestKind.Gpu;
            TestContext cc = new TestContext(), gc = new TestContext();
            cc.W = ctx.W; gc.W = ctx.W;
            gc.Options = new GpuTest.Options { ComputeOnly = true };
            Exception ge = null, ce = null;
            DateTime start = DateTime.Now;
            Thread tg = new Thread(delegate() { try { new GpuTest().Run(gc, rg, seconds); } catch (Exception e) { ge = e; } });
            Thread tc = new Thread(delegate() { try { new CpuTest().Run(cc, rc, seconds); } catch (Exception e) { ce = e; } });
            tg.IsBackground = true; tc.IsBackground = true;
            tg.Start(); Thread.Sleep(1500); tc.Start();
            while (tg.IsAlive || tc.IsAlive)
            {
                if (ctx.Cancel) { cc.Cancel = true; gc.Cancel = true; }
                ctx.Progress = Math.Min(1, (DateTime.Now - start).TotalSeconds / seconds);
                ctx.Phase = "CPU e GPU em carga máxima" + (cc.Errors + gc.Errors > 0 ? " — erros: " + (cc.Errors + gc.Errors) : "");
                Thread.Sleep(500);
            }
            foreach (string s in rc.DetailsCopy()) r.Detail("[CPU] " + s);
            foreach (string s in rg.DetailsCopy()) r.Detail("[GPU] " + s);
            if (ce != null) r.Detail("[CPU] erro: " + ce.Message);
            if (ge != null) r.Detail("[GPU] erro: " + ge.Message);
            r.Errors = rc.Errors + rg.Errors + cc.Errors + gc.Errors;
            r.Set("device_removed", rg.Get("device_removed"));

            // consumo simultâneo máximo registrado
            double peak = double.NaN, peakCpu = double.NaN, peakGpu = double.NaN, minV12 = double.NaN;
            if (ctx.W != null)
                foreach (Sample s in ctx.W.LiveCopy(seconds + 60))
                {
                    if (s.Time < start) continue;
                    double c = s[F.CpuPower], g = s[F.GpuPower];
                    if (!double.IsNaN(c) && !double.IsNaN(g) && (double.IsNaN(peak) || c + g > peak)) { peak = c + g; peakCpu = c; peakGpu = g; }
                    if (!double.IsNaN(s[F.V12]) && (double.IsNaN(minV12) || s[F.V12] < minV12)) minV12 = s[F.V12];
                }
            r.Set("peak_power", peak);
            string power = double.IsNaN(peak) ? "" : " Pico de consumo medido: " + peak.ToString("0") + " W (CPU " + peakCpu.ToString("0") + " W + GPU " + peakGpu.ToString("0") +
                " W; o PC inteiro consome mais, por volta de " + (peak + 60).ToString("0") + " W com placa-mãe, memória, discos e ventoinhas).";
            string rail = double.IsNaN(minV12) ? "" : " +12 V mínimo: " + minV12.ToString("0.00") + " V.";
            double min = (DateTime.Now - start).TotalMinutes;

            if (rc.Status == "failed" || rg.Status == "failed" || ce != null || ge != null)
            {
                r.Status = "failed";
                r.Summary = "Falhou sob carga máxima: " + (rc.Status == "failed" ? "CPU — " + rc.Summary + " " : "") + (rg.Status == "failed" ? "GPU — " + rg.Summary : "") + power + rail;
            }
            else if (!ctx.Cancel)
            {
                r.Status = "passed";
                r.Summary = "O PC aguentou " + min.ToString("0.#") + " min com CPU e GPU em carga máxima ao mesmo tempo, sem desligar, travar ou errar cálculos." + power + rail;
            }
        }
    }
}
