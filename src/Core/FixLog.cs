using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DiagIT
{
    /// <summary>Uma correção que o usuário aplicou (pelo DiagIT ou marcando "Já fiz isso").</summary>
    public class FixEntry
    {
        public string Id = "";
        public DateTime When;
        public string Kind = "";     // origem: "guia:driver-video-limpo", "resolver:memoria-virtual", "achado"...
        public string Title = "";    // em linguagem simples: "Reinstalei o driver de vídeo do jeito certo"
        public string Scope = "";    // que problema acompanhar: telas-azuis | video | desligamentos | engasgos | geral
        public int LastState = -1;   // último estado avisado ao usuário (para notificar só quando muda)
    }

    /// <summary>Correções guardadas em %LOCALAPPDATA%\DiagIT\correcoes.json.</summary>
    public static class FixLog
    {
        static readonly object gate = new object();
        static string FilePath { get { return Path.Combine(Paths.Root, "correcoes.json"); } }

        public static List<FixEntry> Load()
        {
            lock (gate)
            {
                try
                {
                    if (!File.Exists(FilePath)) return new List<FixEntry>();
                    List<FixEntry> l = new JavaScriptSerializer().Deserialize<List<FixEntry>>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (l == null) return new List<FixEntry>();
                    foreach (FixEntry f in l) f.When = f.When.ToLocalTime();   // o serializador guarda em UTC
                    return l;
                }
                catch (Exception e) { Log.Write("FixLog: " + e.Message); return new List<FixEntry>(); }
            }
        }

        public static void Save(List<FixEntry> l)
        {
            lock (gate)
            {
                try
                {
                    Paths.Ensure();
                    File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(l), new UTF8Encoding(false));
                }
                catch (Exception e) { Log.Write("FixLog: " + e.Message); }
            }
        }

        public static FixEntry Add(string kind, string title, string scope)
        {
            List<FixEntry> l = Load();
            // a mesma correção registrada de novo no mesmo dia não duplica
            l.RemoveAll(delegate(FixEntry x) { return x.Kind == kind && (DateTime.Now - x.When).TotalHours < 12; });
            FixEntry f = new FixEntry();
            f.Id = DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + l.Count;
            f.When = DateTime.Now; f.Kind = kind; f.Title = title; f.Scope = string.IsNullOrEmpty(scope) ? "geral" : scope;
            l.Add(f);
            Save(l);
            Log.Write("Correção registrada para acompanhamento: " + title + " (" + f.Scope + ")");
            return f;
        }

        public static void Remove(string id)
        {
            List<FixEntry> l = Load();
            l.RemoveAll(delegate(FixEntry x) { return x.Id == id; });
            Save(l);
        }
    }
}
