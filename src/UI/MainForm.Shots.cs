using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace DiagIT
{
    /// <summary>
    /// Só para desenvolvimento: percorre as páginas e salva uma imagem de cada (--screenshots pasta).
    /// Com --with-run também roda 60 s do teste de processador para fotografar a tela "Verificando" (isso grava um teste no histórico).
    /// </summary>
    public partial class MainForm
    {
        public void ScheduleScreenshots(string dir, bool withRun)
        {
            Directory.CreateDirectory(dir);
            int step = 0;
            Timer t = new Timer();
            t.Interval = 1500;
            t.Tick += delegate
            {
                if (step == 0 && lastDiag == null) return;   // espera a análise inicial
                switch (step)
                {
                    case 0: Navigate(P_HOME); break;
                    case 1: Shot(dir, "1-inicio"); OpenProblem(); break;
                    case 2: Shot(dir, "2-problema"); OpenPlan(symGrid.Selected >= 0 ? symGrid.Selected : 0, null); break;
                    case 3:
                        Shot(dir, "3-plano");
                        Navigate(P_RUN); break;
                    case 4: Shot(dir, "4-verificando"); Navigate(P_DIAG); break;
                    case 5: Shot(dir, "5-resultados"); Navigate(P_TESTS); break;
                    case 6: Shot(dir, "6-verificar-pecas"); Navigate(P_FIX); break;
                    case 7: Shot(dir, "7-resolver"); Navigate(P_LIVE); break;
                    case 8: Shot(dir, "8-ao-vivo"); Navigate(P_HW); break;
                    case 9: Shot(dir, "9-hardware"); Navigate(P_SET); break;
                    case 10:
                        Shot(dir, "10-configuracoes");
                        if (!withRun) { t.Stop(); ExitApp(); return; }
                        // verificação real e curta (processador, 60 s) só para fotografar a tela "Verificando"
                        runPlan.Clear(); runPlan.AddRange(QuickPlan(new System.Collections.Generic.List<TestKind> { TestKind.Cpu, TestKind.Gpu }));
                        runResults.Clear(); runGuided = true;
                        testQueue.Clear(); testQueue.Enqueue(runPlan[1]);
                        if (StartTest(TestKind.Cpu, 60, runPlan[0][2], false)) Navigate(P_RUN);
                        break;
                    case 11: case 12: case 13: case 14: case 15: case 16: break;   // deixa o teste andar
                    case 17: Shot(dir, "11-verificando-ativo"); runTechToggle.Open = true; runTech.Visible = true; runLayoutKey = ""; RefreshRun(); break;
                    case 18: Shot(dir, "12-verificando-tecnico"); testQueue.Clear(); runner.Stop(); break;
                    case 19: case 20: case 21: break;
                    case 22: Shot(dir, "13-apos-parar"); break;
                    default: t.Stop(); ExitApp(); return;
                }
                step++;
            };
            t.Start();
        }

        void Shot(string dir, string name)
        {
            try
            {
                Refresh();
                using (Bitmap b = new Bitmap(Width, Height))
                {
                    DrawToBitmap(b, new Rectangle(0, 0, Width, Height));
                    b.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
                }
            }
            catch (Exception e) { Log.Write("Shot: " + e.Message); }
        }
    }
}
