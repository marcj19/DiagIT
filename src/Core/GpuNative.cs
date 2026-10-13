using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DiagIT
{
    /// <summary>
    /// Sensores de GPU AMD lidos diretamente da biblioteca do driver (atiadlxx.dll), sem programas extras.
    /// Usa a telemetria PMLog (temperatura de borda/hotspot/memória, ventoinha, consumo, clocks, link PCIe, throttling).
    /// </summary>
    public class AdlSource : ISensorSource
    {
        delegate IntPtr AdlAlloc(int size);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Main_Control_Create(AdlAlloc cb, int enumConnected, out IntPtr ctx);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Main_Control_Destroy(IntPtr ctx);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr ctx, out int n);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr ctx, IntPtr info, int size);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_New_QueryPMLogData_Get(IntPtr ctx, int adapter, IntPtr output);

        static readonly AdlAlloc alloc = delegate(int size) { return Marshal.AllocCoTaskMem(size); };

        const int INFO_SIZE = 1572;
        const int PMLOG_MAX = 256;
        const int PMLOG_BYTES = 4 + PMLOG_MAX * 8;

        // índices da enumeração ADL_PMLOG_SENSORS (ADL SDK)
        public const int CLK_GFX = 1, CLK_MEM = 2, CLK_SOC = 3, TEMP_EDGE = 8, TEMP_MEM = 9, TEMP_VRVDDC = 10, TEMP_VRMVDD = 11,
            FAN_RPM = 14, FAN_PCT = 15, SOC_VOLT = 16, SOC_POWER = 17, ACT_GFX = 19, ACT_MEM = 20, GFX_VOLT = 21, MEM_VOLT = 22,
            ASIC_POWER = 23, TEMP_VRSOC = 24, TEMP_HOTSPOT = 27, TEMP_GFX = 28, TEMP_SOC = 29, GFX_POWER = 30,
            THROTTLER = 35, BUS_SPEED = 40, BUS_LANES = 41, TEMP_HOTSPOT_GCD = 50, TEMP_HOTSPOT_MCD = 51, BOARD_POWER = 73;

        IntPtr ctx = IntPtr.Zero;
        int adapter = -1;
        IntPtr buf = IntPtr.Zero;
        public string AdapterName = "";
        public string Name { get { return "AMD ADL (nativo, driver de vídeo)"; } }

        /// <summary>Últimos valores brutos (índice -> valor), para diagnóstico e para o link PCIe.</summary>
        public Dictionary<int, int> Raw = new Dictionary<int, int>();

        public bool Init()
        {
            try
            {
                if (ADL2_Main_Control_Create(alloc, 1, out ctx) != 0 || ctx == IntPtr.Zero) return false;
                int n;
                if (ADL2_Adapter_NumberOfAdapters_Get(ctx, out n) != 0 || n <= 0) { Close(); return false; }
                IntPtr info = Marshal.AllocHGlobal(INFO_SIZE * n);
                buf = Marshal.AllocHGlobal(PMLOG_BYTES);
                try
                {
                    for (int i = 0; i < INFO_SIZE * n; i++) Marshal.WriteByte(info, i, 0);
                    for (int i = 0; i < n; i++) Marshal.WriteInt32(info, i * INFO_SIZE, INFO_SIZE);
                    if (ADL2_Adapter_AdapterInfo_Get(ctx, info, INFO_SIZE * n) != 0) { Close(); return false; }
                    HashSet<int> buses = new HashSet<int>();
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr p = new IntPtr(info.ToInt64() + (long)i * INFO_SIZE);
                        int idx = Marshal.ReadInt32(p, 4);
                        int bus = Marshal.ReadInt32(p, 264);
                        if (!buses.Add(bus)) continue;
                        if (Query(idx) && Raw.Count > 0)
                        {
                            adapter = idx;
                            AdapterName = Marshal.PtrToStringAnsi(new IntPtr(p.ToInt64() + 280));
                            return true;
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(info); }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (Exception e) { Log.Write("ADL: " + e.Message); }
            Close();
            return false;
        }

        bool Query(int idx)
        {
            for (int i = 0; i < PMLOG_BYTES; i += 4) Marshal.WriteInt32(buf, i, 0);
            if (ADL2_New_QueryPMLogData_Get(ctx, idx, buf) != 0) return false;
            Dictionary<int, int> r = new Dictionary<int, int>();
            for (int i = 0; i < PMLOG_MAX; i++)
            {
                int supported = Marshal.ReadInt32(buf, 4 + i * 8);
                if (supported != 0) r[i] = Marshal.ReadInt32(buf, 8 + i * 8);
            }
            Raw = r;
            return true;
        }

        public void Close()
        {
            try { if (ctx != IntPtr.Zero) ADL2_Main_Control_Destroy(ctx); } catch { }
            ctx = IntPtr.Zero;
            if (buf != IntPtr.Zero) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; }
        }

        void Add(List<SensorItem> l, int idx, STyp t, string name, string unit, double scale)
        {
            int v;
            if (!Raw.TryGetValue(idx, out v)) return;
            SensorItem it = new SensorItem();
            it.Hw = AdapterName; it.Cat = SCat.Gpu; it.Typ = t; it.Name = name; it.Unit = unit; it.Val = v * scale;
            l.Add(it);
        }

        public List<SensorItem> Read()
        {
            List<SensorItem> l = new List<SensorItem>();
            if (!Query(adapter)) throw new InvalidOperationException("ADL parou de responder");
            Add(l, TEMP_EDGE, STyp.Temp, "GPU Temperature", "°C", 1);
            if (Raw.ContainsKey(TEMP_HOTSPOT)) Add(l, TEMP_HOTSPOT, STyp.Temp, "GPU Hot Spot", "°C", 1);
            else Add(l, TEMP_HOTSPOT_GCD, STyp.Temp, "GPU Hot Spot", "°C", 1);
            Add(l, TEMP_MEM, STyp.Temp, "GPU Memory Temperature", "°C", 1);
            Add(l, TEMP_VRVDDC, STyp.Temp, "GPU VR VDDC Temperature", "°C", 1);
            Add(l, TEMP_SOC, STyp.Temp, "GPU SoC Temperature", "°C", 1);
            Add(l, FAN_RPM, STyp.Fan, "GPU Fan", "RPM", 1);
            Add(l, FAN_PCT, STyp.Other, "GPU Fan %", "%", 1);
            Add(l, CLK_GFX, STyp.Clock, "GPU Clock", "MHz", 1);
            Add(l, CLK_MEM, STyp.Clock, "GPU Memory Clock", "MHz", 1);
            Add(l, ASIC_POWER, STyp.Power, "GPU ASIC Power", "W", 1);
            Add(l, BOARD_POWER, STyp.Power, "GPU Total Board Power", "W", 1);
            Add(l, GFX_VOLT, STyp.Volt, "GPU Core Voltage", "V", 0.001);
            Add(l, ACT_GFX, STyp.Load, "GPU Activity", "%", 1);
            return l;
        }

        /// <summary>Link PCIe real negociado pela GPU (geração, pistas). -1 se indisponível.</summary>
        public void PcieLink(out int gen, out int lanes)
        {
            int s, w;
            gen = Raw.TryGetValue(BUS_SPEED, out s) ? s : -1;
            lanes = Raw.TryGetValue(BUS_LANES, out w) ? w : -1;
        }

        public string Dump()
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in Raw) sb.AppendLine("  pmlog[" + kv.Key + "] = " + kv.Value);
            return sb.ToString();
        }
    }

    /// <summary>Sensores de GPU NVIDIA via NVML (vem com o driver da NVIDIA).</summary>
    public class NvmlSource : ISensorSource
    {
        [DllImport("nvml.dll")] static extern int nvmlInit_v2();
        [DllImport("nvml.dll")] static extern int nvmlShutdown();
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetCount_v2(out uint n);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint i, out IntPtr dev);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr dev, int sensor, out uint t);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetPowerUsage(IntPtr dev, out uint mw);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetClockInfo(IntPtr dev, int type, out uint mhz);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetFanSpeed(IntPtr dev, out uint pct);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetCurrPcieLinkGeneration(IntPtr dev, out uint gen);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetCurrPcieLinkWidth(IntPtr dev, out uint w);
        [DllImport("nvml.dll", CharSet = CharSet.Ansi)] static extern int nvmlDeviceGetName(IntPtr dev, StringBuilder name, uint len);

        IntPtr dev;
        string devName = "";
        public string Name { get { return "NVIDIA NVML (nativo, driver de vídeo)"; } }
        public int PcieGen = -1, PcieLanes = -1;

        public bool Init()
        {
            try
            {
                if (nvmlInit_v2() != 0) return false;
                uint n;
                if (nvmlDeviceGetCount_v2(out n) != 0 || n == 0) { nvmlShutdown(); return false; }
                if (nvmlDeviceGetHandleByIndex_v2(0, out dev) != 0) { nvmlShutdown(); return false; }
                StringBuilder sb = new StringBuilder(96);
                nvmlDeviceGetName(dev, sb, 96);
                devName = sb.ToString();
                return true;
            }
            catch { return false; }
        }

        public void Close() { try { nvmlShutdown(); } catch { } }

        public List<SensorItem> Read()
        {
            List<SensorItem> l = new List<SensorItem>();
            uint v;
            if (nvmlDeviceGetTemperature(dev, 0, out v) == 0) l.Add(It(STyp.Temp, "GPU Temperature", "°C", v));
            if (nvmlDeviceGetPowerUsage(dev, out v) == 0) l.Add(It(STyp.Power, "GPU Total Board Power", "W", v / 1000.0));
            if (nvmlDeviceGetClockInfo(dev, 0, out v) == 0) l.Add(It(STyp.Clock, "GPU Clock", "MHz", v));
            if (nvmlDeviceGetClockInfo(dev, 2, out v) == 0) l.Add(It(STyp.Clock, "GPU Memory Clock", "MHz", v));
            if (nvmlDeviceGetFanSpeed(dev, out v) == 0) l.Add(It(STyp.Other, "GPU Fan %", "%", v));
            if (nvmlDeviceGetCurrPcieLinkGeneration(dev, out v) == 0) PcieGen = (int)v;
            if (nvmlDeviceGetCurrPcieLinkWidth(dev, out v) == 0) PcieLanes = (int)v;
            return l;
        }

        SensorItem It(STyp t, string n, string u, double v)
        {
            SensorItem it = new SensorItem();
            it.Hw = devName; it.Cat = SCat.Gpu; it.Typ = t; it.Name = n; it.Unit = u; it.Val = v;
            return it;
        }
    }
}
