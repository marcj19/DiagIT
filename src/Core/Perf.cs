using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DiagIT
{
    /// <summary>Acesso à API PDH (a mesma do Monitor de Desempenho), usando nomes de contador em inglês
    /// para funcionar em qualquer idioma do Windows.</summary>
    static class Pdh
    {
        public const uint FMT = 0x00000200 | 0x00008000; // DOUBLE | NOCAP100
        const uint MORE_DATA = 0x800007D2;

        [StructLayout(LayoutKind.Explicit)]
        struct PDH_VALUE
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double Value;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhOpenQuery(string src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
        static extern uint PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")]
        static extern uint PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll")]
        static extern uint PdhCloseQuery(IntPtr q);
        [DllImport("pdh.dll")]
        static extern uint PdhGetFormattedCounterValue(IntPtr c, uint fmt, out uint type, out PDH_VALUE v);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhGetFormattedCounterArrayW(IntPtr c, uint fmt, ref uint size, ref uint count, IntPtr buf);

        public static IntPtr Open()
        {
            IntPtr q;
            if (PdhOpenQuery(null, IntPtr.Zero, out q) != 0) return IntPtr.Zero;
            return q;
        }

        public static IntPtr Add(IntPtr q, string path)
        {
            IntPtr c;
            if (q == IntPtr.Zero) return IntPtr.Zero;
            if (PdhAddEnglishCounter(q, path, IntPtr.Zero, out c) != 0) return IntPtr.Zero;
            return c;
        }

        public static void Collect(IntPtr q) { if (q != IntPtr.Zero) PdhCollectQueryData(q); }
        public static void Close(IntPtr q) { if (q != IntPtr.Zero) PdhCloseQuery(q); }

        public static double Get(IntPtr c)
        {
            if (c == IntPtr.Zero) return double.NaN;
            uint type;
            PDH_VALUE v;
            if (PdhGetFormattedCounterValue(c, FMT, out type, out v) != 0) return double.NaN;
            if (v.CStatus != 0 && v.CStatus != 1) return double.NaN;
            return v.Value;
        }

        public static List<KeyValuePair<string, double>> GetArray(IntPtr c)
        {
            List<KeyValuePair<string, double>> res = new List<KeyValuePair<string, double>>();
            if (c == IntPtr.Zero) return res;
            uint size = 0, count = 0;
            uint r = PdhGetFormattedCounterArrayW(c, FMT, ref size, ref count, IntPtr.Zero);
            if (r != MORE_DATA || size == 0) return res;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                r = PdhGetFormattedCounterArrayW(c, FMT, ref size, ref count, buf);
                if (r != 0) return res;
                const int ITEM = 24; // x64: ponteiro(8) + status(4)+pad(4) + double(8)
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = new IntPtr(buf.ToInt64() + (long)i * ITEM);
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                    uint st = (uint)Marshal.ReadInt32(item, 8);
                    if (st != 0 && st != 1) continue;
                    double val = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                    res.Add(new KeyValuePair<string, double>(name ?? "", val));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return res;
        }
    }

    /// <summary>Coleta CPU/RAM/disco/GPU do sistema com baixíssimo custo.</summary>
    public class PerfCollector : IDisposable
    {
        IntPtr q, cCpuArr, cPerf, cMhz, cDpc, cIntr, cAvail, cCommit, cHard, cDiskLat, cDiskBusy, cDiskQ, cGpuEng, cGpuMem;
        public double TotalVramMB = double.NaN;
        public double TotalRamMB = double.NaN;
        public int Missing;   // quantos contadores não puderam ser criados (diagnóstico)

        static readonly Regex EngRx = new Regex(
            @"^pid_(\d+)_luid_(0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)_phys_\d+_eng_(\d+)_engtype_(.+)$", RegexOptions.Compiled);
        static readonly Regex CoreRx = new Regex(@"^\d+,\d+$", RegexOptions.Compiled);

        public PerfCollector()
        {
            q = Pdh.Open();
            cCpuArr = Add(@"\Processor Information(*)\% Processor Utility");
            cPerf = Add(@"\Processor Information(_Total)\% Processor Performance");
            cMhz = Add(@"\Processor Information(_Total)\Processor Frequency");
            cDpc = Add(@"\Processor Information(_Total)\% DPC Time");
            cIntr = Add(@"\Processor Information(_Total)\% Interrupt Time");
            cAvail = Add(@"\Memory\Available MBytes");
            cCommit = Add(@"\Memory\% Committed Bytes In Use");
            cHard = Add(@"\Memory\Pages Input/sec");
            cDiskLat = Add(@"\PhysicalDisk(*)\Avg. Disk sec/Transfer");
            cDiskBusy = Add(@"\PhysicalDisk(_Total)\% Disk Time");
            cDiskQ = Add(@"\PhysicalDisk(_Total)\Current Disk Queue Length");
            cGpuEng = Add(@"\GPU Engine(*)\Utilization Percentage");
            cGpuMem = Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
            Pdh.Collect(q);

            try { TotalRamMB = NativeInfo.TotalRamMB(); } catch { }
            try { TotalVramMB = NativeInfo.TotalVramMB(); } catch { }
        }

        IntPtr Add(string path)
        {
            IntPtr c = Pdh.Add(q, path);
            if (c == IntPtr.Zero) { Missing++; Log.Write("Contador indisponível: " + path); }
            return c;
        }

        /// <summary>Preenche a amostra e devolve o uso de GPU (3D) por processo.</summary>
        public Dictionary<int, double> Collect(Sample s)
        {
            Dictionary<int, double> pidGpu = new Dictionary<int, double>();
            Pdh.Collect(q);

            // CPU
            double total = double.NaN, coreMax = double.NaN;
            foreach (KeyValuePair<string, double> kv in Pdh.GetArray(cCpuArr))
            {
                if (kv.Key == "_Total") total = kv.Value;
                else if (CoreRx.IsMatch(kv.Key)) { if (double.IsNaN(coreMax) || kv.Value > coreMax) coreMax = kv.Value; }
            }
            s.V[F.Cpu] = Cap(total);
            s.V[F.CoreMax] = Cap(coreMax);
            s.V[F.CpuPerf] = Pdh.Get(cPerf);
            s.V[F.CpuMhz] = Pdh.Get(cMhz);
            s.V[F.Dpc] = Cap(Pdh.Get(cDpc));
            s.V[F.Intr] = Cap(Pdh.Get(cIntr));

            // RAM
            double avail = Pdh.Get(cAvail);
            s.V[F.Avail] = avail;
            if (!double.IsNaN(avail) && !double.IsNaN(TotalRamMB) && TotalRamMB > 0)
                s.V[F.Ram] = Math.Max(0, Math.Min(100, 100.0 * (TotalRamMB - avail) / TotalRamMB));
            s.V[F.Commit] = Pdh.Get(cCommit);
            s.V[F.Hard] = Pdh.Get(cHard);

            // Disco
            double lat = double.NaN;
            foreach (KeyValuePair<string, double> kv in Pdh.GetArray(cDiskLat))
            {
                if (kv.Key == "_Total") continue;
                if (double.IsNaN(lat) || kv.Value > lat) lat = kv.Value;
            }
            s.V[F.DiskLat] = double.IsNaN(lat) ? double.NaN : lat * 1000.0;
            s.V[F.DiskBusy] = Cap(Pdh.Get(cDiskBusy));
            s.V[F.DiskQ] = Pdh.Get(cDiskQ);

            // GPU: escolhe o adaptador com mais VRAM dedicada em uso
            string mainLuid = null;
            double bestMem = -1;
            foreach (KeyValuePair<string, double> kv in Pdh.GetArray(cGpuMem))
            {
                if (kv.Value > bestMem)
                {
                    bestMem = kv.Value;
                    Match m = Regex.Match(kv.Key, @"luid_(0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)");
                    if (m.Success) mainLuid = m.Groups[1].Value;
                }
            }
            if (bestMem >= 0)
            {
                double mb = bestMem / (1024.0 * 1024.0);
                s.V[F.GpuMem] = mb;
                if (!double.IsNaN(TotalVramMB) && TotalVramMB > 0) s.V[F.GpuMemPct] = Math.Min(100, 100.0 * mb / TotalVramMB);
            }

            Dictionary<string, double> engines = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> kv in Pdh.GetArray(cGpuEng))
            {
                Match m = EngRx.Match(kv.Key);
                if (!m.Success) continue;
                if (mainLuid != null && m.Groups[2].Value != mainLuid) continue;
                string key = m.Groups[3].Value + "_" + m.Groups[4].Value;
                double e;
                engines.TryGetValue(key, out e);
                engines[key] = e + kv.Value;
                if (m.Groups[4].Value == "3D")
                {
                    int pid = int.Parse(m.Groups[1].Value);
                    double p;
                    pidGpu.TryGetValue(pid, out p);
                    pidGpu[pid] = Math.Min(100, p + kv.Value);
                }
            }
            if (engines.Count > 0)
            {
                double max = 0;
                foreach (double v in engines.Values) if (v > max) max = v;
                s.V[F.Gpu] = Cap(max);
            }
            return pidGpu;
        }

        static double Cap(double v)
        {
            if (double.IsNaN(v)) return v;
            return Math.Max(0, Math.Min(100, v));
        }

        public void Dispose() { Pdh.Close(q); q = IntPtr.Zero; }
    }

    static class NativeInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")]
        static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        public static double CommitPercent()
        {
            MEMORYSTATUSEX m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            GlobalMemoryStatusEx(ref m);
            if (m.ullTotalPageFile == 0) return 0;
            return 100.0 * (m.ullTotalPageFile - m.ullAvailPageFile) / m.ullTotalPageFile;
        }

        public static double CommitLimitGB()
        {
            MEMORYSTATUSEX m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            GlobalMemoryStatusEx(ref m);
            return m.ullTotalPageFile / (1024.0 * 1024.0 * 1024.0);
        }

        public static double TotalRamMB()
        {
            MEMORYSTATUSEX m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            GlobalMemoryStatusEx(ref m);
            return m.ullTotalPhys / (1024.0 * 1024.0);
        }

        /// <summary>VRAM real (Win32_VideoController.AdapterRAM satura em 4 GB), lida do registro do driver.</summary>
        public static double TotalVramMB()
        {
            double best = double.NaN;
            using (Microsoft.Win32.RegistryKey cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
            {
                if (cls == null) return best;
                foreach (string name in cls.GetSubKeyNames())
                {
                    try
                    {
                        using (Microsoft.Win32.RegistryKey k = cls.OpenSubKey(name))
                        {
                            if (k == null) continue;
                            object v = k.GetValue("HardwareInformation.qwMemorySize");
                            double mb = double.NaN;
                            if (v is long) mb = (long)v / (1024.0 * 1024.0);
                            else if (v is byte[] && ((byte[])v).Length >= 8) mb = BitConverter.ToInt64((byte[])v, 0) / (1024.0 * 1024.0);
                            if (!double.IsNaN(mb) && (double.IsNaN(best) || mb > best)) best = mb;
                        }
                    }
                    catch { }
                }
            }
            return best;
        }
    }
}
