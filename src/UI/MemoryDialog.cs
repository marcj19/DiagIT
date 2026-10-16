using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace DiagIT
{
    /// <summary>
    /// Aviso de memória ocupada antes do teste de RAM, no visual do DiagIT. Diz quanto dá para testar, quais programas
    /// (que a pessoa pode fechar) estão ocupando a memória e deixa claro que não é defeito.
    /// Resultado: Retry = conferir de novo, Ignore = seguir (testar o que dá ou pular o teste), Cancel = não começar.
    /// </summary>
    public class MemoryDialog : Form
    {
        public MemoryDialog(long testableMb, long totalMb, List<KeyValuePair<string, long>> apps, bool canTest, bool midRun)
        {
            Text = "Memória ocupada";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Panel;
            Font = Theme.Font(9.5f);
            int w = Theme.S(520), pad = Theme.S(24), y = pad;
            ClientSize = new Size(w, Theme.S(100));

            Label title = new Label();
            title.Text = canTest ? "Dá para testar só uma parte da memória agora" : "Agora não sobra memória livre para o teste";
            title.Font = Theme.Display(14f); title.ForeColor = Theme.Text; title.AutoSize = false;
            title.SetBounds(pad, y, w - pad * 2, Theme.S(30));
            Controls.Add(title);
            y += Theme.S(38);

            CultureInfoHolder pt = new CultureInfoHolder();
            StringBuilder sb = new StringBuilder();
            sb.Append(canTest
                ? "O teste usa só a memória que está livre, para não atrapalhar o Windows. Agora estão livres " + Gb(testableMb, pt) + " de " + Gb(totalMb, pt) + "."
                : "O teste usa só a memória que está livre, para não atrapalhar o Windows, e agora quase nada está livre.");
            sb.Append(" Isso não é defeito do PC.\n\n");
            if (apps.Count > 0)
            {
                sb.Append("Estes programas estão usando bastante memória:\n");
                foreach (KeyValuePair<string, long> a in apps) sb.Append("   •  " + a.Key + "  —  " + Gb(a.Value >> 20, pt) + "\n");
                sb.Append("\nSe puder, salve o que estiver aberto, feche-os e clique em \"Já fechei\". Quanto mais memória livre, mais completo fica o teste.");
            }
            else
                sb.Append("Quem está usando a memória é o próprio Windows. Para um teste mais completo, reinicie o PC e rode o check-up logo depois, antes de abrir outros programas.");

            Label body = new Label();
            body.Text = sb.ToString(); body.Font = Font; body.ForeColor = Theme.Soft; body.AutoSize = false;
            int bh = TextRenderer.MeasureText(body.Text, body.Font, new Size(w - pad * 2, 2000), TextFormatFlags.WordBreak).Height + Theme.S(6);
            body.SetBounds(pad, y, w - pad * 2, bh);
            Controls.Add(body);
            y += bh + Theme.S(20);

            FlatButton again = new FlatButton(apps.Count > 0 ? "Já fechei, conferir de novo" : "Conferir de novo", true);
            again.Location = new Point(pad, y);
            again.Click += delegate { DialogResult = DialogResult.Retry; Close(); };
            Controls.Add(again);
            FlatButton go = new FlatButton(canTest ? "Testar assim mesmo" : midRun ? "Pular o teste de memória" : "Começar sem o teste de memória", false);
            go.Location = new Point(again.Right + Theme.S(10), y);
            go.Click += delegate { DialogResult = DialogResult.Ignore; Close(); };
            Controls.Add(go);
            ClientSize = new Size(w, y + again.Height + pad);
            AcceptButton = again;
        }

        class CultureInfoHolder { public readonly System.Globalization.CultureInfo C = new System.Globalization.CultureInfo("pt-BR"); }

        static string Gb(long mb, CultureInfoHolder pt) { return (mb / 1024.0).ToString(mb < 10240 ? "0.0" : "0", pt.C) + " GB"; }
    }
}
