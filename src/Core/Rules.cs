using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace DiagIT
{
    public class BugRule
    {
        public int[] Codes = new int[0];
        public string Name = "", Family = "", Advice = "";
        public Dictionary<Comp, double> W = new Dictionary<Comp, double>();
        public int[] AddrParams = new int[0];   // parâmetros (1..4) que costumam trazer o endereço do código que falhou
    }

    public class VideoVendor { public string Brand = "", Panel = "", DriverSite = "", DduName = ""; public string[] Detect = new string[0]; }
    public class CpuVendor { public string Brand = "", MemProfile = "", CpuTuning = ""; public string[] Detect = new string[0]; }
    public class GpuBoost { public string Model = ""; public int Boost; public bool Offset; }
    public class BgProgram { public string Process = "", Name = "", Type = ""; }
    public class BgType { public string Report = "", Advice = ""; }
    public class PlainRule { public string Prefix = "", Simple = ""; }
    public class DriverType { public string Key = "", Phrase = "", Advice = ""; public Dictionary<Comp, double> W = new Dictionary<Comp, double>(); }
    public class DriverRule { public string File = "", Name = "", Type = ""; public bool Core; }
    public class GuideAction { public string Label = "", Type = "", Target = ""; }
    public class GuideStep { public string Title = "", Text = ""; public List<GuideAction> Actions = new List<GuideAction>(); }
    public class Guide { public string Id = "", Title = "", Summary = "", Done = "", Scope = ""; public List<GuideStep> Steps = new List<GuideStep>(); }

    /// <summary>
    /// Regras de conhecimento em dados (pasta data/ do repositório, embutidas no executável).
    /// Quem contribui edita os JSON sem precisar mexer em C#; uma pasta "data" ao lado do .exe substitui as embutidas (para testar edições).
    /// </summary>
    public static class Rules
    {
        static readonly object gate = new object();
        static bool loaded;
        public static string Source = "";

        public static BugRule BugDefault = new BugRule();
        public static readonly List<BugRule> Bugs = new List<BugRule>();
        public static VideoVendor VideoDefault = new VideoVendor();
        public static CpuVendor CpuDefault = new CpuVendor();
        public static readonly List<VideoVendor> Video = new List<VideoVendor>();
        public static readonly List<CpuVendor> Cpus = new List<CpuVendor>();
        public static readonly List<GpuBoost> Boosts = new List<GpuBoost>();
        public static string BoostAdjust = "", BoostAdjustOffset = "";
        public static readonly List<BgProgram> Programs = new List<BgProgram>();
        public static readonly Dictionary<string, BgType> BgTypes = new Dictionary<string, BgType>();
        public static readonly List<PlainRule> Plain = new List<PlainRule>();
        public static readonly List<DriverRule> Drivers = new List<DriverRule>();
        public static readonly Dictionary<string, DriverType> DriverTypes = new Dictionary<string, DriverType>();
        public static readonly List<Guide> Guides = new List<Guide>();
        public static readonly List<KeyValuePair<string, string>> GuideLinks = new List<KeyValuePair<string, string>>();   // começo do título → guia

        public static readonly string[] Files = { "telas-azuis", "fabricantes", "placas-de-video", "programas", "linguagem-simples", "drivers", "guias" };

        // ------------------------------------------------------------------ leitura
        static string ReadText(string name)
        {
            try
            {
                string over = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", name + ".json");
                if (File.Exists(over)) { Source = "pasta data ao lado do programa"; return File.ReadAllText(over, Encoding.UTF8); }
            }
            catch { }
            Source = "embutidas no programa";
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("DiagIT.data." + name + ".json"))
            {
                if (s == null) throw new FileNotFoundException("Regras não encontradas: data/" + name + ".json");
                using (StreamReader r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        static IEnumerable List(object o) { return (o as IEnumerable) ?? new object[0]; }
        static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : ""; }
        static double Num(object v) { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
        static bool Bool(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v is bool && (bool)v; }
        static string[] Strs(Dictionary<string, object> d, string k)
        {
            object v; List<string> r = new List<string>();
            if (d.TryGetValue(k, out v) && !(v is string)) foreach (object x in List(v)) r.Add(Convert.ToString(x, CultureInfo.InvariantCulture));
            return r.ToArray();
        }

        static int Hex(string s)
        {
            s = s.Trim();
            return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(s.Substring(2), 16) : int.Parse(s, CultureInfo.InvariantCulture);
        }

        static BugRule ParseBug(Dictionary<string, object> d)
        {
            BugRule b = new BugRule();
            List<int> codes = new List<int>();
            foreach (string c in Strs(d, "codigos")) codes.Add(Hex(c));
            b.Codes = codes.ToArray();
            b.Name = Str(d, "nome"); b.Family = Str(d, "familia"); b.Advice = Str(d, "conselho");
            foreach (KeyValuePair<string, object> kv in Obj(d.ContainsKey("pesos") ? d["pesos"] : null))
                b.W[(Comp)Enum.Parse(typeof(Comp), kv.Key, false)] = Num(kv.Value);
            List<int> ap = new List<int>();
            if (d.ContainsKey("parametro_endereco")) foreach (object x in List(d["parametro_endereco"])) ap.Add(Convert.ToInt32(x, CultureInfo.InvariantCulture));
            b.AddrParams = ap.ToArray();
            return b;
        }

        static VideoVendor ParseVideo(Dictionary<string, object> d)
        {
            VideoVendor v = new VideoVendor();
            v.Brand = Str(d, "marca"); v.Panel = Str(d, "painel_video"); v.DriverSite = Str(d, "site_driver"); v.DduName = Str(d, "nome_ddu"); v.Detect = Strs(d, "detectar");
            return v;
        }

        static CpuVendor ParseCpu(Dictionary<string, object> d)
        {
            CpuVendor v = new CpuVendor();
            v.Brand = Str(d, "marca"); v.MemProfile = Str(d, "perfil_memoria"); v.CpuTuning = Str(d, "ajustes_cpu"); v.Detect = Strs(d, "detectar");
            return v;
        }

        /// <summary>Carrega (uma vez) todas as regras. Erro de formato nos JSON vira exceção com o nome do arquivo.</summary>
        public static void Ensure()
        {
            if (loaded) return;
            lock (gate)
            {
                if (loaded) return;
                JavaScriptSerializer js = new JavaScriptSerializer();
                string cur = "";
                try
                {
                    cur = "telas-azuis";
                    Dictionary<string, object> b = Obj(js.DeserializeObject(ReadText(cur)));
                    BugDefault = ParseBug(Obj(b["padrao"]));
                    foreach (object o in List(b["codigos"])) Bugs.Add(ParseBug(Obj(o)));

                    cur = "fabricantes";
                    Dictionary<string, object> f = Obj(js.DeserializeObject(ReadText(cur)));
                    foreach (object o in List(f["video"])) Video.Add(ParseVideo(Obj(o)));
                    VideoDefault = ParseVideo(Obj(f["video_padrao"]));
                    foreach (object o in List(f["processador"])) Cpus.Add(ParseCpu(Obj(o)));
                    CpuDefault = ParseCpu(Obj(f["processador_padrao"]));

                    cur = "placas-de-video";
                    Dictionary<string, object> g = Obj(js.DeserializeObject(ReadText(cur)));
                    foreach (object o in List(g["placas"]))
                    {
                        Dictionary<string, object> x = Obj(o);
                        GpuBoost gb = new GpuBoost(); gb.Model = Str(x, "modelo").ToUpperInvariant(); gb.Boost = (int)Num(x["boost_mhz"]); gb.Offset = Bool(x, "deslocamento");
                        Boosts.Add(gb);
                    }
                    BoostAdjust = Str(g, "ajuste"); BoostAdjustOffset = Str(g, "ajuste_deslocamento");

                    cur = "programas";
                    Dictionary<string, object> p = Obj(js.DeserializeObject(ReadText(cur)));
                    foreach (KeyValuePair<string, object> kv in Obj(p["tipos"]))
                    {
                        Dictionary<string, object> x = Obj(kv.Value);
                        BgType t = new BgType(); t.Report = Str(x, "relato"); t.Advice = Str(x, "conselho");
                        BgTypes[kv.Key] = t;
                    }
                    foreach (object o in List(p["programas"]))
                    {
                        Dictionary<string, object> x = Obj(o);
                        BgProgram bp = new BgProgram(); bp.Process = Str(x, "processo").ToLowerInvariant(); bp.Name = Str(x, "nome"); bp.Type = Str(x, "tipo");
                        Programs.Add(bp);
                    }

                    cur = "linguagem-simples";
                    Dictionary<string, object> l = Obj(js.DeserializeObject(ReadText(cur)));
                    foreach (object o in List(l["titulos"]))
                    {
                        Dictionary<string, object> x = Obj(o);
                        PlainRule pr = new PlainRule(); pr.Prefix = Str(x, "inicio_do_titulo"); pr.Simple = Str(x, "simples");
                        Plain.Add(pr);
                    }

                    cur = "drivers";
                    Dictionary<string, object> dr = Obj(js.DeserializeObject(ReadText(cur)));
                    foreach (KeyValuePair<string, object> kv in Obj(dr["tipos"]))
                    {
                        Dictionary<string, object> x = Obj(kv.Value);
                        DriverType t = new DriverType(); t.Key = kv.Key; t.Phrase = Str(x, "frase"); t.Advice = Str(x, "conselho");
                        foreach (KeyValuePair<string, object> w in Obj(x.ContainsKey("pesos") ? x["pesos"] : null))
                            t.W[(Comp)Enum.Parse(typeof(Comp), w.Key, false)] = Num(w.Value);
                        DriverTypes[kv.Key] = t;
                    }
                    foreach (object o in List(dr["drivers"]))
                    {
                        Dictionary<string, object> x = Obj(o);
                        DriverRule r = new DriverRule(); r.File = Str(x, "arquivo").ToLowerInvariant(); r.Name = Str(x, "nome"); r.Type = Str(x, "tipo"); r.Core = Bool(x, "nucleo");
                        Drivers.Add(r);
                    }

                    cur = "guias";
                    Dictionary<string, object> gd = Obj(js.DeserializeObject(ReadText(cur)));
                    foreach (object o in List(gd["guias"]))
                    {
                        Dictionary<string, object> x = Obj(o);
                        Guide gu = new Guide();
                        gu.Id = Str(x, "id"); gu.Title = Str(x, "titulo"); gu.Summary = Str(x, "resumo"); gu.Done = Str(x, "feito"); gu.Scope = Str(x, "escopo");
                        foreach (object so in List(x["passos"]))
                        {
                            Dictionary<string, object> sx = Obj(so);
                            GuideStep st = new GuideStep(); st.Title = Str(sx, "titulo"); st.Text = Str(sx, "texto");
                            if (sx.ContainsKey("acoes"))
                                foreach (object ao in List(sx["acoes"]))
                                {
                                    Dictionary<string, object> ax = Obj(ao);
                                    GuideAction ga = new GuideAction(); ga.Label = Str(ax, "rotulo"); ga.Type = Str(ax, "tipo"); ga.Target = Str(ax, "alvo");
                                    st.Actions.Add(ga);
                                }
                            gu.Steps.Add(st);
                        }
                        Guides.Add(gu);
                    }
                    foreach (object o in List(gd["achados"]))
                    {
                        Dictionary<string, object> x = Obj(o);
                        GuideLinks.Add(new KeyValuePair<string, string>(Str(x, "inicio_do_titulo"), Str(x, "guia")));
                    }
                }
                catch (Exception e)
                {
                    throw new InvalidDataException("Erro nas regras data/" + cur + ".json: " + e.Message, e);
                }
                loaded = true;
            }
        }

        // ------------------------------------------------------------------ consultas
        public static BugRule Bug(int code)
        {
            Ensure();
            foreach (BugRule b in Bugs) if (Array.IndexOf(b.Codes, code) >= 0) return b;
            return BugDefault;
        }

        static bool Matches(string name, string[] detect)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (string d in detect) if (name.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static VideoVendor VideoFor(string gpuName)
        {
            Ensure();
            foreach (VideoVendor v in Video) if (Matches(gpuName, v.Detect)) return v;
            return VideoDefault;
        }

        public static CpuVendor CpuFor(string cpuName)
        {
            Ensure();
            foreach (CpuVendor v in Cpus) if (Matches(cpuName, v.Detect)) return v;
            return CpuDefault;
        }

        /// <summary>O que é um arquivo de driver (o prefixo mais longo vence: "rtkvhd64" antes de "rtk"). null = desconhecido.</summary>
        public static DriverRule Driver(string file)
        {
            Ensure();
            if (string.IsNullOrEmpty(file)) return null;
            string f = file.Trim().ToLowerInvariant();
            DriverRule best = null;
            foreach (DriverRule r in Drivers)
                if (f.StartsWith(r.File, StringComparison.Ordinal) && (best == null || r.File.Length > best.File.Length)) best = r;
            return best;
        }

        public static DriverType DriverTypeOf(DriverRule r)
        {
            DriverType t;
            return r != null && DriverTypes.TryGetValue(r.Type, out t) ? t : null;
        }

        public static Guide GuideById(string id)
        {
            Ensure();
            return Guides.Find(delegate(Guide g) { return g.Id == id; });
        }

        public static BgProgram ProgramByName(string name)
        {
            Ensure();
            foreach (BgProgram p in Programs) if (p.Name == name) return p;
            return null;
        }

        // ------------------------------------------------------------------ textos por fabricante
        [ThreadStatic] static VideoVendor curVideo;
        [ThreadStatic] static CpuVendor curCpu;

        /// <summary>Define as marcas usadas por Fill nesta thread (a análise roda em segundo plano).</summary>
        public static void SetContext(string gpuName, string cpuName)
        {
            curVideo = VideoFor(gpuName);
            curCpu = CpuFor(cpuName);
        }

        /// <summary>Troca {painel_video}, {site_driver}, {nome_ddu}, {perfil_memoria} e {ajustes_cpu} pelos textos da marca do PC.</summary>
        public static string Fill(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('{') < 0) return s;
            Ensure();
            VideoVendor v = curVideo ?? VideoDefault;
            CpuVendor c = curCpu ?? CpuDefault;
            return s.Replace("{painel_video}", v.Panel).Replace("{site_driver}", v.DriverSite).Replace("{nome_ddu}", v.DduName)
                    .Replace("{perfil_memoria}", c.MemProfile).Replace("{ajustes_cpu}", c.CpuTuning);
        }
    }
}
