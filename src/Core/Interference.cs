using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;

namespace DiagIT
{
    /// <summary>
    /// Programas e dispositivos que a comunidade (r/AMDHelp, "Ultimate AMD Performance Fix Guide") associa a engasgos,
    /// quedas de FPS e travamentos. Só são citados no diagnóstico quando cruzam com problemas registrados.
    /// </summary>
    public static class Interference
    {
        // prefixo do nome do processo (minúsculo, sem .exe) -> rótulo mostrado
        static readonly string[][] Known =
        {
            new string[] { "wallpaper32", "Wallpaper Engine" },
            new string[] { "wallpaper64", "Wallpaper Engine" },
            new string[] { "amddvr", "Gravação do Adrenalin (ReLive / Instant Replay)" },
            new string[] { "icue", "Corsair iCUE (RGB)" },
            new string[] { "corsair.service", "Corsair iCUE (RGB)" },
            new string[] { "signalrgb", "SignalRGB (RGB)" },
            new string[] { "openrgb", "OpenRGB (RGB)" },
            new string[] { "l-connect", "Lian Li L-Connect (RGB)" },
            new string[] { "armourycrate", "ASUS Armoury Crate / Aura (RGB)" },
            new string[] { "lightingservice", "ASUS Armoury Crate / Aura (RGB)" },
            new string[] { "msi.centralserver", "MSI Center / Mystic Light (RGB)" },
            new string[] { "mysticlight", "MSI Center / Mystic Light (RGB)" },
            new string[] { "razer synapse", "Razer Synapse (RGB)" },
            new string[] { "razerappengine", "Razer Synapse (RGB)" },
            new string[] { "nzxt cam", "NZXT CAM (RGB)" },
            new string[] { "rgbfusion", "Gigabyte RGB Fusion (RGB)" },
        };

        /// <summary>Rótulos dos programas conhecidos abertos agora (sem repetição).</summary>
        public static List<string> Running()
        {
            List<string> r = new List<string>();
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    string n;
                    try { n = p.ProcessName.ToLowerInvariant(); } catch { n = ""; }
                    p.Dispose();
                    foreach (string[] k in Known)
                        if (n.StartsWith(k[0], StringComparison.Ordinal) && !r.Contains(k[1])) r.Add(k[1]);
                }
            }
            catch (Exception e) { Log.Write("Interference: " + e.Message); }
            r.Sort(StringComparer.Ordinal);
            return r;
        }

        public static bool IsRgb(string label) { return label.EndsWith("(RGB)", StringComparison.Ordinal); }

        /// <summary>Placa de rede Realtek 2.5GbE ativa (nome + versão do driver), ou null. Desativada no Gerenciador de Dispositivos não conta.</summary>
        public static string RealtekNic(out string driver)
        {
            driver = "";
            try
            {
                foreach (ManagementObject o in new ManagementObjectSearcher("SELECT Name,PNPDeviceID,ConfigManagerErrorCode FROM Win32_NetworkAdapter WHERE PhysicalAdapter=TRUE").Get())
                {
                    string n = Convert.ToString(o["Name"]);
                    if (n.IndexOf("Realtek", StringComparison.OrdinalIgnoreCase) < 0 || n.IndexOf("2.5G", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (o["ConfigManagerErrorCode"] != null && Convert.ToInt32(o["ConfigManagerErrorCode"]) == 22) continue;   // desativada
                    string id = Convert.ToString(o["PNPDeviceID"]).Replace("\\", "\\\\").Replace("'", "\\'");
                    foreach (ManagementObject d in new ManagementObjectSearcher("SELECT DriverVersion FROM Win32_PnPSignedDriver WHERE DeviceID='" + id + "'").Get())
                        driver = Convert.ToString(d["DriverVersion"]);
                    return n;
                }
            }
            catch (Exception e) { Log.Write("RealtekNic: " + e.Message); }
            return null;
        }
    }
}
