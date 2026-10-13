using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace DiagIT
{
    public static class Theme
    {
        // identidade "check-up do PC": grafite + íris; status sempre com ícone, cor e texto
        public static readonly Color Bg = Color.FromArgb(14, 19, 24);
        public static readonly Color Side = Color.FromArgb(10, 14, 18);
        public static readonly Color Panel = Color.FromArgb(22, 29, 37);
        public static readonly Color Panel2 = Color.FromArgb(29, 38, 48);
        public static readonly Color Panel3 = Color.FromArgb(19, 26, 33);
        public static readonly Color Border = Color.FromArgb(36, 46, 58);
        public static readonly Color Border2 = Color.FromArgb(54, 65, 79);
        public static readonly Color Text = Color.FromArgb(236, 241, 245);
        public static readonly Color Soft = Color.FromArgb(201, 210, 219);
        public static readonly Color Muted = Color.FromArgb(164, 176, 189);
        public static readonly Color Faint = Color.FromArgb(142, 154, 168);
        public static readonly Color Accent = Color.FromArgb(157, 140, 255);      // íris (marca)
        public static readonly Color AccentHover = Color.FromArgb(179, 166, 255);
        public static readonly Color AccentInk = Color.FromArgb(18, 14, 43);
        public static readonly Color AccentSoft = Color.FromArgb(38, 33, 69);
        public static readonly Color AccentText = Color.FromArgb(201, 191, 255);
        public static readonly Color Good = Color.FromArgb(67, 209, 140);
        public static readonly Color GoodText = Color.FromArgb(91, 224, 160);
        public static readonly Color Warn = Color.FromArgb(255, 181, 71);
        public static readonly Color WarnText = Color.FromArgb(255, 194, 102);
        public static readonly Color Orange = Color.FromArgb(255, 125, 69);
        public static readonly Color Bad = Color.FromArgb(255, 107, 107);
        public static readonly Color BadText = Color.FromArgb(255, 138, 138);
        public static readonly Color None = Color.FromArgb(58, 70, 84);
        public static readonly Color NoneText = Color.FromArgb(180, 190, 201);

        public static Color StatusColor(CompStatus s)
        {
            switch (s) { case CompStatus.Failed: return Bad; case CompStatus.Suspect: return Warn; case CompStatus.Ok: return Good; default: return Faint; }
        }
        public static Color StatusText(CompStatus s)
        {
            switch (s) { case CompStatus.Failed: return BadText; case CompStatus.Suspect: return WarnText; case CompStatus.Ok: return GoodText; default: return NoneText; }
        }
        public static string StatusIcon(CompStatus s)
        {
            switch (s) { case CompStatus.Failed: return "x"; case CompStatus.Suspect: return "alert"; case CompStatus.Ok: return "check"; default: return "dashcircle"; }
        }

        /// <summary>Cor sobreposta com transparência a um fundo (para tintas em controles que não aceitam alfa).</summary>
        public static Color Mix(Color over, Color under, int alpha)
        {
            float a = alpha / 255f;
            return Color.FromArgb((int)(over.R * a + under.R * (1 - a)), (int)(over.G * a + under.G * (1 - a)), (int)(over.B * a + under.B * (1 - a)));
        }

        static float scale = -1;
        public static float Scale
        {
            get
            {
                if (scale < 0)
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
                }
                return scale;
            }
        }

        public static int S(int px) { return (int)Math.Round(px * Scale); }
        public static float S(float px) { return px * Scale; }

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        public static Font Font(float pt, FontStyle st)
        {
            string k = pt + "|" + (int)st;
            Font f;
            if (!fonts.TryGetValue(k, out f))
            {
                f = new Font("Segoe UI", pt, st, GraphicsUnit.Point);
                fonts[k] = f;
            }
            return f;
        }
        public static Font Font(float pt) { return Font(pt, FontStyle.Regular); }

        static string displayFamily;
        /// <summary>Fonte dos títulos: Segoe UI Variable Display (Windows 11) ou Segoe UI Semibold.</summary>
        public static Font Display(float pt)
        {
            if (displayFamily == null)
            {
                displayFamily = "";
                try
                {
                    using (InstalledFontCollection ic = new InstalledFontCollection())
                        foreach (FontFamily ff in ic.Families)
                            if (ff.Name == "Segoe UI Variable Display") displayFamily = ff.Name;
                }
                catch { }
            }
            string k = "D" + pt;
            Font f;
            if (!fonts.TryGetValue(k, out f))
            {
                f = displayFamily.Length > 0 ? new Font(displayFamily, pt, FontStyle.Bold, GraphicsUnit.Point) : new Font("Segoe UI Semibold", pt, FontStyle.Bold, GraphicsUnit.Point);
                fonts[k] = f;
            }
            return f;
        }

        /// <summary>Pílula de status (ícone + texto) com fundo tingido. Devolve a largura usada.</summary>
        public static int StatusPill(Graphics g, int x, int y, CompStatus st, string text, Color under)
        {
            Font f = Font(8.5f, FontStyle.Bold);
            int tw = TextRenderer.MeasureText(g, text, f, Size.Empty, TextFormatFlags.NoPadding).Width;
            int h = S(24), ic = S(14), w = S(8) + ic + S(6) + tw + S(11);
            Rectangle r = new Rectangle(x, y, w, h);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, h / 2f))
            using (SolidBrush b = new SolidBrush(Mix(StatusColor(st), under, 38))) g.FillPath(b, p);
            Color fg = StatusText(st);
            Icons.Draw(g, StatusIcon(st), new RectangleF(x + S(8), y + (h - ic) / 2f, ic, ic), fg, S(2.4f));
            TextRenderer.DrawText(g, text, f, new Rectangle(x + S(8) + ic + S(6), y, tw + S(4), h), fg, TF | TextFormatFlags.VerticalCenter);
            return w;
        }

        /// <summary>Pílula simples de texto (rótulos de confiança, contadores).</summary>
        public static int TagPill(Graphics g, int x, int y, string text, Color fg, Color tint, Color under)
        {
            Font f = Font(7.5f, FontStyle.Bold);
            int tw = TextRenderer.MeasureText(g, text, f, Size.Empty, TextFormatFlags.NoPadding).Width;
            int h = S(20), w = tw + S(18);
            Rectangle r = new Rectangle(x, y, w, h);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, h / 2f))
            using (SolidBrush b = new SolidBrush(Mix(tint, under, 38))) g.FillPath(b, p);
            TextRenderer.DrawText(g, text, f, r, fg, TF | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            return w;
        }

        public static Color SevColor(Sev s)
        {
            switch (s)
            {
                case Sev.Crit: return Bad;
                case Sev.Warn: return Warn;
                case Sev.Ok: return Good;
                default: return Accent;
            }
        }

        public static string SevLabel(Sev s)
        {
            switch (s)
            {
                case Sev.Crit: return "CRÍTICO";
                case Sev.Warn: return "ATENÇÃO";
                case Sev.Ok: return "OK";
                default: return "INFO";
            }
        }

        public static Color ScoreColor(double pct)
        {
            if (pct >= 70) return Bad;
            if (pct >= 45) return Orange;
            if (pct >= 20) return Warn;
            return Muted;
        }

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        /// <summary>Barras de rolagem escuras (Windows 10/11).</summary>
        public static void Dark(Control c)
        {
            EventHandler apply = delegate { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
            if (c.IsHandleCreated) apply(c, EventArgs.Empty);
            c.HandleCreated += apply;
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0.5f) { p.AddRectangle(new RectangleF(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height))); return p; }   // controle ainda sem tamanho
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public const TextFormatFlags TF = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.Left;

        public static void Text_(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags extra)
        {
            TextRenderer.DrawText(g, s, f, r, c, TF | extra);
        }
    }

    // ------------------------------------------------------------------ base com double buffer
    public class BufferedControl : Control
    {
        public BufferedControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }
    }

    // ------------------------------------------------------------------ cartão de métrica
    public class MetricCard : BufferedControl
    {
        public string Title = "", Value = "—", Sub = "";
        public double Bar = double.NaN;       // 0..1
        public int Level = -1;                // -1 neutro, 0 ok, 1 atenção, 2 ruim

        public void Set(string value, string sub, double bar01, int level)
        {
            if (value == Value && sub == Sub && level == Level && (bar01 == Bar || (double.IsNaN(bar01) && double.IsNaN(Bar)))) return;
            Value = value; Sub = sub; Bar = bar01; Level = level;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (GraphicsPath p = Theme.Round(r, Theme.S(8)))
            using (SolidBrush b = new SolidBrush(Theme.Panel))
            using (Pen pen = new Pen(Theme.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            Color accent = Level == 2 ? Theme.Bad : Level == 1 ? Theme.Warn : Level == 0 ? Theme.Good : Theme.Accent;
            int pad = Theme.S(12);
            Theme.Text_(g, Title.ToUpper(), Theme.Font(7.5f, FontStyle.Bold), Theme.Muted, new Rectangle(pad, Theme.S(8), Width - pad * 2, Theme.S(14)), 0);
            Theme.Text_(g, Value, Theme.Font(17f, FontStyle.Bold), Level < 0 ? Theme.Text : accent,
                new Rectangle(pad, Theme.S(22), Width - pad * 2, Theme.S(32)), 0);
            Theme.Text_(g, Sub, Theme.Font(8f), Theme.Muted, new Rectangle(pad, Theme.S(56), Width - pad * 2, Height - Theme.S(56) - Theme.S(10)), TextFormatFlags.WordBreak);
            if (!double.IsNaN(Bar))
            {
                float bh = Theme.S(3f);
                RectangleF track = new RectangleF(pad, Height - Theme.S(9), Width - pad * 2, bh);
                using (SolidBrush tb = new SolidBrush(Theme.Panel2)) g.FillRectangle(tb, track);
                float w = (float)Math.Max(0, Math.Min(1, Bar)) * track.Width;
                using (SolidBrush fb = new SolidBrush(accent)) g.FillRectangle(fb, track.X, track.Y, w, bh);
            }
        }
    }

    // ------------------------------------------------------------------ botão da barra lateral
    public class NavButton : BufferedControl
    {
        public string Caption = "";
        public string Icon = "";
        public bool Selected;
        bool hover;

        public NavButton()
        {
            Cursor = Cursors.Hand; BackColor = Theme.Side;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            AccessibleName = Caption;
            Graphics g = e.Graphics;
            g.Clear(Theme.Side);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(Theme.S(12), 2, Width - Theme.S(24), Height - 4);
            if (Selected || hover)
                using (GraphicsPath p = Theme.Round(r, Theme.S(10)))
                using (SolidBrush b = new SolidBrush(Selected ? Theme.AccentSoft : Theme.Panel2))
                    g.FillPath(b, p);
            if (Focused && ShowFocusCues)
                using (GraphicsPath p = Theme.Round(r, Theme.S(10)))
                using (Pen pen = new Pen(Theme.AccentHover, Theme.S(2f))) g.DrawPath(pen, p);
            Color fg = Selected || hover ? Theme.Text : Theme.Muted;
            float ic = Theme.S(20f);
            Icons.Draw(g, Icon, new RectangleF(Theme.S(24), (Height - ic) / 2f, ic, ic), fg, Theme.S(2f));
            Theme.Text_(g, Caption, Theme.Font(10f, Selected ? FontStyle.Bold : FontStyle.Regular), fg,
                new Rectangle(Theme.S(56), 0, Width - Theme.S(64), Height), TextFormatFlags.VerticalCenter);
        }
    }

    // ------------------------------------------------------------------ botão comum (arredondado, desenhado pelo app)
    public class FlatButton : Button
    {
        readonly bool primary;
        bool hover, down;

        public FlatButton(string text, bool primary)
        {
            this.primary = primary;
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = Theme.Font(9.5f, FontStyle.Bold);
            Height = Theme.S(38);
            Padding = new Padding(0);
            AutoSize = false;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            Width = Math.Max(Theme.S(90), TextRenderer.MeasureText(text, Font).Width + Theme.S(40));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color under = Parent != null ? Parent.BackColor : Theme.Bg;
            g.Clear(under);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill, fg, border = Color.Empty;
            if (!Enabled) { fill = primary ? Theme.Mix(Theme.Accent, under, 70) : under; fg = primary ? Theme.Mix(Theme.AccentInk, fill, 160) : Theme.Faint; border = primary ? Color.Empty : Theme.Border; }
            else if (primary) { fill = hover && !down ? Theme.AccentHover : Theme.Accent; fg = Theme.AccentInk; }
            else { fill = hover || down ? Theme.Panel2 : under; fg = Theme.Text; border = Theme.Border2; }
            using (GraphicsPath p = Theme.Round(r, Theme.S(10)))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                if (border != Color.Empty) using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
                if (Focused && ShowFocusCues) using (Pen pen = new Pen(Theme.AccentHover, Theme.S(2f))) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    // ------------------------------------------------------------------ seletor segmentado (substitui combos claros)
    public class Segmented : BufferedControl
    {
        string[] items = new string[0];
        int sel;
        int[] xs = new int[0];
        public event EventHandler SelectedIndexChanged;

        public Segmented(string[] items, int selected)
        {
            Cursor = Cursors.Hand;
            this.items = items;
            sel = selected;
            Height = Theme.S(32);
            int w = 0;
            foreach (string s in items) w += TextRenderer.MeasureText(s, Theme.Font(9f)).Width + Theme.S(26);
            Width = w + Theme.S(8);
        }

        public int SelectedIndex
        {
            get { return sel; }
            set { if (value != sel) { sel = value; Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); } }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            for (int i = 0; i < xs.Length - 1; i++)
                if (e.X >= xs[i] && e.X < xs[i + 1]) { SelectedIndex = i; break; }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (GraphicsPath p = Theme.Round(r, Theme.S(7)))
            using (SolidBrush b = new SolidBrush(Theme.Panel))
            using (Pen pen = new Pen(Theme.Border)) { g.FillPath(b, p); g.DrawPath(pen, p); }
            xs = new int[items.Length + 1];
            int x = Theme.S(4);
            for (int i = 0; i < items.Length; i++)
            {
                int w = TextRenderer.MeasureText(items[i], Theme.Font(9f)).Width + Theme.S(26);
                xs[i] = x;
                Rectangle seg = new Rectangle(x, Theme.S(3), w, Height - Theme.S(7));
                if (i == sel)
                    using (GraphicsPath sp = Theme.Round(seg, Theme.S(5)))
                    using (SolidBrush sb = new SolidBrush(Theme.AccentSoft)) g.FillPath(sb, sp);
                Theme.Text_(g, items[i], Theme.Font(9f, i == sel ? FontStyle.Bold : FontStyle.Regular), i == sel ? Theme.Text : Theme.Muted, seg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x += w;
            }
            xs[items.Length] = x;
        }
    }

    // ------------------------------------------------------------------ gráfico de linhas
    public class ChartSeries
    {
        public string Label;
        public int Field;
        public Color Color;
        public ChartSeries(string label, int field, Color color) { Label = label; Field = field; Color = color; }
    }

    public class LineChart : BufferedControl
    {
        public string Title = "";
        public string Unit = "";
        public double FixedMax = double.NaN;
        public double MinSpan = 10;
        public double WindowSeconds = 0;      // 0 = usa todo o intervalo dos dados
        public ChartSeries[] Series = new ChartSeries[0];
        public string EmptyText = "Sem dados";
        IList<Sample> data = new List<Sample>();
        int hoverX = -1;

        public void SetData(IList<Sample> d) { data = d ?? new List<Sample>(); Invalidate(); }

        protected override void OnMouseMove(MouseEventArgs e) { hoverX = e.X; Invalidate(); base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverX = -1; Invalidate(); base.OnMouseLeave(e); }

        Rectangle PlotRect()
        {
            return new Rectangle(Theme.S(42), Theme.S(52), Width - Theme.S(42) - Theme.S(12), Height - Theme.S(52) - Theme.S(22));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (GraphicsPath p = Theme.Round(r, Theme.S(8)))
            using (SolidBrush b = new SolidBrush(Theme.Panel))
            using (Pen pen = new Pen(Theme.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            int pad = Theme.S(12);
            Theme.Text_(g, Title, Theme.Font(9.5f, FontStyle.Bold), Theme.Text, new Rectangle(pad, Theme.S(8), Width / 2, Theme.S(18)), 0);

            Rectangle pr = PlotRect();
            if (pr.Width < 20 || pr.Height < 20) return;

            int n = data.Count;
            DateTime t1 = n > 0 ? data[n - 1].Time : DateTime.Now;
            DateTime t0 = WindowSeconds > 0 ? t1.AddSeconds(-WindowSeconds) : (n > 0 ? data[0].Time : t1.AddSeconds(-60));
            double span = Math.Max(1, (t1 - t0).TotalSeconds);

            // legenda com o último valor
            int lx = pad;
            int ly = Theme.S(26);
            foreach (ChartSeries s in Series)
            {
                double last = double.NaN;
                for (int i = n - 1; i >= 0 && i >= n - 5; i--)
                    if (!double.IsNaN(data[i].V[s.Field])) { last = data[i].V[s.Field]; break; }
                string txt = s.Label + "  " + (double.IsNaN(last) ? "—" : last.ToString(Math.Abs(last) >= 100 ? "0" : "0.#"));
                int w = TextRenderer.MeasureText(txt, Theme.Font(8f)).Width;
                if (lx + w + Theme.S(18) > Width - pad) break;
                using (SolidBrush b = new SolidBrush(s.Color)) g.FillEllipse(b, lx, ly + Theme.S(5), Theme.S(7), Theme.S(7));
                Theme.Text_(g, txt, Theme.Font(8f), Theme.Muted, new Rectangle(lx + Theme.S(11), ly, w + 4, Theme.S(16)), 0);
                lx += w + Theme.S(24);
            }

            // escala Y
            double ymin = 0, ymax = double.NaN;
            if (!double.IsNaN(FixedMax)) ymax = FixedMax;
            else
            {
                double mx = double.MinValue, mn = double.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if ((data[i].Time - t0).TotalSeconds < 0) continue;
                    foreach (ChartSeries s in Series)
                    {
                        double v = data[i].V[s.Field];
                        if (double.IsNaN(v)) continue;
                        if (v > mx) mx = v;
                        if (v < mn) mn = v;
                    }
                }
                if (mx == double.MinValue) { mx = MinSpan; mn = 0; }
                ymin = Math.Max(0, Math.Floor((mn - (mx - mn) * 0.15) / 5) * 5);
                if (Unit == "ms" || Unit == "%") ymin = 0;
                ymax = Math.Max(ymin + MinSpan, mx + (mx - mn) * 0.1);
                double step = NiceStep((ymax - ymin) / 4);
                ymax = Math.Ceiling(ymax / step) * step;
            }
            double yr = Math.Max(1e-9, ymax - ymin);

            // grade
            using (Pen gp = new Pen(Color.FromArgb(40, 255, 255, 255)))
            {
                int lines = 4;
                for (int i = 0; i <= lines; i++)
                {
                    float y = pr.Bottom - (float)i / lines * pr.Height;
                    g.DrawLine(gp, pr.Left, y, pr.Right, y);
                    double val = ymin + yr * i / lines;
                    Theme.Text_(g, val.ToString(yr < 8 ? "0.0" : "0"), Theme.Font(7.5f), Theme.Muted,
                        new Rectangle(0, (int)y - Theme.S(7), pr.Left - Theme.S(5), Theme.S(14)), TextFormatFlags.Right);
                }
            }
            Theme.Text_(g, t0.ToString("HH:mm:ss"), Theme.Font(7.5f), Theme.Muted, new Rectangle(pr.Left, pr.Bottom + Theme.S(4), Theme.S(80), Theme.S(14)), 0);
            Theme.Text_(g, t1.ToString("HH:mm:ss"), Theme.Font(7.5f), Theme.Muted, new Rectangle(pr.Right - Theme.S(80), pr.Bottom + Theme.S(4), Theme.S(80), Theme.S(14)), TextFormatFlags.Right);

            if (n < 2)
            {
                Theme.Text_(g, EmptyText, Theme.Font(9f), Theme.Muted, pr, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            // séries
            g.SetClip(pr);
            int stride = Math.Max(1, (int)Math.Ceiling(n / (double)(pr.Width * 1.0)));
            foreach (ChartSeries s in Series)
            {
                List<PointF> seg = new List<PointF>();
                using (Pen lp = new Pen(s.Color, Theme.S(1.6f)))
                {
                    lp.LineJoin = LineJoin.Round;
                    for (int i = 0; i < n; i += stride)
                    {
                        double sum = 0; int c = 0; int end = Math.Min(n, i + stride);
                        for (int k = i; k < end; k++)
                        {
                            double v = data[k].V[s.Field];
                            if (!double.IsNaN(v)) { sum += v; c++; }
                        }
                        double tsec = (data[i].Time - t0).TotalSeconds;
                        if (c == 0 || tsec < 0) { Flush(g, lp, seg); continue; }
                        double val = sum / c;
                        float x = pr.Left + (float)(tsec / span * pr.Width);
                        float y = pr.Bottom - (float)((val - ymin) / yr * pr.Height);
                        seg.Add(new PointF(x, Math.Max(pr.Top - 2, Math.Min(pr.Bottom + 2, y))));
                    }
                    Flush(g, lp, seg);
                }
            }
            g.ResetClip();

            // hover
            if (hoverX >= pr.Left && hoverX <= pr.Right)
            {
                double ts = (hoverX - pr.Left) / (double)pr.Width * span;
                DateTime th = t0.AddSeconds(ts);
                int best = -1; double bd = double.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    double dd = Math.Abs((data[i].Time - th).TotalSeconds);
                    if (dd < bd) { bd = dd; best = i; }
                }
                if (best >= 0 && bd < span / 4)
                {
                    float x = pr.Left + (float)((data[best].Time - t0).TotalSeconds / span * pr.Width);
                    using (Pen hp = new Pen(Color.FromArgb(120, 255, 255, 255))) g.DrawLine(hp, x, pr.Top, x, pr.Bottom);
                    List<string> lines = new List<string>();
                    lines.Add(data[best].Time.ToString("HH:mm:ss"));
                    foreach (ChartSeries s in Series)
                    {
                        double v = data[best].V[s.Field];
                        lines.Add(s.Label + ": " + (double.IsNaN(v) ? "—" : v.ToString("0.#") + " " + Unit));
                    }
                    int tw = 0;
                    foreach (string l in lines) tw = Math.Max(tw, TextRenderer.MeasureText(l, Theme.Font(8f)).Width);
                    int lh = Theme.S(15);
                    Rectangle box = new Rectangle((int)x + Theme.S(10), pr.Top + 4, tw + Theme.S(16), lines.Count * lh + Theme.S(8));
                    if (box.Right > Width - 4) box.X = (int)x - Theme.S(10) - box.Width;
                    using (GraphicsPath bp = Theme.Round(box, Theme.S(5)))
                    using (SolidBrush bb = new SolidBrush(Color.FromArgb(235, 10, 13, 18)))
                    using (Pen bpn = new Pen(Theme.Border))
                    {
                        g.FillPath(bb, bp);
                        g.DrawPath(bpn, bp);
                    }
                    for (int i = 0; i < lines.Count; i++)
                        Theme.Text_(g, lines[i], Theme.Font(8f), i == 0 ? Theme.Muted : Theme.Text,
                            new Rectangle(box.X + Theme.S(8), box.Y + Theme.S(4) + i * lh, box.Width, lh), 0);
                }
            }
        }

        static void Flush(Graphics g, Pen p, List<PointF> seg)
        {
            if (seg.Count >= 2) g.DrawLines(p, seg.ToArray());
            else if (seg.Count == 1) g.FillEllipse(new SolidBrush(p.Color), seg[0].X - 1.5f, seg[0].Y - 1.5f, 3, 3);
            seg.Clear();
        }

        static double NiceStep(double raw)
        {
            if (raw <= 0) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double n = raw / mag;
            double f = n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10;
            return f * mag;
        }
    }

    // ------------------------------------------------------------------ barras de suspeita por componente
    public class ScoreBars : BufferedControl
    {
        public double[] Scores = new double[8];
        public int RowH { get { return Theme.S(30); } }
        public ScoreBars() { Height = Theme.S(30) * 8 + Theme.S(16); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (GraphicsPath p = Theme.Round(r, Theme.S(8)))
            using (SolidBrush b = new SolidBrush(Theme.Panel))
            using (Pen pen = new Pen(Theme.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            int labelW = Math.Min(Theme.S(240), (int)(Width * 0.52));
            int pad = Theme.S(14);
            int rowH = RowH;
            int[] order = new int[8];
            for (int i = 0; i < 8; i++) order[i] = i;
            Array.Sort(order, delegate(int a, int b2) { return Scores[b2].CompareTo(Scores[a]); });
            for (int row = 0; row < 8; row++)
            {
                int i = order[row];
                int y = Theme.S(8) + row * rowH;
                double sc = Scores[i];
                Color c = Theme.ScoreColor(sc);
                Theme.Text_(g, CompInfo.Names[i], Theme.Font(9.5f, row == 0 && sc >= 20 ? FontStyle.Bold : FontStyle.Regular),
                    sc >= 20 ? Theme.Text : Theme.Muted, new Rectangle(pad, y, labelW, rowH), TextFormatFlags.VerticalCenter);
                RectangleF track = new RectangleF(pad + labelW, y + rowH / 2 - Theme.S(5), Width - pad * 2 - labelW - Theme.S(52), Theme.S(10));
                using (GraphicsPath tp = Theme.Round(track, track.Height / 2))
                using (SolidBrush tb = new SolidBrush(Theme.Panel2)) g.FillPath(tb, tp);
                float w = (float)(sc / 100.0) * track.Width;
                if (w > 2)
                {
                    RectangleF fill = new RectangleF(track.X, track.Y, w, track.Height);
                    using (GraphicsPath fp = Theme.Round(fill, track.Height / 2))
                    using (SolidBrush fb = new SolidBrush(c)) g.FillPath(fb, fp);
                }
                Theme.Text_(g, sc.ToString("0") + "%", Theme.Font(9.5f, FontStyle.Bold), c,
                    new Rectangle((int)track.Right + Theme.S(8), y, Theme.S(44), rowH), TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            }
        }
    }

    // ------------------------------------------------------------------ lista de achados (cartões)
    public class FindingList : Panel
    {
        readonly List<FindingCard> cards = new List<FindingCard>();

        public FindingList()
        {
            AutoScroll = true;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Theme.Dark(this);
        }

        public void SetFindings(List<Finding> list)
        {
            SuspendLayout();
            foreach (FindingCard c in cards) { Controls.Remove(c); c.Dispose(); }
            cards.Clear();
            foreach (Finding f in list)
            {
                FindingCard c = new FindingCard(f);
                c.ToggledDetails += delegate { Relayout(); };
                cards.Add(c);
                Controls.Add(c);
            }
            ResumeLayout();
            Relayout();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        void Relayout()
        {
            int w = ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Theme.S(6);
            if (w < 100) return;
            int y = 0;
            SuspendLayout();
            foreach (FindingCard c in cards)
            {
                int h = c.Measure(w);
                c.SetBounds(0, y + AutoScrollPosition.Y, w, h);
                y += h + Theme.S(10);
            }
            AutoScrollMinSize = new Size(0, y);
            ResumeLayout();
        }

        /// <summary>Rola até o achado e o destaca por alguns segundos.</summary>
        public bool Reveal(Finding f)
        {
            FindingCard c = cards.Find(delegate(FindingCard x) { return x.Finding == f; });
            if (c == null) return false;
            AutoScrollPosition = new Point(0, Math.Max(0, c.Top - AutoScrollPosition.Y - Theme.S(4)));
            c.Highlight = true; c.Invalidate();
            Timer t = new Timer();
            t.Interval = 2500;
            t.Tick += delegate { t.Stop(); t.Dispose(); if (!c.IsDisposed) { c.Highlight = false; c.Invalidate(); } };
            t.Start();
            return true;
        }
    }

    /// <summary>Achado em duas camadas: linguagem simples na frente, detalhes técnicos sob demanda.</summary>
    public class FindingCard : BufferedControl
    {
        readonly Finding f;
        const TextFormatFlags WB = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.Left;
        int hTitle, hAdv, hTech;
        Rectangle toggleRect = Rectangle.Empty;
        public bool Highlight, Expanded;
        public event EventHandler ToggledDetails;

        public FindingCard(Finding f) { this.f = f; AccessibleName = f.Plain; AccessibleDescription = f.Advice; }
        public Finding Finding { get { return f; } }

        bool HasTech { get { return f.Evidence.Length > 0 || f.Simple.Length > 0; } }
        string Tech
        {
            get
            {
                string t = (f.Simple.Length > 0 ? f.Title + "\n" : "") + f.Evidence;
                return (f.Source.Length > 0 ? "Fonte: " + f.Source + "\n" : "") + t.Trim();
            }
        }

        public static Color LabelColor(Finding f)
        {
            switch (Analyzer.Confidence(f))
            {
                case "COMPROVADO": return Theme.Bad;
                case "INDÍCIO": return Theme.Warn;
                case "OK": return Theme.Good;
                default: return Theme.Accent;
            }
        }

        static Color LabelText(Finding f)
        {
            switch (Analyzer.Confidence(f))
            {
                case "COMPROVADO": return Theme.BadText;
                case "INDÍCIO": return Theme.WarnText;
                case "OK": return Theme.GoodText;
                default: return Theme.AccentText;
            }
        }

        int InnerW(int width) { return width - Theme.S(40); }

        public int Measure(int width)
        {
            int iw = Math.Max(50, InnerW(width));
            hTitle = TextRenderer.MeasureText(f.Plain, Theme.Font(11f, FontStyle.Bold), new Size(iw, 0), WB).Height;
            hAdv = f.Advice.Length == 0 ? 0 : TextRenderer.MeasureText(f.Advice, Theme.Font(9.5f), new Size(iw, 0), WB).Height;
            hTech = Expanded && HasTech ? TextRenderer.MeasureText(Tech, Theme.Font(9f), new Size(iw, 0), WB).Height : 0;
            return Theme.S(16) + Theme.S(20) + Theme.S(10) + hTitle
                + (hAdv > 0 ? Theme.S(14) + Theme.S(18) + hAdv : 0)
                + (HasTech ? Theme.S(14) + Theme.S(40) + (hTech > 0 ? hTech + Theme.S(4) : 0) : Theme.S(4))
                + Theme.S(12);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = toggleRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (toggleRect.Contains(e.Location))
            {
                Expanded = !Expanded;
                if (ToggledDetails != null) ToggledDetails(this, EventArgs.Empty);
                Invalidate();
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            string conf = Analyzer.Confidence(f);
            Color bg = Highlight ? Theme.AccentSoft : Theme.Panel;
            Color border = Highlight ? Theme.Accent : conf == "COMPROVADO" ? Theme.Mix(Theme.Bad, Theme.Panel, 120) : conf == "INDÍCIO" && f.Sev != Sev.Info ? Theme.Mix(Theme.Warn, Theme.Panel, 90) : Theme.Border;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (GraphicsPath p = Theme.Round(r, Theme.S(14)))
            using (SolidBrush b = new SolidBrush(bg))
            using (Pen pen = new Pen(border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            int x = Theme.S(20), iw = InnerW(Width), y = Theme.S(16);
            int cw = Theme.TagPill(g, x, y, conf, LabelText(f), LabelColor(f), bg);
            if (f.Source.Length > 0)
                Theme.Text_(g, f.Source, Theme.Font(8.5f), Theme.Faint, new Rectangle(x + cw + Theme.S(10), y, iw - cw - Theme.S(10), Theme.S(20)), TextFormatFlags.VerticalCenter);
            y += Theme.S(20) + Theme.S(10);
            TextRenderer.DrawText(g, f.Plain, Theme.Font(11f, FontStyle.Bold), new Rectangle(x, y, iw, hTitle), Theme.Text, WB);
            y += hTitle;
            if (hAdv > 0)
            {
                y += Theme.S(14);
                Theme.Text_(g, "O QUE FAZER", Theme.Font(7.5f, FontStyle.Bold), Theme.AccentText, new Rectangle(x, y, iw, Theme.S(16)), 0);
                y += Theme.S(18);
                TextRenderer.DrawText(g, f.Advice, Theme.Font(9.5f), new Rectangle(x, y, iw, hAdv), Theme.Soft, WB);
                y += hAdv;
            }
            if (HasTech)
            {
                y += Theme.S(14);
                using (Pen sep = new Pen(Theme.Border)) g.DrawLine(sep, Theme.S(1), y, Width - Theme.S(2), y);
                toggleRect = new Rectangle(0, y, Width, Theme.S(40));
                string lbl = Expanded ? "Ocultar detalhes técnicos" : "Ver detalhes técnicos";
                Theme.Text_(g, lbl, Theme.Font(9f, FontStyle.Bold), Theme.Soft, new Rectangle(x, y, iw, Theme.S(40)), TextFormatFlags.VerticalCenter);
                float ic = Theme.S(16f);
                Icons.Draw(g, Expanded ? "chevup" : "chevdown", new RectangleF(Width - Theme.S(20) - ic, y + (Theme.S(40) - ic) / 2f, ic, ic), Theme.Muted, Theme.S(2f));
                y += Theme.S(40);
                if (hTech > 0)
                    TextRenderer.DrawText(g, Tech, Theme.Font(9f), new Rectangle(x, y, iw, hTech), Theme.Muted, WB);
            }
            else toggleRect = Rectangle.Empty;
        }
    }
}

namespace DiagIT
{
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Windows.Forms;

    /// <summary>Estado de cada componente (com defeito / precisa verificar / saudável / não verificado), clicável para ir ao teste ou ao achado que o explica.</summary>
    public class CompStateList : BufferedControl
    {
        public CompState[] States = new CompState[8];
        public event Action<int> RowClicked;
        int hover = -1;
        readonly ToolTip tip = new ToolTip();
        static readonly int[] Order = { (int)Comp.Cpu, (int)Comp.Gpu, (int)Comp.Ram, (int)Comp.Disk, (int)Comp.Psu, (int)Comp.Cooling, (int)Comp.Board, (int)Comp.Software };
        public static readonly string[] CompIcon = { "chip", "gpu", "ram", "ssd", "bolt", "thermo", "window", "board" };

        int RowH { get { return Math.Max(Theme.S(60), (Height - Theme.S(8)) / 8); } }

        int RowAt(int y)
        {
            int r = (y - Theme.S(4)) / RowH;
            return r >= 0 && r < 8 ? Order[r] : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int r = RowAt(e.Y);
            bool canClick = r >= 0 && States[r] != null && (States[r].HasTest || States[r].Cause != null);
            Cursor = canClick ? Cursors.Hand : Cursors.Default;
            if (r != hover)
            {
                hover = r; Invalidate();
                tip.SetToolTip(this, r >= 0 && States[r] != null ? Wrap(States[r].Detail, 90) : "");
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int r = RowAt(e.Y);
            if (r >= 0 && RowClicked != null) RowClicked(r);
            base.OnMouseClick(e);
        }

        static string Wrap(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int col = 0;
            foreach (string w in s.Split(' '))
            {
                if (col > 0 && col + w.Length > n) { sb.Append('\n'); col = 0; }
                else if (col > 0) { sb.Append(' '); col++; }
                sb.Append(w); col += w.Length;
            }
            return sb.ToString();
        }

        public static void Badge(CompStatus s, out string text, out Color color)
        {
            text = Analyzer.StatusLabel(s);
            color = Theme.StatusColor(s);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int rh = RowH;
            for (int row = 0; row < 8; row++)
            {
                int i = Order[row];
                CompState s = States[i];
                Rectangle r = new Rectangle(0, Theme.S(4) + row * rh, Width - 1, rh - Theme.S(8));
                Color bg = hover == i ? Theme.Panel2 : Theme.Panel;
                using (GraphicsPath p = Theme.Round(r, Theme.S(12)))
                using (SolidBrush b = new SolidBrush(bg))
                using (Pen pen = new Pen(s != null && s.Status == CompStatus.Suspect ? Theme.Mix(Theme.Warn, Theme.Panel, 80) : s != null && s.Status == CompStatus.Failed ? Theme.Mix(Theme.Bad, Theme.Panel, 110) : Theme.Border))
                {
                    g.FillPath(b, p);
                    g.DrawPath(pen, p);
                }
                float ic = Theme.S(22f);
                Icons.Draw(g, CompIcon[i], new RectangleF(r.X + Theme.S(14), r.Y + (r.Height - ic) / 2f, ic, ic), Theme.Soft, Theme.S(1.8f));
                CompStatus st = s == null ? CompStatus.Untested : s.Status;
                string label = Analyzer.StatusLabel(st);
                int pw = TextRenderer.MeasureText(label, Theme.Font(8.5f, FontStyle.Bold)).Width + Theme.S(40);
                Theme.StatusPill(g, r.Right - pw - Theme.S(10), r.Y + (r.Height - Theme.S(24)) / 2, st, label, bg);
                int x = r.X + Theme.S(50), tw = r.Right - pw - Theme.S(20) - x;
                Theme.Text_(g, CompInfo.Plain[i], Theme.Font(10f, FontStyle.Bold), Theme.Text, new Rectangle(x, r.Y + Theme.S(8), tw, Theme.S(20)), 0);
                string note = s == null ? "Analisando..." : Analyzer.PlainNote(s, i);
                TextRenderer.DrawText(g, note, Theme.Font(8.5f), new Rectangle(x, r.Y + Theme.S(28), tw, r.Height - Theme.S(30)), Theme.Muted,
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>Lista de fatos de hardware agrupados (aba Hardware).</summary>
    public class FactList : BufferedControl
    {
        List<Fact> facts = new List<Fact>();
        const TextFormatFlags WB = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        public void SetFacts(List<Fact> f) { facts = f ?? new List<Fact>(); Height = Measure(Width); Invalidate(); }

        int NameW(int w) { return Math.Min(Theme.S(300), w / 3); }

        public int Measure(int w)
        {
            int y = 0; string grp = null;
            int vw = w - NameW(w) - Theme.S(60);
            foreach (Fact f in facts)
            {
                if (f.Group != grp) { grp = f.Group; y += Theme.S(grp == facts[0].Group ? 4 : 18) + Theme.S(24); }
                int h = Math.Max(Theme.S(18), TextRenderer.MeasureText(f.Value, Theme.Font(9f), new Size(Math.Max(50, vw), 0), WB).Height);
                if (f.Note.Length > 0) h += TextRenderer.MeasureText(f.Note, Theme.Font(8.5f), new Size(Math.Max(50, vw), 0), WB).Height + Theme.S(2);
                y += h + Theme.S(8);
            }
            return y + Theme.S(10);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int h = Measure(Width);
            if (Math.Abs(h - Height) > 2) Height = h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int y = 0; string grp = null;
            int nw = NameW(Width), vx = nw + Theme.S(40), vw = Width - vx - Theme.S(20);
            foreach (Fact f in facts)
            {
                if (f.Group != grp)
                {
                    grp = f.Group;
                    y += Theme.S(y == 0 ? 4 : 18);
                    Theme.Text_(g, grp.ToUpper(), Theme.Font(8.5f, FontStyle.Bold), Theme.Accent, new Rectangle(0, y, Width, Theme.S(20)), 0);
                    y += Theme.S(24);
                }
                Color dot = f.Level == 2 ? Theme.Bad : f.Level == 1 ? Theme.Warn : f.Level == 0 ? Theme.Good : Theme.Border;
                using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, Theme.S(6), y + Theme.S(5), Theme.S(8), Theme.S(8));
                Theme.Text_(g, f.Name, Theme.Font(9f), Theme.Muted, new Rectangle(Theme.S(24), y, nw, Theme.S(18)), 0);
                int h = Math.Max(Theme.S(18), TextRenderer.MeasureText(f.Value, Theme.Font(9f), new Size(Math.Max(50, vw), 0), WB).Height);
                TextRenderer.DrawText(g, f.Value, Theme.Font(9f, f.Level >= 1 ? FontStyle.Bold : FontStyle.Regular), new Rectangle(vx, y, vw, h),
                    f.Level == 2 ? Theme.Bad : f.Level == 1 ? Theme.Warn : Theme.Text, WB);
                y += h;
                if (f.Note.Length > 0)
                {
                    int nh = TextRenderer.MeasureText(f.Note, Theme.Font(8.5f), new Size(Math.Max(50, vw), 0), WB).Height;
                    TextRenderer.DrawText(g, f.Note, Theme.Font(8.5f), new Rectangle(vx, y + Theme.S(2), vw, nh), Theme.Muted, WB);
                    y += nh + Theme.S(2);
                }
                y += Theme.S(8);
            }
        }
    }
}