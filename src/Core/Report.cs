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

    public static class Report
    {
        static readonly CultureInfo Pt = new CultureInfo("pt-BR");
        static string H(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        static string Num(double v, string f) { return double.IsNaN(v) ? "—" : v.ToString(f, Pt); }

        public static string Build(Diagnosis d, MachineInfo m, List<SessionData> sessions)
        {
            StringBuilder b = new StringBuilder();
            b.Append("<!doctype html><html lang='pt-BR'><head><meta charset='utf-8'><title>Relatório DiagIT</title><style>");
            b.Append("body{font-family:Segoe UI,Arial,sans-serif;background:#0d1117;color:#e6eaf0;margin:0;padding:32px;max-width:1000px;margin:auto}");
            b.Append("h1{font-size:26px;margin:0 0 4px}h2{font-size:17px;margin:32px 0 10px;border-bottom:1px solid #2a313c;padding-bottom:6px}");
            b.Append(".muted{color:#8b94a0}.card{background:#161b22;border:1px solid #2a313c;border-radius:8px;padding:14px 16px;margin:10px 0}");
            b.Append(".bar{display:flex;align-items:center;margin:6px 0}.bar .n{width:260px}.bar .t{flex:1;background:#1e242d;border-radius:6px;height:10px;overflow:hidden}");
            b.Append(".bar .f{height:10px}.bar .p{width:56px;text-align:right;font-weight:600}");
            b.Append(".tag{display:inline-block;font-size:11px;font-weight:700;letter-spacing:.5px;padding:1px 8px;border-radius:10px;margin-right:6px}");
            b.Append(".Crit{border-left:5px solid #ff5c5c}.Warn{border-left:5px solid #ffb020}.Info{border-left:5px solid #4da3ff}.Ok{border-left:5px solid #3dd68c}");
            b.Append(".tCrit{background:#ff5c5c;color:#000}.tWarn{background:#ffb020;color:#000}.tInfo{background:#4da3ff;color:#000}.tOk{background:#3dd68c;color:#000}");
            b.Append("pre{white-space:pre-wrap;font-family:inherit;margin:6px 0;color:#8b94a0}table{border-collapse:collapse;width:100%;font-size:13px}");
            b.Append("td,th{border-bottom:1px solid #2a313c;padding:6px 8px;text-align:left}svg{background:#161b22;border:1px solid #2a313c;border-radius:8px;width:100%}");
            b.Append("</style></head><body>");

            b.Append("<h1>Relatório de diagnóstico — DiagIT</h1><div class='muted'>Gerado em " + DateTime.Now.ToString("dd/MM/yyyy HH:mm", Pt) + "</div>");

            b.Append("<h2>Máquina</h2><div class='card'>");
            b.Append("CPU: " + H(m.Cpu) + " (" + m.Cores + " núcleos / " + m.Threads + " threads)<br>");
            b.Append("GPU: " + H(m.Gpu) + " — driver " + H(m.GpuDriver) + (m.VramGB > 0 ? " — VRAM " + m.VramGB.ToString("0.#", Pt) + " GB" : "") + "<br>");
            b.Append("Placa-mãe: " + H(m.Board) + "<br>RAM: " + m.RamGB.ToString("0.0", Pt) + " GB<br>Sistema: " + H(m.Os) + "</div>");

            b.Append("<h2>Conclusão</h2><div class='card'><b style='white-space:pre-wrap'>" + H(d.Verdict) + "</b><div class='muted' style='margin-top:6px'>Base: " +
                d.Tests.Count + " teste(s) de hardware, " + d.Sessions + " sessão(ões) gravada(s) (" + d.HoursRecorded.ToString("0.0", Pt) + " h) e eventos do Windows dos últimos " + d.Days + " dias.</div></div>");

            b.Append("<h2>Estado de cada componente</h2><table><tr><th>Componente</th><th>Estado</th><th>Motivo</th></tr>");
            for (int i = 0; i < 8; i++)
            {
                CompState s = d.States[i];
                if (s == null) continue;
                string col = s.Status == CompStatus.Failed ? "#ff5c5c" : s.Status == CompStatus.Suspect ? "#ff7d45" : s.Status == CompStatus.Ok ? "#3dd68c" : "#8b94a0";
                string lbl = s.Status == CompStatus.Failed ? "PROBLEMA" : s.Status == CompStatus.Suspect ? "SUSPEITO" : s.Status == CompStatus.Ok ? "OK" : "NÃO TESTADO";
                b.Append("<tr><td>" + H(CompInfo.Names[i]) + "</td><td style='color:" + col + ";font-weight:700'>" + lbl + "</td><td>" + H(s.Headline) +
                    "<div class='muted'>" + H(s.Detail) + "</div></td></tr>");
            }
            b.Append("</table>");

            if (d.Tests.Count > 0)
            {
                b.Append("<h2>Testes de hardware executados</h2><table><tr><th>Quando</th><th>Teste</th><th>Resultado</th><th>Resumo</th></tr>");
                int n = 0;
                foreach (TestResult t in d.Tests)
                {
                    if (n++ >= 20) break;
                    string col = t.Status == "passed" ? "#3dd68c" : (t.Status == "failed" || t.Status == "crashed") ? "#ff5c5c" : "#8b94a0";
                    b.Append("<tr><td>" + t.Start.ToString("dd/MM HH:mm", Pt) + "</td><td>" + H(TestResult.KindLabel(t.Kind)) + "</td><td style='color:" + col + ";font-weight:700'>" +
                        H(t.StatusLabel) + "</td><td>" + H(t.Summary) + "</td></tr>");
                }
                b.Append("</table>");
            }

            if (d.Hw != null)
            {
                b.Append("<h2>Hardware</h2><table>");
                string grp = null;
                foreach (Fact f in d.Hw.Facts)
                {
                    if (f.Group != grp) { grp = f.Group; b.Append("<tr><th colspan='2'>" + H(grp) + "</th></tr>"); }
                    string col = f.Level == 2 ? "#ff5c5c" : f.Level == 1 ? "#ffb020" : "#e6eaf0";
                    b.Append("<tr><td class='muted'>" + H(f.Name) + "</td><td style='color:" + col + "'>" + H(f.Value) + (f.Note.Length > 0 ? "<div class='muted'>" + H(f.Note) + "</div>" : "") + "</td></tr>");
                }
                b.Append("</table>");
            }

            b.Append("<h2>Evidências e o que fazer</h2>");
            foreach (Finding f in d.Findings)
            {
                string tcls = f.Confirmed ? "Crit" : f.Sev == Sev.Ok ? "Ok" : f.Sev == Sev.Info ? "Info" : f.Label == "PISTA" ? "Info" : "Warn";
                b.Append("<div class='card " + tcls + "'><span class='tag t" + tcls + "'>" + H(f.Label) + "</span><span class='muted'>" + H(f.Source) + "</span>");
                b.Append("<div style='font-weight:600;margin-top:6px'>" + H(f.Title) + "</div>");
                if (f.Evidence.Length > 0) b.Append("<pre>" + H(f.Evidence) + "</pre>");
                if (f.Advice.Length > 0) b.Append("<div><b style='color:#4da3ff'>O que fazer:</b> " + H(f.Advice) + "</div>");
                b.Append("</div>");
            }

            if (sessions.Count > 0)
            {
                b.Append("<h2>Sessões analisadas</h2><table><tr><th>Início</th><th>Jogo</th><th>Duração</th><th>Estado</th><th>CPU méd/máx</th><th>GPU méd/máx</th><th>Temp CPU máx</th><th>Temp GPU máx</th><th>RAM máx</th></tr>");
                foreach (SessionData sd in sessions)
                {
                    double[] cpu = Stat.Vals(sd.S, F.Cpu), gpu = Stat.Vals(sd.S, F.Gpu), ct = Stat.Vals(sd.S, F.CpuTemp), gt = Stat.Vals(sd.S, F.GpuTemp), ram = Stat.Vals(sd.S, F.Ram);
                    b.Append("<tr><td>" + sd.Info.Start.ToString("dd/MM HH:mm", Pt) + "</td><td>" + H(sd.Info.Game) + "</td><td>" + Fmt(sd.Info.Duration) + "</td><td>" + H(sd.Info.StatusLabel) +
                        "</td><td>" + Num(Stat.Avg(cpu), "0") + " / " + Num(Stat.Max(cpu), "0") + " %</td><td>" + Num(Stat.Avg(gpu), "0") + " / " + Num(Stat.Max(gpu), "0") + " %</td><td>" +
                        Num(Stat.Max(ct), "0") + " °C</td><td>" + Num(Stat.Max(gt), "0") + " °C</td><td>" + Num(Stat.Max(ram), "0") + " %</td></tr>");
                }
                b.Append("</table>");

                // gráficos da sessão mais longa
                SessionData big = sessions[0];
                foreach (SessionData sd in sessions) if (sd.S.Count > big.S.Count) big = sd;
                if (big.S.Count > 5)
                {
                    b.Append("<h2>Gráficos — " + H(big.Info.Game) + " (" + big.Info.Start.ToString("dd/MM HH:mm", Pt) + ")</h2>");
                    b.Append(Svg("Uso (%)", big.S, new int[] { F.Cpu, F.Gpu, F.Ram }, new string[] { "CPU", "GPU", "RAM" }, new string[] { "#4da3ff", "#3dd68c", "#ffb020" }, 100));
                    if (Stat.Vals(big.S, F.CpuTemp).Length > 0 || Stat.Vals(big.S, F.GpuTemp).Length > 0)
                        b.Append(Svg("Temperaturas (°C)", big.S, new int[] { F.CpuTemp, F.GpuTemp, F.GpuHot }, new string[] { "CPU", "GPU", "GPU hotspot" }, new string[] { "#4da3ff", "#3dd68c", "#ff5c5c" }, double.NaN));
                }
            }

            b.Append("<p class='muted' style='margin-top:30px'>Este relatório é uma estimativa baseada em evidências coletadas pelo DiagIT; não substitui testes dedicados (MemTest86, teste de fonte, etc.).</p>");
            b.Append("</body></html>");
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
            b.Append("<svg viewBox='0 0 " + W + " " + Hh + "' xmlns='http://www.w3.org/2000/svg'>");
            b.Append("<text x='" + L + "' y='18' fill='#e6eaf0' font-size='13' font-weight='600'>" + H(title) + "</text>");
            for (int g = 0; g <= 4; g++)
            {
                double y = T + (Hh - T - B) * (1 - g / 4.0);
                b.Append("<line x1='" + L + "' x2='" + (W - R) + "' y1='" + Inv(y) + "' y2='" + Inv(y) + "' stroke='#2a313c'/>");
                b.Append("<text x='" + (L - 6) + "' y='" + Inv(y + 4) + "' fill='#8b94a0' font-size='10' text-anchor='end'>" + Inv(mx * g / 4) + "</text>");
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
                    double x = L + (data[i].Time - data[0].Time).TotalSeconds / t1 * (W - L - R);
                    double y = T + (Hh - T - B) * (1 - Math.Min(1, v / mx));
                    pts.Append(Inv(x) + "," + Inv(y) + " ");
                }
                if (pts.Length > 0)
                    b.Append("<polyline fill='none' stroke='" + colors[k] + "' stroke-width='1.6' points='" + pts + "'/>");
                b.Append("<circle cx='" + lx + "' cy='14' r='4' fill='" + colors[k] + "'/><text x='" + (lx + 8) + "' y='18' fill='#8b94a0' font-size='11'>" + H(names[k]) + "</text>");
                lx += 110;
            }
            b.Append("<text x='" + L + "' y='" + (Hh - 6) + "' fill='#8b94a0' font-size='10'>" + data[0].Time.ToString("HH:mm:ss") + "</text>");
            b.Append("<text x='" + (W - R) + "' y='" + (Hh - 6) + "' fill='#8b94a0' font-size='10' text-anchor='end'>" + data[n - 1].Time.ToString("HH:mm:ss") + "</text>");
            b.Append("</svg><br><br>");
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
