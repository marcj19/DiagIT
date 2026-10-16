using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DiagIT
{
    /// <summary>Uma anotação do usuário sobre um evento do histórico ("fui eu que desliguei o PC").</summary>
    public class Annotation
    {
        public string Kind = "";     // desligamento
        public DateTime When;        // hora do evento anotado
        public DateTime NotedAt;     // quando o usuário anotou
    }

    /// <summary>
    /// "Fui eu": o usuário diz que um evento do histórico foi ação dele (ex.: segurou o botão de energia). O evento continua no histórico e no relatório,
    /// mas deixa de contar como problema. Só vale para aquela hora: se acontecer de novo sem anotação, o alerta volta inteiro.
    /// Guardado em %LOCALAPPDATA%\DiagIT\anotacoes.json.
    /// </summary>
    public static class Annotations
    {
        static readonly object gate = new object();
        static List<Annotation> items;
        static bool memoryOnly;   // autoteste: não lê nem grava o arquivo do usuário
        const double ToleranceMin = 5;

        static string FilePath { get { return Path.Combine(Paths.Root, "anotacoes.json"); } }

        static List<Annotation> Items
        {
            get
            {
                lock (gate)
                {
                    if (items != null) return items;
                    items = new List<Annotation>();
                    if (memoryOnly) return items;
                    try
                    {
                        if (File.Exists(FilePath))
                        {
                            List<Annotation> l = new JavaScriptSerializer().Deserialize<List<Annotation>>(File.ReadAllText(FilePath, Encoding.UTF8));
                            if (l != null) foreach (Annotation a in l) { a.When = a.When.ToLocalTime(); a.NotedAt = a.NotedAt.ToLocalTime(); items.Add(a); }
                        }
                    }
                    catch (Exception e) { Log.Write("Anotações: " + e.Message); }
                    return items;
                }
            }
        }

        /// <summary>Só para o autoteste: usa uma lista na memória e ignora o arquivo.</summary>
        public static void UseMemory(List<Annotation> list) { lock (gate) { memoryOnly = true; items = list ?? new List<Annotation>(); } }
        public static void UseFile() { lock (gate) { memoryOnly = false; items = null; } }

        /// <summary>O usuário disse que o evento dessa hora (± 5 min) foi ele?</summary>
        public static bool Has(string kind, DateTime when)
        {
            foreach (Annotation a in Items) if (a.Kind == kind && Math.Abs((a.When - when).TotalMinutes) <= ToleranceMin) return true;
            return false;
        }

        public static int CountOf(string kind) { int n = 0; foreach (Annotation a in Items) if (a.Kind == kind) n++; return n; }

        public static void Add(string kind, List<DateTime> times)
        {
            lock (gate)
            {
                foreach (DateTime t in times)
                {
                    if (Has(kind, t)) continue;
                    Annotation a = new Annotation(); a.Kind = kind; a.When = t; a.NotedAt = DateTime.Now;
                    Items.Add(a);
                }
                if (memoryOnly) return;
                try
                {
                    Paths.Ensure();
                    File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(Items), new UTF8Encoding(false));
                    Log.Write("Anotado pelo usuário (" + kind + "): " + times.Count + " evento(s)");
                }
                catch (Exception e) { Log.Write("Anotações: " + e.Message); }
            }
        }
    }
}
