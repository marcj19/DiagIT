using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DiagIT
{
    /// <summary>Soluções guiadas (data/guias.json): passo a passo com ações; ao concluir, a correção entra no acompanhamento.</summary>
    public partial class MainForm
    {
        FlowPanel guideFlow;
        StepHeader guideHead;
        ButtonRow guideButtons;
        FlatButton guideFinish;
        readonly List<GuideStepCard> guideCards = new List<GuideStepCard>();
        Guide curGuide;
        bool[] guideDone = new bool[0];
        int guideStep;

        static string GuideProgressFile { get { return Path.Combine(Paths.Root, "guia-atual.txt"); } }

        void BuildGuide()
        {
            Panel p = pages[P_GUIDE];
            guideFlow = new FlowPanel();
            guideFlow.Dock = DockStyle.Fill;
            p.Controls.Add(guideFlow);
            LinkButton back = new LinkButton("Voltar", "back");
            back.Click += delegate { Navigate(P_DIAG); };
            guideFlow.Controls.Add(back);
            guideHead = new StepHeader();
            guideFlow.Controls.Add(guideHead);
            guideButtons = new ButtonRow();
            guideFinish = new FlatButton("Concluir", true);
            guideFinish.Click += delegate { FinishGuide(); };
            guideButtons.Controls.Add(guideFinish);
            LinkButton later = new LinkButton("Continuar depois", null);
            later.Click += delegate { SaveGuideProgress(); Navigate(P_HOME); };
            guideButtons.Controls.Add(later);
            guideFlow.Controls.Add(guideButtons);
        }

        /// <summary>Guia em andamento (salvo antes de reinícios): id e passo atual, ou null.</summary>
        static string[] GuideInProgress()
        {
            try { return File.Exists(GuideProgressFile) ? File.ReadAllText(GuideProgressFile).Trim().Split('|') : null; }
            catch { return null; }
        }

        void SaveGuideProgress()
        {
            if (curGuide == null) return;
            try
            {
                Paths.Ensure();
                string bits = "";
                foreach (bool b in guideDone) bits += b ? "1" : "0";
                File.WriteAllText(GuideProgressFile, curGuide.Id + "|" + guideStep + "|" + bits);
            }
            catch { }
        }

        static void ClearGuideProgress() { try { File.Delete(GuideProgressFile); } catch { } }

        void OpenGuide(string id, Finding from)
        {
            Guide g = Rules.GuideById(id);
            if (g == null) return;
            // textos da marca do PC ({site_driver}, {painel_video}...) também na interface
            Rules.SetContext(hw != null ? hw.GpuName : machine != null ? machine.Gpu : null, hw != null ? hw.CpuName : machine != null ? machine.Cpu : null);
            curGuide = g;
            guideDone = new bool[g.Steps.Count];
            guideStep = 0;
            string[] saved = GuideInProgress();
            if (saved != null && saved.Length >= 2 && saved[0] == id)
            {
                int.TryParse(saved[1], out guideStep);
                if (saved.Length >= 3) for (int i = 0; i < guideDone.Length && i < saved[2].Length; i++) guideDone[i] = saved[2][i] == '1';
                guideStep = Math.Max(0, Math.Min(g.Steps.Count - 1, guideStep));
            }
            guideHead.Set("PASSO A PASSO", g.Title, Rules.Fill(g.Summary));
            foreach (GuideStepCard c in guideCards) { guideFlow.Controls.Remove(c); c.Dispose(); }
            guideCards.Clear();
            guideFlow.Controls.Remove(guideButtons);
            for (int i = 0; i < g.Steps.Count; i++)
            {
                GuideStepCard c = new GuideStepCard(i, g.Steps[i], i == g.Steps.Count - 1);
                int idx = i;
                c.HeaderClicked += delegate { guideStep = idx; LayoutGuide(); };
                c.DoneButton.Click += delegate
                {
                    guideDone[idx] = true;
                    guideStep = Math.Min(g.Steps.Count - 1, idx + 1);
                    for (int k = 0; k < guideDone.Length; k++) if (!guideDone[k]) { guideStep = k; break; }
                    SaveGuideProgress();
                    LayoutGuide();
                };
                foreach (FlatButton b in c.ActionButtons)
                {
                    GuideAction a = (GuideAction)b.Tag;
                    b.Click += delegate { RunGuideAction(a); };
                }
                guideCards.Add(c);
                guideFlow.Controls.Add(c);
            }
            guideFlow.Controls.Add(guideButtons);
            SaveGuideProgress();
            Navigate(P_GUIDE);
            LayoutGuide();
        }

        void LayoutGuide()
        {
            bool all = true;
            for (int i = 0; i < guideCards.Count; i++)
            {
                guideCards[i].State = i == guideStep && !(guideDone[i] && AllDone()) ? 1 : guideDone[i] ? 2 : 0;
                guideCards[i].Invalidate();
                if (!guideDone[i]) all = false;
            }
            SetCaption(guideFinish, all ? "Concluir e acompanhar" : "Concluir (" + Count(guideDone) + " de " + guideDone.Length + " passos feitos)");
            guideFlow.Relayout();
        }

        bool AllDone() { foreach (bool b in guideDone) if (!b) return false; return true; }
        static int Count(bool[] a) { int n = 0; foreach (bool b in a) if (b) n++; return n; }

        void FinishGuide()
        {
            if (curGuide == null) return;
            if (!AllDone() && MessageBox.Show(this, "Ainda faltam passos. Marcar o guia como concluído mesmo assim?", "Passo a passo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            DateTime when = DateTime.Now;
            if (curGuide.Id == "driver-video-limpo")
            {
                // o que vale é a hora em que o driver novo entrou, não a hora do clique: o que acontecer depois dela já é do driver novo
                DateTime at = VideoAdapters.InstalledAt(VideoAdapters.Primary(VideoAdapters.ReadAll()));
                if (at > DateTime.MinValue && (DateTime.Now - at).TotalDays <= 3) when = at;
            }
            FixLog.Add("guia:" + curGuide.Id, curGuide.Done, curGuide.Scope, when);
            ClearGuideProgress();
            MessageBox.Show(this, "Pronto! A partir de agora o DiagIT acompanha os " + Analyzer.ScopeName(curGuide.Scope) +
                ": se pararem, ele avisa que resolveu; se voltarem, mostra o próximo passo. Você vê isso no Início, em \"Suas correções\".",
                "Passo a passo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            curGuide = null;
            StartAnalyzeClick(false);
            Navigate(P_HOME);
        }

        static bool RunHidden(string exe, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi)) { p.WaitForExit(8000); return p.HasExited && p.ExitCode == 0; }
            }
            catch { return false; }
        }

        void RunGuideAction(GuideAction a)
        {
            string target = Rules.Fill(a.Target);
            if (a.Type == "abrir")
            {
                if (target.Length == 0) target = "ms-settings:windowsupdate-optionalupdates";
                try { Process.Start(target); } catch (Exception e) { MessageBox.Show(this, "Não foi possível abrir: " + e.Message, "Passo a passo"); }
                return;
            }
            if (a.Type != "diagit") return;
            string msg;
            switch (target)
            {
                case "bloquear-wu":
                    if (!NeedAdmin()) return;
                    Fixes.SetWuDriversBlocked(true, out msg);
                    MessageBox.Show(this, msg, "Passo a passo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case "verificar-driver":
                    VerifyVideoDriver();
                    break;
                case "tirar-hd":
                    if (!NeedAdmin()) return;
                    if (Fixes.PagefileOnHdd().Count == 0) msg = "Não há arquivo de paginação no HD (ou o Windows não está em SSD). Nada a alterar.";
                    else Fixes.MovePagefileOffHdd(out msg);
                    MessageBox.Show(this, msg, "Passo a passo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case "reiniciar-avancado":
                case "reiniciar-bios":
                    bool bios = target == "reiniciar-bios";
                    if (MessageBox.Show(this, "Salve o que estiver aberto. O PC vai reiniciar " + (bios ? "direto na BIOS" : "nas opções avançadas de inicialização") +
                        " em 5 segundos.\n\nO DiagIT guarda em que passo você está e continua dali quando o Windows voltar.", "Reiniciar agora?",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                    guideDone[guideStep] = true;
                    guideStep = Math.Min(guideDone.Length - 1, guideStep + 1);
                    SaveGuideProgress();
                    if (!RunHidden(Fixes.Sys("shutdown.exe"), bios ? "/r /fw /t 5" : "/r /o /t 5"))
                    {
                        if (bios) MessageBox.Show(this, "Este PC não aceitou reiniciar direto na BIOS (é preciso abrir o DiagIT como administrador, ou a placa não suporta).\n\n" +
                            "Reinicie normalmente e aperte Delete (ou F2) várias vezes logo que o PC ligar.", "Passo a passo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        else try { Process.Start("ms-settings:recovery"); } catch { }   // Inicialização avançada → Reiniciar agora
                    }
                    LayoutGuide();
                    break;
                case "teste-gpu":
                case "teste-memoria":
                    SaveGuideProgress();
                    List<TestKind> k = new List<TestKind>();
                    k.Add(target == "teste-gpu" ? TestKind.Gpu : TestKind.Ram);
                    StartPlan(QuickPlan(k), false);
                    break;
            }
        }
    }
}
