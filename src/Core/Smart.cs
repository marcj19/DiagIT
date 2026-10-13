using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiagIT
{
    /// <summary>Saúde lida diretamente do disco (NVMe: log de saúde; SATA: atributos SMART). Requer administrador.</summary>
    public class DriveHealth
    {
        public int Number;
        public string Model = "", Bus = "";
        public long SizeBytes;
        public bool Read;                  // conseguiu ler os dados de saúde do próprio disco
        public string Error = "";
        // comuns
        public double TempC = double.NaN, PowerOnHours = double.NaN, PowerCycles = double.NaN, UnsafeShutdowns = double.NaN;
        public double UsedPct = double.NaN;         // desgaste (NVMe "percentage used" / SATA atributo de vida)
        public double SparePct = double.NaN, SpareThreshold = double.NaN;
        public double MediaErrors = double.NaN, ErrLogEntries = double.NaN;
        public double TbWritten = double.NaN, TbRead = double.NaN;
        public int CriticalWarning = -1;
        // SATA
        public double Reallocated = double.NaN, Pending = double.NaN, Uncorrectable = double.NaN, CrcErrors = double.NaN, ReportedUncorrect = double.NaN;
        public List<string> Failing = new List<string>();   // atributos abaixo do limite do fabricante

        public List<string> Problems()
        {
            List<string> p = new List<string>();
            if (CriticalWarning > 0)
            {
                List<string> w = new List<string>();
                if ((CriticalWarning & 1) != 0) w.Add("área reserva abaixo do limite");
                if ((CriticalWarning & 2) != 0) w.Add("temperatura fora do limite");
                if ((CriticalWarning & 4) != 0) w.Add("confiabilidade degradada por erros");
                if ((CriticalWarning & 8) != 0) w.Add("disco em modo somente leitura");
                if ((CriticalWarning & 16) != 0) w.Add("falha no backup de energia");
                p.Add("ALERTA CRÍTICO do próprio SSD: " + string.Join(", ", w.ToArray()));
            }
            if (!double.IsNaN(MediaErrors) && MediaErrors > 0) p.Add(MediaErrors.ToString("N0") + " erro(s) de mídia/integridade de dados registrados pelo SSD");
            if (!double.IsNaN(UsedPct) && UsedPct >= 90) p.Add("vida útil consumida: " + UsedPct.ToString("0") + "%");
            if (!double.IsNaN(SparePct) && !double.IsNaN(SpareThreshold) && SparePct <= SpareThreshold) p.Add("área reserva esgotada (" + SparePct + "%)");
            if (!double.IsNaN(Reallocated) && Reallocated > 0) p.Add(Reallocated.ToString("N0") + " setor(es) realocado(s) (setores defeituosos substituídos)");
            if (!double.IsNaN(Pending) && Pending > 0) p.Add(Pending.ToString("N0") + " setor(es) pendente(s) (ilegíveis, aguardando realocação)");
            if (!double.IsNaN(Uncorrectable) && Uncorrectable > 0) p.Add(Uncorrectable.ToString("N0") + " setor(es) incorrigível(is)");
            if (!double.IsNaN(ReportedUncorrect) && ReportedUncorrect > 0) p.Add(ReportedUncorrect.ToString("N0") + " erro(s) incorrigível(is) reportado(s)");
            if (!double.IsNaN(CrcErrors) && CrcErrors > 0) p.Add(CrcErrors.ToString("N0") + " erro(s) de comunicação (CRC) — cabo SATA/porta com mau contato");
            foreach (string f in Failing) p.Add("atributo abaixo do limite do fabricante: " + f);
            if (!double.IsNaN(TempC) && TempC >= 70) p.Add("temperatura alta agora: " + TempC.ToString("0") + " °C");
            return p;
        }
    }

    public static class Smart
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int ret, IntPtr ov);

        const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
        const uint SMART_RCV_DRIVE_DATA = 0x7C088;
        const uint IOCTL_DISK_GET_LENGTH_INFO = 0x7405C;

        public static List<DriveHealth> ReadAll()
        {
            List<DriveHealth> r = new List<DriveHealth>();
            try
            {
                foreach (System.Management.ManagementObject o in new System.Management.ManagementObjectSearcher(
                    "SELECT Index,Model,InterfaceType,Size FROM Win32_DiskDrive").Get())
                {
                    DriveHealth d = new DriveHealth();
                    d.Number = Convert.ToInt32(o["Index"]);
                    d.Model = Convert.ToString(o["Model"]).Trim();
                    try { d.SizeBytes = Convert.ToInt64(o["Size"]); } catch { }
                    r.Add(d);
                }
            }
            catch (Exception e) { Log.Write("Smart lista: " + e.Message); }
            // tipo de barramento real (NVMe/SATA) vem do MSFT_PhysicalDisk
            try
            {
                System.Management.ManagementScope sc = new System.Management.ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                foreach (System.Management.ManagementObject o in new System.Management.ManagementObjectSearcher(sc,
                    new System.Management.ObjectQuery("SELECT DeviceId,BusType FROM MSFT_PhysicalDisk")).Get())
                {
                    int id = Convert.ToInt32(o["DeviceId"]);
                    int bus = Convert.ToInt32(o["BusType"]);
                    foreach (DriveHealth d in r) if (d.Number == id) d.Bus = bus == 17 ? "NVMe" : bus == 11 ? "SATA" : bus == 7 ? "USB" : bus == 8 ? "RAID" : "outro";
                }
            }
            catch { }
            r.Sort(delegate(DriveHealth a, DriveHealth b) { return a.Number.CompareTo(b.Number); });
            foreach (DriveHealth d in r)
            {
                try
                {
                    if (d.Bus == "NVMe") ReadNvme(d);
                    else ReadSata(d);
                }
                catch (Exception e) { d.Error = e.Message; }
            }
            // alternativa: contadores de confiabilidade do Windows para discos que recusaram a leitura direta
            bool missing = false;
            foreach (DriveHealth d in r) if (!d.Read) missing = true;
            if (missing && SysUtil.IsAdmin())
            {
                try
                {
                    foreach (DiskHealth h in DiskInfo.Read())
                    {
                        int id;
                        if (!int.TryParse(h.DeviceId, out id)) continue;
                        foreach (DriveHealth d in r)
                        {
                            if (d.Number != id || d.Read) continue;
                            d.TempC = h.Temp; d.UsedPct = h.Wear; d.PowerOnHours = h.PowerOnHours;
                            double unc = 0;
                            if (!double.IsNaN(h.ReadErrUncorrected)) unc += h.ReadErrUncorrected;
                            if (!double.IsNaN(h.WriteErrUncorrected)) unc += h.WriteErrUncorrected;
                            if (!double.IsNaN(h.ReadErrUncorrected) || !double.IsNaN(h.WriteErrUncorrected)) d.Uncorrectable = unc;
                            if (h.Health == 1 || h.Health == 2) d.Failing.Add(h.Health == 2 ? "Windows classifica o disco como NÃO SAUDÁVEL" : "Windows classifica o disco com ALERTA");
                            d.Read = true;
                            d.Error = "";
                        }
                    }
                }
                catch { }
            }
            return r;
        }

        static SafeFileHandle Open(int n, bool write)
        {
            return CreateFile(@"\\.\PhysicalDrive" + n, write ? 0xC0000000u : 0x80000000u, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        }

        static ulong U64(byte[] b, int o) { return BitConverter.ToUInt64(b, o); }
        static double U128(byte[] b, int o) { return BitConverter.ToUInt64(b, o) + BitConverter.ToUInt64(b, o + 8) * 18446744073709551616.0; }

        static void ReadNvme(DriveHealth d)
        {
            using (SafeFileHandle h = Open(d.Number, true))
            {
                if (h.IsInvalid) { d.Error = "sem acesso ao disco (requer administrador)"; return; }
                // STORAGE_PROPERTY_QUERY + STORAGE_PROTOCOL_SPECIFIC_DATA (40 bytes) + 512 bytes de log
                byte[] buf = new byte[8 + 40 + 512];
                BitConverter.GetBytes(50).CopyTo(buf, 0);   // StorageDeviceProtocolSpecificProperty
                BitConverter.GetBytes(0).CopyTo(buf, 4);    // PropertyStandardQuery
                BitConverter.GetBytes(3).CopyTo(buf, 8);    // ProtocolTypeNvme
                BitConverter.GetBytes(2).CopyTo(buf, 12);   // NVMeDataTypeLogPage
                BitConverter.GetBytes(2).CopyTo(buf, 16);   // log de saúde (0x02)
                BitConverter.GetBytes(0).CopyTo(buf, 20);
                BitConverter.GetBytes(40).CopyTo(buf, 24);  // ProtocolDataOffset
                BitConverter.GetBytes(512).CopyTo(buf, 28); // ProtocolDataLength
                int ret;
                if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, buf, buf.Length, buf, buf.Length, out ret, IntPtr.Zero))
                {
                    d.Error = "o disco recusou a consulta de saúde (erro " + Marshal.GetLastWin32Error() + ")";
                    return;
                }
                int off = 8 + BitConverter.ToInt32(buf, 8 + 16);   // STORAGE_PROTOCOL_DATA_DESCRIPTOR: dados após o cabeçalho
                if (off < 8 || off + 200 > buf.Length) off = 48;
                d.CriticalWarning = buf[off];
                int k = BitConverter.ToUInt16(buf, off + 1);
                if (k > 200) d.TempC = k - 273;
                d.SparePct = buf[off + 3];
                d.SpareThreshold = buf[off + 4];
                d.UsedPct = buf[off + 5];
                d.TbRead = U128(buf, off + 32) * 512000.0 / 1e12;
                d.TbWritten = U128(buf, off + 48) * 512000.0 / 1e12;
                d.PowerCycles = U128(buf, off + 112);
                d.PowerOnHours = U128(buf, off + 128);
                d.UnsafeShutdowns = U128(buf, off + 144);
                d.MediaErrors = U128(buf, off + 160);
                d.ErrLogEntries = U128(buf, off + 176);
                d.Read = true;
            }
        }

        static void ReadSata(DriveHealth d)
        {
            using (SafeFileHandle h = Open(d.Number, true))
            {
                if (h.IsInvalid) { d.Error = "sem acesso ao disco (requer administrador)"; return; }
                byte[] attrs = SmartCmd(h, d.Number, 0xD0);
                if (attrs == null) { d.Error = "o disco não respondeu ao comando SMART"; return; }
                byte[] thr = SmartCmd(h, d.Number, 0xD1);
                Dictionary<int, int> thresholds = new Dictionary<int, int>();
                if (thr != null)
                    for (int i = 0; i < 30; i++) { int o = 2 + i * 12; if (thr[o] != 0) thresholds[thr[o]] = thr[o + 1]; }
                for (int i = 0; i < 30; i++)
                {
                    int o = 2 + i * 12;
                    int id = attrs[o];
                    if (id == 0) continue;
                    int cur = attrs[o + 3];
                    ulong raw = 0;
                    for (int b = 5; b >= 0; b--) raw = (raw << 8) | attrs[o + 5 + b];
                    double rv = raw;
                    switch (id)
                    {
                        case 5: d.Reallocated = rv; break;
                        case 9: d.PowerOnHours = raw & 0xFFFFFFFF; break;
                        case 12: d.PowerCycles = rv; break;
                        case 187: d.ReportedUncorrect = rv; break;
                        case 194: case 190: if (double.IsNaN(d.TempC)) d.TempC = raw & 0xFF; break;
                        case 196: if (double.IsNaN(d.Reallocated) || d.Reallocated == 0) d.Reallocated = rv; break;
                        case 197: d.Pending = rv; break;
                        case 198: d.Uncorrectable = rv; break;
                        case 199: d.CrcErrors = rv; break;
                        case 192: case 174: d.UnsafeShutdowns = rv; break;
                        case 241: d.TbWritten = rv * 32 * 1048576.0 / 1e12; break;   // comum em SSDs: unidades de 32 MB (aproximado)
                        case 169: case 177: case 202: case 231: case 233:
                            // indicadores de vida restante (valor normalizado 100 -> 0)
                            if (cur > 0 && cur <= 100 && double.IsNaN(d.UsedPct)) d.UsedPct = 100 - cur;
                            break;
                    }
                    int t;
                    if (thresholds.TryGetValue(id, out t) && t > 0 && cur > 0 && cur <= t) d.Failing.Add("ID " + id + " (atual " + cur + " ≤ limite " + t + ")");
                }
                d.Read = true;
            }
        }

        static byte[] SmartCmd(SafeFileHandle h, int drive, byte feature)
        {
            byte[] inb = new byte[32 + 512];
            BitConverter.GetBytes(512).CopyTo(inb, 0);   // cBufferSize
            inb[4] = feature;     // bFeaturesReg
            inb[5] = 1;           // bSectorCountReg
            inb[6] = 1;           // bSectorNumberReg
            inb[7] = 0x4F;        // bCylLowReg
            inb[8] = 0xC2;        // bCylHighReg
            inb[9] = (byte)(0xA0 | ((drive & 1) << 4));
            inb[10] = 0xB0;       // SMART
            inb[12] = (byte)drive;
            byte[] outb = new byte[16 + 512];
            int ret;
            if (!DeviceIoControl(h, SMART_RCV_DRIVE_DATA, inb, 33, outb, outb.Length, out ret, IntPtr.Zero)) return null;
            byte[] data = new byte[512];
            Array.Copy(outb, 16, data, 0, 512);
            return data;
        }

        public static long DiskLength(SafeFileHandle h)
        {
            byte[] o = new byte[8];
            int ret;
            if (!DeviceIoControl(h, IOCTL_DISK_GET_LENGTH_INFO, null, 0, o, 8, out ret, IntPtr.Zero)) return 0;
            return BitConverter.ToInt64(o, 0);
        }
    }
}
