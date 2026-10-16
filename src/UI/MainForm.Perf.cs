using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DiagIT
{
    /// <summary>Página "Desempenho": limites do conjunto, ajustes de graça e onde um upgrade rende (dicas calculadas em AnalyzerBalance).</summary>
    public partial class MainForm
    {
        FindingList perfList;
        Label perfEmpty;

        void BuildPerf()
        {
            Panel p = pages[P_PERF];
            perfList = new FindingList();
            perfList.Dock = DockStyle.Fill;
            perfList.CardAction += delegate(Finding f, string action) { if (action == "guide" && f.Guide != null) OpenGuide(f.Guide, f); };
            p.Controls.Add(perfList);
            perfEmpty = Body("Analisando o seu PC...", true);
            perfEmpty.SetBounds(0, Theme.S(110), Theme.S(700), Theme.S(60));
            p.Controls.Add(perfEmpty);
            perfEmpty.BringToFront();
            p.Controls.Add(Header("Desempenho: o que segura o seu PC",
                "Limites do conjunto e onde um ajuste ou upgrade rende. Só aparece o que o DiagIT mediu no seu PC; o que já está bom aparece em verde. Isto não mexe no veredito de saúde."));
        }

        /// <summary>Mostra as dicas da última análise.</summary>
        void ShowPerf(Diagnosis d)
        {
            if (perfList == null) return;
            bool has = d != null && d.Tips != null && d.Tips.Count > 0;
            perfEmpty.Visible = !has;
            if (has) perfList.SetFindings(d.Tips);
        }
    }
}
