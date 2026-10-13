using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DiagIT
{
    /// <summary>
    /// "Pacote para o técnico": um zip com o relatório e os dados brutos dos últimos 30 dias, para quem vai analisar o PC.
    /// Os arquivos de tela azul ficam de fora (contêm pedaços da memória do PC); vai só o que o DiagIT extraiu deles.
    /// </summary>
    public static class TechPackage
    {
        public static readonly string[] Contents =
        {
            "relatorio.html — o relatório completo (abre no navegador, dá para imprimir)",
            "linha-do-tempo.csv e eventos.csv — o que o Windows registrou nos últimos 30 dias",
            "telas-azuis.txt — código, driver culpado e pilha de cada tela azul (os arquivos .dmp não vão)",
            "drivers.csv e sistema.txt — drivers instalados e tudo que foi lido do hardware",
            "partidas e testes — as medições gravadas (inclui o nome dos jogos)",
            "correcoes.json — as correções que você marcou e o acompanhamento"
        };

        static string Csv(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            return s.IndexOfAny(new char[] { ';', '"' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        static void Text(ZipArchive z, string name, string content)
        {
            ZipArchiveEntry e = z.CreateEntry(name, CompressionLevel.Optimal);
            using (StreamWriter w = new StreamWriter(e.Open(), new UTF8Encoding(true))) w.Write(content);
        }

        static void FileIn(ZipArchive z, string path, string name)
        {
            try { if (File.Exists(path)) z.CreateEntryFromFile(path, name, CompressionLevel.Optimal); }
            catch (Exception e) { Log.Write("Pacote: " + Path.GetFileName(path) + ": " + e.Message); }
        }

        public static void Build(string zipPath, string reportHtml, Diagnosis d, ReportExtras x, List<SessionData> sessions)
        {
            CultureInfo pt = new CultureInfo("pt-BR");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (ZipArchive z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                Text(z, "relatorio.html", reportHtml);

                StringBuilder rd = new StringBuilder();
                rd.AppendLine("PACOTE PARA O TÉCNICO — DiagIT " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
                rd.AppendLine("Gerado em " + DateTime.Now.ToString("dd/MM/yyyy HH:mm", pt) + " · " + x.Windows);
                rd.AppendLine();
                foreach (string c in Contents) rd.AppendLine("- " + c);
                rd.AppendLine();
                rd.AppendLine("CSV separados por ponto e vírgula, em UTF-8. Partidas: uma linha por leitura (t = horário Unix), colunas descritas em Fields.cs do código.");
                rd.AppendLine("Código e documentação: https://github.com/marcj19/DiagIT");
                Text(z, "LEIA-ME.txt", rd.ToString());

                StringBuilder tl = new StringBuilder("quando;tipo;nivel;o_que_aconteceu\r\n");
                foreach (TimelineItem it in Analyzer.Timeline(x.Events, d.Hw, sessions, d.Tests, x.Fixes, d.Days))
                    tl.Append(it.Time.ToString("yyyy-MM-dd HH:mm:ss") + ";" + Csv(it.Kind) + ";" + it.Level + ";" + Csv(it.Text) + "\r\n");
                Text(z, "linha-do-tempo.csv", tl.ToString());

                StringBuilder ev = new StringBuilder("quando;log;provedor;id;nivel;propriedades;mensagem\r\n");
                foreach (WinEvent e in x.Events)
                    ev.Append(e.Time.ToString("yyyy-MM-dd HH:mm:ss") + ";" + Csv(e.Log) + ";" + Csv(e.Provider) + ";" + e.Id + ";" + e.Level + ";" +
                        Csv(string.Join(" | ", e.P)) + ";" + Csv(e.Msg.Length > 300 ? e.Msg.Substring(0, 300) : e.Msg) + "\r\n");
                Text(z, "eventos.csv", ev.ToString());

                StringBuilder bs = new StringBuilder();
                if (d.Hw == null || d.Hw.Dumps.Count == 0) bs.AppendLine(SysUtil.IsAdmin() ? "Nenhum arquivo de tela azul encontrado." : "Arquivos de tela azul não lidos (o DiagIT não estava como administrador).");
                else
                    foreach (CrashDump c in d.Hw.Dumps)
                    {
                        bs.AppendLine(Path.GetFileName(c.File) + (c.Live ? " (LiveKernelReports, sem tela azul)" : "") + " — " + c.Time.ToString("dd/MM/yyyy HH:mm", pt));
                        bs.AppendLine("  código " + c.CodeHex + " " + Rules.Bug((int)(c.Code & 0xFFFF)).Name + "  parâmetros 0x" + c.P[0].ToString("X") + ", 0x" + c.P[1].ToString("X") + ", 0x" + c.P[2].ToString("X") + ", 0x" + c.P[3].ToString("X"));
                        bs.AppendLine("  drivers lidos: " + c.ModulesRead + "  culpado: " + (c.Culprit ?? "não identificado") + (c.Culprit != null ? " (por " + c.CulpritHow + ")" : ""));
                        if (c.StackModules.Count > 0) bs.AppendLine("  pilha: " + string.Join(" -> ", c.StackModules.ToArray()));
                        bs.AppendLine();
                    }
                Text(z, "telas-azuis.txt", bs.ToString());

                StringBuilder dr = new StringBuilder("tipo;dispositivo;fornecedor;versao;data\r\n");
                foreach (DriverRow r in x.Drivers)
                    dr.Append(Csv(r.Class) + ";" + Csv(r.Device) + ";" + Csv(r.Provider) + ";" + Csv(r.Version) + ";" + (r.Date == DateTime.MinValue ? "" : r.Date.ToString("yyyy-MM-dd")) + "\r\n");
                Text(z, "drivers.csv", dr.ToString());

                StringBuilder sy = new StringBuilder();
                if (d.Hw != null)
                {
                    string grp = null;
                    foreach (Fact f in d.Hw.Facts)
                    {
                        if (f.Group != grp) { grp = f.Group; sy.AppendLine(); sy.AppendLine("[" + grp + "]"); }
                        sy.AppendLine(f.Name + ": " + f.Value + (f.Note.Length > 0 ? "  (" + f.Note + ")" : ""));
                    }
                }
                Text(z, "sistema.txt", "Windows: " + x.Windows + "\r\n" + sy.ToString());

                foreach (SessionData sd in sessions)
                {
                    FileIn(z, sd.Info.MetaPath, "partidas/" + Path.GetFileName(sd.Info.MetaPath));
                    FileIn(z, sd.Info.CsvPath, "partidas/" + Path.GetFileName(sd.Info.CsvPath));
                }
                foreach (TestResult t in d.Tests)
                    if (t.Start > DateTime.Now.AddDays(-d.Days)) FileIn(z, t.FilePath, "testes/" + Path.GetFileName(t.FilePath));
                FileIn(z, Path.Combine(Paths.Root, "correcoes.json"), "correcoes.json");
            }
        }
    }
}
