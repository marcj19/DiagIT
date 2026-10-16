using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DiagIT
{
    public static class Paths
    {
        public static readonly string Root = Environment.GetEnvironmentVariable("DIAGIT_DATA") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiagIT");
        public static readonly string Sessions = Path.Combine(Root, "sessoes");
        public static readonly string Reports = Path.Combine(Root, "relatorios");
        public static readonly string Tests = Path.Combine(Root, "testes");
        public static readonly string SettingsFile = Path.Combine(Root, "config.ini");
        public static readonly string LogFile = Path.Combine(Root, "diagit.log");

        /// <summary>Versões antigas gravavam em %LOCALAPPDATA%\DiagJogo: move os dados para a pasta nova uma única vez.</summary>
        public static void MigrateLegacy()
        {
            try
            {
                if (Environment.GetEnvironmentVariable("DIAGIT_DATA") != null) return;
                string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Diag" + "Jogo");
                if (!Directory.Exists(old) || Directory.Exists(Root)) return;
                Directory.Move(old, Root);
                string oldLog = Path.Combine(Root, "diag" + "jogo.log");
                if (File.Exists(oldLog) && !File.Exists(LogFile)) File.Move(oldLog, LogFile);
                Log.Write("Dados migrados de " + old);
            }
            catch { }
        }

        public static void Ensure()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Sessions);
            Directory.CreateDirectory(Reports);
            Directory.CreateDirectory(Tests);
        }
    }

    public static class Log
    {
        static readonly object gate = new object();

        public static void Write(string msg)
        {
            try
            {
                lock (gate)
                {
                    FileInfo fi = new FileInfo(Paths.LogFile);
                    if (fi.Exists && fi.Length > 512 * 1024)
                    {
                        string old = Paths.LogFile + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(Paths.LogFile, old);
                    }
                    File.AppendAllText(Paths.LogFile,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    public class AppSettings
    {
        public double IntervalSec = 2;
        public bool AutoDetect = true;
        public double MinGpuForGame = 20;
        public double StartDelaySec = 12;
        public bool CloseToTray = true;
        public bool StartMinimized = false;
        public bool Notify = true;
        public bool AlwaysAdmin = true;
        public bool Overlay = true;          // painel no jogo (FPS, temperaturas) durante as partidas
        public bool Technical = false;       // modo de exibição: false = simples (padrão), true = técnico (códigos, fontes, tudo aberto)
        public int OverlayCorner = 0;        // 0 sup. esquerdo, 1 sup. direito, 2 inf. esquerdo, 3 inf. direito
        public string IgnoreList =
            "dwm,explorer,chrome,msedge,msedgewebview2,firefox,brave,opera,vivaldi,steamwebhelper,discord,spotify," +
            "vlc,obs64,obs-browser-page,taskmgr,searchhost,startmenuexperiencehost,shellexperiencehost," +
            "applicationframehost,textinputhost,widgets,whatsapp,claude,code,devenv,wallpaper32,wallpaper64," +
            "systemsettings,mpc-hc64,potplayer,potplayermini64,amdrsserv,radeonsoftware,cncmd,diagit,lockapp," +
            "photos,microsoft.photos,mspaint,snippingtool,screenclippinghost";

        public string GameList =
            "league of legends,valorant-win64-shipping,cs2,csgo,dota2,fortniteclient-win64-shipping,gta5,gtav,r5apex,overwatch," +
            "eldenring,witcher3,cyberpunk2077,starfield,tslgame,destiny2,rocketleague,robloxplayerbeta,fifa23,fc24,fc25,fc26," +
            "minecraft.windows,wow,wow-64,hogwartslegacy,rdr2,apex_legends,pubg,battlefield2042,bf2042,codbo6,cod,modernwarfare," +
            "helldivers2,palworld-win64-shipping,baldursgate3,bg3,bg3_dx11,eurotrucks2,ets2,aoe2de_s,sc2_x64,diablo iv,lostark,tarkov," +
            "rainbowsix,rainbowsix_vulkan,rainbowsix_be";

        HashSet<string> ignoreSet, gameSet;

        public HashSet<string> Games
        {
            get
            {
                if (gameSet == null)
                {
                    gameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string p in GameList.Split(new char[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = p.Trim();
                        if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 4);
                        if (t.Length > 0) gameSet.Add(t);
                    }
                }
                return gameSet;
            }
        }

        public HashSet<string> Ignored
        {
            get
            {
                if (ignoreSet == null)
                {
                    ignoreSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string p in IgnoreList.Split(new char[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = p.Trim();
                        if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 4);
                        if (t.Length > 0) ignoreSet.Add(t);
                    }
                }
                return ignoreSet;
            }
        }

        public void ResetCache() { ignoreSet = null; gameSet = null; }

        /// <summary>true = esse programa nunca é um jogo (comunicação, navegador, produtividade): a lista fica em data/normal.json.</summary>
        public static bool IsNeverGame(string name) { return Rules.IsNeverGame(name); }


        /// <summary>"Isso não é um jogo": tira o programa da lista de jogos e põe na de ignorados, e salva.</summary>
        public void MarkNotAGame(string proc)
        {
            if (string.IsNullOrEmpty(proc)) return;
            string n = proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? proc.Substring(0, proc.Length - 4) : proc;
            List<string> keep = new List<string>();
            foreach (string g in GameList.Split(new char[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = g.Trim();
                if (!t.Equals(n, StringComparison.OrdinalIgnoreCase) && !t.Equals(n + ".exe", StringComparison.OrdinalIgnoreCase)) keep.Add(t);
            }
            GameList = string.Join(",", keep.ToArray());
            AddIgnored(n);
            ResetCache();
            Save();
        }

        public void AddIgnored(string proc)
        {
            if (Ignored.Contains(proc)) return;
            IgnoreList = IgnoreList.TrimEnd(',') + "," + proc;
            ignoreSet = null;
        }

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try
            {
                if (!File.Exists(Paths.SettingsFile)) return s;
                foreach (string line in File.ReadAllLines(Paths.SettingsFile, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant();
                    string v = line.Substring(i + 1).Trim();
                    double d;
                    switch (k)
                    {
                        case "interval": if (Dbl(v, out d)) s.IntervalSec = Math.Max(1, Math.Min(10, d)); break;
                        case "autodetect": s.AutoDetect = v == "1"; break;
                        case "mingpu": if (Dbl(v, out d)) s.MinGpuForGame = Math.Max(5, Math.Min(95, d)); break;
                        case "startdelay": if (Dbl(v, out d)) s.StartDelaySec = Math.Max(3, Math.Min(120, d)); break;
                        case "closetotray": s.CloseToTray = v == "1"; break;
                        case "startminimized": s.StartMinimized = v == "1"; break;
                        case "notify": s.Notify = v == "1"; break;
                        case "admin": s.AlwaysAdmin = v == "1"; break;
                        case "overlay": s.Overlay = v == "1"; break;
                        case "mode": s.Technical = v == "tecnico"; break;
                        case "overlay_corner": int oc; if (int.TryParse(v, out oc)) s.OverlayCorner = Math.Max(0, Math.Min(3, oc)); break;
                        case "ignore": if (v.Length > 0) s.IgnoreList = v; break;
                        case "games": if (v.Length > 0) s.GameList = v; break;
                    }
                }
            }
            catch (Exception e) { Log.Write("Falha ao ler config: " + e.Message); }
            return s;
        }

        static bool Dbl(string v, out double d)
        {
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }

        public void Save()
        {
            try
            {
                Paths.Ensure();
                string[] lines =
                {
                    "interval=" + IntervalSec.ToString(CultureInfo.InvariantCulture),
                    "autodetect=" + (AutoDetect ? "1" : "0"),
                    "mingpu=" + MinGpuForGame.ToString(CultureInfo.InvariantCulture),
                    "startdelay=" + StartDelaySec.ToString(CultureInfo.InvariantCulture),
                    "closetotray=" + (CloseToTray ? "1" : "0"),
                    "startminimized=" + (StartMinimized ? "1" : "0"),
                    "notify=" + (Notify ? "1" : "0"),
                    "admin=" + (AlwaysAdmin ? "1" : "0"),
                    "overlay=" + (Overlay ? "1" : "0"),
                    "mode=" + (Technical ? "tecnico" : "simples"),
                    "overlay_corner=" + OverlayCorner,
                    "games=" + GameList,
                    "ignore=" + IgnoreList
                };
                File.WriteAllLines(Paths.SettingsFile, lines, Encoding.UTF8);
            }
            catch (Exception e) { Log.Write("Falha ao salvar config: " + e.Message); }
        }
    }

    public class SessionInfo
    {
        public string Id;
        public string Game = "";
        public DateTime Start;
        public DateTime? End;
        public bool Manual;
        public string Status = "running"; // running | ok | interrupted
        public string Note = "";
        /// <summary>Programas em segundo plano que a comunidade associa a engasgos/travamentos, abertos durante a partida.</summary>
        public List<string> Bg = new List<string>();
        /// <summary>Desempenho medido pelo PresentMon (NaN/0 = não medido).</summary>
        public double FpsAvg = double.NaN, FpsLow = double.NaN;
        public int Stutters, Frames;
        public int GamePid = -1;           // não persistido

        public string MetaPath { get { return Path.Combine(Paths.Sessions, Id + ".meta"); } }
        public string CsvPath { get { return Path.Combine(Paths.Sessions, Id + ".csv"); } }

        public TimeSpan Duration
        {
            get { return (End.HasValue ? End.Value : DateTime.Now) - Start; }
        }

        public string StatusLabel
        {
            get
            {
                switch (Status)
                {
                    case "running": return "Gravando";
                    case "interrupted": return "INTERROMPIDA (queda/travamento?)";
                    default: return "Concluída";
                }
            }
        }

        public static long ToUnix(DateTime local)
        {
            return (long)(local.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        public static DateTime FromUnix(double secs)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(secs).ToLocalTime();
        }

        public void Save()
        {
            List<string> l = new List<string>();
            l.Add("id=" + Id);
            l.Add("game=" + Game.Replace('\n', ' '));
            l.Add("start=" + ToUnix(Start));
            if (End.HasValue) l.Add("end=" + ToUnix(End.Value));
            l.Add("manual=" + (Manual ? "1" : "0"));
            l.Add("status=" + Status);
            l.Add("note=" + Note.Replace('\n', ' ').Replace('\r', ' '));
            if (Bg.Count > 0) l.Add("bg=" + string.Join("|", Bg.ToArray()));
            if (Frames > 0)
            {
                l.Add("fps_avg=" + FpsAvg.ToString("0.0", CultureInfo.InvariantCulture));
                l.Add("fps_low1=" + FpsLow.ToString("0.0", CultureInfo.InvariantCulture));
                l.Add("stutters=" + Stutters);
                l.Add("frames=" + Frames);
            }
            File.WriteAllLines(MetaPath, l.ToArray(), Encoding.UTF8);
        }

        public static SessionInfo LoadMeta(string path)
        {
            try
            {
                SessionInfo s = new SessionInfo();
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i);
                    string v = line.Substring(i + 1);
                    double d;
                    switch (k)
                    {
                        case "id": s.Id = v; break;
                        case "game": s.Game = v; break;
                        case "start": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.Start = FromUnix(d); break;
                        case "end": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.End = FromUnix(d); break;
                        case "manual": s.Manual = v == "1"; break;
                        case "status": s.Status = v; break;
                        case "note": s.Note = v; break;
                        case "bg": s.Bg = new List<string>(v.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries)); break;
                        case "fps_avg": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.FpsAvg = d; break;
                        case "fps_low1": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.FpsLow = d; break;
                        case "stutters": int.TryParse(v, out s.Stutters); break;
                        case "frames": int.TryParse(v, out s.Frames); break;
                    }
                }
                if (string.IsNullOrEmpty(s.Id)) return null;
                return s;
            }
            catch { return null; }
        }
    }

    public class SessionWriter : IDisposable
    {
        StreamWriter w;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public SessionWriter(string csvPath)
        {
            bool exists = File.Exists(csvPath);
            w = new StreamWriter(new FileStream(csvPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
            w.AutoFlush = true;
            if (!exists) w.WriteLine("t," + string.Join(",", F.Names));
        }

        public void Append(Sample s)
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append(SessionInfo.ToUnix(s.Time).ToString(Inv));
            for (int i = 0; i < F.Count; i++)
            {
                sb.Append(',');
                double v = s.V[i];
                if (!double.IsNaN(v) && !double.IsInfinity(v)) sb.Append(v.ToString("0.###", Inv));
            }
            w.WriteLine(sb.ToString());
        }

        public void Dispose()
        {
            try { w.Dispose(); } catch { }
        }
    }

    public static class SessionStore
    {
        public static List<SessionInfo> ListAll()
        {
            List<SessionInfo> r = new List<SessionInfo>();
            try
            {
                Paths.Ensure();
                foreach (string f in Directory.GetFiles(Paths.Sessions, "*.meta"))
                {
                    SessionInfo s = SessionInfo.LoadMeta(f);
                    if (s != null) r.Add(s);
                }
            }
            catch (Exception e) { Log.Write("ListAll: " + e.Message); }
            r.Sort(delegate(SessionInfo a, SessionInfo b) { return b.Start.CompareTo(a.Start); });
            return r;
        }

        public static List<Sample> LoadSamples(SessionInfo s)
        {
            List<Sample> r = new List<Sample>();
            try
            {
                if (!File.Exists(s.CsvPath)) return r;
                using (FileStream fs = new FileStream(s.CsvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string header = sr.ReadLine();
                    if (header == null) return r;
                    string[] cols = header.Split(',');
                    int[] map = new int[cols.Length];
                    for (int i = 0; i < cols.Length; i++) map[i] = Array.IndexOf(F.Names, cols[i]);
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        string[] p = line.Split(',');
                        if (p.Length < 2) continue;
                        double t;
                        if (!double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out t)) continue;
                        Sample sm = new Sample();
                        sm.Time = SessionInfo.FromUnix(t);
                        for (int i = 1; i < p.Length && i < map.Length; i++)
                        {
                            if (map[i] < 0 || p[i].Length == 0) continue;
                            double v;
                            if (double.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) sm.V[map[i]] = v;
                        }
                        r.Add(sm);
                    }
                }
            }
            catch (Exception e) { Log.Write("LoadSamples: " + e.Message); }
            return r;
        }

        /// <summary>Sessões ainda marcadas como "running" na inicialização = o app/PC caiu durante a partida.</summary>
        public static List<SessionInfo> RecoverInterrupted()
        {
            List<SessionInfo> rec = new List<SessionInfo>();
            foreach (SessionInfo s in ListAll())
            {
                if (s.Status != "running") continue;
                List<Sample> smp = LoadSamples(s);
                s.End = smp.Count > 0 ? smp[smp.Count - 1].Time : s.Start;
                if (SysUtil.BootTime() < s.End.Value)
                {
                    // o Windows não reiniciou depois da gravação: só o DiagIT foi fechado
                    s.Status = "ok";
                    if (s.Note.Length == 0 || s.Note == "teste") s.Note = (s.Note == "teste" ? "teste; " : "") + "gravação encerrada porque o DiagIT foi fechado";
                    try { s.Save(); } catch { }
                    continue;
                }
                s.Status = "interrupted";
                try { s.Save(); } catch { }
                rec.Add(s);
            }
            return rec;
        }

        public static void Delete(SessionInfo s)
        {
            try { if (File.Exists(s.CsvPath)) File.Delete(s.CsvPath); } catch { }
            try { if (File.Exists(s.MetaPath)) File.Delete(s.MetaPath); } catch { }
        }
    }
}
