using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace DiagIT
{
    /// <summary>"Verificando": andamento da verificação em linguagem simples, com os dados técnicos sob demanda.</summary>
    public partial class MainForm
    {
        FlowPanel runFlow;
        StepHeader runHead;
        RunStepper runStepper;
        RunCard runCard;
        ReadingCards runRead;
        Toggle runTechToggle;
        Panel runTech;
        Label runTechText;
        LineChart runTechTemps, runTechPower;
        ButtonRow runButtons;
        FlatButton runStop, runSeeResults;
        string runLayoutKey = "";

        static string KindTesting(TestKind k)
        {
            switch (k)
            {
                case TestKind.Cpu: return "Testando o processador";
                case TestKind.Ram: return "Testando a memória RAM";
                case TestKind.Gpu: return "Testando a placa de vídeo";
                case TestKind.Disk: return "Testando o armazenamento";
                default: return "Testando a fonte de energia";
            }
        }

        static string KindExplain(TestKind k)
        {
            switch (k)
            {
                case TestKind.Cpu: return "O processador está fazendo cálculos no máximo da força, em todos os núcleos, e o DiagIT confere cada resultado. Um processador bom nunca erra.";
                case TestKind.Ram: return "O DiagIT está gravando padrões na memória e conferindo se cada bit volta igual. Uma memória boa nunca troca um bit.";
                case TestKind.Gpu: return "A placa de vídeo está fazendo milhões de cálculos no máximo da força, e o DiagIT confere cada resultado. Uma placa boa nunca erra.";
                case TestKind.Disk: return "O DiagIT está lendo o disco de ponta a ponta e conferindo a saúde que ele mesmo informa. Nada é apagado ou gravado.";
                default: return "Processador e placa de vídeo estão no máximo ao mesmo tempo: o maior consumo que o PC pode ter. Se o PC desligar agora, a fonte é a principal suspeita.";
            }
        }

        void BuildRun()
        {
            Panel p = pages[P_RUN];
            runFlow = new FlowPanel();
            runFlow.Dock = DockStyle.Fill;
            p.Controls.Add(runFlow);

            runHead = new StepHeader();
            runFlow.Controls.Add(runHead);
            runStepper = new RunStepper();
            runFlow.Controls.Add(runStepper);
            runCard = new RunCard();
            runFlow.Controls.Add(runCard);
            runRead = new ReadingCards();
            runFlow.Controls.Add(runRead);

            runTechToggle = new Toggle("Ver dados técnicos");
            runTechToggle.Toggled += delegate { runTech.Visible = runTechToggle.Open; runFlow.Relayout(); };
            runFlow.Controls.Add(runTechToggle);
            runTech = new Panel();
            runTech.BackColor = Theme.Bg;
            runTech.Height = Theme.S(280);
            runTech.Visible = false;
            runTechText = Body("", false);
            runTechText.Font = Theme.Font(9f);
            runTechText.ForeColor = Theme.Soft;
            runTechText.SetBounds(0, 0, Theme.S(600), Theme.S(60));
            runTech.Controls.Add(runTechText);
            runTechTemps = new LineChart();
            runTechTemps.Title = "Temperaturas (°C)"; runTechTemps.Unit = "°C";
            runTechTemps.Series = new ChartSeries[] { new ChartSeries("CPU", F.CpuTemp, CCpu), new ChartSeries("GPU", F.GpuTemp, CGpu), new ChartSeries("Hotspot", F.GpuHot, CHot) };
            runTech.Controls.Add(runTechTemps);
            runTechPower = new LineChart();
            runTechPower.Title = "Consumo (W)"; runTechPower.Unit = "W";
            runTechPower.Series = new ChartSeries[] { new ChartSeries("CPU", F.CpuPower, CCpu), new ChartSeries("GPU", F.GpuPower, CGpu) };
            runTech.Controls.Add(runTechPower);
            runTech.Resize += delegate
            {
                int cw = (runTech.Width - Theme.S(12)) / 2;
                runTechText.Width = runTech.Width;
                runTechTemps.SetBounds(0, Theme.S(66), Math.Max(100, cw), runTech.Height - Theme.S(66));
                runTechPower.SetBounds(cw + Theme.S(12), Theme.S(66), Math.Max(100, cw), runTech.Height - Theme.S(66));
            };
            runFlow.Controls.Add(runTech);

            runButtons = new ButtonRow();
            runStop = new FlatButton("Parar verificação", false);
            runStop.Click += delegate { testQueue.Clear(); runner.Stop(); };
            runButtons.Controls.Add(runStop);
            runSeeResults = new FlatButton("Ver os resultados", true);
            runSeeResults.Click += delegate { Navigate(P_DIAG); };
            runButtons.Controls.Add(runSeeResults);
            runFlow.Controls.Add(runButtons);
        }

        static ReadingCards.Reading Temp(ReadingCards.Reading r, string label, double t, double normal, double hot, double scale, string note)
        {
            r.Label = label;
            r.Pill = null;
            if (double.IsNaN(t)) { r.Value = "—"; r.Level = -1; r.Mark = double.NaN; r.Note = "Sensor indisponível (abra o DiagIT como administrador)."; return r; }
            r.Value = t.ToString("0") + " °C";
            r.Level = t < normal ? 0 : t < hot ? 1 : 2;
            r.Mark = t / scale; r.Warn = normal / scale; r.Hot = hot / scale;
            r.Note = note;
            return r;
        }

        void RefreshRun()
        {
            if (runFlow == null) return;
            TestResult r = runner.Current;
            TestContext c = runner.Ctx;
            bool busy = r != null && c != null;

            bool halted = !busy && runPlan.Count > 0 && runResults.Count < runPlan.Count;
            runHead.Set(runLong ? "TESTE LONGO" : runGuided ? "PASSO 3 DE 3" : "VERIFICAÇÃO", busy ? "Verificando seu PC" : halted ? "Verificação interrompida" : runResults.Count > 0 ? "Verificação concluída" : "Nenhuma verificação em andamento",
                busy ? (runLong ? "Pode ir dormir: o PC não vai entrar em suspensão e o DiagIT mostra o resultado quando terminar." : "Pode sair de perto. O DiagIT avisa quando terminar.") :
                halted ? "As peças que já passaram continuam valendo. Recomece quando quiser." :
                runResultsList().Count > 0 ? (goResultsAfterRun ? "Analisando o resultado..." : "Veja o que foi encontrado em Resultados.") :
                "Escolha o que verificar em Verificar peças, ou faça o check-up pelo Início.");

            // etapas
            runStepper.Items.Clear();
            List<TestResult> done = runResultsList();
            bool stopped = !busy && runPlan.Count > 0 && (done.Count < runPlan.Count || done.Exists(delegate(TestResult t) { return t.Status == "aborted" || t.Status == "error"; }));
            int next = 0;
            for (int i = 0; i < runPlan.Count; i++)
            {
                RunStepper.Item it = new RunStepper.Item();
                TestKind k = (TestKind)runPlan[i][0];
                it.Title = KindName(k);
                if (i < done.Count)
                {
                    TestResult t = done[i];
                    it.State = t.Status == "passed" ? 2 : t.Status == "aborted" || t.Status == "error" ? 0 : 3;
                    it.Note = t.Status == "passed" ? "Passou" : t.Status == "aborted" ? "Interrompido" : t.Status == "error" ? "Não conseguiu testar" : "Falhou";
                }
                else if (busy && i == done.Count) { it.State = 1; it.Note = "Agora"; }
                else if (!busy) { it.State = 0; it.Note = "Não feito"; }
                else { it.State = 0; it.Note = (next++ == 0 ? "Em seguida" : "Depois") + " · " + Minutes((int)runPlan[i][1]); }
                runStepper.Items.Add(it);
            }

            // etapa atual
            Sample s = w.Last;
            if (busy)
            {
                TimeSpan el = DateTime.Now - r.Start;
                int leftStep = Math.Max(0, r.PlannedSec - (int)el.TotalSeconds);
                int leftAll = leftStep;
                foreach (object[] q in testQueue) leftAll += (int)q[1] + 15;
                runCard.Title = KindTesting(r.Kind);
                runCard.Text_ = KindExplain(r.Kind) + (c.Errors > 0 ? " Atenção: " + c.Errors + (c.Errors == 1 ? " erro encontrado" : " erros encontrados") + " até agora." : "");
                runCard.Progress = c.Progress;
                runCard.ErrorState = c.Errors > 0;
                runCard.TimeLeft = "Faltam cerca de " + Minutes(leftStep) + " nesta etapa";
                runCard.Total = testQueue.Count > 0 ? "cerca de " + Minutes(leftAll) + " no total" : "";
            }
            else if (stopped)
            {
                int ok = done.FindAll(delegate(TestResult t) { return t.Status == "passed"; }).Count;
                runCard.Progress = runPlan.Count > 0 ? ok / (double)runPlan.Count : 0;
                runCard.ErrorState = done.Exists(delegate(TestResult t) { return t.Status == "failed" || t.Status == "crashed"; });
                runCard.Title = runCard.ErrorState ? "A verificação parou com um problema" : "Verificação interrompida";
                runCard.Text_ = runCard.ErrorState ? "Veja em Resultados o que falhou e o que fazer." :
                    "A verificação parou antes do fim. As peças que faltaram continuam sem verificação; você pode recomeçar quando quiser em Verificar peças." +
                    (done.Exists(delegate(TestResult t) { return t.Status == "error"; }) ? " Um dos testes não conseguiu rodar: abra o DiagIT como administrador." : "");
                runCard.TimeLeft = ""; runCard.Total = "";
            }
            else
            {
                runCard.Progress = done.Count > 0 ? 1 : 0;
                runCard.ErrorState = done.Exists(delegate(TestResult t) { return t.Status == "failed" || t.Status == "crashed"; });
                runCard.Title = done.Count > 0 ? (runCard.ErrorState ? "A verificação encontrou um problema" : "Verificação concluída") : "Tudo parado";
                runCard.Text_ = done.Count > 0 ? (runCard.ErrorState ? "Veja em Resultados o que falhou e o que fazer." : "As peças verificadas passaram. Veja em Resultados o que isso significa para o seu PC.") :
                    "Nenhum teste está rodando agora.";
                runCard.TimeLeft = ""; runCard.Total = "";
            }

            // leituras
            TestKind kind = busy ? r.Kind : TestKind.Cpu;
            double cpuT = s != null ? s[F.CpuTemp] : double.NaN, gpuT = s != null ? s[F.GpuTemp] : double.NaN, dT = s != null ? s[F.DiskTemp] : double.NaN;
            Temp(runRead.Items[0], "Temperatura do processador", cpuT, 85, 95, 110, "Até 85 °C é normal com o PC no máximo.");
            if (kind == TestKind.Gpu)
            {
                Temp(runRead.Items[0], "Temperatura da placa de vídeo", gpuT, 85, 95, 110, "Até 85 °C é normal com a placa no máximo.");
                Temp(runRead.Items[1], "Temperatura do processador", cpuT, 85, 95, 110, "Quase parado nesta etapa, como esperado.");
            }
            else if (kind == TestKind.Disk)
            {
                Temp(runRead.Items[0], "Temperatura do disco", dT, 60, 70, 90, "Até 60 °C é normal para um disco.");
                Temp(runRead.Items[1], "Temperatura do processador", cpuT, 85, 95, 110, "Até 85 °C é normal.");
            }
            else
            {
                Temp(runRead.Items[1], "Temperatura da placa de vídeo", gpuT, 85, 95, 110, kind == TestKind.Combined ? "Até 85 °C é normal com a placa no máximo." : "Pouco usada nesta etapa.");
            }
            ReadingCards.Reading er = runRead.Items[2];
            int errs = busy ? c.Errors : 0;
            er.Label = "Erros encontrados";
            er.Value = errs.ToString();
            er.Level = errs == 0 ? 0 : 2;
            er.Pill = errs == 0 ? "Tudo certo" : "Defeito";
            er.Mark = double.NaN;
            er.Note = "Um único erro já basta para apontar defeito.";

            // dados técnicos
            if (runTech.Visible)
            {
                StringBuilder sb = new StringBuilder();
                if (busy) sb.Append(TestResult.KindLabel(r.Kind) + " — " + c.Phase + "\n");
                if (s != null)
                {
                    sb.Append("CPU " + N(s[F.Cpu], "0") + "% " + N(s[F.CpuTemp], "0") + " °C " + N(s[F.CpuPower], "0") + " W " + N(s[F.CpuClock], "0") + " MHz    ");
                    sb.Append("GPU " + N(s[F.Gpu], "0") + "% " + N(s[F.GpuTemp], "0") + " °C (hotspot " + N(s[F.GpuHot], "0") + ") " + N(s[F.GpuPower], "0") + " W " + N(s[F.GpuClock], "0") + " MHz\n");
                    if (!double.IsNaN(s[F.V12])) sb.Append("+12 V " + s[F.V12].ToString("0.00") + " V    ");
                    if (!double.IsNaN(s[F.CpuFan])) sb.Append("Ventoinha CPU " + N(s[F.CpuFan], "0") + " RPM");
                }
                runTechText.Text = sb.ToString();
                if (busy)
                {
                    List<Sample> l = w.LiveCopy((int)(DateTime.Now - r.Start).TotalSeconds + 10);
                    runTechTemps.WindowSeconds = 0; runTechTemps.SetData(l);
                    runTechPower.WindowSeconds = 0; runTechPower.SetData(l);
                }
            }

            runStepper.Visible = runPlan.Count > 0;
            runStop.Visible = busy;
            runSeeResults.Visible = !busy && done.Count > 0;
            runRead.Visible = busy;
            runTechToggle.Visible = busy;
            if (!busy) runTech.Visible = false;

            string key = runPlan.Count + "|" + runStepper.Visible + "|" + busy + "|" + done.Count + "|" + runTech.Visible + "|" + runCard.Title;
            if (key != runLayoutKey) { runLayoutKey = key; runFlow.Relayout(); }
            else { runHead.Invalidate(); runStepper.Invalidate(); runCard.Invalidate(); runRead.Invalidate(); }
        }

        List<TestResult> runResultsList() { return runResults; }

        /// <summary>"12 min", "1 h 20 min".</summary>
        static string Minutes(int secs)
        {
            int m = Math.Max(1, (int)Math.Ceiling(secs / 60.0));
            if (m < 60) return m + " min";
            return (m / 60) + " h" + (m % 60 > 0 ? " " + (m % 60) + " min" : "");
        }
    }
}
