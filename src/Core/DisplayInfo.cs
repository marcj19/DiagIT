using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DiagIT
{
    /// <summary>O modo de vídeo da tela principal: resolução e taxa de atualização atuais e a maior que o monitor oferece nessa resolução.</summary>
    public class DisplayMode
    {
        public int Width, Height, CurrentHz, MaxHz;
        public List<int> Rates = new List<int>();
    }

    public static class DisplayInfo
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType,
                dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        const int ENUM_CURRENT_SETTINGS = -1;

        /// <summary>Lê o modo atual da tela principal e as taxas de atualização oferecidas na mesma resolução. null se não der para ler.</summary>
        public static DisplayMode Read()
        {
            try
            {
                DEVMODE cur = new DEVMODE();
                cur.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref cur) || cur.dmPelsWidth <= 0) return null;
                DisplayMode m = new DisplayMode();
                m.Width = cur.dmPelsWidth; m.Height = cur.dmPelsHeight; m.CurrentHz = cur.dmDisplayFrequency;
                for (int i = 0; i < 400; i++)
                {
                    DEVMODE dm = new DEVMODE();
                    dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                    if (!EnumDisplaySettings(null, i, ref dm)) break;
                    if (dm.dmPelsWidth != cur.dmPelsWidth || dm.dmPelsHeight != cur.dmPelsHeight || dm.dmBitsPerPel != cur.dmBitsPerPel) continue;
                    int hz = dm.dmDisplayFrequency;
                    if (hz > 1 && !m.Rates.Contains(hz)) m.Rates.Add(hz);
                }
                m.Rates.Sort();
                m.MaxHz = m.Rates.Count > 0 ? m.Rates[m.Rates.Count - 1] : m.CurrentHz;
                if (m.CurrentHz > m.MaxHz) m.MaxHz = m.CurrentHz;
                return m;
            }
            catch (Exception e) { Log.Write("DisplayInfo: " + e.Message); return null; }
        }
    }
}
