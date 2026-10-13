using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace DiagIT
{
    public partial class MainForm
    {
        ListView lvSess;
        Label lblSessTitle, lblSessStats;
        LineChart[] sessCharts;
        FlatButton btnSessAnalyze, btnSessDelete, btnSessFolder;
        List<SessionInfo> sessions = new List<SessionInfo>();
        SessionInfo selSess;
        List<Sample> selSamples = new List<Sample>();

        void BuildSessions()
        {
            Panel p = pages[P_SESS];
            p.Controls.Add(Header("Sessões gravadas", ""));

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 2;
            t.RowCount = 1;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(440)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.Controls.Add(t);
            t.BringToFront();

            // ---- lista
            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.Padding = new Padding(0, 0, Theme.S(14), 0);
            t.Controls.Add(left, 0, 0);

            lvSess = new ListView();
            lvSess.View = View.Details;
            lvSess.FullRowSelect = true;
            lvSess.MultiSelect = false;
            lvSess.HideSelection = false;
            lvSess.BorderStyle = BorderStyle.None;
            lvSess.BackColor = Theme.Panel;
            lvSess.ForeColor = Theme.Text;
            lvSess.Font = Theme.Font(9f);
            lvSess.OwnerDraw = true;
            lvSess.Dock = DockStyle.Fill;
            lvSess.Columns.Add("Início", Theme.S(98));
            lvSess.Columns.Add("Jogo", Theme.S(128));
            lvSess.Columns.Add("Duração", Theme.S(66));
            lvSess.Columns.Add("Estado", Theme.S(120));
            Theme.Dark(lvSess);
            lvSess.Resize += delegate
            {
                int used = 0;
                for (int i = 0; i < lvSess.Columns.Count - 1; i++) used += lvSess.Columns[i].Width;
                int rest = lvSess.ClientSize.Width - used;
                if (rest > Theme.S(60)) lvSess.Columns[lvSess.Columns.Count - 1].Width = rest;
            };
            lvSess.DrawColumnHeader += delegate(object s, DrawListViewColumnHeaderEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Theme.Panel2)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.Font(8.5f, FontStyle.Bold),
                    new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y, e.Bounds.Width - Theme.S(8), e.Bounds.Height), Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            };
            lvSess.DrawItem += delegate(object s, DrawListViewItemEventArgs e) { };
            lvSess.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e)
            {
                Color bg = e.Item.Selected ? Color.FromArgb(33, 62, 100) : Theme.Panel;
                using (SolidBrush b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
                SessionInfo si = e.Item.Tag as SessionInfo;
                Color fg = Theme.Text;
                if (si != null)
                {
                    if (si.Status == "interrupted") fg = Theme.Bad;
                    else if (si.Status == "running") fg = Theme.Warn;
                    else if (si.Note.StartsWith("O jogo fechou com erro")) fg = Theme.Orange;
                }
                if (e.ColumnIndex == 0 && si != null && si.Status == "ok" && !si.Note.StartsWith("O jogo fechou")) fg = Theme.Text;
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Theme.Font(9f),
                    new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y, e.Bounds.Width - Theme.S(8), e.Bounds.Height), fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            };
            lvSess.SelectedIndexChanged += delegate
            {
                if (lvSess.SelectedItems.Count > 0) SelectSession(lvSess.SelectedItems[0].Tag as SessionInfo);
            };
            left.Controls.Add(lvSess);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = Theme.S(46);
            left.Controls.Add(bottom);
            btnSessAnalyze = new FlatButton("Analisar esta sessão", true);
            btnSessAnalyze.Location = new Point(0, Theme.S(8));
            btnSessAnalyze.Click += delegate
            {
                if (selSess == null) return;
                cmbScope.SelectedIndex = 2;
                Navigate(P_DIAG);
                StartAnalyzeClick(false);
            };
            bottom.Controls.Add(btnSessAnalyze);
            btnSessDelete = new FlatButton("Excluir", false);
            btnSessDelete.Location = new Point(btnSessAnalyze.Right + Theme.S(8), Theme.S(8));
            btnSessDelete.Width = Theme.S(80);
            btnSessDelete.Click += delegate { DeleteSelected(); };
            bottom.Controls.Add(btnSessDelete);
            btnSessFolder = new FlatButton("Abrir pasta", false);
            btnSessFolder.Location = new Point(btnSessDelete.Right + Theme.S(8), Theme.S(8));
            btnSessFolder.Click += delegate { try { Paths.Ensure(); Process.Start(Paths.Root); } catch { } };
            bottom.Controls.Add(btnSessFolder);

            // ---- detalhe
            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            t.Controls.Add(right, 1, 0);

            TableLayoutPanel rt = new TableLayoutPanel();
            rt.Dock = DockStyle.Fill;
            rt.ColumnCount = 1;
            rt.RowCount = 4;
            rt.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(116)));
            rt.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
            rt.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
            rt.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
            right.Controls.Add(rt);

            Panel head = new Panel();
            head.Dock = DockStyle.Fill;
            lblSessTitle = new Label();
            lblSessTitle.Font = Theme.Font(12f, FontStyle.Bold);
            lblSessTitle.ForeColor = Theme.Text;
            lblSessTitle.Dock = DockStyle.Top;
            lblSessTitle.Height = Theme.S(28);
            lblSessTitle.Text = "Selecione uma sessão";
            lblSessStats = new Label();
            lblSessStats.Font = Theme.Font(9f);
            lblSessStats.ForeColor = Theme.Muted;
            lblSessStats.Dock = DockStyle.Fill;
            head.Controls.Add(lblSessStats);
            head.Controls.Add(lblSessTitle);
            rt.Controls.Add(head, 0, 0);

            sessCharts = new LineChart[3];
            sessCharts[0] = SessChart(rt, 1, "Uso (%)", "%", 100, new ChartSeries("CPU", F.Cpu, CCpu), new ChartSeries("GPU", F.Gpu, CGpu), new ChartSeries("RAM", F.Ram, CRam));
            sessCharts[1] = SessChart(rt, 2, "Temperaturas (°C)", "°C", double.NaN, new ChartSeries("CPU", F.CpuTemp, CCpu), new ChartSeries("GPU", F.GpuTemp, CGpu), new ChartSeries("GPU hotspot", F.GpuHot, CHot));
            sessCharts[2] = SessChart(rt, 3, "Clocks (MHz)", "MHz", double.NaN, new ChartSeries("CPU", F.CpuClock, CCpu), new ChartSeries("GPU", F.GpuClock, CGpu));
            sessCharts[2].MinSpan = 500;
        }

        LineChart SessChart(TableLayoutPanel t, int row, string title, string unit, double fixedMax, params ChartSeries[] series)
        {
            LineChart c = new LineChart();
            c.Title = title;
            c.Unit = unit;
            c.FixedMax = fixedMax;
            c.Series = series;
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(0, 0, 0, Theme.S(8));
            t.Controls.Add(c, 0, row);
            return c;
        }

        void ReloadSessions()
        {
            string keepId = selSess != null ? selSess.Id : null;
            sessions = SessionStore.ListAll();
            lvSess.BeginUpdate();
            lvSess.Items.Clear();
            ListViewItem toSelect = null;
            foreach (SessionInfo s in sessions)
            {
                string st = s.Status == "running" ? "Gravando" : s.Status == "interrupted" ? "INTERROMPIDA" :
                    s.Note.StartsWith("O jogo fechou com erro") ? "Jogo travou" : "Concluída";
                ListViewItem it = new ListViewItem(new string[]
                {
                    s.Start.ToString("dd/MM HH:mm"), s.Game, ((int)s.Duration.TotalHours).ToString("00") + ":" + s.Duration.Minutes.ToString("00") + ":" + s.Duration.Seconds.ToString("00"), st
                });
                it.Tag = s;
                lvSess.Items.Add(it);
                if (keepId != null && s.Id == keepId) toSelect = it;
            }
            lvSess.EndUpdate();
            if (toSelect != null) toSelect.Selected = true;
            else if (lvSess.Items.Count > 0) lvSess.Items[0].Selected = true;
            else
            {
                selSess = null;
                lblSessTitle.Text = "Nenhuma sessão gravada ainda";
                lblSessStats.Text = "Jogue normalmente com o DiagIT aberto (pode ficar na bandeja). Quando um jogo for detectado, a gravação começa sozinha.";
                foreach (LineChart c in sessCharts) c.SetData(new List<Sample>());
            }
        }

        void SelectSession(SessionInfo s)
        {
            selSess = s;
            if (s == null) return;
            UpdateSelectedSession(true);
        }

        void UpdateSelectedSession(bool full)
        {
            if (selSess == null) return;
            selSamples = SessionStore.LoadSamples(selSess);
            foreach (LineChart c in sessCharts) { c.WindowSeconds = 0; c.SetData(selSamples); }
            if (!full && selSess.Status != "running") return;

            lblSessTitle.Text = selSess.Game + "  —  " + selSess.Start.ToString("dd/MM/yyyy HH:mm") + "  (" + selSess.StatusLabel + ")";
            StringBuilder sb = new StringBuilder();
            if (selSess.Note.Length > 0) sb.AppendLine("⚠ " + selSess.Note);
            if (selSamples.Count > 0)
            {
                sb.AppendLine("CPU  média " + Stat.Fmt(Stat.Avg(Stat.Vals(selSamples, F.Cpu)), "%", 0) + "  •  máx " + Stat.Fmt(Stat.Max(Stat.Vals(selSamples, F.Cpu)), "%", 0) +
                    "      GPU  média " + Stat.Fmt(Stat.Avg(Stat.Vals(selSamples, F.Gpu)), "%", 0) + "  •  máx " + Stat.Fmt(Stat.Max(Stat.Vals(selSamples, F.Gpu)), "%", 0));
                double[] ct = Stat.Vals(selSamples, F.CpuTemp), gt = Stat.Vals(selSamples, F.GpuTemp);
                sb.AppendLine("Temp. CPU máx " + Stat.Fmt(Stat.Max(ct), "°C", 0) + "      Temp. GPU máx " + Stat.Fmt(Stat.Max(gt), "°C", 0) +
                    "      RAM máx " + Stat.Fmt(Stat.Max(Stat.Vals(selSamples, F.Ram)), "%", 0));
                sb.AppendLine("Latência de disco p99 " + Stat.Fmt(Stat.Percentile(Stat.Vals(selSamples, F.DiskLat), 0.99), "ms", 1) +
                    "      DPC+interrupções média " + Stat.Fmt(Stat.Avg(Stat.Vals(selSamples, F.Dpc)) + Stat.Avg(Stat.Vals(selSamples, F.Intr)), "%", 1));
                sb.Append(selSamples.Count + " amostras gravadas.");
            }
            else sb.Append("Sem amostras nesta sessão.");
            lblSessStats.Text = sb.ToString();
        }

        void DeleteSelected()
        {
            if (selSess == null) return;
            if (selSess.Status == "running") { MessageBox.Show(this, "Pare a gravação antes de excluir.", "DiagIT"); return; }
            if (MessageBox.Show(this, "Excluir a sessão \"" + selSess.Game + "\" de " + selSess.Start.ToString("dd/MM HH:mm") + "?\nOs dados não poderão ser recuperados.",
                "Excluir sessão", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            SessionStore.Delete(selSess);
            selSess = null;
            ReloadSessions();
        }
    }
}
