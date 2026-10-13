using System;
using System.Collections.Generic;

namespace DiagIT
{
    /// <summary>Índices dos campos de cada amostra. NaN = sensor indisponível.</summary>
    public static class F
    {
        public const int Cpu = 0, CpuPerf = 1, CpuMhz = 2, CoreMax = 3, Dpc = 4, Intr = 5,
            CpuTemp = 6, CpuPower = 7, CpuClock = 8,
            Gpu = 9, GpuMem = 10, GpuMemPct = 11, GpuTemp = 12, GpuHot = 13, GpuPower = 14, GpuClock = 15, GpuFan = 16,
            Ram = 17, Avail = 18, Hard = 19, Commit = 20,
            DiskLat = 21, DiskQ = 22, DiskBusy = 23, DiskTemp = 24,
            GameCpu = 25, GameGpu = 26, V12 = 27, Gap = 28,
            GpuMemTemp = 29, CpuFan = 30, V5 = 31, V33 = 32, Vcore = 33, BoardTemp = 34, PcieGen = 35, PcieLanes = 36, GpuVolt = 37,
            Fps = 38, FpsLow = 39, FrameMs = 40, Stutter = 41;

        public const int Count = 42;

        public static readonly string[] Names =
        {
            "cpu", "cpu_perf", "cpu_mhz", "core_max", "dpc", "intr",
            "cpu_temp", "cpu_power", "cpu_clock",
            "gpu", "gpu_mem_mb", "gpu_mem_pct", "gpu_temp", "gpu_hot", "gpu_power", "gpu_clock", "gpu_fan",
            "ram", "avail_mb", "hard_faults", "commit",
            "disk_lat_ms", "disk_queue", "disk_busy", "disk_temp",
            "game_cpu", "game_gpu", "v12", "gap_s",
            "gpu_mem_temp", "cpu_fan", "v5", "v33", "vcore", "board_temp", "pcie_gen", "pcie_lanes", "gpu_vcore",
            "fps", "fps_low1", "frame_ms", "stutters"
        };

        public static readonly string[] Labels =
        {
            "CPU", "Desempenho CPU", "Frequência CPU", "Núcleo mais ocupado", "DPC (drivers)", "Interrupções",
            "Temp. CPU", "Consumo CPU", "Clock CPU",
            "GPU", "VRAM usada", "VRAM", "Temp. GPU", "Temp. GPU (hotspot)", "Consumo GPU", "Clock GPU", "Ventoinha GPU",
            "RAM", "RAM livre", "Falhas de página", "Memória comprometida",
            "Latência do disco", "Fila do disco", "Disco ocupado", "Temp. disco",
            "CPU do jogo", "GPU do jogo", "Rail +12V", "Intervalo da amostra",
            "Temp. VRAM", "Ventoinha CPU", "Rail +5V", "Rail +3,3V", "Tensão do núcleo (Vcore)", "Temp. placa-mãe (máx)", "PCIe (geração)", "PCIe (pistas)", "Tensão GPU",
            "FPS", "FPS 1% low", "Tempo de quadro", "Engasgos"
        };

        public static readonly string[] Units =
        {
            "%", "%", "MHz", "%", "%", "%",
            "°C", "W", "MHz",
            "%", "MB", "%", "°C", "°C", "W", "MHz", "RPM",
            "%", "MB", "/s", "%",
            "ms", "", "%", "°C",
            "%", "%", "V", "s",
            "°C", "RPM", "V", "V", "V", "°C", "", "", "V",
            "", "", "ms", ""
        };
    }

    public class Sample
    {
        public DateTime Time;
        public double[] V = new double[F.Count];

        public Sample()
        {
            for (int i = 0; i < V.Length; i++) V[i] = double.NaN;
        }

        public double this[int f] { get { return V[f]; } set { V[f] = value; } }
    }

    public static class Stat
    {
        public static double[] Vals(IList<Sample> l, int f)
        {
            List<double> r = new List<double>(l.Count);
            for (int i = 0; i < l.Count; i++)
            {
                double v = l[i].V[f];
                if (!double.IsNaN(v) && !double.IsInfinity(v)) r.Add(v);
            }
            return r.ToArray();
        }

        public static double Avg(double[] v)
        {
            if (v.Length == 0) return double.NaN;
            double s = 0;
            for (int i = 0; i < v.Length; i++) s += v[i];
            return s / v.Length;
        }

        public static double Max(double[] v)
        {
            if (v.Length == 0) return double.NaN;
            double m = v[0];
            for (int i = 1; i < v.Length; i++) if (v[i] > m) m = v[i];
            return m;
        }

        public static double Min(double[] v)
        {
            if (v.Length == 0) return double.NaN;
            double m = v[0];
            for (int i = 1; i < v.Length; i++) if (v[i] < m) m = v[i];
            return m;
        }

        public static double Percentile(double[] v, double p)
        {
            if (v.Length == 0) return double.NaN;
            double[] c = (double[])v.Clone();
            Array.Sort(c);
            double idx = (c.Length - 1) * p;
            int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
            if (lo == hi) return c[lo];
            return c[lo] + (c[hi] - c[lo]) * (idx - lo);
        }

        public static double FracAbove(double[] v, double thr)
        {
            if (v.Length == 0) return 0;
            int n = 0;
            for (int i = 0; i < v.Length; i++) if (v[i] > thr) n++;
            return (double)n / v.Length;
        }

        public static double FracBelow(double[] v, double thr)
        {
            if (v.Length == 0) return 0;
            int n = 0;
            for (int i = 0; i < v.Length; i++) if (v[i] < thr) n++;
            return (double)n / v.Length;
        }

        public static string Fmt(double v, string unit, int dec)
        {
            if (double.IsNaN(v)) return "—";
            return v.ToString("F" + dec, System.Globalization.CultureInfo.CurrentCulture) + (unit.Length > 0 ? " " + unit : "");
        }
    }
}
