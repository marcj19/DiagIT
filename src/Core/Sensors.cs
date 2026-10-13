using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Management;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace DiagIT
{
    public enum SCat { Cpu, Gpu, Storage, Board, Mem, Other }
    public enum STyp { Temp, Power, Clock, Volt, Fan, Load, Other }

    public class SensorItem
    {
        public string Hw = "";
        public SCat Cat;
        public STyp Typ;
        public string Name = "";
        public string Unit = "";
        public double Val;
    }

    public interface ISensorSource
    {
        string Name { get; }
        bool Init();
        List<SensorItem> Read();
        void Close();
    }

    // ---------------------------------------------------------------- HWiNFO (memória compartilhada)
    public class HwinfoSource : ISensorSource
    {
        MemoryMappedFile mmf;
        MemoryMappedViewAccessor acc;
        public string MapName = "Global\\HWiNFO_SENS_SM2";
        public string Name { get { return "HWiNFO (memória compartilhada)"; } }

        public bool Init()
        {
            try
            {
                mmf = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
                acc = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                return acc.ReadUInt32(0) == 0x53695748; // "HWiS"
            }
            catch { Close(); return false; }
        }

        public void Close()
        {
            try { if (acc != null) acc.Dispose(); } catch { }
            try { if (mmf != null) mmf.Dispose(); } catch { }
            acc = null; mmf = null;
        }

        static string Str(byte[] b, int off, int len)
        {
            int n = 0;
            while (n < len && b[off + n] != 0) n++;
            return Encoding.UTF8.GetString(b, off, n);
        }

        public List<SensorItem> Read()
        {
            List<SensorItem> res = new List<SensorItem>();
            if (acc == null) return res;
            uint sig = acc.ReadUInt32(0);
            if (sig != 0x53695748) throw new InvalidOperationException("HWiNFO parou de publicar dados");
            uint sOff = acc.ReadUInt32(20), sSize = acc.ReadUInt32(24), sNum = acc.ReadUInt32(28);
            uint rOff = acc.ReadUInt32(32), rSize = acc.ReadUInt32(36), rNum = acc.ReadUInt32(40);
            if (sSize < 136 || rSize < 316 || sNum > 2000 || rNum > 20000) return res;

            byte[] sb = new byte[sSize * sNum];
            acc.ReadArray<byte>(sOff, sb, 0, sb.Length);
            string[] sNames = new string[sNum];
            for (int i = 0; i < sNum; i++)
            {
                int o = (int)(i * sSize);
                string orig = Str(sb, o + 8, 128);
                string usr = Str(sb, o + 8 + 128, 128);
                sNames[i] = usr.Length > 0 ? usr : orig;
            }

            byte[] rb = new byte[rSize * rNum];
            acc.ReadArray<byte>(rOff, rb, 0, rb.Length);
            for (int i = 0; i < rNum; i++)
            {
                int o = (int)(i * rSize);
                int type = BitConverter.ToInt32(rb, o);
                int sIdx = BitConverter.ToInt32(rb, o + 4);
                string label = Str(rb, o + 140, 128);
                if (label.Length == 0) label = Str(rb, o + 12, 128);
                string unit = Str(rb, o + 268, 16);
                double val = BitConverter.ToDouble(rb, o + 284);
                if (double.IsNaN(val) || double.IsInfinity(val)) continue;
                string sName = (sIdx >= 0 && sIdx < sNames.Length) ? sNames[sIdx] : "";

                SensorItem it = new SensorItem();
                it.Hw = sName;
                it.Name = label;
                it.Unit = unit;
                it.Val = val;
                it.Cat = CatOf(sName);
                switch (type)
                {
                    case 1: it.Typ = STyp.Temp; break;
                    case 2: it.Typ = STyp.Volt; break;
                    case 3: it.Typ = STyp.Fan; break;
                    case 5: it.Typ = STyp.Power; break;
                    case 6: it.Typ = STyp.Clock; break;
                    case 7: it.Typ = STyp.Load; break;
                    default: it.Typ = STyp.Other; break;
                }
                res.Add(it);
            }
            return res;
        }

        static SCat CatOf(string sensorName)
        {
            if (sensorName.StartsWith("CPU [", StringComparison.OrdinalIgnoreCase)) return SCat.Cpu;
            if (sensorName.StartsWith("GPU [", StringComparison.OrdinalIgnoreCase)) return SCat.Gpu;
            if (sensorName.StartsWith("S.M.A.R.T.", StringComparison.OrdinalIgnoreCase) ||
                sensorName.StartsWith("Drive:", StringComparison.OrdinalIgnoreCase)) return SCat.Storage;
            if (sensorName.StartsWith("DIMM", StringComparison.OrdinalIgnoreCase) ||
                sensorName.StartsWith("Memory", StringComparison.OrdinalIgnoreCase)) return SCat.Mem;
            if (sensorName.StartsWith("Network", StringComparison.OrdinalIgnoreCase)) return SCat.Other;
            return SCat.Board;
        }

        /// <summary>Gera um buffer no formato do HWiNFO (usado só nos testes internos).</summary>
        public static byte[] BuildTestBuffer(string[] sensors, object[][] readings)
        {
            const int SS = 264, RS = 316;
            int sOff = 44, rOff = sOff + SS * sensors.Length;
            byte[] b = new byte[rOff + RS * readings.Length];
            BitConverter.GetBytes(0x53695748u).CopyTo(b, 0);
            BitConverter.GetBytes(2u).CopyTo(b, 4);
            BitConverter.GetBytes((uint)sOff).CopyTo(b, 20);
            BitConverter.GetBytes((uint)SS).CopyTo(b, 24);
            BitConverter.GetBytes((uint)sensors.Length).CopyTo(b, 28);
            BitConverter.GetBytes((uint)rOff).CopyTo(b, 32);
            BitConverter.GetBytes((uint)RS).CopyTo(b, 36);
            BitConverter.GetBytes((uint)readings.Length).CopyTo(b, 40);
            for (int i = 0; i < sensors.Length; i++)
                Encoding.UTF8.GetBytes(sensors[i]).CopyTo(b, sOff + i * SS + 8);
            for (int i = 0; i < readings.Length; i++)
            {
                int o = rOff + i * RS;
                BitConverter.GetBytes((int)readings[i][0]).CopyTo(b, o);
                BitConverter.GetBytes((int)readings[i][1]).CopyTo(b, o + 4);
                Encoding.UTF8.GetBytes((string)readings[i][2]).CopyTo(b, o + 12);
                Encoding.UTF8.GetBytes((string)readings[i][3]).CopyTo(b, o + 268);
                BitConverter.GetBytes((double)readings[i][4]).CopyTo(b, o + 284);
            }
            return b;
        }
    }

    // ---------------------------------------------------------------- LibreHardwareMonitor (DLL por reflexão)
    public class LhmDllSource : ISensorSource
    {
        object comp;
        Type tComputer, tHw, tSensor;
        PropertyInfo pHwSensors, pHwSub, pHwType, pHwName, pSName, pSType, pSValue;
        MethodInfo mUpdate;
        PropertyInfo pHardware;
        public string Name { get { return "LibreHardwareMonitor (DLL)"; } }
        public string Error = "";
        public bool EnableGpu = true;
        static bool resolverInstalled;

        static void InstallResolver(string dir)
        {
            if (resolverInstalled) return;
            resolverInstalled = true;
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs a)
            {
                try
                {
                    string simple = new AssemblyName(a.Name).Name;
                    string f = Path.Combine(dir, simple + ".dll");
                    if (File.Exists(f)) return Assembly.LoadFrom(f);
                }
                catch { }
                return null;
            };
        }

        public static string FindDll()
        {
            string[] dirs =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensores"),
                AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(Paths.Root, "sensores")
            };
            foreach (string d in dirs)
            {
                string f = Path.Combine(d, "LibreHardwareMonitorLib.dll");
                if (File.Exists(f)) return f;
            }
            return null;
        }

        public bool Init()
        {
            try
            {
                string dll = FindDll();
                if (dll == null) return false;
                InstallResolver(Path.GetDirectoryName(dll));
                Assembly asm = Assembly.LoadFrom(dll);
                tComputer = asm.GetType("LibreHardwareMonitor.Hardware.Computer");
                tHw = asm.GetType("LibreHardwareMonitor.Hardware.IHardware");
                tSensor = asm.GetType("LibreHardwareMonitor.Hardware.ISensor");
                if (tComputer == null || tHw == null || tSensor == null) { Error = "DLL incompatível"; return false; }
                comp = Activator.CreateInstance(tComputer);
                string[] flags = { "IsCpuEnabled", "IsMotherboardEnabled", "CPUEnabled", "MainboardEnabled" };
                foreach (string fl in flags)
                {
                    PropertyInfo p = tComputer.GetProperty(fl);
                    if (p != null && p.CanWrite) p.SetValue(comp, true, null);
                }
                foreach (string fl in new string[] { "IsGpuEnabled", "GPUEnabled" })
                {
                    PropertyInfo p = tComputer.GetProperty(fl);
                    if (p != null && p.CanWrite) p.SetValue(comp, EnableGpu, null);
                }
                tComputer.GetMethod("Open", Type.EmptyTypes).Invoke(comp, null);
                pHardware = tComputer.GetProperty("Hardware");
                pHwSensors = tHw.GetProperty("Sensors");
                pHwSub = tHw.GetProperty("SubHardware");
                pHwType = tHw.GetProperty("HardwareType");
                pHwName = tHw.GetProperty("Name");
                mUpdate = tHw.GetMethod("Update");
                pSName = tSensor.GetProperty("Name");
                pSType = tSensor.GetProperty("SensorType");
                pSValue = tSensor.GetProperty("Value");
                return pHardware != null && pHwSensors != null;
            }
            catch (Exception e)
            {
                Error = e.InnerException != null ? e.InnerException.Message : e.Message;
                Log.Write("LHM DLL: " + Error);
                return false;
            }
        }

        public void Close()
        {
            try
            {
                if (comp != null) tComputer.GetMethod("Close").Invoke(comp, null);
            }
            catch { }
            comp = null;
        }

        public List<SensorItem> Read()
        {
            List<SensorItem> res = new List<SensorItem>();
            foreach (object hw in (IEnumerable)pHardware.GetValue(comp, null)) Walk(hw, res);
            return res;
        }

        void Walk(object hw, List<SensorItem> res)
        {
            try { mUpdate.Invoke(hw, null); } catch { }
            string hwType = Convert.ToString(pHwType.GetValue(hw, null));
            string hwName = Convert.ToString(pHwName.GetValue(hw, null));
            foreach (object sn in (IEnumerable)pHwSensors.GetValue(hw, null))
            {
                object v = pSValue.GetValue(sn, null);
                if (v == null) continue;
                SensorItem it = new SensorItem();
                it.Hw = hwName;
                it.Name = Convert.ToString(pSName.GetValue(sn, null));
                it.Val = Convert.ToDouble(v);
                if (double.IsNaN(it.Val)) continue;
                Classify(hwType, Convert.ToString(pSType.GetValue(sn, null)), it);
                res.Add(it);
            }
            foreach (object sub in (IEnumerable)pHwSub.GetValue(hw, null)) Walk(sub, res);
        }

        public static void Classify(string hwType, string sType, SensorItem it)
        {
            string h = hwType.ToLowerInvariant();
            if (h.Contains("cpu")) it.Cat = SCat.Cpu;
            else if (h.Contains("gpu")) it.Cat = SCat.Gpu;
            else if (h.Contains("storage") || h.Contains("hdd")) it.Cat = SCat.Storage;
            else if (h.Contains("memory") || h.Contains("ram")) it.Cat = SCat.Mem;
            else it.Cat = SCat.Board;
            switch ((sType ?? "").ToLowerInvariant())
            {
                case "temperature": it.Typ = STyp.Temp; it.Unit = "°C"; break;
                case "power": it.Typ = STyp.Power; it.Unit = "W"; break;
                case "clock": it.Typ = STyp.Clock; it.Unit = "MHz"; break;
                case "voltage": it.Typ = STyp.Volt; it.Unit = "V"; break;
                case "fan": it.Typ = STyp.Fan; it.Unit = "RPM"; break;
                case "load": it.Typ = STyp.Load; it.Unit = "%"; break;
                default: it.Typ = STyp.Other; break;
            }
        }
    }

    // ---------------------------------------------------------------- LibreHardwareMonitor / OpenHardwareMonitor rodando (WMI)
    public class LhmWmiSource : ISensorSource
    {
        ManagementObjectSearcher searcher;
        string ns = "";
        public string Name { get { return "LibreHardwareMonitor/OpenHardwareMonitor (WMI)"; } }

        public bool Init()
        {
            foreach (string n in new string[] { "root\\LibreHardwareMonitor", "root\\OpenHardwareMonitor" })
            {
                try
                {
                    ManagementObjectSearcher s = new ManagementObjectSearcher(n, "SELECT Name,SensorType,Value,Identifier FROM Sensor");
                    int cnt = 0;
                    foreach (ManagementBaseObject o in s.Get()) { cnt++; o.Dispose(); if (cnt > 0) break; }
                    if (cnt > 0) { searcher = s; ns = n; return true; }
                }
                catch { }
            }
            return false;
        }

        public void Close() { searcher = null; }

        public List<SensorItem> Read()
        {
            List<SensorItem> res = new List<SensorItem>();
            foreach (ManagementBaseObject o in searcher.Get())
            {
                try
                {
                    object v = o["Value"];
                    if (v == null) continue;
                    SensorItem it = new SensorItem();
                    it.Name = Convert.ToString(o["Name"]);
                    it.Val = Convert.ToDouble(v);
                    string id = Convert.ToString(o["Identifier"]); // ex.: /amdcpu/0/temperature/0
                    string[] parts = id.Split('/');
                    string hwType = parts.Length > 1 ? parts[1] : "";
                    it.Hw = hwType;
                    LhmDllSource.Classify(hwType, Convert.ToString(o["SensorType"]), it);
                    res.Add(it);
                }
                finally { o.Dispose(); }
            }
            return res;
        }
    }

    // ---------------------------------------------------------------- Hub (várias fontes ao mesmo tempo)
    public class SensorHub
    {
        readonly List<ISensorSource> active = new List<ISensorSource>();
        readonly Dictionary<string, int> failures = new Dictionary<string, int>();
        DateTime lastProbe = DateTime.MinValue;
        public List<SensorItem> Latest = new List<SensorItem>();
        public AdlSource Adl;
        public NvmlSource Nvml;
        public LhmDllSource Lhm;
        public static string BoardVendor = "";
        readonly object gate = new object();

        public SensorHub()
        {
            try
            {
                foreach (System.Management.ManagementObject o in new System.Management.ManagementObjectSearcher(
                    "SELECT Manufacturer FROM Win32_BaseBoard").Get()) { BoardVendor = Convert.ToString(o["Manufacturer"]); break; }
            }
            catch { }
        }

        public bool Available { get { return active.Count > 0; } }

        public string ActiveName
        {
            get
            {
                lock (gate)
                {
                    if (active.Count == 0) return null;
                    List<string> n = new List<string>();
                    foreach (ISensorSource s in active) n.Add(s.Name);
                    return string.Join(" + ", n.ToArray());
                }
            }
        }

        /// <summary>Resumo do que cada parte do PC está fornecendo.</summary>
        public string Coverage
        {
            get
            {
                bool cpuT = false, gpuT = false, board = false;
                foreach (SensorItem it in Latest)
                {
                    if (it.Typ == STyp.Temp && it.Val > 0)
                    {
                        if (it.Cat == SCat.Cpu) cpuT = true;
                        if (it.Cat == SCat.Gpu) gpuT = true;
                    }
                    if (it.Cat == SCat.Board && (it.Typ == STyp.Volt || it.Typ == STyp.Fan)) board = true;
                }
                return "Temperatura da CPU: " + (cpuT ? "sim" : "não") + "  •  GPU: " + (gpuT ? "sim" : "não") +
                    "  •  Placa-mãe (tensões/ventoinhas): " + (board ? "sim" : "não");
            }
        }

        public string LastProbeNote = "";

        void Probe()
        {
            lastProbe = DateTime.Now;
            List<string> notes = new List<string>();
            // GPU nativa
            if (Adl == null && Nvml == null)
            {
                AdlSource a = new AdlSource();
                if (Try(a)) Adl = a;
                else
                {
                    NvmlSource nv = new NvmlSource();
                    if (Try(nv)) Nvml = nv;
                }
            }
            // CPU e placa-mãe (LibreHardwareMonitor + PawnIO)
            if (Lhm == null && LhmDllSource.FindDll() != null)
            {
                LhmDllSource l = new LhmDllSource();
                l.EnableGpu = Adl == null && Nvml == null;
                if (Try(l)) Lhm = l;
                else notes.Add("LibreHardwareMonitor: " + l.Error);
            }
            // HWiNFO opcional
            bool hasHw = false;
            lock (gate) foreach (ISensorSource s in active) if (s is HwinfoSource) hasHw = true;
            if (!hasHw)
            {
                HwinfoSource h = new HwinfoSource();
                Try(h);
            }
            if (!SysUtil.IsAdmin()) notes.Add("Sem privilégios de administrador: temperatura da CPU e sensores da placa-mãe ficam indisponíveis.");
            LastProbeNote = string.Join(" ", notes.ToArray());
        }

        bool Try(ISensorSource s)
        {
            bool ok = false;
            try { ok = s.Init(); } catch { }
            if (ok)
            {
                try { if (s.Read().Count == 0) ok = false; } catch { ok = false; }
            }
            if (!ok) { try { s.Close(); } catch { } return false; }
            lock (gate) active.Add(s);
            failures[s.Name] = 0;
            return true;
        }

        public void Poll()
        {
            if ((DateTime.Now - lastProbe).TotalSeconds >= 30 && (Adl == null && Nvml == null || Lhm == null || active.Count < 3)) Probe();
            List<SensorItem> all = new List<SensorItem>();
            List<ISensorSource> snap;
            lock (gate) snap = new List<ISensorSource>(active);
            foreach (ISensorSource s in snap)
            {
                try
                {
                    all.AddRange(s.Read());
                    failures[s.Name] = 0;
                }
                catch (Exception e)
                {
                    int f; failures.TryGetValue(s.Name, out f); failures[s.Name] = ++f;
                    if (f >= 3)
                    {
                        Log.Write("Fonte de sensores perdida (" + s.Name + "): " + e.Message);
                        try { s.Close(); } catch { }
                        lock (gate) active.Remove(s);
                        if (s == Adl) Adl = null;
                        if (s == Nvml) Nvml = null;
                        if (s == Lhm) Lhm = null;
                    }
                }
            }
            Latest = all;
        }

        public void Close()
        {
            lock (gate)
            {
                foreach (ISensorSource s in active) { try { s.Close(); } catch { } }
                active.Clear();
            }
        }

        // ---------- mapeamento sensor -> campo da amostra
        static readonly Regex Rail12 = new Regex(@"(?<![-\d.])\+?12(\.0+)?\s*V\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex Rail5 = new Regex(@"(?<![-\d.])\+?5(\.0+)?\s*V\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex Rail33 = new Regex(@"(?<![-\d.])\+?3[.,]3\s*V\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static bool Has(string s, params string[] any)
        {
            foreach (string a in any) if (s.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public void Apply(Sample s)
        {
            Map(Latest, s);
            if (Adl != null)
            {
                int g, w; Adl.PcieLink(out g, out w);
                if (g >= 0) s.V[F.PcieGen] = g;
                if (w > 0) s.V[F.PcieLanes] = w;
            }
            else if (Nvml != null)
            {
                if (Nvml.PcieGen > 0) s.V[F.PcieGen] = Nvml.PcieGen;
                if (Nvml.PcieLanes > 0) s.V[F.PcieLanes] = Nvml.PcieLanes;
            }
        }

        static bool ValidTemp(double v) { return v > 1 && v < 126; }

        public static void Map(List<SensorItem> items, Sample s)
        {
            int bestCpuT = -1, bestGpuT = -1, bestGpuH = -1, bestCpuP = -1, bestGpuP = -1, bestGpuC = -1, bestFan = -1;
            double coreClkSum = 0; int coreClkN = 0; double avgClk = double.NaN;
            double diskT = double.NaN, boardT = double.NaN;
            bool gigaIte = Has(BoardVendor, "gigabyte");

            foreach (SensorItem it in items)
            {
                string n = it.Name;
                if (it.Cat == SCat.Cpu)
                {
                    if (it.Typ == STyp.Temp && ValidTemp(it.Val) && !Has(n, "distance", "margin", "limit", "tjmax", "throttl", "iod", "offset", "max"))
                    {
                        int sc = Has(n, "tdie") ? 5 : Has(n, "tctl") ? 4 : Has(n, "die") ? 4 : Has(n, "package") ? 3 : Has(n, "cpu") ? 2 : 1;
                        if (sc > bestCpuT) { bestCpuT = sc; s.V[F.CpuTemp] = it.Val; }
                    }
                    else if (it.Typ == STyp.Power && it.Val > 0 && Has(n, "package", "ppt", "total"))
                    {
                        int sc = Has(n, "package") ? 3 : 2;
                        if (sc > bestCpuP) { bestCpuP = sc; s.V[F.CpuPower] = it.Val; }
                    }
                    else if (it.Typ == STyp.Clock && it.Val > 100)
                    {
                        if (Has(n, "core clocks (avg)", "cores (average)") && !Has(n, "effective"))
                        {
                            avgClk = it.Val;
                        }
                        else if (Regex.IsMatch(n, @"^Core\s*#?\d+", RegexOptions.IgnoreCase) && !Has(n, "effective", "ratio", "bus", "mult", "t1"))
                        {
                            coreClkSum += it.Val; coreClkN++;
                        }
                    }
                    else if (it.Typ == STyp.Volt && (Has(n, "svi2 tfn", "vcore", "core voltage") && !Has(n, "soc", "vid")))
                    {
                        if (double.IsNaN(s.V[F.Vcore])) s.V[F.Vcore] = it.Val;
                    }
                }
                else if (it.Cat == SCat.Gpu)
                {
                    if (it.Typ == STyp.Temp && ValidTemp(it.Val))
                    {
                        if (Has(n, "hot", "junction") && !Has(n, "mem")) { if (bestGpuH < 0) { bestGpuH = 1; s.V[F.GpuHot] = it.Val; } }
                        else if (Has(n, "mem")) { if (double.IsNaN(s.V[F.GpuMemTemp])) s.V[F.GpuMemTemp] = it.Val; }
                        else if (!Has(n, "vr", "vdd", "soc", "hbm", "limit", "distance", "liquid", "intake", "pcb"))
                        {
                            if (bestGpuT < 0) { bestGpuT = 1; s.V[F.GpuTemp] = it.Val; }
                        }
                    }
                    else if (it.Typ == STyp.Power && it.Val > 0 && !Has(n, "soc", "mem", "mvdd", "vddci", "pcie", "8-pin", "6-pin", "12v", "percent", "%") && it.Unit != "%")
                    {
                        int sc = Has(n, "total board", "board") ? 6 : Has(n, "ppt") ? 5 : Has(n, "package") ? 4 : Has(n, "asic", "gpu power") ? 3 : 1;
                        if (sc > bestGpuP) { bestGpuP = sc; s.V[F.GpuPower] = it.Val; }
                    }
                    else if (it.Typ == STyp.Clock && it.Val > 0 && !Has(n, "mem", "video", "soc", "effective"))
                    {
                        int sc = Has(n, "gpu clock", "core") ? 2 : 1;
                        if (sc > bestGpuC) { bestGpuC = sc; s.V[F.GpuClock] = it.Val; }
                    }
                    else if (it.Typ == STyp.Fan && it.Unit != "%" && double.IsNaN(s.V[F.GpuFan]))
                    {
                        s.V[F.GpuFan] = it.Val;
                    }
                    else if (it.Typ == STyp.Volt && Has(n, "core") && double.IsNaN(s.V[F.GpuVolt]))
                    {
                        s.V[F.GpuVolt] = it.Val;
                    }
                }
                else if (it.Cat == SCat.Storage)
                {
                    if (it.Typ == STyp.Temp && !Has(n, "warning", "critical", "limit", "threshold", "max") && ValidTemp(it.Val))
                        if (double.IsNaN(diskT) || it.Val > diskT) diskT = it.Val;
                }
                else if (it.Cat == SCat.Board)
                {
                    if (it.Typ == STyp.Volt)
                    {
                        // chips ITE das placas Gigabyte sem perfil no LHM: aplica os divisores padrão da Gigabyte
                        if (gigaIte && it.Hw.StartsWith("ITE IT86", StringComparison.OrdinalIgnoreCase) && n.StartsWith("Voltage #"))
                        {
                            double v;
                            if (n == "Voltage #1" && it.Val > 0.3 && it.Val < 1.7) s.V[F.Vcore] = it.Val;
                            else if (n == "Voltage #2") { v = it.Val * 1.65; if (v > 2.8 && v < 3.8) s.V[F.V33] = v; }
                            else if (n == "Voltage #3") { v = it.Val * 6.0; if (v > 10 && v < 14) s.V[F.V12] = v; }
                            else if (n == "Voltage #4") { v = it.Val * 2.5; if (v > 4.2 && v < 5.8) s.V[F.V5] = v; }
                        }
                        else if (Rail12.IsMatch(n) && it.Val > 8 && it.Val < 15) s.V[F.V12] = it.Val;
                        else if (Rail5.IsMatch(n) && it.Val > 4 && it.Val < 6) s.V[F.V5] = it.Val;
                        else if (Rail33.IsMatch(n) && it.Val > 2.8 && it.Val < 3.8) s.V[F.V33] = it.Val;
                        else if (Has(n, "vcore", "cpu core") && double.IsNaN(s.V[F.Vcore]) && it.Val > 0.3 && it.Val < 1.7) s.V[F.Vcore] = it.Val;
                    }
                    else if (it.Typ == STyp.Temp && ValidTemp(it.Val))
                    {
                        if (double.IsNaN(boardT) || it.Val > boardT) boardT = it.Val;
                    }
                    else if (it.Typ == STyp.Fan && it.Unit != "%")
                    {
                        int sc = Has(n, "cpu") ? 3 : n == "Fan #1" ? 2 : 0;
                        if (sc > bestFan) { bestFan = sc; s.V[F.CpuFan] = it.Val; }
                    }
                }
            }

            if (!double.IsNaN(avgClk)) s.V[F.CpuClock] = avgClk;
            else if (coreClkN > 0) s.V[F.CpuClock] = coreClkSum / coreClkN;
            if (!double.IsNaN(diskT)) s.V[F.DiskTemp] = diskT;
            if (!double.IsNaN(boardT)) s.V[F.BoardTemp] = boardT;
        }
    }
}
