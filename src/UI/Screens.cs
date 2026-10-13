using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DiagIT
{
    /// <summary>Controle que sabe a própria altura para uma largura (usado pelo FlowPanel).</summary>
    public interface IFlow { int Measure(int width); }

    /// <summary>Coluna rolável: empilha os filhos visíveis na ordem em que foram adicionados.</summary>
    public class FlowPanel : Panel
    {
        public int Gap = Theme.S(20);
        public int MaxContent = Theme.S(1100);

        public FlowPanel()
        {
            AutoScroll = true;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Theme.Dark(this);
        }

        // ordem própria: a coleção Controls segue a ordem z, que o WinForms muda quando um filho aparece
        readonly List<Control> order = new List<Control>();
        protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); if (!order.Contains(e.Control)) order.Add(e.Control); }
        protected override void OnControlRemoved(ControlEventArgs e) { base.OnControlRemoved(e); order.Remove(e.Control); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        public void Relayout()
        {
            // reserva sempre a largura da barra de rolagem: assim o conteúdo nunca passa da borda (sem barra horizontal)
            int w = Math.Min(MaxContent, Width - SystemInformation.VerticalScrollBarWidth - Theme.S(8));
            if (w < 100) return;
            SuspendLayout();
            int y = 0;
            foreach (Control c in order)
            {
                if (!c.Visible) continue;
                IFlow fl = c as IFlow;
                int h = fl != null ? fl.Measure(w) : c.Height;
                c.SetBounds(0, y + AutoScrollPosition.Y, w, h);
                y += h + Gap;
            }
            AutoScrollMinSize = new Size(0, Math.Max(0, y - Gap + Theme.S(24)));
            ResumeLayout();
            Invalidate(true);
        }
    }

    /// <summary>Base para controles desenhados que ocupam um cartão.</summary>
    public class FlowControl : BufferedControl, IFlow
    {
        protected const TextFormatFlags WB = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.Left;
        public virtual int Measure(int width) { return Height; }
        protected static int H(string s, Font f, int w) { return string.IsNullOrEmpty(s) ? 0 : TextRenderer.MeasureText(s, f, new Size(Math.Max(20, w), 0), WB).Height; }
        protected static void Card(Graphics g, Rectangle r, Color fill, Color border, int radius)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Theme.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1.5f, r.Height - 1.5f), radius))
            using (SolidBrush b = new SolidBrush(fill))
            {
                g.FillPath(b, p);
                if (border != Color.Empty) using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }
        }
    }

    // ------------------------------------------------------------------ marca na barra lateral
    public class BrandMark : BufferedControl
    {
        public BrandMark() { BackColor = Theme.Side; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Side);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int s = Theme.S(38), x = Theme.S(20), y = (Height - s) / 2;
            using (GraphicsPath p = Theme.Round(new RectangleF(x, y, s, s), Theme.S(11)))
            using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
            float ic = Theme.S(24f);
            Icons.Draw(g, "pulse", new RectangleF(x + (s - ic) / 2f, y + (s - ic) / 2f, ic, ic), Theme.AccentInk, Theme.S(2.6f));
            int tx = x + s + Theme.S(12);
            Theme.Text_(g, "DiagIT", Theme.Display(15f), Theme.Text, new Rectangle(tx, y - Theme.S(3), Width - tx, Theme.S(28)), 0);
            Theme.Text_(g, "check-up do seu PC", Theme.Font(8.5f), Theme.Faint, new Rectangle(tx, y + Theme.S(23), Width - tx, Theme.S(18)), 0);
        }
    }

    /// <summary>Cartão de status no rodapé da barra lateral (monitorando / gravando / testando).</summary>
    public class SideStatus : BufferedControl
    {
        public Color Dot = Theme.Good;
        public string Title = "", Sub = "";
        public SideStatus() { BackColor = Theme.Side; Cursor = Cursors.Hand; }
        public void Set(Color dot, string title, string sub)
        {
            if (dot == Dot && title == Title && sub == Sub) return;
            Dot = dot; Title = title; Sub = sub; AccessibleName = title + ". " + sub; Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Side);
            Rectangle r = new Rectangle(Theme.S(12), 0, Width - Theme.S(24), Height - 1);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Theme.Round(r, Theme.S(12)))
            using (SolidBrush b = new SolidBrush(Theme.Panel3))
            using (Pen pen = new Pen(Theme.Border)) { g.FillPath(b, p); g.DrawPath(pen, p); }
            int d = Theme.S(10);
            using (SolidBrush b = new SolidBrush(Dot)) g.FillEllipse(b, r.X + Theme.S(14), r.Y + Theme.S(17), d, d);
            int x = r.X + Theme.S(34), w = r.Right - x - Theme.S(10);
            Theme.Text_(g, Title, Theme.Font(9f, FontStyle.Bold), Theme.Text, new Rectangle(x, r.Y + Theme.S(11), w, Theme.S(20)), 0);
            TextRenderer.DrawText(g, Sub, Theme.Font(8.5f), new Rectangle(x, r.Y + Theme.S(31), w, r.Height - Theme.S(34)), Theme.Faint,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    // ------------------------------------------------------------------ título de seção (com legenda opcional)
    public class SectionTitle : FlowControl
    {
        public string Text_ = "";
        public bool Legend;
        public SectionTitle(string text, bool legend) { Text_ = text; Legend = legend; AccessibleName = text; }
        public override int Measure(int width) { return Theme.S(30); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            Theme.Text_(g, Text_, Theme.Display(13f), Theme.Text, new Rectangle(0, 0, Width, Height), TextFormatFlags.VerticalCenter);
            if (!Legend) return;
            CompStatus[] sts = { CompStatus.Ok, CompStatus.Suspect, CompStatus.Failed, CompStatus.Untested };
            int x = Width;
            for (int i = sts.Length - 1; i >= 0; i--)
            {
                string t = Analyzer.StatusLabel(sts[i]);
                int tw = TextRenderer.MeasureText(t, Theme.Font(8.5f)).Width;
                x -= tw + Theme.S(20);
                if (x < TextRenderer.MeasureText(Text_, Theme.Display(13f)).Width + Theme.S(20)) break;
                float ic = Theme.S(14f);
                Icons.Draw(g, Theme.StatusIcon(sts[i]), new RectangleF(x, (Height - ic) / 2f, ic, ic), Theme.StatusText(sts[i]), Theme.S(2.2f));
                Theme.Text_(g, t, Theme.Font(8.5f), Theme.Muted, new Rectangle(x + Theme.S(18), 0, tw + Theme.S(4), Height), TextFormatFlags.VerticalCenter);
            }
        }
    }

    /// <summary>Cabeçalho de etapa: "PASSO 1 DE 3", título e explicação.</summary>
    public class StepHeader : FlowControl
    {
        public string Kicker = "", Title = "", Sub = "";
        int hT, hS;
        public void Set(string kicker, string title, string sub) { Kicker = kicker; Title = title; Sub = sub; AccessibleName = title; Invalidate(); }
        public override int Measure(int width)
        {
            hT = H(Title, Theme.Display(20f), width);
            hS = H(Sub, Theme.Font(11f), Math.Min(width, Theme.S(720)));
            return (Kicker.Length > 0 ? Theme.S(24) : 0) + hT + (hS > 0 ? Theme.S(8) + hS : 0);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            int y = 0;
            if (Kicker.Length > 0) { Theme.Text_(g, Kicker, Theme.Font(8.5f, FontStyle.Bold), Theme.AccentText, new Rectangle(0, 0, Width, Theme.S(20)), 0); y += Theme.S(24); }
            TextRenderer.DrawText(g, Title, Theme.Display(20f), new Rectangle(0, y, Width, hT), Theme.Text, WB);
            y += hT + Theme.S(8);
            if (hS > 0) TextRenderer.DrawText(g, Sub, Theme.Font(11f), new Rectangle(0, y, Math.Min(Width, Theme.S(720)), hS), Theme.Soft, WB);
        }
    }

    /// <summary>Link de texto com ícone (Voltar, Agora não...).</summary>
    public class LinkButton : FlowControl
    {
        public string Caption, Icon;
        bool hover;
        public LinkButton(string caption, string icon)
        {
            Caption = caption; Icon = icon; Cursor = Cursors.Hand; AccessibleName = caption; AccessibleRole = AccessibleRole.Link;
            SetStyle(ControlStyles.Selectable, true); TabStop = true;
            Height = Theme.S(32);
        }
        public override int Measure(int width) { return Theme.S(32); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Color c = hover ? Theme.Text : Theme.AccentHover;
            int x = 0;
            if (!string.IsNullOrEmpty(Icon))
            {
                float ic = Theme.S(16f);
                Icons.Draw(g, Icon, new RectangleF(0, (Height - ic) / 2f, ic, ic), c, Theme.S(2f));
                x = Theme.S(22);
            }
            Font f = Theme.Font(9.5f, hover ? FontStyle.Underline : FontStyle.Regular);
            Theme.Text_(g, Caption, f, c, new Rectangle(x, 0, Width - x, Height), TextFormatFlags.VerticalCenter);
            if (Focused && ShowFocusCues)
                using (Pen p = new Pen(Theme.AccentHover)) { p.DashStyle = DashStyle.Dot; g.DrawRectangle(p, 0, 0, Math.Min(Width - 1, x + TextRenderer.MeasureText(Caption, f).Width + 2), Height - 1); }
        }
        public int PreferredWidth { get { return (string.IsNullOrEmpty(Icon) ? 0 : Theme.S(22)) + TextRenderer.MeasureText(Caption, Theme.Font(9.5f)).Width + Theme.S(6); } }
    }

    /// <summary>Linha de botões alinhados à esquerda.</summary>
    public class ButtonRow : Panel, IFlow
    {
        public ButtonRow() { BackColor = Theme.Bg; }
        public int Measure(int width)
        {
            int x = 0;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                LinkButton lb = c as LinkButton;
                int w = lb != null ? lb.PreferredWidth : c.Width;
                c.SetBounds(x, (Theme.S(46) - c.Height) / 2, w, c.Height);
                x += w + Theme.S(14);
            }
            return Theme.S(46);
        }
    }

    // ------------------------------------------------------------------ veredito com o anel das 8 peças
    public class HeroCard : FlowControl
    {
        public CompStatus[] Status = new CompStatus[8];
        public string Meta = "", Title = "", Sub = "", Basis = "";
        public readonly List<Control> Buttons = new List<Control>();
        int hT, hS, hB, ring;
        public HeroCard() { BackColor = Theme.Panel; }

        public void AddButton(Control b) { Buttons.Add(b); Controls.Add(b); }

        public override int Measure(int width)
        {
            ring = Theme.S(172);
            bool stacked = width < Theme.S(620);
            int tw = stacked ? width - Theme.S(56) : width - ring - Theme.S(56) - Theme.S(32);
            hT = H(Title, Theme.Display(18f), tw);
            hS = H(Sub, Theme.Font(11f), tw);
            hB = H(Basis, Theme.Font(8.5f), tw);
            int textH = Theme.S(20) + Theme.S(10) + hT + Theme.S(12) + hS + Theme.S(18) + Theme.S(42) + Theme.S(12) + hB;
            int tx = stacked ? Theme.S(28) : Theme.S(28) + ring + Theme.S(32);
            int top = stacked ? Theme.S(28) + ring + Theme.S(20) : Theme.S(28);
            int by = top + Theme.S(30) + hT + Theme.S(12) + hS + Theme.S(18);
            int bx = tx;
            foreach (Control b in Buttons)
            {
                if (!b.Visible) continue;
                if (bx + b.Width > width - Theme.S(28) && bx > tx) { bx = tx; by += b.Height + Theme.S(10); }
                b.Location = new Point(bx, by);
                bx += b.Width + Theme.S(10);
            }
            int h = stacked ? top + textH + Theme.S(28) : Math.Max(ring + Theme.S(56), textH + Theme.S(56));
            return h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            Card(g, ClientRectangle, Theme.Panel, Theme.Border, Theme.S(20));
            bool stacked = Width < Theme.S(620);
            int rx = Theme.S(28), ry = stacked ? Theme.S(28) : (Height - ring) / 2;
            // anel: 8 segmentos, um por peça, na cor do estado
            float thick = Theme.S(15f);
            RectangleF rr = new RectangleF(rx + thick / 2, ry + thick / 2, ring - thick, ring - thick);
            int ok = 0;
            for (int i = 0; i < 8; i++)
            {
                CompStatus st = Status[i];
                if (st == CompStatus.Ok) ok++;
                Color c = st == CompStatus.Untested ? Theme.None : Theme.StatusColor(st);
                using (Pen p = new Pen(c, thick)) g.DrawArc(p, rr, -90 + i * 45 + 2.5f, 40);
            }
            Theme.Text_(g, ok + " / 8", Theme.Display(22f), Theme.Text, new Rectangle(rx, ry + ring / 2 - Theme.S(30), ring, Theme.S(40)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.Text_(g, "peças saudáveis", Theme.Font(8.5f), Theme.Muted, new Rectangle(rx, ry + ring / 2 + Theme.S(10), ring, Theme.S(18)), TextFormatFlags.HorizontalCenter);
            int tx = stacked ? Theme.S(28) : rx + ring + Theme.S(32), tw = Width - tx - Theme.S(28);
            int y = stacked ? ry + ring + Theme.S(20) : Theme.S(28);
            Theme.Text_(g, Meta, Theme.Font(8.5f), Theme.Muted, new Rectangle(tx, y, tw, Theme.S(20)), 0);
            y += Theme.S(30);
            TextRenderer.DrawText(g, Title, Theme.Display(18f), new Rectangle(tx, y, tw, hT), Theme.Text, WB);
            y += hT + Theme.S(12);
            TextRenderer.DrawText(g, Sub, Theme.Font(11f), new Rectangle(tx, y, tw, hS), Theme.Soft, WB);
            int by = 0;
            foreach (Control b in Buttons) if (b.Visible) by = Math.Max(by, b.Bottom);
            if (by == 0) by = y + hS;
            TextRenderer.DrawText(g, Basis, Theme.Font(8.5f), new Rectangle(tx, by + Theme.S(12), tw, hB), Theme.Faint, WB);
        }
    }

    // ------------------------------------------------------------------ grade com as 8 peças
    public class PartsGrid : FlowControl
    {
        public CompState[] States = new CompState[8];
        public string[] Models = new string[8];
        public event Action<int> TileClicked;
        static readonly int[] Order = { (int)Comp.Cpu, (int)Comp.Gpu, (int)Comp.Ram, (int)Comp.Disk, (int)Comp.Psu, (int)Comp.Cooling, (int)Comp.Software, (int)Comp.Board };
        readonly Rectangle[] rects = new Rectangle[8];
        int hover = -1, cols = 4;
        readonly ToolTip tip = new ToolTip();

        public override int Measure(int width)
        {
            int gap = Theme.S(12);
            cols = Math.Max(1, Math.Min(4, (width + gap) / (Theme.S(220) + gap)));
            int rows = (8 + cols - 1) / cols;
            int th = Theme.S(138);
            return rows * th + (rows - 1) * gap;
        }

        int At(Point p) { for (int i = 0; i < 8; i++) if (rects[i].Contains(p)) return Order[i]; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = At(e.Location);
            Cursor = i >= 0 && States[i] != null && (States[i].Cause != null || States[i].Status != CompStatus.Untested) ? Cursors.Hand : Cursors.Default;
            if (i != hover) { hover = i; Invalidate(); tip.SetToolTip(this, i >= 0 && States[i] != null ? States[i].Detail : ""); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = At(e.Location);
            if (i >= 0 && TileClicked != null) TileClicked(i);
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            int gap = Theme.S(12), tw = (Width - (cols - 1) * gap) / cols, th = Theme.S(138);
            for (int k = 0; k < 8; k++)
            {
                int i = Order[k];
                CompState s = States[i];
                CompStatus st = s == null ? CompStatus.Untested : s.Status;
                Rectangle r = new Rectangle((k % cols) * (tw + gap), (k / cols) * (th + gap), tw, th);
                rects[k] = r;
                bool warm = st == CompStatus.Suspect, bad = st == CompStatus.Failed;
                Color bg = bad ? Theme.Mix(Theme.Bad, Theme.Panel, 22) : warm ? Theme.Mix(Theme.Warn, Theme.Panel, 18) : hover == i ? Theme.Panel2 : Theme.Panel;
                Color border = bad ? Theme.Mix(Theme.Bad, Theme.Panel, 120) : warm ? Theme.Mix(Theme.Warn, Theme.Panel, 100) : hover == i ? Theme.Border2 : Theme.Border;
                Card(g, r, bg, border, Theme.S(14));
                float ic = Theme.S(26f);
                Icons.Draw(g, CompStateList.CompIcon[i], new RectangleF(r.X + Theme.S(16), r.Y + Theme.S(16), ic, ic), Theme.Soft, Theme.S(1.8f));
                int x = r.X + Theme.S(52), w = r.Right - x - Theme.S(12);
                Theme.Text_(g, CompInfo.Plain[i], Theme.Font(10f, FontStyle.Bold), Theme.Text, new Rectangle(x, r.Y + Theme.S(13), w, Theme.S(20)), 0);
                Theme.Text_(g, string.IsNullOrEmpty(Models[i]) ? CompInfo.Role[i] : Models[i], Theme.Font(8.5f), Theme.Faint, new Rectangle(x, r.Y + Theme.S(33), w, Theme.S(18)), 0);
                Theme.StatusPill(g, r.X + Theme.S(16), r.Y + Theme.S(58), st, Analyzer.StatusLabel(st), bg);
                string note = s == null ? "Analisando..." : Analyzer.PlainNote(s, i);
                TextRenderer.DrawText(g, note, Theme.Font(9f), new Rectangle(r.X + Theme.S(16), r.Y + Theme.S(92), r.Width - Theme.S(28), r.Height - Theme.S(98)),
                    warm || bad ? Theme.Soft : Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }
        }
    }

    // ------------------------------------------------------------------ passo numerado com botão (O que fazer agora)
    public class StepRow : FlowControl
    {
        public int Number;
        public string Title = "", Sub = "", Duration = "";
        public bool Primary;
        public FlatButton Button;
        int hT, hS;
        public StepRow(int n, string title, string sub, string button, bool primary)
        {
            Number = n; Title = title; Sub = sub; Primary = primary; BackColor = Theme.Panel; AccessibleName = title;
            if (!string.IsNullOrEmpty(button)) { Button = new FlatButton(button, primary); Controls.Add(Button); }
        }
        public override int Measure(int width)
        {
            int bw = Button != null ? Button.Width + Theme.S(16) : 0;
            int tw = width - Theme.S(70) - bw - Theme.S(18);
            hT = H(Title, Theme.Font(10.5f, FontStyle.Bold), tw);
            hS = H(Sub, Theme.Font(9.5f), tw);
            int h = Math.Max(Theme.S(72), Theme.S(32) + hT + Theme.S(4) + hS);
            if (Button != null) Button.Location = new Point(width - Button.Width - Theme.S(18), (h - Button.Height) / 2);
            return h;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            Card(g, ClientRectangle, Theme.Panel, Theme.Border, Theme.S(14));
            int d = Theme.S(32), cy = (Height - d) / 2;
            using (SolidBrush b = new SolidBrush(Primary ? Theme.AccentSoft : Theme.Panel2)) g.FillEllipse(b, Theme.S(18), cy, d, d);
            Theme.Text_(g, Number.ToString(), Theme.Font(10f, FontStyle.Bold), Primary ? Theme.AccentText : Theme.Muted, new Rectangle(Theme.S(18), cy, d, d),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int x = Theme.S(70), bw = Button != null ? Button.Width + Theme.S(16) : 0, tw = Width - x - bw - Theme.S(18);
            int y = (Height - (hT + Theme.S(4) + hS)) / 2;
            TextRenderer.DrawText(g, Title, Theme.Font(10.5f, FontStyle.Bold), new Rectangle(x, y, tw, hT), Theme.Text, WB);
            TextRenderer.DrawText(g, Sub, Theme.Font(9.5f), new Rectangle(x, y + hT + Theme.S(4), tw, hS), Theme.Muted, WB);
        }
    }

    // ------------------------------------------------------------------ sintomas (escolha única)
    public class SymptomGrid : FlowControl
    {
        public class Item { public string Key, Icon, Title, Desc, Badge; public Color Tint; }
        public readonly List<Item> Items = new List<Item>();
        public int Selected = -1;
        public event EventHandler SelectionChanged;
        readonly List<Rectangle> rects = new List<Rectangle>();
        int hover = -1, cols = 3, th;

        public SymptomGrid() { SetStyle(ControlStyles.Selectable, true); TabStop = true; AccessibleRole = AccessibleRole.List; AccessibleName = "Sintomas"; }

        public override int Measure(int width)
        {
            int gap = Theme.S(12);
            cols = Math.Max(1, Math.Min(3, (width + gap) / (Theme.S(280) + gap)));
            int tw = (width - (cols - 1) * gap) / cols;
            th = Theme.S(118);
            foreach (Item it in Items)
                th = Math.Max(th, Theme.S(20) + Theme.S(24) + H(it.Desc, Theme.Font(9.5f), tw - Theme.S(110)) + (string.IsNullOrEmpty(it.Badge) ? 0 : Theme.S(32)) + Theme.S(22));
            int rows = (Items.Count + cols - 1) / cols;
            return rows * th + Math.Max(0, rows - 1) * gap;
        }

        int At(Point p) { for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) return i; return -1; }
        void Pick(int i)
        {
            if (i < 0 || i >= Items.Count || i == Selected) return;
            Selected = i; AccessibleDescription = Items[i].Title; Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }
        protected override void OnMouseMove(MouseEventArgs e) { int i = At(e.Location); Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; if (i != hover) { hover = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { Focus(); Pick(At(e.Location)); base.OnMouseClick(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int s = Math.Max(0, Selected);
            if (e.KeyCode == Keys.Right) Pick(Math.Min(Items.Count - 1, s + 1));
            else if (e.KeyCode == Keys.Left) Pick(Math.Max(0, s - 1));
            else if (e.KeyCode == Keys.Down) Pick(Math.Min(Items.Count - 1, s + cols));
            else if (e.KeyCode == Keys.Up) Pick(Math.Max(0, s - cols));
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            rects.Clear();
            int gap = Theme.S(12), tw = (Width - (cols - 1) * gap) / cols;
            for (int i = 0; i < Items.Count; i++)
            {
                Item it = Items[i];
                Rectangle r = new Rectangle((i % cols) * (tw + gap), (i / cols) * (th + gap), tw, th);
                rects.Add(r);
                bool sel = i == Selected;
                Color bg = sel ? Theme.Mix(Theme.Accent, Theme.Panel, 26) : hover == i ? Theme.Panel2 : Theme.Panel;
                Card(g, r, bg, sel ? Theme.Accent : Theme.Border, Theme.S(16));
                if (sel)
                    using (GraphicsPath p = Theme.Round(new RectangleF(r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3), Theme.S(15)))
                    using (Pen pen = new Pen(Theme.Accent, Theme.S(1.5f))) g.DrawPath(pen, p);
                if (sel && Focused && ShowFocusCues)
                    using (GraphicsPath p = Theme.Round(new RectangleF(r.X - 3, r.Y - 3, r.Width + 5, r.Height + 5), Theme.S(18)))
                    using (Pen pen = new Pen(Theme.AccentHover)) { pen.DashStyle = DashStyle.Dot; g.DrawPath(pen, p); }
                int s = Theme.S(44);
                Rectangle ib = new Rectangle(r.X + Theme.S(18), r.Y + Theme.S(18), s, s);
                using (GraphicsPath p = Theme.Round(ib, Theme.S(12)))
                using (SolidBrush b = new SolidBrush(Theme.Mix(it.Tint, Theme.Panel, 50))) g.FillPath(b, p);
                float ic = Theme.S(24f);
                Icons.Draw(g, it.Icon, new RectangleF(ib.X + (s - ic) / 2f, ib.Y + (s - ic) / 2f, ic, ic), it.Tint, Theme.S(2f));
                int x = ib.Right + Theme.S(14), w = r.Right - x - Theme.S(44);
                Theme.Text_(g, it.Title, Theme.Font(10.5f, FontStyle.Bold), Theme.Text, new Rectangle(x, r.Y + Theme.S(18), w, Theme.S(22)), 0);
                int hd = H(it.Desc, Theme.Font(9.5f), w);
                TextRenderer.DrawText(g, it.Desc, Theme.Font(9.5f), new Rectangle(x, r.Y + Theme.S(44), w, hd), Theme.Muted, WB);
                if (!string.IsNullOrEmpty(it.Badge))
                    Theme.TagPill(g, x, r.Y + Theme.S(52) + hd, it.Badge, Theme.WarnText, Theme.Warn, bg);
                // botão de opção
                int rd = Theme.S(20);
                Rectangle rb = new Rectangle(r.Right - Theme.S(18) - rd, r.Y + Theme.S(20), rd, rd);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (sel)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, rb);
                    int inner = Theme.S(8);
                    using (SolidBrush b = new SolidBrush(bg)) g.FillEllipse(b, rb.X + (rd - inner) / 2, rb.Y + (rd - inner) / 2, inner, inner);
                }
                else using (Pen p = new Pen(Theme.Border2, Theme.S(2f))) g.DrawEllipse(p, rb);
            }
        }
    }

    // ------------------------------------------------------------------ cartão com lista de itens (O que já sabemos / Antes de começar)
    public class ListCard : FlowControl
    {
        public class Row { public CompStatus Kind; public string Icon, Bold, Text; }
        public string Title = "", Callout = "";
        public readonly List<Row> Rows = new List<Row>();
        int[] hr = new int[0];
        int hC;
        public ListCard(string title) { Title = title; AccessibleName = title; }
        public void Add(CompStatus kind, string icon, string bold, string text) { Row r = new Row(); r.Kind = kind; r.Icon = icon; r.Bold = bold; r.Text = text; Rows.Add(r); }
        string Line(Row r) { return (string.IsNullOrEmpty(r.Bold) ? "" : r.Bold) + (string.IsNullOrEmpty(r.Text) ? "" : (string.IsNullOrEmpty(r.Bold) ? "" : "\n") + r.Text); }
        public override int Measure(int width)
        {
            int tw = width - Theme.S(44) - Theme.S(40);
            hr = new int[Rows.Count];
            int h = Theme.S(22) + Theme.S(26) + Theme.S(12);
            for (int i = 0; i < Rows.Count; i++)
            {
                hr[i] = Math.Max(Theme.S(28), H(Rows[i].Bold, Theme.Font(10f, FontStyle.Bold), tw) + H(Rows[i].Text, Theme.Font(9.5f), tw));
                h += hr[i] + Theme.S(14);
            }
            hC = H(Callout, Theme.Font(9.5f), width - Theme.S(44) - Theme.S(56));
            if (hC > 0) h += hC + Theme.S(28) + Theme.S(4);
            return h + Theme.S(8);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            Card(g, ClientRectangle, Theme.Panel, Theme.Border, Theme.S(18));
            int x = Theme.S(22), y = Theme.S(22);
            Theme.Text_(g, Title, Theme.Display(12f), Theme.Text, new Rectangle(x, y, Width - x * 2, Theme.S(26)), 0);
            y += Theme.S(26) + Theme.S(12);
            int tw = Width - x * 2 - Theme.S(40);
            for (int i = 0; i < Rows.Count && i < hr.Length; i++)
            {
                Row r = Rows[i];
                int s = Theme.S(28);
                Rectangle ib = new Rectangle(x, y, s, s);
                Color tint = r.Kind == CompStatus.Untested && r.Icon == "info" ? Theme.Faint : Theme.StatusColor(r.Kind);
                using (GraphicsPath p = Theme.Round(ib, Theme.S(8)))
                using (SolidBrush b = new SolidBrush(Theme.Mix(tint, Theme.Panel, 40))) g.FillPath(b, p);
                float ic = Theme.S(16f);
                Icons.Draw(g, string.IsNullOrEmpty(r.Icon) ? Theme.StatusIcon(r.Kind) : r.Icon, new RectangleF(ib.X + (s - ic) / 2f, ib.Y + (s - ic) / 2f, ic, ic),
                    r.Kind == CompStatus.Untested ? Theme.NoneText : Theme.StatusText(r.Kind), Theme.S(2.4f));
                int tx = x + s + Theme.S(12), hb = H(r.Bold, Theme.Font(10f, FontStyle.Bold), tw);
                if (hb > 0) TextRenderer.DrawText(g, r.Bold, Theme.Font(10f, FontStyle.Bold), new Rectangle(tx, y + Theme.S(2), tw, hb), Theme.Text, WB);
                TextRenderer.DrawText(g, r.Text, Theme.Font(9.5f), new Rectangle(tx, y + Theme.S(2) + hb, tw, hr[i]), hb > 0 ? Theme.Muted : Theme.Soft, WB);
                y += hr[i] + Theme.S(14);
            }
            if (hC > 0)
            {
                Rectangle cr = new Rectangle(x, y + Theme.S(4), Width - x * 2, hC + Theme.S(28));
                Card(g, cr, Theme.Mix(Theme.Accent, Theme.Panel, 22), Color.Empty, Theme.S(12));
                float ic = Theme.S(20f);
                Icons.Draw(g, "shield", new RectangleF(cr.X + Theme.S(14), cr.Y + Theme.S(13), ic, ic), Theme.AccentHover, Theme.S(2f));
                TextRenderer.DrawText(g, Callout, Theme.Font(9.5f), new Rectangle(cr.X + Theme.S(44), cr.Y + Theme.S(14), cr.Width - Theme.S(56), hC), Theme.Soft, WB);
            }
        }
    }

    /// <summary>"O que vamos fazer": passos numerados com duração e total.</summary>
    public class PlanCard : FlowControl
    {
        public class Step { public string Title, Desc, Duration; }
        public readonly List<Step> Steps = new List<Step>();
        public string Total = "", Empty = "";
        int[] hs = new int[0];
        int hE;
        public PlanCard() { AccessibleName = "O que vamos fazer"; }
        public override int Measure(int width)
        {
            int tw = width - Theme.S(44) - Theme.S(32) - Theme.S(90);
            hs = new int[Steps.Count];
            int h = Theme.S(22) + Theme.S(26) + Theme.S(14);
            for (int i = 0; i < Steps.Count; i++)
            {
                hs[i] = Math.Max(Theme.S(56), Theme.S(28) + H(Steps[i].Title, Theme.Font(10f, FontStyle.Bold), tw) + H(Steps[i].Desc, Theme.Font(9f), tw));
                h += hs[i] + Theme.S(10);
            }
            hE = Steps.Count == 0 ? H(Empty, Theme.Font(10f), width - Theme.S(44)) : 0;
            return h + hE + Theme.S(12);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            Card(g, ClientRectangle, Theme.Panel, Theme.Border, Theme.S(18));
            int x = Theme.S(22), y = Theme.S(22), w = Width - x * 2;
            Theme.Text_(g, "O que vamos fazer", Theme.Display(12f), Theme.Text, new Rectangle(x, y, w, Theme.S(26)), 0);
            if (Total.Length > 0) Theme.Text_(g, Total, Theme.Font(9.5f, FontStyle.Bold), Theme.Soft, new Rectangle(x, y, w, Theme.S(26)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            y += Theme.S(26) + Theme.S(14);
            if (Steps.Count == 0) { TextRenderer.DrawText(g, Empty, Theme.Font(10f), new Rectangle(x, y, w, hE), Theme.Soft, WB); return; }
            for (int i = 0; i < Steps.Count && i < hs.Length; i++)
            {
                Rectangle r = new Rectangle(x, y, w, hs[i]);
                Card(g, r, Theme.Panel2, Color.Empty, Theme.S(12));
                int d = Theme.S(28);
                using (SolidBrush b = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(b, r.X + Theme.S(14), r.Y + Theme.S(14), d, d);
                Theme.Text_(g, (i + 1).ToString(), Theme.Font(9.5f, FontStyle.Bold), Theme.AccentText, new Rectangle(r.X + Theme.S(14), r.Y + Theme.S(14), d, d),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                int tx = r.X + Theme.S(56), tw = r.Width - Theme.S(56) - Theme.S(90);
                int ht = H(Steps[i].Title, Theme.Font(10f, FontStyle.Bold), tw);
                TextRenderer.DrawText(g, Steps[i].Title, Theme.Font(10f, FontStyle.Bold), new Rectangle(tx, r.Y + Theme.S(14), tw, ht), Theme.Text, WB);
                TextRenderer.DrawText(g, Steps[i].Desc, Theme.Font(9f), new Rectangle(tx, r.Y + Theme.S(16) + ht, tw, r.Height - ht - Theme.S(16)), Theme.Muted, WB);
                Theme.Text_(g, Steps[i].Duration, Theme.Font(9f), Theme.Muted, new Rectangle(r.Right - Theme.S(86), r.Y + Theme.S(14), Theme.S(72), Theme.S(20)), TextFormatFlags.Right);
                y += hs[i] + Theme.S(10);
            }
        }
    }

    /// <summary>Dois cartões lado a lado (empilha em telas estreitas).</summary>
    public class TwoColumns : Panel, IFlow
    {
        public FlowControl A, B;
        public float Ratio = 0.42f;
        public TwoColumns(FlowControl a, FlowControl b) { A = a; B = b; BackColor = Theme.Bg; Controls.Add(a); Controls.Add(b); }
        public int Measure(int width)
        {
            int gap = Theme.S(16);
            if (width < Theme.S(760))
            {
                int ha = A.Measure(width), hb = B.Measure(width);
                A.SetBounds(0, 0, width, ha); B.SetBounds(0, ha + gap, width, hb);
                return ha + gap + hb;
            }
            int wa = (int)((width - gap) * Ratio), wb = width - gap - wa;
            int h = Math.Max(A.Measure(wa), B.Measure(wb));
            A.SetBounds(0, 0, wa, h); B.SetBounds(wa + gap, 0, wb, h);
            return h;
        }
    }

    // ------------------------------------------------------------------ verificação em andamento
    public class RunStepper : FlowControl
    {
        public class Item { public string Title, Note; public int State; }   // 0 próximo, 1 agora, 2 feito, 3 falhou
        public readonly List<Item> Items = new List<Item>();
        int cols;
        public override int Measure(int width)
        {
            int n = Math.Max(1, Items.Count);
            cols = Math.Max(1, Math.Min(n, (width + Theme.S(10)) / (Theme.S(170) + Theme.S(10))));
            int rows = (n + cols - 1) / cols;
            return rows * Theme.S(70) + (rows - 1) * Theme.S(10);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            int gap = Theme.S(10), w = (Width - (cols - 1) * gap) / Math.Max(1, cols);
            for (int i = 0; i < Items.Count; i++)
            {
                Item it = Items[i];
                Rectangle r = new Rectangle((i % cols) * (w + gap), (i / cols) * (Theme.S(70) + gap), w, Theme.S(70));
                bool now = it.State == 1;
                Card(g, r, now ? Theme.Mix(Theme.Accent, Theme.Panel, 26) : Theme.Panel, now ? Theme.Accent : Theme.Border, Theme.S(14));
                Color nc = now ? Theme.AccentText : it.State == 2 ? Theme.GoodText : it.State == 3 ? Theme.BadText : Theme.Faint;
                int x = r.X + Theme.S(16);
                if (it.State >= 2)
                {
                    float ic = Theme.S(14f);
                    Icons.Draw(g, it.State == 2 ? "check" : "x", new RectangleF(x, r.Y + Theme.S(15), ic, ic), nc, Theme.S(2.6f));
                    x += Theme.S(20);
                }
                Theme.Text_(g, it.Note, Theme.Font(8.5f, FontStyle.Bold), nc, new Rectangle(x, r.Y + Theme.S(12), r.Right - x - Theme.S(10), Theme.S(20)), 0);
                Theme.Text_(g, (i + 1) + ". " + it.Title, Theme.Font(10f, FontStyle.Bold), now ? Theme.Text : Theme.Soft, new Rectangle(r.X + Theme.S(16), r.Y + Theme.S(36), r.Width - Theme.S(26), Theme.S(22)), 0);
            }
        }
    }

    /// <summary>Etapa atual: anel de progresso + explicação em linguagem simples.</summary>
    public class RunCard : FlowControl
    {
        public double Progress;
        public string Title = "", Text_ = "", TimeLeft = "", Total = "";
        public bool ErrorState;
        int hT, hX;
        public override int Measure(int width)
        {
            int ring = Theme.S(176), tw = width - ring - Theme.S(56) - Theme.S(32);
            if (width < Theme.S(600)) tw = width - Theme.S(56);
            hT = H(Title, Theme.Display(16f), tw);
            hX = H(Text_, Theme.Font(10.5f), tw);
            int textH = hT + Theme.S(12) + hX + Theme.S(14) + Theme.S(22);
            return width < Theme.S(600) ? Theme.S(28) + ring + Theme.S(20) + textH + Theme.S(28) : Math.Max(ring + Theme.S(56), textH + Theme.S(56));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            Card(g, ClientRectangle, Theme.Panel, Theme.Border, Theme.S(20));
            bool stacked = Width < Theme.S(600);
            int ring = Theme.S(176), rx = Theme.S(28), ry = stacked ? Theme.S(28) : (Height - ring) / 2;
            float thick = Theme.S(13f);
            RectangleF rr = new RectangleF(rx + thick / 2, ry + thick / 2, ring - thick, ring - thick);
            using (Pen p = new Pen(Theme.Border, thick)) g.DrawEllipse(p, rr);
            float sweep = (float)(Math.Max(0, Math.Min(1, Progress)) * 360);
            if (sweep > 0.5f)
                using (Pen p = new Pen(ErrorState ? Theme.Bad : Theme.Accent, thick)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawArc(p, rr, -90, sweep); }
            Theme.Text_(g, (int)Math.Round(Progress * 100) + "%", Theme.Display(22f), Theme.Text, new Rectangle(rx, ry + ring / 2 - Theme.S(30), ring, Theme.S(40)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.Text_(g, "desta etapa", Theme.Font(8.5f), Theme.Muted, new Rectangle(rx, ry + ring / 2 + Theme.S(10), ring, Theme.S(18)), TextFormatFlags.HorizontalCenter);
            int tx = stacked ? Theme.S(28) : rx + ring + Theme.S(32), tw = Width - tx - Theme.S(28);
            int y = stacked ? ry + ring + Theme.S(20) : (Height - (hT + Theme.S(12) + hX + Theme.S(14) + Theme.S(22))) / 2;
            TextRenderer.DrawText(g, Title, Theme.Display(16f), new Rectangle(tx, y, tw, hT), Theme.Text, WB);
            y += hT + Theme.S(12);
            TextRenderer.DrawText(g, Text_, Theme.Font(10.5f), new Rectangle(tx, y, tw, hX), Theme.Soft, WB);
            y += hX + Theme.S(14);
            Theme.Text_(g, TimeLeft + (Total.Length > 0 ? "      " + Total : ""), Theme.Font(9.5f), Theme.Muted, new Rectangle(tx, y, tw, Theme.S(22)), 0);
        }
    }

    /// <summary>Leituras em linguagem simples: valor, "Normal / Atenção / Alto" e faixa do que é normal.</summary>
    public class ReadingCards : FlowControl
    {
        public class Reading { public string Label, Value, Note, Pill; public int Level = -1; public double Mark = double.NaN, Warn = 0.72, Hot = 0.88; }
        public readonly Reading[] Items = { new Reading(), new Reading(), new Reading() };
        int cols;
        public override int Measure(int width)
        {
            cols = Math.Max(1, Math.Min(3, (width + Theme.S(12)) / (Theme.S(230) + Theme.S(12))));
            int rows = (3 + cols - 1) / cols;
            return rows * Theme.S(132) + (rows - 1) * Theme.S(12);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            int gap = Theme.S(12), w = (Width - (cols - 1) * gap) / cols, h = Theme.S(132);
            for (int i = 0; i < 3; i++)
            {
                Reading it = Items[i];
                Rectangle r = new Rectangle((i % cols) * (w + gap), (i / cols) * (h + gap), w, h);
                Card(g, r, Theme.Panel, Theme.Border, Theme.S(16));
                int x = r.X + Theme.S(18), tw = r.Width - Theme.S(36);
                Theme.Text_(g, it.Label ?? "", Theme.Font(9f), Theme.Muted, new Rectangle(x, r.Y + Theme.S(14), tw, Theme.S(20)), 0);
                Theme.Text_(g, it.Value ?? "—", Theme.Display(17f), Theme.Text, new Rectangle(x, r.Y + Theme.S(36), tw, Theme.S(34)), 0);
                if (it.Level >= 0)
                {
                    CompStatus st = it.Level == 0 ? CompStatus.Ok : it.Level == 1 ? CompStatus.Suspect : CompStatus.Failed;
                    string lbl = !string.IsNullOrEmpty(it.Pill) ? it.Pill : it.Level == 0 ? "Normal" : it.Level == 1 ? "Atenção" : "Alto";
                    int pw = TextRenderer.MeasureText(lbl, Theme.Font(8.5f, FontStyle.Bold)).Width + Theme.S(40);
                    Theme.StatusPill(g, r.Right - Theme.S(18) - pw, r.Y + Theme.S(40), st, lbl, Theme.Panel);
                }
                int by = r.Y + Theme.S(78);
                if (!double.IsNaN(it.Mark))
                {
                    int bh = Theme.S(8);
                    float a = tw * (float)it.Warn, b = tw * (float)it.Hot;
                    using (SolidBrush z = new SolidBrush(Theme.Mix(Theme.Good, Theme.Panel, 80))) g.FillRectangle(z, x, by, a, bh);
                    using (SolidBrush z = new SolidBrush(Theme.Mix(Theme.Warn, Theme.Panel, 80))) g.FillRectangle(z, x + a, by, b - a, bh);
                    using (SolidBrush z = new SolidBrush(Theme.Mix(Theme.Bad, Theme.Panel, 80))) g.FillRectangle(z, x + b, by, tw - b, bh);
                    float mx = x + (float)Math.Max(0, Math.Min(1, it.Mark)) * tw;
                    using (SolidBrush m = new SolidBrush(Theme.Text)) g.FillRectangle(m, mx - Theme.S(2f), by - Theme.S(3f), Theme.S(4f), bh + Theme.S(6f));
                    by += bh + Theme.S(10);
                }
                TextRenderer.DrawText(g, it.Note ?? "", Theme.Font(8.5f), new Rectangle(x, by, tw, r.Bottom - by - Theme.S(8)), Theme.Faint, WB | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>Linha "Ver dados técnicos" que abre/fecha um painel.</summary>
    public class Toggle : FlowControl
    {
        public string Caption;
        public bool Open;
        public event EventHandler Toggled;
        public Toggle(string caption) { Caption = caption; Cursor = Cursors.Hand; SetStyle(ControlStyles.Selectable, true); TabStop = true; AccessibleRole = AccessibleRole.PushButton; AccessibleName = caption; }
        public override int Measure(int width) { return Theme.S(50); }
        protected override void OnClick(EventArgs e) { Open = !Open; Invalidate(); if (Toggled != null) Toggled(this, EventArgs.Empty); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            Card(g, ClientRectangle, Theme.Panel3, Focused && ShowFocusCues ? Theme.AccentHover : Theme.Border, Theme.S(14));
            Theme.Text_(g, Open ? "Ocultar dados técnicos" : Caption, Theme.Font(9.5f, FontStyle.Bold), Theme.Soft, new Rectangle(Theme.S(18), 0, Width - Theme.S(60), Height), TextFormatFlags.VerticalCenter);
            float ic = Theme.S(16f);
            Icons.Draw(g, Open ? "chevup" : "chevdown", new RectangleF(Width - Theme.S(18) - ic, (Height - ic) / 2f, ic, ic), Theme.Muted, Theme.S(2f));
        }
    }

    /// <summary>"Suas correções": cada correção com o estado do acompanhamento (resolvido / acompanhando / não resolveu).</summary>
    public class FixList : FlowControl
    {
        public readonly List<FixStatus> Items = new List<FixStatus>();
        public event Action<FixStatus> RemoveClicked;
        readonly List<Rectangle> removeRects = new List<Rectangle>();
        int[] hs = new int[0];

        public override int Measure(int width)
        {
            hs = new int[Items.Count];
            int h = 0, tw = width - Theme.S(36) - Theme.S(170);
            for (int i = 0; i < Items.Count; i++)
            {
                hs[i] = Math.Max(Theme.S(78), Theme.S(52) + H(Items[i].Text, Theme.Font(9.5f), tw) + Theme.S(16));
                h += hs[i] + (i > 0 ? Theme.S(10) : 0);
            }
            return h;
        }

        static CompStatus Look(int state) { return state == 1 ? CompStatus.Ok : state == 2 ? CompStatus.Suspect : CompStatus.Untested; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hit = removeRects.Exists(delegate(Rectangle r) { return r.Contains(e.Location); });
            Cursor = hit ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            for (int i = 0; i < removeRects.Count && i < Items.Count; i++)
                if (removeRects[i].Contains(e.Location) && RemoveClicked != null) { RemoveClicked(Items[i]); break; }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            removeRects.Clear();
            int y = 0;
            for (int i = 0; i < Items.Count && i < hs.Length; i++)
            {
                FixStatus st = Items[i];
                Rectangle r = new Rectangle(0, y, Width, hs[i]);
                CompStatus look = Look(st.State);
                Card(g, r, Theme.Panel, st.State == 2 ? Theme.Mix(Theme.Warn, Theme.Panel, 100) : st.State == 1 ? Theme.Mix(Theme.Good, Theme.Panel, 90) : Theme.Border, Theme.S(14));
                int x = r.X + Theme.S(18), tw = r.Width - Theme.S(36) - Theme.S(170);
                int pw = Theme.StatusPill(g, x, r.Y + Theme.S(14), look, st.Headline, Theme.Panel);
                Theme.Text_(g, st.Fix.Title, Theme.Font(10f, FontStyle.Bold), Theme.Text, new Rectangle(x + pw + Theme.S(12), r.Y + Theme.S(14), tw - pw - Theme.S(12), Theme.S(24)), TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, st.Text, Theme.Font(9.5f), new Rectangle(x, r.Y + Theme.S(48), tw, r.Height - Theme.S(52)), Theme.Muted, WB);
                Theme.Text_(g, "Feito em " + st.Fix.When.ToString("dd/MM"), Theme.Font(8.5f), Theme.Faint, new Rectangle(r.Right - Theme.S(170), r.Y + Theme.S(16), Theme.S(152), Theme.S(20)), TextFormatFlags.Right);
                string rl = "Parar de acompanhar";
                int rw = TextRenderer.MeasureText(rl, Theme.Font(8.5f)).Width;
                Rectangle rr = new Rectangle(r.Right - Theme.S(18) - rw, r.Bottom - Theme.S(30), rw, Theme.S(20));
                Theme.Text_(g, rl, Theme.Font(8.5f), Theme.AccentHover, rr, 0);
                removeRects.Add(rr);
                y += hs[i] + Theme.S(10);
            }
        }
    }

    /// <summary>Um passo de guia: o atual aparece aberto com o texto e os botões; os feitos ficam com um "✓".</summary>
    public class GuideStepCard : FlowControl
    {
        public int Index;
        public GuideStep Step;
        public int State;   // 0 a fazer, 1 atual, 2 feito
        public readonly List<FlatButton> ActionButtons = new List<FlatButton>();
        public FlatButton DoneButton;
        public event EventHandler HeaderClicked;
        int hT;
        const int Collapsed = 66;

        public GuideStepCard(int index, GuideStep step, bool last)
        {
            Index = index; Step = step; BackColor = Theme.Panel; AccessibleName = step.Title; Cursor = Cursors.Hand;
            foreach (GuideAction a in step.Actions)
            {
                FlatButton b = new FlatButton(a.Label, false);
                b.Tag = a;
                ActionButtons.Add(b); Controls.Add(b);
            }
            DoneButton = new FlatButton(last ? "Feito" : "Feito, próximo passo", true);
            Controls.Add(DoneButton);
        }

        public override int Measure(int width)
        {
            bool open = State == 1;
            foreach (Control c in Controls) c.Visible = open;
            if (!open) return Theme.S(Collapsed);
            int tx = Theme.S(70), tw = width - tx - Theme.S(24);
            hT = H(Rules.Fill(Step.Text), Theme.Font(10.5f), tw);
            int y = Theme.S(20) + Theme.S(26) + Theme.S(8) + hT + Theme.S(16);
            int x = tx;
            foreach (FlatButton b in ActionButtons)
            {
                if (x + b.Width > width - Theme.S(24) && x > tx) { x = tx; y += b.Height + Theme.S(10); }
                b.Location = new Point(x, y); x += b.Width + Theme.S(10);
            }
            if (x + DoneButton.Width > width - Theme.S(24) && x > tx) { x = tx; y += DoneButton.Height + Theme.S(10); }
            DoneButton.Location = new Point(x, y);
            return y + DoneButton.Height + Theme.S(20);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Y < Theme.S(Collapsed) && HeaderClicked != null) HeaderClicked(this, EventArgs.Empty);
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            bool open = State == 1, done = State == 2;
            Card(g, ClientRectangle, open ? Theme.Mix(Theme.Accent, Theme.Panel, 18) : Theme.Panel, open ? Theme.Accent : Theme.Border, Theme.S(16));
            int d = Theme.S(32), cy = Theme.S(17);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(done ? Theme.Mix(Theme.Good, Theme.Panel, 50) : open ? Theme.AccentSoft : Theme.Panel2)) g.FillEllipse(b, Theme.S(20), cy, d, d);
            if (done) { float ic = Theme.S(16f); Icons.Draw(g, "check", new RectangleF(Theme.S(20) + (d - ic) / 2f, cy + (d - ic) / 2f, ic, ic), Theme.GoodText, Theme.S(2.6f)); }
            else Theme.Text_(g, (Index + 1).ToString(), Theme.Font(10f, FontStyle.Bold), open ? Theme.AccentText : Theme.Muted, new Rectangle(Theme.S(20), cy, d, d), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int tx = Theme.S(70), tw = Width - tx - Theme.S(24);
            Theme.Text_(g, Rules.Fill(Step.Title), Theme.Font(11f, FontStyle.Bold), done ? Theme.Muted : Theme.Text, new Rectangle(tx, Theme.S(20), tw, Theme.S(26)), TextFormatFlags.VerticalCenter);
            if (open) TextRenderer.DrawText(g, Rules.Fill(Step.Text), Theme.Font(10.5f), new Rectangle(tx, Theme.S(54), tw, hT), Theme.Soft, WB);
        }
    }

    /// <summary>Veredito no topo da página de Resultados.</summary>
    public class VerdictBlock : BufferedControl
    {
        public string Title = "", Sub = "";
        int hT, hS;
        const TextFormatFlags WB = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.Left;
        public void Set(string title, string sub) { Title = title; Sub = sub; AccessibleName = title + ". " + sub; Fit(); Invalidate(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Fit(); }
        void Fit()
        {
            int w = Math.Max(50, Width - Theme.S(14));
            hT = TextRenderer.MeasureText(Title, Theme.Display(15f), new Size(w, 0), WB).Height;
            hS = string.IsNullOrEmpty(Sub) ? 0 : TextRenderer.MeasureText(Sub, Theme.Font(10f), new Size(w, 0), WB).Height;
            int h = hT + (hS > 0 ? Theme.S(8) + hS : 0) + Theme.S(18);
            if (Math.Abs(h - Height) > 1) Height = h;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            int w = Width - Theme.S(14);
            TextRenderer.DrawText(g, Title, Theme.Display(15f), new Rectangle(0, 0, w, hT), Theme.Text, WB);
            if (hS > 0) TextRenderer.DrawText(g, Sub, Theme.Font(10f), new Rectangle(0, hT + Theme.S(8), w, hS), Theme.Soft, WB);
        }
    }
}
