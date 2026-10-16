using System;
using System.Collections.Generic;
using System.Management;

namespace DiagIT
{
    /// <summary>Uma placa de vídeo como o Windows a enxerga agora.</summary>
    public class VideoAdapter
    {
        public string Name = "", DriverVersion = "", PnpId = "", Maker = "", Drivers = "", Inf = "";
        public DateTime DriverDate = DateTime.MinValue;
        /// <summary>Adaptador genérico do Windows (sem driver do fabricante) ou virtual (área de trabalho remota).</summary>
        public bool Generic;
    }

    /// <summary>Leitura única das placas de vídeo, igual em qualquer idioma do Windows.</summary>
    public static class VideoAdapters
    {
        /// <summary>
        /// O Windows chama o adaptador sem driver de "Microsoft Basic Display Adapter" em inglês e "Adaptador de Vídeo Básico da Microsoft"
        /// em português: o nome muda com o idioma, então o critério principal é o fabricante (placa de verdade nunca é "Microsoft")
        /// e o arquivo do driver (BasicDisplay.sys).
        /// </summary>
        public static bool IsGeneric(string name, string maker, string drivers)
        {
            if ((maker ?? "").StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) return true;
            string dr = (drivers ?? "").ToLowerInvariant();
            if (dr.Contains("basicdisplay") || dr.Contains("basicrender")) return true;
            string n = (name ?? "").ToLowerInvariant();
            return n.Contains("basic") || n.Contains("básico") || n.Contains("basico") || n.Contains("remote") || n.Contains("remoto") ||
                   n.Contains("virtual") || n.Contains("indirect display") || n.Contains("mirror driver");
        }

        public static List<VideoAdapter> ReadAll()
        {
            List<VideoAdapter> l = new List<VideoAdapter>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name,DriverVersion,DriverDate,PNPDeviceID,AdapterCompatibility,InstalledDisplayDrivers,InfFilename FROM Win32_VideoController"))
                    foreach (ManagementObject o in s.Get())
                    {
                        VideoAdapter a = new VideoAdapter();
                        a.Name = Convert.ToString(o["Name"]) ?? "";
                        a.DriverVersion = Convert.ToString(o["DriverVersion"]) ?? "";
                        a.PnpId = Convert.ToString(o["PNPDeviceID"]) ?? "";
                        a.Maker = Convert.ToString(o["AdapterCompatibility"]) ?? "";
                        a.Drivers = Convert.ToString(o["InstalledDisplayDrivers"]) ?? "";
                        a.Inf = Convert.ToString(o["InfFilename"]) ?? "";
                        try { string dd = Convert.ToString(o["DriverDate"]); if (!string.IsNullOrEmpty(dd)) a.DriverDate = ManagementDateTimeConverter.ToDateTime(dd); } catch { }
                        a.Generic = IsGeneric(a.Name, a.Maker, a.Drivers);
                        l.Add(a);
                    }
            }
            catch (Exception e) { Log.Write("VideoAdapters: " + e.Message); }
            return l;
        }

        /// <summary>Quando o driver em uso entrou no Windows (o .inf é copiado para C:\Windows\INF na instalação), ou MinValue se não der para saber.</summary>
        public static DateTime InstalledAt(VideoAdapter a)
        {
            try
            {
                if (a == null || a.Inf.Length == 0) return DateTime.MinValue;
                string p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF", a.Inf);
                return System.IO.File.Exists(p) ? System.IO.File.GetCreationTime(p) : DateTime.MinValue;
            }
            catch { return DateTime.MinValue; }
        }

        /// <summary>A placa de verdade (a primeira que não é genérica), ou null se só há o adaptador básico.</summary>
        /// <summary>
        /// Placa de vídeo integrada ao processador (usa a memória RAM do PC): Intel UHD / Iris Xe / "Arc Graphics" dos Core Ultra,
        /// AMD "Radeon Graphics" / "Radeon 780M" / "Vega 8". As dedicadas têm número de modelo: Intel Arc A770, Radeon RX 7600, qualquer NVIDIA.
        /// </summary>
        public static bool IsIntegrated(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Length == 0 || n.Contains("nvidia") || n.Contains("geforce") || n.Contains("quadro")) return false;
            if (n.Contains("intel"))
                return !System.Text.RegularExpressions.Regex.IsMatch(n, @"arc\S*\s+(\(tm\)\s+)?[ab]\d{3}");
            if (n.Contains("radeon") || n.Contains("amd") || n.Contains("vega"))
                return !n.Contains(" rx ") && !n.Contains("rx ") && !n.Contains(" pro ") &&
                       (n.Contains("vega") || System.Text.RegularExpressions.Regex.IsMatch(n, @"\d{3}m\b") || n.TrimEnd().EndsWith("graphics"));
            return false;
        }

        /// <summary>A placa principal: a dedicada quando o PC tem as duas (notebook com Intel + NVIDIA, por exemplo), senão a integrada.</summary>
        public static VideoAdapter Primary(List<VideoAdapter> l)
        {
            foreach (VideoAdapter a in l) if (!a.Generic && !IsIntegrated(a.Name)) return a;
            foreach (VideoAdapter a in l) if (!a.Generic) return a;
            return null;
        }

        /// <summary>Marca da placa pelo código do fabricante no PCI, para quando ainda não há driver (e o nome não diz nada).</summary>
        public static string VendorFromPnp(string pnpId)
        {
            string id = (pnpId ?? "").ToUpperInvariant();
            if (id.Contains("VEN_10DE")) return "NVIDIA";
            if (id.Contains("VEN_1002") || id.Contains("VEN_1022")) return "AMD Radeon";
            if (id.Contains("VEN_8086")) return "Intel";
            return "";
        }

        /// <summary>Resumo que muda quando a placa ou o driver mudam (usado para perceber uma reinstalação com o app aberto).</summary>
        public static string Signature(List<VideoAdapter> l)
        {
            List<string> p = new List<string>();
            foreach (VideoAdapter a in l) p.Add(a.Name + "|" + a.DriverVersion + "|" + (a.Generic ? "g" : "r"));
            p.Sort(StringComparer.Ordinal);
            return string.Join(";", p.ToArray());
        }

        public static string Describe(VideoAdapter a)
        {
            if (a == null) return "sem driver de vídeo";
            return a.Name + " — driver " + a.DriverVersion + (a.DriverDate > DateTime.MinValue ? " de " + a.DriverDate.ToString("dd/MM/yyyy") : "");
        }
    }
}
