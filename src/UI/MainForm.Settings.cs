using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DiagIT
{
    public static class AppIcon
    {
        static Icon icon;

        public static Icon Get()
        {
            if (icon != null) return icon;
            try
            {
                icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch { }
            using (Bitmap bmp = Render(32))
                icon = Icon.FromHandle(bmp.GetHicon());
            return icon;
        }

        /// <summary>Ícone: fundo escuro arredondado com uma linha de "batimento" (monitoramento).</summary>
        public static Bitmap Render(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                RectangleF r = new RectangleF(0, 0, size - 1, size - 1);
                using (GraphicsPath p = Theme.Round(r, size * 0.22f))
                using (LinearGradientBrush b = new LinearGradientBrush(r, Color.FromArgb(32, 44, 64), Color.FromArgb(10, 14, 22), 90f))
                    g.FillPath(b, p);
                float u = size / 32f;
                PointF[] pts =
                {
                    new PointF(4 * u, 17 * u), new PointF(10 * u, 17 * u), new PointF(13 * u, 8 * u), new PointF(18 * u, 26 * u),
                    new PointF(21 * u, 14 * u), new PointF(24 * u, 17 * u), new PointF(28 * u, 17 * u)
                };
                using (Pen pen = new Pen(Color.FromArgb(77, 214, 160), Math.Max(1.5f, 2.2f * u)))
                {
                    pen.LineJoin = LineJoin.Round; pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, pts);
                }
            }
            return bmp;
        }
    }

    /// <summary>Gera um .ico com PNGs embutidos (usado só no build).</summary>
    public static class IconWriter
    {
        public static void Write(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            List<byte[]> pngs = new List<byte[]>();
            foreach (int s in sizes)
                using (Bitmap b = AppIcon.Render(s))
                using (MemoryStream ms = new MemoryStream())
                {
                    b.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }
            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (byte[] p in pngs) w.Write(p);
            }
        }
    }

    public partial class MainForm
    {
        NumericUpDown numInterval, numMinGpu, numDelay;
        TextBox txtGames;
        CheckBox chkAuto, chkTray, chkMin, chkNotify, chkAdmin;
        TextBox txtIgnore, txtSensors;
        Label lblSensorSrc, lblAdmin, lblPerf;
        FlatButton btnAutostart;
        System.Windows.Forms.Timer autoRefresh;

        void BuildSettings()
        {
            Panel p = pages[P_SET];
            p.Controls.Add(Header("Configurações", ""));
            Panel scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            Theme.Dark(scroll);
            p.Controls.Add(scroll);
            scroll.BringToFront();

            int y = 0;
            int colW = Theme.S(720);

            y = Section(scroll, "Monitoramento", y);
            numInterval = Num(scroll, "Intervalo entre leituras (segundos)", cfg.IntervalSec, 1, 10, ref y);
            chkAuto = Chk(scroll, "Detectar jogos automaticamente e gravar a partida", cfg.AutoDetect, ref y);
            numMinGpu = Num(scroll, "Considerar jogo quando um app usar a GPU acima de (%)", cfg.MinGpuForGame, 5, 95, ref y);
            numDelay = Num(scroll, "…por pelo menos (segundos)", cfg.StartDelaySec, 3, 120, ref y);
            Label lg = Body("Jogos conhecidos (nomes de processo, separados por vírgula): são sempre gravados quando abertos, mesmo que usem pouca GPU.", true);
            lg.SetBounds(0, y, colW, Theme.S(20)); scroll.Controls.Add(lg); y += Theme.S(22);
            txtGames = new TextBox();
            txtGames.Multiline = true;
            txtGames.ScrollBars = ScrollBars.Vertical;
            txtGames.BackColor = Theme.Panel;
            txtGames.ForeColor = Theme.Text;
            txtGames.BorderStyle = BorderStyle.FixedSingle;
            txtGames.Text = cfg.GameList;
            txtGames.SetBounds(0, y, colW, Theme.S(70));
            Theme.Dark(txtGames);
            scroll.Controls.Add(txtGames); y += Theme.S(80);
            Label l = Body("Programas ignorados na detecção (nomes de processo, separados por vírgula). Navegadores, Discord, etc. já vêm na lista.", true);
            l.SetBounds(0, y, colW, Theme.S(20)); scroll.Controls.Add(l); y += Theme.S(22);
            txtIgnore = new TextBox();
            txtIgnore.Multiline = true;
            txtIgnore.ScrollBars = ScrollBars.Vertical;
            txtIgnore.BackColor = Theme.Panel;
            txtIgnore.ForeColor = Theme.Text;
            txtIgnore.BorderStyle = BorderStyle.FixedSingle;
            txtIgnore.Text = cfg.IgnoreList;
            txtIgnore.SetBounds(0, y, colW, Theme.S(70));
            Theme.Dark(txtIgnore);
            scroll.Controls.Add(txtIgnore); y += Theme.S(80);

            y = Section(scroll, "Aplicativo", y + Theme.S(8));
            chkTray = Chk(scroll, "Ao fechar a janela, continuar em segundo plano (bandeja do sistema)", cfg.CloseToTray, ref y);
            chkMin = Chk(scroll, "Iniciar minimizado (sem abrir a janela)", cfg.StartMinimized, ref y);
            chkNotify = Chk(scroll, "Mostrar notificações (jogo detectado, resumo ao fim da partida)", cfg.Notify, ref y);
            chkAdmin = Chk(scroll, "Sempre abrir como administrador (necessário para CPU, placa-mãe, SMART e telas azuis)", cfg.AlwaysAdmin, ref y);

            btnAutostart = new FlatButton("", false);
            btnAutostart.Location = new Point(0, y);
            btnAutostart.Width = Theme.S(340);
            btnAutostart.Click += delegate { ToggleAutostart(); };
            scroll.Controls.Add(btnAutostart);
            Label ah = Body("Cria uma tarefa agendada que abre o DiagIT ao entrar no Windows, com privilégios para ler os sensores (o Windows pede confirmação uma vez).", true);
            ah.SetBounds(btnAutostart.Right + Theme.S(12), y - Theme.S(4), colW - btnAutostart.Width - Theme.S(12), Theme.S(42));
            scroll.Controls.Add(ah);
            y += Theme.S(46);

            FlatButton save = new FlatButton("Salvar configurações", true);
            save.Location = new Point(0, y);
            save.Click += delegate { SaveSettings(); };
            scroll.Controls.Add(save);
            y += Theme.S(54);

            y = Section(scroll, "Sensores de temperatura e energia", y);
            lblSensorSrc = Body("", false);
            lblSensorSrc.Font = Theme.Font(10f, FontStyle.Bold);
            lblSensorSrc.SetBounds(0, y, colW, Theme.S(22)); scroll.Controls.Add(lblSensorSrc); y += Theme.S(24);
            Label hint = Body(
                "Leitura embutida, sem programas extras:\n" +
                "  • GPU (temperatura, hotspot, VRAM, ventoinha, consumo, link PCIe): direto do driver de vídeo AMD (ADL) ou NVIDIA (NVML).\n" +
                "  • CPU e placa-mãe (temperatura, consumo, tensões +12/+5/+3,3 V, ventoinhas): LibreHardwareMonitor embutido + driver PawnIO.\n" +
                "    Requer o DiagIT como administrador e o PawnIO instalado (página Resolver).\n" +
                "  • HWiNFO é opcional: se estiver aberto com \"Shared Memory Support\", também é lido.", true);
            hint.SetBounds(0, y, colW, Theme.S(100)); scroll.Controls.Add(hint); y += Theme.S(104);

            lblAdmin = Body("", true);
            lblAdmin.SetBounds(0, y, colW - Theme.S(220), Theme.S(22)); scroll.Controls.Add(lblAdmin);
            if (!SysUtil.IsAdmin())
            {
                FlatButton adm = new FlatButton("Reiniciar como administrador", false);
                adm.Location = new Point(colW - Theme.S(220), y - Theme.S(6));
                adm.Width = Theme.S(220);
                adm.Click += delegate
                {
                    if (SysUtil.RelaunchElevated("--elevated")) ExitApp();
                };
                scroll.Controls.Add(adm);
            }
            y += Theme.S(34);

            txtSensors = new TextBox();
            txtSensors.Multiline = true;
            txtSensors.ReadOnly = true;
            txtSensors.ScrollBars = ScrollBars.Vertical;
            txtSensors.BackColor = Theme.Panel;
            txtSensors.ForeColor = Theme.Muted;
            txtSensors.BorderStyle = BorderStyle.FixedSingle;
            txtSensors.Font = new Font("Consolas", 9f);
            txtSensors.SetBounds(0, y, colW, Theme.S(170));
            Theme.Dark(txtSensors);
            scroll.Controls.Add(txtSensors); y += Theme.S(180);

            y = Section(scroll, "Dados", y + Theme.S(8));
            lblPerf = Body("", true);
            lblPerf.SetBounds(0, y, colW, Theme.S(22)); scroll.Controls.Add(lblPerf); y += Theme.S(28);
            FlatButton openData = new FlatButton("Abrir pasta de dados", false);
            openData.Location = new Point(0, y);
            openData.Click += delegate { try { Paths.Ensure(); Process.Start(Paths.Root); } catch { } };
            scroll.Controls.Add(openData);
            FlatButton openRep = new FlatButton("Abrir relatórios", false);
            openRep.Location = new Point(openData.Right + Theme.S(8), y);
            openRep.Click += delegate { try { Paths.Ensure(); Process.Start(Paths.Reports); } catch { } };
            scroll.Controls.Add(openRep);
            y += Theme.S(50);
            Label pad = Body("", true); pad.SetBounds(0, y, 10, Theme.S(20)); scroll.Controls.Add(pad);

            RefreshAutostartButton();

            autoRefresh = new System.Windows.Forms.Timer();
            autoRefresh.Interval = 3000;
            autoRefresh.Tick += delegate { if (curPage == P_SET && Visible) RefreshSensorsBox(); };
            autoRefresh.Start();
        }

        int Section(Control parent, string title, int y)
        {
            Label l = new Label();
            l.Text = title;
            l.Font = Theme.Font(11f, FontStyle.Bold);
            l.ForeColor = Theme.Accent;
            l.SetBounds(0, y, Theme.S(720), Theme.S(26));
            parent.Controls.Add(l);
            return y + Theme.S(32);
        }

        CheckBox Chk(Control parent, string text, bool val, ref int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Checked = val;
            c.ForeColor = Theme.Text;
            c.FlatStyle = FlatStyle.Flat;
            c.AutoSize = false;
            c.SetBounds(0, y, Theme.S(720), Theme.S(26));
            parent.Controls.Add(c);
            y += Theme.S(30);
            return c;
        }

        NumericUpDown Num(Control parent, string text, double val, int min, int max, ref int y)
        {
            Label l = Body(text, false);
            l.SetBounds(0, y + Theme.S(3), Theme.S(480), Theme.S(22));
            parent.Controls.Add(l);
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min; n.Maximum = max;
            n.Value = (decimal)Math.Max(min, Math.Min(max, val));
            n.BackColor = Theme.Panel;
            n.ForeColor = Theme.Text;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.SetBounds(Theme.S(490), y, Theme.S(80), Theme.S(26));
            parent.Controls.Add(n);
            y += Theme.S(32);
            return n;
        }

        void SaveSettings()
        {
            cfg.IntervalSec = (double)numInterval.Value;
            cfg.AutoDetect = chkAuto.Checked;
            cfg.MinGpuForGame = (double)numMinGpu.Value;
            cfg.StartDelaySec = (double)numDelay.Value;
            cfg.CloseToTray = chkTray.Checked;
            cfg.StartMinimized = chkMin.Checked;
            cfg.Notify = chkNotify.Checked;
            cfg.AlwaysAdmin = chkAdmin.Checked;
            cfg.IgnoreList = txtIgnore.Text.Replace("\r\n", ",").Replace("\n", ",");
            cfg.GameList = txtGames.Text.Replace("\r\n", ",").Replace("\n", ",");
            cfg.ResetCache();
            cfg.Save();
            MessageBox.Show(this, "Configurações salvas.", "DiagIT");
        }

        void RefreshAutostartButton()
        {
            btnAutostart.Text = SysUtil.AutostartEnabled() ? "Desativar início com o Windows" : "Ativar início com o Windows";
        }

        void ToggleAutostart()
        {
            bool on = SysUtil.AutostartEnabled();
            if (SysUtil.IsAdmin())
            {
                SysUtil.SetAutostart(!on);
                RefreshAutostartButton();
                return;
            }
            if (SysUtil.RelaunchElevated("--autostart " + (on ? "off" : "on")))
            {
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 4000;
                t.Tick += delegate { t.Stop(); t.Dispose(); RefreshAutostartButton(); };
                t.Start();
            }
        }

        void RefreshSensorsBox()
        {
            string src = w.Sensors.ActiveName;
            lblSensorSrc.ForeColor = src == null ? Theme.Warn : Theme.Good;
            lblSensorSrc.Text = src == null ? "● Nenhuma fonte de sensores encontrada (a busca se repete a cada 30 s)" : "● Fonte ativa: " + src;
            lblAdmin.Text = SysUtil.IsAdmin() ? "Executando como administrador." : "Executando sem privilégios de administrador (necessário para a DLL do LibreHardwareMonitor ler a CPU).";
            lblPerf.Text = "Custo de cada leitura: " + w.TickCostMs + " ms  •  " + w.SamplesTotal + " leituras desde que o app abriu  •  contadores do Windows indisponíveis: " + w.Perf.Missing;

            StringBuilder sb = new StringBuilder();
            List<SensorItem> items = w.Sensors.Latest;
            int n = 0;
            foreach (SensorItem it in items)
            {
                if (it.Typ == STyp.Other || it.Typ == STyp.Load) continue;
                sb.AppendLine(("[" + it.Cat + "/" + it.Typ + "]").PadRight(18) + Trim(it.Name, 34).PadRight(36) + it.Val.ToString("0.##") + " " + it.Unit);
                if (++n >= 250) break;
            }
            if (n == 0) sb.Append("(nenhum sensor lido ainda)");
            string txt = sb.ToString();
            if (txtSensors.Text != txt)
            {
                int sel = txtSensors.SelectionStart;
                txtSensors.Text = txt;
                txtSensors.SelectionStart = Math.Min(sel, txt.Length);
            }
        }
    }
}
