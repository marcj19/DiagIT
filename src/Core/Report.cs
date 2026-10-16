using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Principal;
using System.Text;

namespace DiagIT
{
    public static class SysUtil
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern ulong GetTickCount64();

        /// <summary>Momento em que o Windows foi iniciado pela última vez.</summary>
        public static DateTime BootTime() { return DateTime.Now.AddMilliseconds(-(double)GetTickCount64()); }

        public static bool IsAdmin()
        {
            try
            {
                WindowsPrincipal p = new WindowsPrincipal(WindowsIdentity.GetCurrent());
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static bool RelaunchElevated(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, args);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                return true;
            }
            catch { return false; }   // usuário cancelou o UAC
        }

        const string TaskName = "DiagIT";

        static int Schtasks(string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return p.ExitCode;
            }
        }

        public static bool AutostartEnabled()
        {
            try { return Schtasks("/Query /TN " + TaskName) == 0; } catch { return false; }
        }

        /// <summary>Exige administrador (a tarefa agendada roda com privilégios para ler sensores sem pedir UAC a cada login).</summary>
        public static bool SetAutostart(bool on)
        {
            try
            {
                try { Schtasks("/Delete /TN " + "Diag" + "Jogo" + " /F"); } catch { }   // tarefa com o nome antigo
                if (on)
                {
                    string exe = System.Windows.Forms.Application.ExecutablePath;
                    return Schtasks("/Create /TN " + TaskName + " /TR \"\\\"" + exe + "\\\" --background\" /SC ONLOGON /DELAY 0000:20 /RL HIGHEST /F") == 0;
                }
                return Schtasks("/Delete /TN " + TaskName + " /F") == 0;
            }
            catch (Exception e) { Log.Write("Autostart: " + e.Message); return false; }
        }
    }

    /// <summary>
    /// Relatório imprimível em duas partes: página 1 em linguagem simples (para o dono do PC) e a parte técnica (para quem vai analisar).
    /// Fundo claro para imprimir/salvar em PDF.
    /// </summary>
    public static partial class Report
    {
        static readonly CultureInfo Pt = new CultureInfo("pt-BR");
        static string H(string s) { return WebUtility.HtmlEncode(s ?? ""); }
        static string Num(double v, string f) { return double.IsNaN(v) ? "—" : v.ToString(f, Pt); }
        static string Dt(DateTime t) { return t == DateTime.MinValue ? "—" : t.ToString("dd/MM/yyyy HH:mm", Pt); }

        const string Css =
            ":root{--ink:#141922;--soft:#3d4654;--muted:#5b6573;--line:#dfe3e8;--bg:#ffffff;--panel:#f6f7f9;--brand:#5b45d6;--brandSoft:#eeebfd;" +
            "--ok:#11744f;--okBg:#e3f5ec;--warn:#8f5200;--warnBg:#fdf1dc;--bad:#b42323;--badBg:#fde6e6;--none:#5f6875;--noneBg:#eef0f3}" +
            "*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.5 'Segoe UI',system-ui,sans-serif}" +
            ".wrap{max-width:980px;margin:0 auto;padding:36px 32px 48px}" +
            "header{display:flex;align-items:center;gap:14px;border-bottom:2px solid var(--ink);padding-bottom:14px;margin-bottom:22px}" +
            ".mark{width:40px;height:40px;border-radius:11px;background:var(--brand);display:flex;align-items:center;justify-content:center;flex:none}" +
            "h1{font-size:24px;margin:0;letter-spacing:-.2px}h2{font-size:18px;margin:30px 0 10px}h3{font-size:15px;margin:20px 0 8px}" +
            ".meta{color:var(--muted);font-size:12.5px}.muted{color:var(--muted)}.small{font-size:12.5px}" +
            ".kicker{font-size:11.5px;font-weight:700;letter-spacing:.8px;text-transform:uppercase;color:var(--brand);margin:0 0 4px}" +
            ".verdict{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:18px 20px}" +
            ".verdict .t{font-size:20px;font-weight:700;line-height:1.3}.verdict .s{color:var(--soft);margin-top:6px}" +
            "table{border-collapse:collapse;width:100%;font-size:13px}th{text-align:left;font-size:11.5px;text-transform:uppercase;letter-spacing:.5px;color:var(--muted);" +
            "border-bottom:1.5px solid var(--ink);padding:6px 8px}td{border-bottom:1px solid var(--line);padding:7px 8px;vertical-align:top}" +
            ".pill{display:inline-block;padding:2px 10px;border-radius:999px;font-size:12px;font-weight:700;white-space:nowrap}" +
            ".ok{color:var(--ok);background:var(--okBg)}.warn{color:var(--warn);background:var(--warnBg)}.bad{color:var(--bad);background:var(--badBg)}.none{color:var(--none);background:var(--noneBg)}.act{color:var(--brand);background:var(--brandSoft)}" +
            ".card{border:1px solid var(--line);border-radius:12px;padding:12px 14px;margin:10px 0;break-inside:avoid}.card .h{font-weight:700;margin:6px 0 4px}" +
            ".todo{display:flex;gap:12px;align-items:flex-start;border:1px solid var(--line);border-radius:12px;padding:12px 14px;margin:8px 0;break-inside:avoid}" +
            ".n{width:26px;height:26px;border-radius:50%;background:var(--brandSoft);color:var(--brand);font-weight:700;display:flex;align-items:center;justify-content:center;flex:none}" +
            "pre{white-space:pre-wrap;font:12.5px/1.5 Consolas,'Cascadia Mono',monospace;color:var(--soft);margin:6px 0;background:var(--panel);padding:8px 10px;border-radius:8px}" +
            "code{font:12.5px Consolas,'Cascadia Mono',monospace}" +
            ".break{break-before:page;page-break-before:always}svg{width:100%;border:1px solid var(--line);border-radius:10px;margin:8px 0}" +
            "footer{margin-top:36px;border-top:1px solid var(--line);padding-top:10px;color:var(--muted);font-size:12px}" +
            "@media print{.wrap{padding:0}body{font-size:12.5px}h2{margin-top:22px}a{color:inherit;text-decoration:none}}";

        const string Mark = "<svg viewBox='0 0 24 24' width='24' height='24' fill='none' stroke='#fff' stroke-width='2.4' stroke-linecap='round' stroke-linejoin='round' style='border:0;margin:0;width:24px'><path d='M2 12h4l2.5-6 4 12 2.5-6H22'/></svg>";

        static string Pill(CompStatus s)
        {
            string c = s == CompStatus.Ok ? "ok" : s == CompStatus.Suspect ? "warn" : s == CompStatus.Failed ? "bad" : "none";
            return "<span class='pill " + c + "'>" + H(Analyzer.StatusLabel(s)) + "</span>";
        }

        static string ConfPill(Finding f)
        {
            string c = Analyzer.Confidence(f);
            string cls = c == "COMPROVADO" ? "bad" : c == "INDÍCIO" ? "warn" : c == "OK" ? "ok" : "act";
            return "<span class='pill " + cls + "'>" + H(c) + "</span>";
        }

        static string Version { get { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } }

        public static string Build(Diagnosis d, MachineInfo m, List<SessionData> sessions) { return Build(d, m, sessions, null); }

        public static string Build(Diagnosis d, MachineInfo m, List<SessionData> sessions, ReportExtras x)
        {
            if (x == null) x = new ReportExtras();
            HwSnapshot hw = d.Hw;
            StringBuilder b = new StringBuilder();
            b.Append("<!doctype html><html lang='pt-BR'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
            b.Append("<title>Relatório do PC — DiagIT</title><style>" + Css + "</style></head><body><div class='wrap'>");
            b.Append("<header><div class='mark'>" + Mark + "</div><div><h1>Relatório do PC</h1><div class='meta'>Gerado pelo DiagIT " + Version + " em " +
                DateTime.Now.ToString("dd/MM/yyyy 'às' HH:mm", Pt) + " · " + H(m.Cpu) + " · " + H(m.Gpu) + "</div></div></header>");

            // ================================================================ PARTE 1: para o dono do PC
            string vt, vs;
            Analyzer.PlainVerdict(d, out vt, out vs);
            b.Append("<p class='kicker'>Resumo</p><div class='verdict'><div class='t'>" + H(vt) + "</div><div class='s'>" + H(vs) + "</div>" +
                "<div class='meta' style='margin-top:8px'>Baseado em " + d.Tests.Count + " teste(s), " + d.Sessions + " partida(s) gravada(s) e " + d.Days + " dias de registros do Windows.</div></div>");

            b.Append("<h2>Suas peças</h2><table><tr><th>Peça</th><th>Estado</th><th>O que isso quer dizer</th></tr>");
            int[] order = { (int)Comp.Cpu, (int)Comp.Gpu, (int)Comp.Ram, (int)Comp.Disk, (int)Comp.Psu, (int)Comp.Cooling, (int)Comp.Software, (int)Comp.Board };
            foreach (int i in order)
            {
                CompState s = d.States[i];
                if (s == null) continue;
                b.Append("<tr><td><b>" + H(CompInfo.Plain[i]) + "</b></td><td>" + Pill(s.Status) + "</td><td>" + H(Analyzer.PlainNote(s, i)) + "</td></tr>");
            }
            b.Append("</table>");

            List<Finding> todo = d.Findings.FindAll(delegate(Finding f) { return (f.Sev == Sev.Crit || f.Sev == Sev.Warn) && f.Advice.Length > 0; });
            if (todo.Count > 0)
            {
                b.Append("<h2>O que fazer</h2>");
                int n = 0;
                foreach (Finding f in todo)
                {
                    if (n >= 5) break;
                    Guide g = f.Guide != null ? Rules.GuideById(f.Guide) : null;
                    b.Append("<div class='todo'><div class='n'>" + (++n) + "</div><div><div><b>" + H(f.Plain) + "</b> " + ConfPill(f) + "</div><div>" + H(f.Advice) + "</div>" +
                        (g != null ? "<div class='muted small'>No DiagIT: Resultados → \"Resolver passo a passo\" (" + H(g.Title) + ").</div>" : "") + "</div></div>");
                }
            }

            if (d.Fixes.Count > 0)
            {
                b.Append("<h2>Suas correções</h2><table><tr><th>Correção</th><th>Feita em</th><th>Resultado</th></tr>");
                foreach (FixStatus st in d.Fixes)
                {
                    string cls = st.State == 1 ? "ok" : st.State == 2 ? "warn" : "none";
                    b.Append("<tr><td>" + H(st.Fix.Title) + "</td><td>" + st.Fix.When.ToString("dd/MM/yyyy", Pt) + "</td><td><span class='pill " + cls + "'>" + H(st.Headline) +
                        "</span><div class='small'>" + H(st.Text) + "</div></td></tr>");
                }
                b.Append("</table>");
            }

            // ================================================================ PARTE 2: para o técnico
            b.Append("<div class='break'></div><p class='kicker' style='margin-top:26px'>Detalhes para o técnico</p>");
            b.Append("<h2 style='margin-top:4px'>Sistema</h2><table>");
            Action<string, string> row = delegate(string k, string v) { if (!string.IsNullOrEmpty(v)) b.Append("<tr><td class='muted' style='width:200px'>" + H(k) + "</td><td>" + H(v) + "</td></tr>"); };
            row("Windows", x.Windows.Length > 0 ? x.Windows : m.Os);
            row("Processador", m.Cpu + (m.Cores > 0 ? " (" + m.Cores + " núcleos / " + m.Threads + " threads)" : ""));
            row("Placa de vídeo", m.Gpu + " — driver " + m.GpuDriver + (m.VramGB > 0 ? " — " + m.VramGB.ToString("0.#", Pt) + " GB" : ""));
            row("Placa-mãe", m.Board);
            if (hw != null)
                foreach (Fact f in hw.Facts)
                    if ((f.Group == "Placa-mãe" && f.Name == "BIOS") || f.Group == "Memória RAM" || (f.Group == "Placa de vídeo" && f.Name == "Link PCIe") || f.Group == "Volumes" || f.Group == "Windows e inicialização")
                        row(f.Group + " · " + f.Name, f.Value + (f.Note.Length > 0 ? " — " + f.Note : ""));
            if (hw != null)
                foreach (DriveHealth dr in hw.Drives)
                {
                    List<string> p = dr.Problems();
                    row("Disco " + dr.Number, dr.Model + " (" + dr.Bus + ", " + (dr.SizeBytes / 1e9).ToString("0", Pt) + " GB)" +
                        (!dr.Read ? " — saúde não lida" : p.Count == 0 ? " — saúde OK" + (double.IsNaN(dr.UsedPct) ? "" : ", desgaste " + dr.UsedPct.ToString("0", Pt) + "%") : " — " + string.Join("; ", p.ToArray())));
                }
            row("Administrador", SysUtil.IsAdmin() ? "sim (leitura completa)" : "não (temperatura da CPU, SMART, telas azuis e FPS podem faltar)");
            row("Regras de diagnóstico", "data/*.json (" + Rules.Source + ")");
            b.Append("</table>");

            List<TimelineItem> tl = Analyzer.Timeline(x.Events, hw, sessions, d.Tests, x.Fixes, d.Days);
            if (tl.Count > 0)
            {
                b.Append("<h2>Linha do tempo (" + d.Days + " dias)</h2><table><tr><th style='width:140px'>Quando</th><th style='width:110px'>Tipo</th><th>O que aconteceu</th></tr>");
                foreach (TimelineItem it in tl)
                {
                    string cls = it.Level >= 2 ? "bad" : it.Level == 1 ? "warn" : it.Level < 0 ? "act" : "none";
                    b.Append("<tr><td>" + it.Time.ToString("dd/MM/yyyy HH:mm", Pt) + "</td><td><span class='pill " + cls + "'>" + H(it.Kind) + "</span></td><td>" + H(it.Text) + "</td></tr>");
                }
                b.Append("</table>");
            }

            if (hw != null && hw.Dumps.Exists(delegate(CrashDump c) { return !c.Live; }))
            {
                b.Append("<h2>Telas azuis: arquivos de despejo</h2><table><tr><th>Quando</th><th>Código</th><th>Parâmetros</th><th>Driver culpado</th><th>Pilha na hora da falha</th></tr>");
                foreach (CrashDump c in hw.Dumps)
                {
                    if (c.Live) continue;
                    DriverRule dr = Rules.Driver(c.Culprit);
                    b.Append("<tr><td>" + Dt(c.Time) + "<div class='muted small'>" + H(Path.GetFileName(c.File)) + "</div></td><td><code>" + H(c.CodeHex) + "</code><div class='small'>" +
                        H(Rules.Bug((int)(c.Code & 0xFFFF)).Name) + "</div></td><td><code class='small'>0x" + c.P[0].ToString("X") + "<br>0x" + c.P[1].ToString("X") + "<br>0x" + c.P[2].ToString("X") + "<br>0x" + c.P[3].ToString("X") + "</code></td><td>" +
                        (c.Culprit != null ? "<code>" + H(c.Culprit) + "</code><div class='small'>" + H(dr != null ? dr.Name : "driver desconhecido") + "</div><div class='muted small'>por " + H(c.CulpritHow) + "</div>" : "<span class='muted'>não identificado</span>") +
                        "</td><td class='small'>" + H(string.Join(" → ", c.StackModules.ToArray())) + "</td></tr>");
                }
                b.Append("</table>");
            }
            else if (hw != null && !SysUtil.IsAdmin())
                b.Append("<p class='muted'>Arquivos de tela azul não lidos (o DiagIT não estava como administrador). Os drivers apontados pelo Windows aparecem na linha do tempo.</p>");

            b.Append("<h2>Componentes (análise completa)</h2><table><tr><th>Componente</th><th>Estado</th><th>Pontuação</th><th>Motivo</th></tr>");
            for (int i = 0; i < 8; i++)
            {
                CompState s = d.States[i];
                if (s == null) continue;
                b.Append("<tr><td>" + H(CompInfo.Names[i]) + "</td><td>" + Pill(s.Status) + "</td><td>" + d.Score[i].ToString("0", Pt) + "%</td><td>" + H(s.Headline) +
                    "<div class='muted small'>" + H(s.Detail) + "</div></td></tr>");
            }
            b.Append("</table>");

            b.Append("<h2>Achados e evidências</h2>");
            foreach (Finding f in d.Findings)
            {
                b.Append("<div class='card'>" + ConfPill(f) + " <span class='muted small'>" + H(f.Source) + "</span>");
                b.Append("<div class='h'>" + H(f.Title) + "</div>");
                if (f.Simple.Length > 0) b.Append("<div class='small'>Em linguagem simples: " + H(f.Simple) + "</div>");
                if (f.Evidence.Length > 0) b.Append("<pre>" + H(f.Evidence) + "</pre>");
                if (f.Advice.Length > 0) b.Append("<div><b>O que fazer:</b> " + H(f.Advice) + "</div>");
                if (f.W.Count > 0)
                {
                    List<string> w = new List<string>();
                    foreach (KeyValuePair<Comp, double> kv in f.W) w.Add(CompInfo.Plain[(int)kv.Key] + " " + (kv.Value * 100).ToString("0", Pt) + "%");
                    b.Append("<div class='muted small'>Peso contra: " + H(string.Join(", ", w.ToArray())) + "</div>");
                }
                b.Append("</div>");
            }

            if (d.Tests.Count > 0)
            {
                b.Append("<h2>Testes de hardware</h2><table><tr><th>Quando</th><th>Teste</th><th>Resultado</th><th>Medições</th></tr>");
                int n = 0;
                foreach (TestResult t in d.Tests)
                {
                    if (n++ >= 30) break;
                    string cls = t.Status == "passed" ? "ok" : t.Status == "failed" || t.Status == "crashed" ? "bad" : "none";
                    b.Append("<tr><td>" + Dt(t.Start) + "<div class='muted small'>" + (t.PlannedSec / 60) + " min</div></td><td>" + H(TestResult.KindLabel(t.Kind)) + "</td><td><span class='pill " + cls + "'>" +
                        H(t.StatusLabel) + "</span><div class='small'>" + H(t.Summary) + "</div></td><td class='small'>CPU máx " + Num(t.Get("t_cpu_temp_max"), "0") + " °C · GPU máx " +
                        Num(t.Get("t_gpu_temp_max"), "0") + " °C (hotspot " + Num(t.Get("t_gpu_hot_max"), "0") + ")<br>CPU " + Num(t.Get("t_cpu_power_max"), "0") + " W · GPU " +
                        Num(t.Get("t_gpu_power_max"), "0") + " W · +12 V mín " + Num(t.Get("t_v12_min"), "0.00") + " V<br>Erros: " + t.Errors + "</td></tr>");
                }
                b.Append("</table>");
            }

            if (x.Drivers.Count > 0)
            {
                b.Append("<h2>Drivers instalados (principais)</h2><table><tr><th>Tipo</th><th>Dispositivo</th><th>Fornecedor</th><th>Versão</th><th>Data</th></tr>");
                foreach (DriverRow r in x.Drivers)
                    b.Append("<tr><td class='muted small'>" + H(r.Class) + "</td><td>" + H(r.Device) + "</td><td>" + H(r.Provider) + "</td><td><code>" + H(r.Version) + "</code></td><td>" +
                        (r.Date == DateTime.MinValue ? "—" : r.Date.ToString("dd/MM/yyyy", Pt)) + "</td></tr>");
                b.Append("</table>");
            }

            if (sessions.Count > 0)
            {
                b.Append("<h2>Partidas gravadas</h2><table><tr><th>Início</th><th>Jogo</th><th>Duração</th><th>Estado</th><th>FPS / 1% low</th><th>Engasgos</th><th>CPU / GPU máx</th><th>Temp. CPU / GPU máx</th></tr>");
                foreach (SessionData sd in sessions)
                {
                    double[] cpu = Stat.Vals(sd.S, F.Cpu), gpu = Stat.Vals(sd.S, F.Gpu), ct = Stat.Vals(sd.S, F.CpuTemp), gt = Stat.Vals(sd.S, F.GpuTemp);
                    b.Append("<tr><td>" + sd.Info.Start.ToString("dd/MM HH:mm", Pt) + "</td><td>" + H(sd.Info.Game) + "</td><td>" + Fmt(sd.Info.Duration) + "</td><td>" + H(sd.Info.StatusLabel) +
                        (sd.Info.Note.Length > 0 && sd.Info.Note != "teste" ? "<div class='muted small'>" + H(sd.Info.Note) + "</div>" : "") + "</td><td>" +
                        (sd.Info.Frames > 0 ? Num(sd.Info.FpsAvg, "0") + " / " + Num(sd.Info.FpsLow, "0") : "—") + "</td><td>" + (sd.Info.Frames > 0 ? sd.Info.Stutters.ToString() : "—") + "</td><td>" +
                        Num(Stat.Max(cpu), "0") + " / " + Num(Stat.Max(gpu), "0") + " %</td><td>" + Num(Stat.Max(ct), "0") + " / " + Num(Stat.Max(gt), "0") + " °C</td></tr>");
                }
                b.Append("</table>");

                SessionData big = sessions[0];
                foreach (SessionData sd in sessions) if (sd.S.Count > big.S.Count) big = sd;
                if (big.S.Count > 5)
                {
                    b.Append("<h3>Gráficos da partida mais longa — " + H(big.Info.Game) + " (" + big.Info.Start.ToString("dd/MM HH:mm", Pt) + ")</h3>");
                    b.Append(Svg("Uso (%)", big.S, new int[] { F.Cpu, F.Gpu, F.Ram }, new string[] { "CPU", "GPU", "RAM" }, new string[] { "#2f6fdb", "#11744f", "#c77700" }, 100));
                    if (Stat.Vals(big.S, F.CpuTemp).Length > 0 || Stat.Vals(big.S, F.GpuTemp).Length > 0)
                        b.Append(Svg("Temperaturas (°C)", big.S, new int[] { F.CpuTemp, F.GpuTemp, F.GpuHot }, new string[] { "CPU", "GPU", "GPU hotspot" }, new string[] { "#2f6fdb", "#11744f", "#b42323" }, double.NaN));
                    if (Stat.Vals(big.S, F.Fps).Length > 0)
                        b.Append(Svg("FPS", big.S, new int[] { F.Fps, F.FpsLow }, new string[] { "FPS", "1% low" }, new string[] { "#5b45d6", "#c77700" }, double.NaN));
                }
            }

            if (hw != null)
            {
                b.Append("<h2>Tudo que foi lido do hardware</h2><table>");
                string grp = null;
                foreach (Fact f in hw.Facts)
                {
                    if (f.Group != grp) { grp = f.Group; b.Append("<tr><th colspan='2'>" + H(grp) + "</th></tr>"); }
                    string cls = f.Level == 2 ? "bad" : f.Level == 1 ? "warn" : "";
                    b.Append("<tr><td class='muted' style='width:260px'>" + H(f.Name) + "</td><td>" + (cls.Length > 0 ? "<span class='pill " + cls + "'>" + H(f.Value) + "</span>" : H(f.Value)) +
                        (f.Note.Length > 0 ? "<div class='muted small'>" + H(f.Note) + "</div>" : "") + "</td></tr>");
                }
                b.Append("</table>");
            }

            b.Append("<footer>Gerado pelo DiagIT " + Version + " (código aberto, GPL-3.0). As conclusões se baseiam em testes com resultado verificável e em registros do próprio Windows; " +
                "\"Indício\" significa que os dados apontam para a causa, mas ela só se confirma com o teste ou a correção indicados.</footer>");
            b.Append("</div></body></html>");
            return b.ToString();
        }

        static string Fmt(TimeSpan t)
        {
            return ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }

        static string Svg(string title, List<Sample> data, int[] fields, string[] names, string[] colors, double fixedMax)
        {
            const int W = 960, Hh = 220, L = 44, R = 10, T = 28, B = 22;
            int n = data.Count;
            double mx = double.IsNaN(fixedMax) ? 0 : fixedMax;
            if (double.IsNaN(fixedMax))
                foreach (Sample s in data) foreach (int f in fields) if (!double.IsNaN(s.V[f]) && s.V[f] > mx) mx = s.V[f];
            if (mx <= 0) mx = 10;
            mx = Math.Ceiling(mx / 10) * 10;
            double t1 = (data[n - 1].Time - data[0].Time).TotalSeconds;
            if (t1 <= 0) t1 = 1;
            StringBuilder b = new StringBuilder();
            b.Append("<svg viewBox='0 0 " + W + " " + Hh + "' xmlns='http://www.w3.org/2000/svg' role='img' aria-label='" + H(title) + "'>");
            b.Append("<text x='" + L + "' y='18' fill='#141922' font-size='13' font-weight='600'>" + H(title) + "</text>");
            for (int g = 0; g <= 4; g++)
            {
                double y = T + (Hh - T - B) * (1 - g / 4.0);
                b.Append("<line x1='" + L + "' x2='" + (W - R) + "' y1='" + Inv(y) + "' y2='" + Inv(y) + "' stroke='#e3e6ea'/>");
                b.Append("<text x='" + (L - 6) + "' y='" + Inv(y + 4) + "' fill='#5b6573' font-size='10' text-anchor='end'>" + Inv(mx * g / 4) + "</text>");
            }
            int lx = 260;
            for (int k = 0; k < fields.Length; k++)
            {
                StringBuilder pts = new StringBuilder();
                int stride = Math.Max(1, n / 500);
                for (int i = 0; i < n; i += stride)
                {
                    double v = data[i].V[fields[k]];
                    if (double.IsNaN(v)) continue;
                    double xx = L + (data[i].Time - data[0].Time).TotalSeconds / t1 * (W - L - R);
                    double y = T + (Hh - T - B) * (1 - Math.Min(1, v / mx));
                    pts.Append(Inv(xx) + "," + Inv(y) + " ");
                }
                if (pts.Length > 0)
                    b.Append("<polyline fill='none' stroke='" + colors[k] + "' stroke-width='1.6' points='" + pts + "'/>");
                b.Append("<circle cx='" + lx + "' cy='14' r='4' fill='" + colors[k] + "'/><text x='" + (lx + 8) + "' y='18' fill='#3d4654' font-size='11'>" + H(names[k]) + "</text>");
                lx += 110;
            }
            b.Append("<text x='" + L + "' y='" + (Hh - 6) + "' fill='#5b6573' font-size='10'>" + data[0].Time.ToString("HH:mm:ss") + "</text>");
            b.Append("<text x='" + (W - R) + "' y='" + (Hh - 6) + "' fill='#5b6573' font-size='10' text-anchor='end'>" + data[n - 1].Time.ToString("HH:mm:ss") + "</text>");
            b.Append("</svg>");
            return b.ToString();
        }

        static string Inv(double v) { return v.ToString("0.#", CultureInfo.InvariantCulture); }

        public static string Save(string html)
        {
            Paths.Ensure();
            string path = Path.Combine(Paths.Reports, "relatorio_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");
            File.WriteAllText(path, html, new UTF8Encoding(true));
            return path;
        }
    }
}
