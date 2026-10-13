using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DiagIT
{
    public partial class MainForm
    {
        MetricCard cCpu, cGpu, cVram, cRam, cCpuT, cGpuT, cDisk, cGame;
        LineChart[] liveCharts;
        Label lblBanner;
        FlatButton btnRec;
        Segmented cmbWin;
        readonly int[] winSecs = { 120, 300, 900, 3600 };

        static readonly Color CCpu = Color.FromArgb(77, 163, 255);
        static readonly Color CGpu = Color.FromArgb(61, 214, 140);
        static readonly Color CRam = Color.FromArgb(255, 176, 32);
        static readonly Color CHot = Color.FromArgb(255, 92, 92);
        static readonly Color CVio = Color.FromArgb(178, 137, 255);

        void BuildLive()
        {
            Panel p = pages[P_LIVE];

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(54)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(214)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            p.Controls.Add(root);

            // faixa de status
            Panel banner = new Panel();
            banner.Dock = DockStyle.Fill;
            lblBanner = new Label();
            lblBanner.Font = Theme.Font(11f, FontStyle.Bold);
            lblBanner.ForeColor = Theme.Text;
            lblBanner.AutoSize = false;
            lblBanner.TextAlign = ContentAlignment.MiddleLeft;
            lblBanner.Dock = DockStyle.Fill;
            banner.Controls.Add(lblBanner);

            Panel right = new Panel();
            right.Dock = DockStyle.Right;
            right.Width = Theme.S(520);
            banner.Controls.Add(right);
            cmbWin = new Segmented(new string[] { "2 min", "5 min", "15 min", "1 h" }, 1);
            cmbWin.Location = new Point(0, Theme.S(11));
            cmbWin.SelectedIndexChanged += delegate { RefreshLive(); };
            right.Controls.Add(cmbWin);
            btnRec = new FlatButton("Iniciar gravação manual", false);
            btnRec.Location = new Point(cmbWin.Right + Theme.S(12), Theme.S(9));
            btnRec.Width = Theme.S(220);
            btnRec.Click += delegate { ToggleManual(); };
            right.Controls.Add(btnRec);
            root.Controls.Add(banner, 0, 0);

            // cartões
            TableLayoutPanel cards = new TableLayoutPanel();
            cards.Dock = DockStyle.Fill;
            cards.ColumnCount = 4;
            cards.RowCount = 2;
            for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            cCpu = NewCard("Processador", cards, 0, 0);
            cGpu = NewCard("Placa de vídeo (uso 3D)", cards, 1, 0);
            cVram = NewCard("Memória de vídeo (VRAM)", cards, 2, 0);
            cRam = NewCard("Memória RAM", cards, 3, 0);
            cCpuT = NewCard("Temperatura da CPU", cards, 0, 1);
            cGpuT = NewCard("Temperatura da GPU", cards, 1, 1);
            cDisk = NewCard("Fonte / consumo", cards, 2, 1);
            cGame = NewCard("Jogo", cards, 3, 1);
            root.Controls.Add(cards, 0, 1);

            // gráficos
            TableLayoutPanel charts = new TableLayoutPanel();
            charts.Dock = DockStyle.Fill;
            charts.ColumnCount = 3;
            charts.RowCount = 2;
            for (int i = 0; i < 3; i++) charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            charts.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            charts.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            liveCharts = new LineChart[6];
            liveCharts[0] = NewChart("Uso (%)", "%", 100, charts, 0, 0, new ChartSeries("CPU", F.Cpu, CCpu), new ChartSeries("GPU", F.Gpu, CGpu));
            liveCharts[1] = NewChart("Consumo (W)", "W", double.NaN, charts, 1, 0, new ChartSeries("CPU", F.CpuPower, CCpu), new ChartSeries("GPU", F.GpuPower, CGpu));
            liveCharts[2] = NewChart("Temperaturas (°C)", "°C", double.NaN, charts, 2, 0,
                new ChartSeries("CPU", F.CpuTemp, CCpu), new ChartSeries("GPU", F.GpuTemp, CGpu), new ChartSeries("GPU hotspot", F.GpuHot, CHot));
            liveCharts[3] = NewChart("Clocks (MHz)", "MHz", double.NaN, charts, 0, 1, new ChartSeries("CPU", F.CpuClock, CCpu), new ChartSeries("GPU", F.GpuClock, CGpu));
            liveCharts[4] = NewChart("Latência do disco (ms)", "ms", double.NaN, charts, 1, 1, new ChartSeries("Disco", F.DiskLat, CRam));
            liveCharts[5] = NewChart("Drivers (DPC / interrupções, %)", "%", double.NaN, charts, 2, 1, new ChartSeries("DPC", F.Dpc, CVio), new ChartSeries("Interrupções", F.Intr, CHot));
            liveCharts[4].MinSpan = 5;
            liveCharts[5].MinSpan = 5;
            liveCharts[3].MinSpan = 500;
            root.Controls.Add(charts, 0, 2);
        }

        MetricCard NewCard(string title, TableLayoutPanel t, int col, int row)
        {
            MetricCard c = new MetricCard();
            c.Title = title;
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(0, 0, Theme.S(8), Theme.S(8));
            t.Controls.Add(c, col, row);
            return c;
        }

        LineChart NewChart(string title, string unit, double fixedMax, TableLayoutPanel t, int col, int row, params ChartSeries[] series)
        {
            LineChart c = new LineChart();
            c.Title = title;
            c.Unit = unit;
            c.FixedMax = fixedMax;
            c.Series = series;
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(0, 0, Theme.S(8), Theme.S(8));
            t.Controls.Add(c, col, row);
            return c;
        }

        static string N(double v, string f) { return double.IsNaN(v) ? "—" : v.ToString(f); }

        void RefreshLive()
        {
            Sample s = w.Last;
            SessionInfo cur = w.Current;

            if (cur != null)
            {
                lblBanner.ForeColor = Theme.Bad;
                TimeSpan d = DateTime.Now - cur.Start;
                lblBanner.Text = "● Gravando: " + cur.Game + "  —  " + ((int)d.TotalMinutes).ToString("00") + ":" + d.Seconds.ToString("00");
                btnRec.Text = "Parar gravação";
            }
            else
            {
                lblBanner.ForeColor = Theme.Good;
                lblBanner.Text = cfg.AutoDetect ? "● Monitorando — grava sozinho quando detectar um jogo" : "● Monitorando (detecção automática desligada)";
                btnRec.Text = "Iniciar gravação manual";
            }

            if (s == null) return;
            double cpu = s[F.Cpu], gpu = s[F.Gpu];

            cCpu.Set(N(cpu, "0") + "%",
                "Núcleo mais ocupado: " + N(s[F.CoreMax], "0") + "%" + (double.IsNaN(s[F.CpuClock]) ? "" : "  •  " + (s[F.CpuClock] / 1000.0).ToString("0.00") + " GHz"),
                cpu / 100, cpu >= 98 ? 1 : -1);

            cGpu.Set(N(gpu, "0") + "%",
                (double.IsNaN(s[F.GpuPower]) ? "" : N(s[F.GpuPower], "0") + " W") + (double.IsNaN(s[F.GpuClock]) ? "" : "  •  " + N(s[F.GpuClock], "0") + " MHz"),
                gpu / 100, -1);

            double vramPct = s[F.GpuMemPct];
            cVram.Set(double.IsNaN(s[F.GpuMem]) ? "—" : (s[F.GpuMem] / 1024.0).ToString("0.0") + " GB",
                double.IsNaN(w.Perf.TotalVramMB) ? "" : "de " + (w.Perf.TotalVramMB / 1024.0).ToString("0.0") + " GB (" + N(vramPct, "0") + "%)",
                vramPct / 100, vramPct >= 95 ? 1 : -1);

            double avail = s[F.Avail], commit = s[F.Commit];
            int ramLevel = (avail < 512 || commit >= 97) ? 2 : (avail < 1024 || commit >= 92) ? 1 : -1;
            cRam.Set(N(s[F.Ram], "0") + "%",
                N(avail / 1024.0, "0.0") + " GB livres  •  comprometida " + N(commit, "0") + "%",
                s[F.Ram] / 100, ramLevel);

            double ct = s[F.CpuTemp], gt = s[F.GpuTemp];
            if (double.IsNaN(ct))
                cCpuT.Set("—", SysUtil.IsAdmin() ? "Sensor indisponível (veja Resolver → PawnIO)" : "Requer executar como administrador", double.NaN, -1);
            else
                cCpuT.Set(ct.ToString("0") + " °C", (double.IsNaN(s[F.CpuPower]) ? "" : "Consumo " + N(s[F.CpuPower], "0") + " W") +
                    (double.IsNaN(s[F.CpuFan]) ? "" : "  •  ventoinha " + N(s[F.CpuFan], "0") + " RPM"), ct / 100, ct >= 90 ? 2 : ct >= 80 ? 1 : 0);
            if (double.IsNaN(gt))
                cGpuT.Set("—", w.Sensors.Available ? "Sensor de GPU não exposto pela fonte atual" : "Sem sensor — veja Configurações", double.NaN, -1);
            else
            {
                string sub = "";
                if (!double.IsNaN(s[F.GpuHot])) sub += "hotspot " + N(s[F.GpuHot], "0") + " °C";
                if (!double.IsNaN(s[F.GpuMemTemp])) sub += (sub.Length > 0 ? "  •  " : "") + "VRAM " + N(s[F.GpuMemTemp], "0") + " °C";
                if (!double.IsNaN(s[F.GpuFan])) sub += (sub.Length > 0 ? "  •  " : "") + "ventoinha " + N(s[F.GpuFan], "0") + " RPM";
                double hot = double.IsNaN(s[F.GpuHot]) ? 0 : s[F.GpuHot];
                cGpuT.Set(gt.ToString("0") + " °C", sub, gt / 110, (gt >= 90 || hot >= 105) ? 2 : (gt >= 82 || hot >= 95) ? 1 : 0);
            }

            double v12 = s[F.V12], cw = s[F.CpuPower], gw = s[F.GpuPower];
            string watts = (double.IsNaN(cw) && double.IsNaN(gw)) ? "" : "CPU+GPU " + ((double.IsNaN(cw) ? 0 : cw) + (double.IsNaN(gw) ? 0 : gw)).ToString("0") + " W";
            cDisk.Set(double.IsNaN(v12) ? (watts.Length > 0 ? watts : "—") : v12.ToString("0.00") + " V",
                double.IsNaN(v12) ? "Rail +12 V indisponível (requer administrador)" : "+12 V  •  " + watts + (double.IsNaN(s[F.V5]) ? "" : "  •  +5 V " + s[F.V5].ToString("0.00")),
                double.NaN, double.IsNaN(v12) ? -1 : (v12 < 11.4 || v12 > 12.6) ? 2 : 0);

            if (cur != null)
                cGame.Set(Trim(cur.Game, 18), "GPU do jogo " + N(s[F.GameGpu], "0") + "%  •  CPU do jogo " + N(s[F.GameCpu], "0") + "%", double.NaN, 0);
            else if (w.TopApp.Length > 0)
                cGame.Set("Nenhum", "Maior uso de GPU: " + Trim(w.TopApp, 16) + " (" + w.TopScore.ToString("0") + "%)", double.NaN, -1);
            else
                cGame.Set("Nenhum", "Aguardando um jogo...", double.NaN, -1);

            int win = winSecs[Math.Max(0, cmbWin.SelectedIndex)];
            List<Sample> live = w.LiveCopy(win);
            foreach (LineChart c in liveCharts)
            {
                c.WindowSeconds = win;
                c.SetData(live);
            }
        }
    }
}
