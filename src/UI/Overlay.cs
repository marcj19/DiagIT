using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DiagIT
{
    /// <summary>
    /// Painel no jogo (como o do MSI Afterburner, mas sem injetar nada no jogo): uma janela por cima de tudo, transparente,
    /// que não recebe cliques nem foco. Aparece em jogos em tela cheia sem bordas ou em janela; em tela cheia exclusiva o Windows pode escondê-la.
    /// </summary>
    public class GameOverlay : Form
    {
        readonly Watcher w;
        readonly AppSettings cfg;
        readonly Timer timer = new Timer();
        const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;

        public GameOverlay(Watcher w, AppSettings cfg)
        {
            this.w = w; this.cfg = cfg;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(Theme.S(264), Theme.S(132));
            timer.Interval = 500;
            timer.Tick += delegate { if (Visible) Render(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        /// <summary>Mostra ou esconde, posicionando no canto escolhido da tela principal.</summary>
        public void SetShown(bool on)
        {
            if (on == Visible) { if (on) Place(); return; }
            if (on) { Place(); Show(); Render(); timer.Start(); }
            else { timer.Stop(); Hide(); }
        }

        void Place()
        {
            Rectangle s = Screen.PrimaryScreen.Bounds;
            int m = Theme.S(16);
            int x = cfg.OverlayCorner == 1 || cfg.OverlayCorner == 3 ? s.Right - Width - m : s.Left + m;
            int y = cfg.OverlayCorner >= 2 ? s.Bottom - Height - m - Theme.S(40) : s.Top + m;
            if (Left != x || Top != y) Location = new Point(x, y);
        }

        // ------------------------------------------------------------------ desenho com transparência por pixel
        [StructLayout(LayoutKind.Sequential)] struct PT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SZ { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PT pptDst, ref SZ psize, IntPtr hdcSrc, ref PT pprSrc, int crKey, ref BLEND pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

        static Color A(int a, Color c) { return Color.FromArgb(a, c.R, c.G, c.B); }

        static Color TempColor(double t, double warn, double hot) { return double.IsNaN(t) ? Theme.Muted : t >= hot ? Theme.BadText : t >= warn ? Theme.WarnText : Theme.GoodText; }

        void Render()
        {
            if (!IsHandleCreated) return;
            Place();
            using (Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp)) Draw(g);
                Push(bmp);
            }
        }

        void Draw(Graphics g)
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Round(r, Theme.S(14)))
            {
                using (SolidBrush b = new SolidBrush(A(222, Theme.Bg))) g.FillPath(b, p);
                using (Pen pen = new Pen(A(150, Theme.Border2))) g.DrawPath(pen, p);
            }
            double fps, low;
            w.Frames.Peek(out fps, out low);
            int pad = Theme.S(14);
            float ic = Theme.S(14f);
            Icons.Draw(g, "pulse", new RectangleF(Width - pad - ic, pad + Theme.S(2), ic, ic), Theme.Accent, Theme.S(2f));

            using (Font big = new Font(Theme.Display(22f).FontFamily, Theme.Display(22f).Size, FontStyle.Bold))
            using (Font small = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            using (Font smallB = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (SolidBrush text = new SolidBrush(Theme.Text))
            using (SolidBrush muted = new SolidBrush(Theme.Muted))
            {
                string f = double.IsNaN(fps) ? "--" : fps.ToString("0");
                g.DrawString(f, big, text, pad - Theme.S(2), pad - Theme.S(6));
                float fw = g.MeasureString(f, big).Width;
                g.DrawString("FPS", smallB, new SolidBrush(Theme.AccentText), pad + fw - Theme.S(4), pad + Theme.S(14));
                FrameMonitor fm = w.Frames;
                string who = fm.TargetName.Length > 0 ? fm.TargetName : "";
                string sub;
                if (!SysUtil.IsAdmin()) sub = "FPS: abra o DiagIT como administrador";
                else if (!double.IsNaN(low)) sub = "1% low  " + low.ToString("0");
                else if (fm.Error.Length > 0) sub = fm.Error.Length > 34 ? fm.Error.Substring(0, 33) + "…" : fm.Error;
                else if (!fm.Running) sub = "abra um jogo ou outro programa";
                else if ((DateTime.Now - fm.StartedAt).TotalSeconds < 4) sub = "medindo " + who + "...";
                else sub = "sem quadros (pausado ou minimizado)";
                g.DrawString(sub, small, muted, pad + fw + Theme.S(30), pad + Theme.S(14));

                // tempo de quadro dos últimos 5 s: picos = engasgos
                RectangleF gr = new RectangleF(pad, Theme.S(54), Width - pad * 2, Theme.S(30));
                using (SolidBrush gb = new SolidBrush(A(110, Theme.Panel2))) g.FillRectangle(gb, gr);
                List<double> ft = w.Frames.RecentFrames(5);
                if (ft.Count > 2)
                {
                    double max = 0; foreach (double v in ft) max = Math.Max(max, v);
                    max = Math.Max(max, 40);
                    PointF[] pts = new PointF[ft.Count];
                    for (int i = 0; i < ft.Count; i++)
                        pts[i] = new PointF(gr.X + gr.Width * i / (ft.Count - 1), gr.Bottom - (float)(Math.Min(ft[i], max) / max) * (gr.Height - 2));
                    using (Pen lp = new Pen(Theme.Accent, Theme.S(1.4f))) g.DrawLines(lp, pts);
                }
                using (SolidBrush fb = new SolidBrush(A(170, Theme.Faint)))
                    g.DrawString(!double.IsNaN(fps) && who.Length > 0 ? "tempo de quadro · " + (who.Length > 22 ? who.Substring(0, 21) + "…" : who) : "tempo de quadro", small, fb, gr.X + Theme.S(4), gr.Y + Theme.S(2));

                // uso e temperatura de GPU e CPU
                Sample s = w.Last;
                double gu = s != null ? s[F.Gpu] : double.NaN, gt = s != null ? s[F.GpuTemp] : double.NaN;
                double cu = s != null ? s[F.Cpu] : double.NaN, ct = s != null ? s[F.CpuTemp] : double.NaN;
                float y = Theme.S(94);
                DrawPair(g, "GPU", gu, gt, TempColor(gt, 85, 95), pad, y, smallB, small);
                DrawPair(g, "CPU", cu, ct, TempColor(ct, 85, 95), Width / 2f + Theme.S(2), y, smallB, small);
            }
        }

        static void DrawPair(Graphics g, string name, double use, double temp, Color tc, float x, float y, Font bold, Font regular)
        {
            using (SolidBrush mb = new SolidBrush(Theme.Muted))
            using (SolidBrush tb = new SolidBrush(Theme.Text))
            using (SolidBrush cb = new SolidBrush(tc))
            {
                g.DrawString(name, bold, mb, x, y);
                float nx = x + g.MeasureString(name, bold).Width + Theme.S(2);
                string u = double.IsNaN(use) ? "—" : use.ToString("0") + "%";
                g.DrawString(u, bold, tb, nx, y);
                float tx = nx + g.MeasureString(u, bold).Width + Theme.S(4);
                g.DrawString(double.IsNaN(temp) ? "" : temp.ToString("0") + "°C", bold, cb, tx, y);
            }
        }

        /// <summary>Só para desenvolvimento: imagem do painel sobre um fundo que imita um jogo.</summary>
        public void SavePreview(string path)
        {
            int m = Theme.S(28);
            using (Bitmap panel = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            using (Bitmap scene = new Bitmap(Width + m * 2 + Theme.S(220), Height + m * 2 + Theme.S(80)))
            {
                using (Graphics g = Graphics.FromImage(panel)) Draw(g);
                using (Graphics g = Graphics.FromImage(scene))
                {
                    using (LinearGradientBrush lb = new LinearGradientBrush(new Rectangle(0, 0, scene.Width, scene.Height), Color.FromArgb(70, 120, 150), Color.FromArgb(150, 110, 60), 35f))
                        g.FillRectangle(lb, 0, 0, scene.Width, scene.Height);
                    using (SolidBrush hill = new SolidBrush(Color.FromArgb(40, 70, 40))) g.FillEllipse(hill, -Theme.S(60), scene.Height - Theme.S(90), scene.Width + Theme.S(120), Theme.S(200));
                    g.DrawImage(panel, m, m);
                }
                scene.Save(path, ImageFormat.Png);
            }
        }

        void Push(Bitmap bmp)
        {
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hbmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hbmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(mem, hbmp);
                SZ size = new SZ(); size.cx = bmp.Width; size.cy = bmp.Height;
                PT src = new PT(), dst = new PT(); dst.x = Left; dst.y = Top;
                BLEND bl = new BLEND(); bl.Op = 0; bl.Flags = 0; bl.Alpha = 255; bl.Format = 1;   // AC_SRC_ALPHA
                UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref bl, 2);   // ULW_ALPHA
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screen);
                if (hbmp != IntPtr.Zero) { SelectObject(mem, old); DeleteObject(hbmp); }
                DeleteDC(mem);
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
