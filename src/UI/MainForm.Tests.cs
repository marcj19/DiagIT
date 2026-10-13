using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace DiagIT
{
    public class ProgressLine : BufferedControl
    {
        public double Value;
        public Color Bar = Theme.Accent;
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Panel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Round(r, Height / 2f))
            using (SolidBrush b = new SolidBrush(Theme.Panel2)) g.FillPath(b, p);
            float w = (float)Math.Max(0, Math.Min(1, Value)) * (Width - 1);
            if (w > Height)
                using (GraphicsPath p = Theme.Round(new RectangleF(0, 0, w, Height - 1), Height / 2f))
                using (SolidBrush b = new SolidBrush(Bar)) g.FillPath(b, p);
        }
    }

    public class TestDef
    {
        public TestKind Kind;
        public string Title, Desc;
        public string[] Labels;
        public int[] Secs;
        public object[] Opts;
    }

    public partial class MainForm
    {
        TestDef[] testDefs;
        readonly Dictionary<TestKind, Panel> testCards = new Dictionary<TestKind, Panel>();
        readonly Dictionary<TestKind, Segmented> testDur = new Dictionary<TestKind, Segmented>();
        readonly Dictionary<TestKind, Label> testLast = new Dictionary<TestKind, Label>();
        readonly List<FlatButton> testButtons = new List<FlatButton>();
        Panel runPanel;
        Label lblRunTitle, lblRunPhase, lblRunStats, lblTestsBanner;
        ProgressLine runBar;
        LineChart runTemps, runPower;
        ListView lvTests;
        TextBox txtTest;
        readonly Queue<object[]> testQueue = new Queue<object[]>();
        TableLayoutPanel testsRoot;

        void BuildTests()
        {
            testDefs = new TestDef[]
            {
                Def(TestKind.Cpu, "Processador (CPU)", "Todos os núcleos em carga máxima calculando e conferindo resultados bit a bit. Detecta erro de cálculo, superaquecimento e queda de clock.",
                    new string[] { "2 min", "10 min", "30 min" }, new int[] { 120, 600, 1800 }, null),
                Def(TestKind.Ram, "Memória RAM", "Grava e confere padrões em toda a RAM livre (5 padrões por passada). Qualquer bit trocado é erro real de memória.",
                    new string[] { "5 min", "15 min", "45 min" }, new int[] { 300, 900, 2700 }, null),
                Def(TestKind.Gpu, "Placa de vídeo e VRAM", "Carga total na GPU com cálculo repetido e comparado, teste de padrões na VRAM e detecção de travamento da GPU.",
                    new string[] { "3 min", "10 min", "30 min" }, new int[] { 180, 600, 1800 }, null),
                Def(TestKind.Disk, "Armazenamento", "Lê a saúde (SMART/NVMe) direto de cada disco e faz leituras por todo o disco medindo erros e lentidão. Somente leitura.",
                    new string[] { "Amostragem", "Disco inteiro" }, new int[] { 120, 14400 }, new object[] { new DiskTest.Options(), new DiskTest.Options { FullScan = true } }),
                Def(TestKind.Combined, "Fonte de energia (carga máxima)", "CPU e GPU no máximo ao mesmo tempo: o maior consumo que o PC consegue gerar. Se ele desligar, a fonte é a causa mais provável.",
                    new string[] { "5 min", "15 min", "30 min" }, new int[] { 300, 900, 1800 }, null)
            };

            Panel p = pages[P_TESTS];
            testsRoot = new TableLayoutPanel();
            testsRoot.Dock = DockStyle.Fill;
            testsRoot.ColumnCount = 1;
            testsRoot.RowCount = 5;
            testsRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(36)));
            testsRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(44)));
            testsRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(396)));
            testsRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            testsRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            p.Controls.Add(testsRoot);

            Label h = Header("Testes de hardware", "");
            h.Dock = DockStyle.Fill;
            testsRoot.Controls.Add(h, 0, 0);

            lblTestsBanner = Body("", true);
            lblTestsBanner.Dock = DockStyle.Fill;
            lblTestsBanner.Text = "Feche jogos e programas pesados antes de testar (o DiagIT recusa iniciar com jogo aberto). O teste para sozinho se a temperatura ficar crítica. " +
                "Se o PC desligar no meio do teste, isso já é o resultado: abra o DiagIT de novo e ele registra o que aconteceu.";
            testsRoot.Controls.Add(lblTestsBanner, 0, 1);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 3;
            grid.RowCount = 2;
            for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            for (int i = 0; i < testDefs.Length; i++) grid.Controls.Add(TestCard(testDefs[i]), i % 3, i / 3);
            grid.Controls.Add(BatteryCard(), 2, 1);
            testsRoot.Controls.Add(grid, 0, 2);

            // painel do teste em execução
            runPanel = new Panel();
            runPanel.Dock = DockStyle.Fill;
            runPanel.BackColor = Theme.Panel;
            runPanel.Padding = new Padding(Theme.S(14));
            lblRunTitle = Body("", false);
            lblRunTitle.Font = Theme.Font(11f, FontStyle.Bold);
            lblRunTitle.BackColor = Theme.Panel;
            lblRunTitle.SetBounds(Theme.S(14), Theme.S(10), Theme.S(520), Theme.S(24));
            runPanel.Controls.Add(lblRunTitle);
            lblRunPhase = Body("", true);
            lblRunPhase.BackColor = Theme.Panel;
            lblRunPhase.SetBounds(Theme.S(14), Theme.S(36), Theme.S(520), Theme.S(20));
            runPanel.Controls.Add(lblRunPhase);
            runBar = new ProgressLine();
            runBar.SetBounds(Theme.S(14), Theme.S(62), Theme.S(520), Theme.S(10));
            runPanel.Controls.Add(runBar);
            lblRunStats = Body("", false);
            lblRunStats.BackColor = Theme.Panel;
            lblRunStats.SetBounds(Theme.S(14), Theme.S(80), Theme.S(520), Theme.S(66));
            runPanel.Controls.Add(lblRunStats);
            FlatButton stop = new FlatButton("Parar teste", false);
            stop.Location = new Point(Theme.S(14), Theme.S(150) - stop.Height + Theme.S(4));
            stop.Click += delegate { testQueue.Clear(); runner.Stop(); };
            runPanel.Controls.Add(stop);
            runTemps = new LineChart();
            runTemps.Title = "Temperaturas (°C)"; runTemps.Unit = "°C";
            runTemps.Series = new ChartSeries[] { new ChartSeries("CPU", F.CpuTemp, CCpu), new ChartSeries("GPU", F.GpuTemp, CGpu), new ChartSeries("Hotspot", F.GpuHot, CHot) };
            runPanel.Controls.Add(runTemps);
            runPower = new LineChart();
            runPower.Title = "Consumo (W)"; runPower.Unit = "W";
            runPower.Series = new ChartSeries[] { new ChartSeries("CPU", F.CpuPower, CCpu), new ChartSeries("GPU", F.GpuPower, CGpu) };
            runPanel.Controls.Add(runPower);
            runPanel.Resize += delegate
            {
                int x = Theme.S(560), cw = (runPanel.Width - x - Theme.S(24)) / 2;
                runTemps.SetBounds(x, Theme.S(8), Math.Max(100, cw), runPanel.Height - Theme.S(16));
                runPower.SetBounds(x + cw + Theme.S(8), Theme.S(8), Math.Max(100, cw), runPanel.Height - Theme.S(16));
            };
            testsRoot.Controls.Add(runPanel, 0, 3);

            // histórico
            TableLayoutPanel hist = new TableLayoutPanel();
            hist.Dock = DockStyle.Fill;
            hist.ColumnCount = 2;
            hist.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(480)));
            hist.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            hist.Padding = new Padding(0, Theme.S(8), 0, 0);
            lvTests = new ListView();
            lvTests.View = View.Details;
            lvTests.FullRowSelect = true;
            lvTests.MultiSelect = false;
            lvTests.HideSelection = false;
            lvTests.BorderStyle = BorderStyle.None;
            lvTests.BackColor = Theme.Panel;
            lvTests.ForeColor = Theme.Text;
            lvTests.Font = Theme.Font(9f);
            lvTests.OwnerDraw = true;
            lvTests.Dock = DockStyle.Fill;
            lvTests.Margin = new Padding(0, 0, Theme.S(12), 0);
            lvTests.Columns.Add("Quando", Theme.S(96));
            lvTests.Columns.Add("Teste", Theme.S(170));
            lvTests.Columns.Add("Resultado", Theme.S(190));
            Theme.Dark(lvTests);
            lvTests.Resize += delegate
            {
                int used = lvTests.Columns[0].Width + lvTests.Columns[1].Width;
                int rest = lvTests.ClientSize.Width - used;
                if (rest > Theme.S(80)) lvTests.Columns[2].Width = rest;
            };
            lvTests.DrawColumnHeader += delegate(object s, DrawListViewColumnHeaderEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Theme.Panel2)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.Font(8.5f, FontStyle.Bold), new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y, e.Bounds.Width, e.Bounds.Height),
                    Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
            lvTests.DrawItem += delegate(object s, DrawListViewItemEventArgs e) { };
            lvTests.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(e.Item.Selected ? Color.FromArgb(33, 62, 100) : Theme.Panel)) e.Graphics.FillRectangle(b, e.Bounds);
                TestResult t = e.Item.Tag as TestResult;
                Color fg = Theme.Text;
                if (e.ColumnIndex == 2 && t != null)
                    fg = t.Status == "passed" ? Theme.Good : (t.Status == "failed" || t.Status == "crashed") ? Theme.Bad : Theme.Muted;
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Theme.Font(9f, e.ColumnIndex == 2 ? FontStyle.Bold : FontStyle.Regular),
                    new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y, e.Bounds.Width - Theme.S(8), e.Bounds.Height), fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            };
            lvTests.SelectedIndexChanged += delegate { if (lvTests.SelectedItems.Count > 0) ShowTestResult(lvTests.SelectedItems[0].Tag as TestResult); };
            hist.Controls.Add(lvTests, 0, 0);
            txtTest = new TextBox();
            txtTest.Multiline = true;
            txtTest.ReadOnly = true;
            txtTest.ScrollBars = ScrollBars.Vertical;
            txtTest.BackColor = Theme.Panel;
            txtTest.ForeColor = Theme.Text;
            txtTest.BorderStyle = BorderStyle.None;
            txtTest.Font = Theme.Font(9f);
            txtTest.Dock = DockStyle.Fill;
            Theme.Dark(txtTest);
            hist.Controls.Add(txtTest, 1, 0);
            testsRoot.Controls.Add(hist, 0, 4);
        }

        static TestDef Def(TestKind k, string title, string desc, string[] labels, int[] secs, object[] opts)
        {
            TestDef d = new TestDef();
            d.Kind = k; d.Title = title; d.Desc = desc; d.Labels = labels; d.Secs = secs; d.Opts = opts;
            return d;
        }

        Panel CardShell()
        {
            Panel c = new Panel();
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(0, 0, Theme.S(10), Theme.S(10));
            c.BackColor = Theme.Panel;
            c.Padding = new Padding(Theme.S(14));
            return c;
        }

        Panel TestCard(TestDef d)
        {
            Panel c = CardShell();
            Label t = Body(d.Title, false);
            t.Font = Theme.Font(11f, FontStyle.Bold);
            t.BackColor = Theme.Panel;
            t.SetBounds(Theme.S(14), Theme.S(12), Theme.S(330), Theme.S(22));
            c.Controls.Add(t);
            Label desc = Body(d.Desc, true);
            desc.BackColor = Theme.Panel;
            desc.SetBounds(Theme.S(14), Theme.S(36), Theme.S(330), Theme.S(54));
            c.Controls.Add(desc);
            Label last = Body("", true);
            last.BackColor = Theme.Panel;
            last.Font = Theme.Font(8.5f, FontStyle.Bold);
            last.SetBounds(Theme.S(14), Theme.S(94), Theme.S(330), Theme.S(36));
            c.Controls.Add(last);
            Segmented dur = new Segmented(d.Labels, d.Labels.Length > 2 ? 1 : 0);
            dur.BackColor = Theme.Panel;
            c.Controls.Add(dur);
            FlatButton go = new FlatButton("Iniciar", true);
            go.Width = Theme.S(96);
            go.Click += delegate
            {
                int i = dur.SelectedIndex;
                StartTest(d.Kind, d.Secs[i], d.Opts != null ? d.Opts[i] : null);
            };
            c.Controls.Add(go);
            testButtons.Add(go);
            c.Resize += delegate
            {
                int w = c.Width - Theme.S(28);
                t.Width = w; desc.Width = w; last.Width = w;
                desc.Height = Math.Max(Theme.S(36), c.Height - Theme.S(36) - Theme.S(100));
                last.Top = desc.Bottom + Theme.S(2);
                dur.Location = new Point(Theme.S(14), c.Height - dur.Height - Theme.S(12));
                go.Location = new Point(c.Width - go.Width - Theme.S(14), c.Height - go.Height - Theme.S(12));
            };
            testCards[d.Kind] = c;
            testDur[d.Kind] = dur;
            testLast[d.Kind] = last;
            return c;
        }

        Panel BatteryCard()
        {
            Panel c = CardShell();
            Label t = Body("Bateria completa", false);
            t.Font = Theme.Font(11f, FontStyle.Bold);
            t.BackColor = Theme.Panel;
            t.SetBounds(Theme.S(14), Theme.S(12), Theme.S(330), Theme.S(22));
            c.Controls.Add(t);
            Label desc = Body("Roda todos os testes em sequência, na duração rápida (cerca de 20 minutos). Ao final, o Diagnóstico mostra o estado comprovado de cada peça.", true);
            desc.BackColor = Theme.Panel;
            desc.SetBounds(Theme.S(14), Theme.S(36), Theme.S(330), Theme.S(70));
            c.Controls.Add(desc);
            FlatButton go = new FlatButton("Iniciar bateria", true);
            go.Click += delegate
            {
                testQueue.Clear();
                foreach (TestDef d in testDefs) testQueue.Enqueue(new object[] { d.Kind, d.Secs[0], d.Opts != null ? d.Opts[0] : null });
                object[] first = testQueue.Dequeue();
                if (!StartTest((TestKind)first[0], (int)first[1], first[2])) testQueue.Clear();
            };
            c.Controls.Add(go);
            testButtons.Add(go);
            c.Resize += delegate
            {
                t.Width = c.Width - Theme.S(28); desc.Width = c.Width - Theme.S(28);
                go.Location = new Point(c.Width - go.Width - Theme.S(14), c.Height - go.Height - Theme.S(12));
            };
            return c;
        }

        bool StartTest(TestKind k, int secs, object opt)
        {
            if (!SysUtil.IsAdmin() && testQueue.Count == 0)
            {
                DialogResult dr = MessageBox.Show(this, "O DiagIT está sem privilégios de administrador: a temperatura da CPU, as tensões da fonte e o SMART dos discos não serão medidos durante o teste.\n\n" +
                    "Reiniciar o DiagIT como administrador agora? (Não = testar assim mesmo)", "Testes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
                if (dr == DialogResult.Cancel) return false;
                if (dr == DialogResult.Yes) { if (SysUtil.RelaunchElevated("--elevated --page=" + P_TESTS)) ExitApp(); return false; }
            }
            string refuse;
            TestResult r = runner.Start(k, secs, opt, false, out refuse);
            if (r == null && refuse != null)
            {
                if (runner.Busy) { MessageBox.Show(this, refuse, "Testes"); return false; }
                if (MessageBox.Show(this, refuse + "\n\nTestar mesmo assim?", "Jogo aberto", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
                r = runner.Start(k, secs, opt, true, out refuse);
            }
            if (r == null) return false;
            testsRoot.RowStyles[3].Height = Theme.S(160);
            foreach (FlatButton b in testButtons) b.Enabled = false;
            RefreshTestRun();
            return true;
        }

        void RefreshTestRun()
        {
            TestResult r = runner.Current;
            TestContext c = runner.Ctx;
            if (r == null || c == null) return;
            TimeSpan el = DateTime.Now - r.Start;
            lblRunTitle.Text = "Testando: " + TestResult.KindLabel(r.Kind) + (testQueue.Count > 0 ? "   (faltam " + testQueue.Count + " na bateria)" : "");
            lblRunPhase.Text = c.Phase;
            runBar.Value = c.Progress;
            runBar.Bar = c.Errors > 0 ? Theme.Bad : Theme.Accent;
            runBar.Invalidate();
            Sample s = w.Last;
            StringBuilder sb = new StringBuilder();
            sb.Append("Tempo: " + ((int)el.TotalMinutes).ToString("00") + ":" + el.Seconds.ToString("00") + " de ~" + (r.PlannedSec / 60) + " min    ");
            sb.Append("Erros: " + c.Errors + "\n");
            if (s != null)
            {
                sb.Append("CPU " + N(s[F.Cpu], "0") + "% " + N(s[F.CpuTemp], "0") + " °C " + N(s[F.CpuPower], "0") + " W    ");
                sb.Append("GPU " + N(s[F.Gpu], "0") + "% " + N(s[F.GpuTemp], "0") + " °C (hotspot " + N(s[F.GpuHot], "0") + ") " + N(s[F.GpuPower], "0") + " W\n");
                if (!double.IsNaN(s[F.V12])) sb.Append("+12 V " + s[F.V12].ToString("0.00") + " V    ");
                if (!double.IsNaN(s[F.CpuFan])) sb.Append("Ventoinha CPU " + N(s[F.CpuFan], "0") + " RPM");
            }
            lblRunStats.Text = sb.ToString();
            List<Sample> l = w.LiveCopy((int)el.TotalSeconds + 10);
            runTemps.WindowSeconds = 0; runTemps.SetData(l);
            runPower.WindowSeconds = 0; runPower.SetData(l);
        }

        void OnTestFinished(TestResult r)
        {
            ReloadTestHistory();
            ShowTestResult(r);
            bool stopQueue = r.Get("thermal_abort") == 1 || r.Status == "crashed";
            if (testQueue.Count > 0 && !stopQueue)
            {
                object[] next = testQueue.Dequeue();
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 5000;
                t.Tick += delegate
                {
                    t.Stop(); t.Dispose();
                    if (!StartTest((TestKind)next[0], (int)next[1], next[2])) { testQueue.Clear(); EndRunUi(); }
                };
                t.Start();
                lblRunPhase.Text = "Próximo teste em 5 s (resfriando)...";
                return;
            }
            testQueue.Clear();
            EndRunUi();
            Balloon("Teste concluído: " + TestResult.KindLabel(r.Kind), r.StatusLabel + " — " + r.Summary,
                r.Status == "passed" ? ToolTipIcon.Info : ToolTipIcon.Warning);
            StartAnalyzeClick(false);
        }

        void EndRunUi()
        {
            testsRoot.RowStyles[3].Height = 0;
            foreach (FlatButton b in testButtons) b.Enabled = true;
        }

        void HighlightTest(TestKind k)
        {
            Panel c;
            if (!testCards.TryGetValue(k, out c)) return;
            c.BackColor = Color.FromArgb(30, 52, 82);
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 1500;
            t.Tick += delegate { t.Stop(); t.Dispose(); c.BackColor = Theme.Panel; };
            t.Start();
        }

        void ReloadTestHistory()
        {
            List<TestResult> all = TestStore.ListAll();
            string keep = lvTests.SelectedItems.Count > 0 ? ((TestResult)lvTests.SelectedItems[0].Tag).Id : null;
            lvTests.BeginUpdate();
            lvTests.Items.Clear();
            foreach (TestResult t in all)
            {
                ListViewItem it = new ListViewItem(new string[] { t.Start.ToString("dd/MM HH:mm"), TestResult.KindLabel(t.Kind), t.StatusLabel + (t.Errors > 0 ? " (" + t.Errors + " erros)" : "") });
                it.Tag = t;
                lvTests.Items.Add(it);
                if (t.Id == keep) it.Selected = true;
            }
            lvTests.EndUpdate();
            // último resultado em cada cartão
            foreach (TestDef d in testDefs)
            {
                TestResult last = null;
                foreach (TestResult t in all) if (t.Kind == d.Kind && t.Status != "running") { last = t; break; }
                Label l = testLast[d.Kind];
                if (last == null) { l.Text = "Nunca executado."; l.ForeColor = Theme.Muted; continue; }
                l.Text = "Último: " + last.StatusLabel + " em " + last.Start.ToString("dd/MM HH:mm");
                l.ForeColor = last.Status == "passed" ? Theme.Good : (last.Status == "failed" || last.Status == "crashed") ? Theme.Bad : Theme.Muted;
            }
            if (lvTests.SelectedItems.Count == 0 && lvTests.Items.Count > 0) lvTests.Items[0].Selected = true;
            if (lvTests.Items.Count == 0) txtTest.Text = "Nenhum teste executado ainda. Escolha um teste acima ou rode a bateria completa.";
        }

        void ShowTestResult(TestResult t)
        {
            if (t == null) return;
            CultureInfo pt = new CultureInfo("pt-BR");
            StringBuilder sb = new StringBuilder();
            TimeSpan dur = (t.End ?? DateTime.Now) - t.Start;
            sb.AppendLine(TestResult.KindLabel(t.Kind).ToUpper() + " — " + t.StatusLabel);
            sb.AppendLine("Em " + t.Start.ToString("dd/MM/yyyy HH:mm", pt) + ", duração " + ((int)dur.TotalMinutes) + " min " + dur.Seconds + " s.");
            sb.AppendLine();
            sb.AppendLine(t.Summary);
            sb.AppendLine();
            List<string> m = new List<string>();
            AddM(m, t, "t_cpu_temp_max", "Temperatura máxima da CPU", "°C", "0");
            AddM(m, t, "t_gpu_temp_max", "Temperatura máxima da GPU", "°C", "0");
            AddM(m, t, "t_gpu_hot_max", "Hotspot máximo da GPU", "°C", "0");
            AddM(m, t, "t_gpu_mem_temp_max", "Temperatura máxima da VRAM", "°C", "0");
            AddM(m, t, "t_cpu_power_max", "Consumo máximo da CPU", "W", "0");
            AddM(m, t, "t_gpu_power_max", "Consumo máximo da GPU", "W", "0");
            AddM(m, t, "peak_power", "Pico simultâneo CPU+GPU", "W", "0");
            AddM(m, t, "t_v12_min", "+12 V mínimo", "V", "0.00");
            AddM(m, t, "t_v5_min", "+5 V mínimo", "V", "0.00");
            AddM(m, t, "t_cpu_fan_min", "Ventoinha da CPU (mínimo)", "RPM", "0");
            AddM(m, t, "t_gpu_fan_max", "Ventoinha da GPU (máximo)", "RPM", "0");
            double ce = t.Get("t_cpu_clock_early"), cl = t.Get("t_cpu_clock_late");
            if (!double.IsNaN(ce) && !double.IsNaN(cl)) m.Add("Clock médio da CPU: " + ce.ToString("0") + " MHz no início → " + cl.ToString("0") + " MHz no fim");
            double ge = t.Get("t_gpu_clock_early"), gl = t.Get("t_gpu_clock_late");
            if (!double.IsNaN(ge) && !double.IsNaN(gl) && (t.Kind == TestKind.Gpu || t.Kind == TestKind.Combined)) m.Add("Clock médio da GPU: " + ge.ToString("0") + " MHz no início → " + gl.ToString("0") + " MHz no fim");
            double lanes = t.Get("t_pcie_lanes_max");
            if (!double.IsNaN(lanes) && lanes > 0) m.Add("Link PCIe da GPU: x" + lanes.ToString("0"));
            AddM(m, t, "pcie_up_gbs", "Banda PCIe RAM→GPU", "GB/s", "0.0");
            AddM(m, t, "vram_tested_mb", "VRAM testada", "MB", "N0");
            if (m.Count > 0)
            {
                sb.AppendLine("MEDIÇÕES DURANTE O TESTE");
                foreach (string s in m) sb.AppendLine("  " + s);
                sb.AppendLine();
            }
            if (t.Details.Count > 0)
            {
                sb.AppendLine("DETALHES");
                foreach (string d in t.Details) sb.AppendLine("  " + d);
            }
            txtTest.Text = sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
        }

        static void AddM(List<string> l, TestResult t, string key, string label, string unit, string fmt)
        {
            double v = t.Get(key);
            if (!double.IsNaN(v)) l.Add(label + ": " + v.ToString(fmt) + " " + unit);
        }
    }
}
