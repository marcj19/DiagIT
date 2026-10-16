using System;
using System.Collections.Generic;
using System.Text;

namespace DiagIT
{
    /// <summary>
    /// Atestado de saúde do PC: uma página para mostrar a quem vai comprar, vender ou receber o computador. Só traz o que foi MEDIDO neste PC,
    /// com data e prova de cada peça, e diz com todas as letras o que não foi verificado. Não é laudo técnico.
    /// </summary>
    public static partial class Report
    {
        const string CertCss =
            "@page{size:A4;margin:12mm}.wrap{max-width:820px;padding:26px 28px 30px}h2{font-size:15px;margin:18px 0 6px}" +
            ".big{display:flex;gap:16px;align-items:center;background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:14px 18px}" +
            ".big .seal{width:54px;height:54px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:28px;font-weight:700;flex:none}" +
            ".sealok{background:var(--okBg);color:var(--ok)}.sealwarn{background:var(--warnBg);color:var(--warn)}.sealbad{background:var(--badBg);color:var(--bad)}" +
            "td,th{padding:4px 8px}table{font-size:12.5px}.nums td{text-align:center;border:1px solid var(--line);padding:8px 4px}.nums .v{font-size:20px;font-weight:700;display:block}" +
            "ul{margin:6px 0 0 18px;padding:0}li{margin:2px 0}@media print{.wrap{padding:0;max-width:none}}";

        static string Mts(int v, int type) { return (type == 26 || type == 34) && v > 0 && v < 1300 ? (v * 2) + " MT/s" : v > 0 ? v + " MT/s" : ""; }

