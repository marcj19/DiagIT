using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DiagIT
{
    /// <summary>
    /// Ícones vetoriais de traço (grade 24×24, como os do protótipo), desenhados com GDI+.
    /// Não dependem de fonte de ícones: ficam iguais no Windows 10 e 11 e em qualquer escala.
    /// </summary>
    public static class Icons
    {
        public static void Draw(Graphics g, string name, RectangleF r, Color c, float stroke)
        {
            if (string.IsNullOrEmpty(name)) return;
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X, r.Y);
            float k = Math.Min(r.Width, r.Height) / 24f;
            g.ScaleTransform(k, k);
            using (Pen p = new Pen(c, stroke / k))
            using (SolidBrush b = new SolidBrush(c))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                Paint(g, name, p, b);
            }
            g.Restore(st);
        }

        static void L(Graphics g, Pen p, float x1, float y1, float x2, float y2) { g.DrawLine(p, x1, y1, x2, y2); }

        static void Poly(Graphics g, Pen p, bool closed, params float[] xy)
        {
            PointF[] pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
            if (closed) g.DrawPolygon(p, pts); else g.DrawLines(p, pts);
        }

        static void R(Graphics g, Pen p, float x, float y, float w, float h, float rad)
        {
            if (rad <= 0) { g.DrawRectangle(p, x, y, w, h); return; }
            using (GraphicsPath gp = Theme.Round(new RectangleF(x, y, w, h), rad)) g.DrawPath(p, gp);
        }

        static void C(Graphics g, Pen p, float cx, float cy, float rad) { g.DrawEllipse(p, cx - rad, cy - rad, rad * 2, rad * 2); }
        static void Dot(Graphics g, Brush b, float cx, float cy, float rad) { g.FillEllipse(b, cx - rad, cy - rad, rad * 2, rad * 2); }

        static void Paint(Graphics g, string n, Pen p, Brush b)
        {
            switch (n)
            {
                case "pulse": Poly(g, p, false, 2, 12, 6, 12, 8.5f, 6, 12.5f, 18, 15, 12, 22, 12); break;
                case "home": Poly(g, p, false, 3, 11, 12, 4, 21, 11); Poly(g, p, false, 5, 9.5f, 5, 20, 10, 20, 10, 14, 14, 14, 14, 20, 19, 20, 19, 9.5f); break;
                case "help":
                    C(g, p, 12, 12, 9);
                    g.DrawArc(p, 9.5f, 6.5f, 5, 5, 190, 250);
                    L(g, p, 13.7f, 10.6f, 12, 12.6f); L(g, p, 12, 12.6f, 12, 13.6f);
                    Dot(g, b, 12, 17, 1.1f); break;
                case "search": C(g, p, 11, 11, 7); L(g, p, 16, 16, 20.5f, 20.5f); break;
                case "clipboard": R(g, p, 5, 4.5f, 14, 16.5f, 2); R(g, p, 9, 2.5f, 6, 3.5f, 1); L(g, p, 9, 11, 15, 11); L(g, p, 9, 15, 13, 15); break;
                case "wrench": g.DrawArc(p, 11, 4, 9, 9, 150, 280); L(g, p, 12.4f, 11.6f, 4, 20); break;
                case "activity": Poly(g, p, false, 3, 12, 7, 12, 10, 20, 14, 4, 17, 12, 21, 12); break;
                case "chip":
                    R(g, p, 6, 6, 12, 12, 1.5f); R(g, p, 9.5f, 9.5f, 5, 5, 0);
                    foreach (float v in new float[] { 9, 15 }) { L(g, p, v, 2, v, 5); L(g, p, v, 19, v, 22); L(g, p, 2, v, 5, v); L(g, p, 19, v, 22, v); }
                    break;
                case "gamepad": R(g, p, 2, 7, 20, 12, 3); L(g, p, 7, 11, 7, 15); L(g, p, 5, 13, 9, 13); Dot(g, b, 16, 12, 1.1f); Dot(g, b, 18, 14.5f, 1.1f); break;
                case "gamepadx": R(g, p, 2, 7, 20, 12, 3); L(g, p, 7, 11, 7, 15); L(g, p, 5, 13, 9, 13); L(g, p, 15, 11, 19, 15); L(g, p, 19, 11, 15, 15); break;
                case "gear":
                    C(g, p, 12, 12, 3); C(g, p, 12, 12, 7);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = Math.PI / 4 * i;
                        L(g, p, 12 + (float)(Math.Cos(a) * 7), 12 + (float)(Math.Sin(a) * 7), 12 + (float)(Math.Cos(a) * 9.5), 12 + (float)(Math.Sin(a) * 9.5));
                    }
                    break;
                case "gpu": R(g, p, 2, 6, 20, 11, 2); C(g, p, 9, 11.5f, 3); L(g, p, 15, 9.5f, 19, 9.5f); L(g, p, 15, 13, 19, 13); L(g, p, 5, 17, 5, 19); L(g, p, 8, 17, 8, 19); break;
                case "ram":
                    R(g, p, 2, 7, 20, 9, 1.5f);
                    foreach (float x in new float[] { 6, 10, 14, 18 }) L(g, p, x, 10.5f, x, 12.5f);
                    foreach (float x in new float[] { 5, 9, 15, 19 }) L(g, p, x, 16, x, 19);
                    break;
                case "ssd": R(g, p, 3, 6, 18, 12, 2); L(g, p, 7, 10, 13, 10); L(g, p, 7, 14, 10, 14); Dot(g, b, 17, 14, 1.1f); break;
                case "bolt": Poly(g, p, true, 13, 2, 5, 13, 11, 13, 10, 22, 18, 11, 12, 11); break;
                case "thermo":
                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        gp.AddArc(10, 2.5f, 4, 4, 180, 180);
                        gp.AddLine(14, 4.5f, 14, 14.3f);
                        gp.AddArc(8, 13, 8, 8, -48, 276);
                        gp.CloseFigure();
                        g.DrawPath(p, gp);
                    }
                    L(g, p, 12, 9, 12, 16); break;
                case "window": R(g, p, 3, 4, 18, 16, 2); L(g, p, 3, 9, 21, 9); Dot(g, b, 7, 6.5f, 0.9f); Dot(g, b, 10, 6.5f, 0.9f); break;
                case "board": R(g, p, 3, 3, 18, 18, 2); R(g, p, 7, 7, 4, 4, 0); Poly(g, p, false, 15, 7, 15, 11, 17, 11); Poly(g, p, false, 7, 15, 9, 15, 9, 17); R(g, p, 13, 14, 4, 3, 0); break;
                case "check": Poly(g, p, false, 5, 12.5f, 9.5f, 17, 19, 7.5f); break;
                case "alert": Poly(g, p, true, 12, 3.5f, 22, 20.5f, 2, 20.5f); L(g, p, 12, 10, 12, 14.5f); Dot(g, b, 12, 17.5f, 1.1f); break;
                case "x": L(g, p, 6, 6, 18, 18); L(g, p, 18, 6, 6, 18); break;
                case "dashcircle":
                    using (Pen d = (Pen)p.Clone()) { d.DashPattern = new float[] { 1.4f, 1.4f }; d.DashCap = DashCap.Round; C(g, d, 12, 12, 8); }
                    break;
                case "play": g.FillPolygon(b, new PointF[] { new PointF(7, 4.5f), new PointF(19, 12), new PointF(7, 19.5f) }); break;
                case "shield": Poly(g, p, true, 12, 3, 20, 6, 20, 12, 17, 17.5f, 12, 21, 7, 17.5f, 4, 12, 4, 6); Poly(g, p, false, 8.5f, 12, 11, 14.5f, 15.5f, 9.5f); break;
                case "monitor": R(g, p, 2, 4, 20, 13, 2); L(g, p, 8, 21, 16, 21); L(g, p, 12, 17, 12, 21); break;
                case "monitorsad":
                    R(g, p, 2, 4, 20, 13, 2); L(g, p, 8, 21, 16, 21); L(g, p, 12, 17, 12, 21);
                    Dot(g, b, 8.5f, 9.5f, 1.1f); Dot(g, b, 15.5f, 9.5f, 1.1f); g.DrawArc(p, 9, 12.5f, 6, 4, 200, 140); break;
                case "monitoroff": R(g, p, 2, 4, 20, 13, 2); L(g, p, 8, 21, 16, 21); L(g, p, 12, 17, 12, 21); L(g, p, 6, 8, 18, 13); break;
                case "power": L(g, p, 12, 3, 12, 11); g.DrawArc(p, 4, 4, 16, 16, 300, 300); break;
                case "stutter": Poly(g, p, false, 2, 12, 5, 12, 7, 8, 10, 17, 12, 11, 14, 14, 16, 14, 18, 11, 20, 12, 22, 12); break;
                case "gauge": g.DrawArc(p, 4, 8, 16, 16, 180, 180); L(g, p, 12, 16, 8.5f, 11); L(g, p, 3, 20, 21, 20); break;
                case "file": R(g, p, 5, 3, 14, 18, 2); L(g, p, 9, 8, 15, 8); L(g, p, 9, 12, 15, 12); L(g, p, 9, 16, 13, 16); break;
                case "back": L(g, p, 19, 12, 5, 12); Poly(g, p, false, 11, 6, 5, 12, 11, 18); break;
                case "arrow": L(g, p, 5, 12, 19, 12); Poly(g, p, false, 13, 6, 19, 12, 13, 18); break;
                case "chevdown": Poly(g, p, false, 6, 9, 12, 15, 18, 9); break;
                case "chevup": Poly(g, p, false, 6, 15, 12, 9, 18, 15); break;
                case "info": C(g, p, 12, 12, 9); L(g, p, 12, 11, 12, 16.5f); Dot(g, b, 12, 7.8f, 1.1f); break;
                case "clock": C(g, p, 12, 12, 9); Poly(g, p, false, 12, 7, 12, 12, 15.5f, 14); break;
                case "list": L(g, p, 8, 6, 20, 6); L(g, p, 8, 12, 20, 12); L(g, p, 8, 18, 20, 18); Dot(g, b, 4, 6, 1.1f); Dot(g, b, 4, 12, 1.1f); Dot(g, b, 4, 18, 1.1f); break;
            }
        }
    }
}