        public static string BuildCertificate(Diagnosis d, MachineInfo m, ReportExtras x)
        {
            if (x == null) x = new ReportExtras();
            HwSnapshot hw = d.Hw;
            StringBuilder b = new StringBuilder();
            string vt, vs;
            Analyzer.PlainVerdict(d, out vt, out vs);
            bool healthy = vt == "Seu PC está saudável";
            MonthSummary ms = d.Month ?? new MonthSummary();
            // para quem lê de fora, sem referências ao app ("veja em Resultados"): frase própria, com base nos números
            if (healthy && ms.BlueScreens == 0 && ms.UnexpectedShutdowns == 0 && ms.VideoSymptoms == 0)
                vs = "Todas as peças verificadas passaram nos testes de carga" + (ms.TestsTotal > 0 ? " (" + ms.TestsPassed + " de " + ms.TestsTotal + " testes aprovados nos últimos 30 dias)" : "") +
                     " e não houve tela azul, desligamento sem aviso nem queda de vídeo nos últimos 30 dias.";
            bool anyBad = false;
            foreach (CompState s in d.States) if (s != null && s.Status == CompStatus.Failed) anyBad = true;
            string seal = anyBad ? "sealbad" : healthy ? "sealok" : "sealwarn";

            b.Append("<!doctype html><html lang='pt-BR'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
            b.Append("<title>Atestado de saúde do PC — DiagIT</title><style>" + Css + CertCss + "</style></head><body><div class='wrap'>");
            b.Append("<header><div class='mark'>" + Mark + "</div><div><h1>Atestado de saúde do PC</h1><div class='meta'>Emitido pelo DiagIT " + Version + " em " +
                DateTime.Now.ToString("dd/MM/yyyy 'às' HH:mm", Pt) + "</div></div></header>");

            b.Append("<div class='big'><div class='seal " + seal + "'>" + (anyBad ? "!" : healthy ? "✓" : "•") + "</div><div><div style='font-size:19px;font-weight:700'>" + H(vt) + "</div><div class='soft'>" + H(vs) + "</div></div></div>");

            // ---------------------------------------------------------------- configuração
            b.Append("<h2>Configuração</h2><table>");
            Action<string, string> row = delegate(string k, string v) { if (!string.IsNullOrEmpty(v)) b.Append("<tr><td class='muted' style='width:150px'>" + H(k) + "</td><td>" + H(v) + "</td></tr>"); };
            row("Computador", m.Board);
            row("Processador", m.Cpu + (m.Cores > 0 ? " (" + m.Cores + " núcleos, " + m.Threads + " threads)" : ""));
            row("Placa de vídeo", m.Gpu + (m.GpuDriver.Length > 0 ? " — driver " + m.GpuDriver : "") + (m.VramGB > 0 ? " — " + m.VramGB.ToString("0.#", Pt) + " GB" : ""));
            if (hw != null && hw.RamModules > 0)
                row("Memória", hw.RamTotalGb.ToString("0", Pt) + " GB em " + hw.RamModules + (hw.RamModules == 1 ? " pente" : " pentes") + (hw.DualChannel ? " (dual channel)" : " (canal único)") +
                    (hw.RamConfMts > 0 ? ", " + Mts(hw.RamConfMts, hw.RamType) : ""));
            else row("Memória", m.RamGB > 0 ? m.RamGB.ToString("0", Pt) + " GB" : "");
            if (hw != null)
                foreach (DriveHealth dh in hw.Drives)
                {
                    string state = !dh.Read ? "saúde não lida" : dh.Problems().Count == 0 ? "saudável" + (!double.IsNaN(dh.UsedPct) ? " (desgaste " + dh.UsedPct.ToString("0", Pt) + "%)" : "") : "ATENÇÃO: " + string.Join("; ", dh.Problems().ToArray());
                    row("Armazenamento", dh.Model + " (" + dh.Bus + ", " + (dh.SizeBytes / 1e9).ToString("0", Pt) + " GB) — " + state);
                }
            row("Windows", x.Windows.Length > 0 ? x.Windows : m.Os);
            if (hw != null)
                foreach (Fact f in hw.Facts) if (f.Group == "Placa-mãe" && f.Name == "BIOS") row("BIOS", f.Value);
            b.Append("</table>");

            // ---------------------------------------------------------------- peça por peça, com a prova
            b.Append("<h2>Peça por peça (e a prova)</h2><table><tr><th>Peça</th><th>Estado</th><th>Prova</th></tr>");
            int[] order = { (int)Comp.Cpu, (int)Comp.Gpu, (int)Comp.Ram, (int)Comp.Disk, (int)Comp.Psu, (int)Comp.Cooling, (int)Comp.Board, (int)Comp.Software };
            List<string> notChecked = new List<string>();
            foreach (int i in order)
            {
                CompState s = d.States[i];
                if (s == null) continue;
                string proof;
                if (s.Passed != null)
                {
                    List<string> meas = new List<string>();
                    double cpuT = s.Passed.Get("t_cpu_temp_max"), hot = s.Passed.Get("t_gpu_hot_max"), v12 = s.Passed.Get("t_v12_min");
                    if (i == (int)Comp.Cpu && !double.IsNaN(cpuT)) meas.Add("CPU até " + Num(cpuT, "0") + " °C");
                    if (i == (int)Comp.Gpu && !double.IsNaN(hot)) meas.Add("GPU até " + Num(hot, "0") + " °C");
                    if (i == (int)Comp.Psu && !double.IsNaN(v12)) meas.Add("+12 V mínimo " + Num(v12, "0.00") + " V");
                    proof = "Teste de " + TestResult.KindLabel(s.Passed.Kind) + " aprovado em " + s.Passed.Start.ToString("dd/MM/yyyy", Pt) + (meas.Count > 0 ? " (" + string.Join(", ", meas.ToArray()) + ")" : "");
                }
                else if (s.Status == CompStatus.Untested) { proof = "Não verificado"; notChecked.Add(CompInfo.Plain[i]); }
                else proof = Analyzer.PlainNote(s, i);
                b.Append("<tr><td><b>" + H(CompInfo.Plain[i]) + "</b></td><td>" + Pill(s.Status) + "</td><td>" + H(proof) + "</td></tr>");
            }
            b.Append("</table>");

            // ---------------------------------------------------------------- 30 dias
            b.Append("<h2>Últimos 30 dias</h2><table class='nums'><tr>" +
                "<td><span class='v'>" + ms.BlueScreens + "</span>telas azuis</td>" +
                "<td><span class='v'>" + ms.UnexpectedShutdowns + "</span>desligamentos sem aviso</td>" +
                "<td><span class='v'>" + ms.VideoSymptoms + "</span>quedas de vídeo com sintoma</td>" +
                "<td><span class='v'>" + ms.HoursPlayed.ToString("0.#", Pt) + " h</span>jogadas (" + ms.Sessions + " partidas)</td>" +
                "<td><span class='v'>" + (ms.TestsTotal == 0 ? "—" : ms.TestsPassed + " de " + ms.TestsTotal) + "</span>testes aprovados</td></tr></table>");

            // ---------------------------------------------------------------- o que não cobre
            b.Append("<h2>O que este atestado não cobre</h2><ul>");
            if (notChecked.Count > 0) b.Append("<li><b>Não verificado:</b> " + H(string.Join(", ", notChecked.ToArray())) + ".</li>");
            b.Append("<li>O teste de memória cobre a memória livre na hora do teste, não a que o Windows estava usando.</li>");
            b.Append("<li>A tensão de +12 V vem do sensor da placa-mãe (precisão de alguns por cento).</li>");
            b.Append("<li>Defeitos que aparecem de vez em quando podem não aparecer num teste de poucos minutos.</li>");
            b.Append("<li>Não é laudo técnico nem garantia: mostra o que o DiagIT mediu, com data, neste computador.</li></ul>");

            b.Append("<footer>Gerado pelo DiagIT " + Version + " em " + DateTime.Now.ToString("dd/MM/yyyy 'às' HH:mm", Pt) + ". Todas as medições foram feitas neste PC; nada foi enviado para a internet.</footer>");
            b.Append("</div></body></html>");
            return b.ToString();
        }
    }
}
