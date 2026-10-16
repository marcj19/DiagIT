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
                    case 0:
                        // Dados de exemplo só com pedido explícito (DIAGIT_SAMPLES=1), para ilustrar a documentação. Sem a variável, as imagens mostram apenas dados reais.
                        if (Environment.GetEnvironmentVariable("DIAGIT_SAMPLES") == "1")
                        {
                            lastDiag.Fixes.Clear();
                            lastDiag.Fixes.Add(SampleFix(2, "Reinstalei o driver de vídeo do jeito certo", "O problema voltou", "Voltou em 15/10 às 22:00. Na mesma hora: RainbowSixHelper.exe fechou com erro (o módulo que falhou é RainbowSixHelper.exe, do próprio programa, não do driver de vídeo). Antes: 4 em 1 dias. Isso não quer dizer que a correção foi em vão: o DiagIT montou um plano de testes, um passo de cada vez.", 1, 7, "video-voltou"));
                            lastDiag.Fixes.Add(SampleFix(0, "Tirei a memória virtual do HD", "Acompanhando", "Nenhum problema de verdade desde 11/10 às 20:10. Antes: 2 em 4 dias. Faltam cerca de 4 dias de uso sem problemas para o DiagIT dar como resolvido.", 3, 7, ""));
                            lastDiag.Fixes.Add(SampleFix(1, "Impedi o Windows Update de trocar drivers", "Resolvido", "Nenhum registro de travamentos de vídeo em 12 dias desde a correção. Antes: 3 em 6 dias.", 12, 7, ""));
                        }
                        RefreshHome();
                        Navigate(P_HOME); break;
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
                        homeFlow.AutoScrollPosition = new Point(0, 0);
                        Navigate(P_HOME);
                        homeFlow.AutoScrollPosition = new Point(0, homeFlow.AutoScrollMinSize.Height);
                        break;
                    case 11:
                        Shot(dir, "11-inicio-correcoes"); Navigate(P_PERF);
                        try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "17-atestado.html"), Report.BuildCertificate(lastDiag, machine ?? new MachineInfo(), new ReportExtras()), new System.Text.UTF8Encoding(true)); } catch (Exception ex) { Log.Write("Atestado (prévia): " + ex.Message); }
                        break;
                    case 12: Shot(dir, "11b-desempenho"); OpenGuide("video-voltou", null); guideDone[0] = true; guideDone[1] = true; guideStep = 3; LayoutGuide(); break;
                    case 13:
                        Shot(dir, "12-guia");
                        curGuide = null; ClearGuideProgress();
                        // painel no jogo com quadros de exemplo (~144 FPS com dois engasgos)
                        System.Collections.Generic.List<double> ft = new System.Collections.Generic.List<double>();
                        Random rnd = new Random(7);
                        for (int i = 0; i < 700; i++) ft.Add(i == 300 ? 58 : i == 520 ? 71 : 6.2 + rnd.NextDouble() * 1.6);
                        w.Frames.InjectPreview(ft);
                        try { overlay.SavePreview(System.IO.Path.Combine(dir, "16-painel-no-jogo.png")); } catch (Exception e) { Log.Write("Prévia do painel: " + e.Message); }
                        // pausa entre duas etapas do check-up (o processador passou, a memória começa em instantes)
                        runPlan.Clear(); runPlan.AddRange(QuickPlan(new System.Collections.Generic.List<TestKind> { TestKind.Cpu, TestKind.Ram, TestKind.Gpu }));
                        TestResult pr = new TestResult(); pr.Kind = TestKind.Cpu; pr.Status = "passed"; pr.Start = DateTime.Now.AddMinutes(-2);
                        runResults.Clear(); runResults.Add(pr); runGuided = true;
                        testQueue.Clear(); testQueue.Enqueue(runPlan[2]);
                        betweenSteps = true; Navigate(P_RUN); runLayoutKey = ""; RefreshRun();
                        break;
                    case 14:
                        Shot(dir, "13a-entre-etapas");
                        betweenSteps = false; runResults.Clear(); runPlan.Clear(); testQueue.Clear();
                        if (!withRun) { t.Stop(); ExitApp(); return; }
                        // verificação real e curta (processador, 60 s) só para fotografar a tela "Verificando"
                        runPlan.Clear(); runPlan.AddRange(QuickPlan(new System.Collections.Generic.List<TestKind> { TestKind.Cpu, TestKind.Gpu }));
                        runResults.Clear(); runGuided = true;
                        testQueue.Clear(); testQueue.Enqueue(runPlan[1]);
                        if (StartTest(TestKind.Cpu, 60, runPlan[0][2], false)) Navigate(P_RUN);
                        break;
                    case 15: case 16: case 17: case 18: case 19: break;   // deixa o teste andar
                    case 20: Shot(dir, "13-verificando-ativo"); runTechToggle.Open = true; runTech.Visible = true; runLayoutKey = ""; RefreshRun(); break;
                    case 21: Shot(dir, "14-verificando-tecnico"); testQueue.Clear(); runner.Stop(); break;
                    case 22: case 23: case 24: break;
                    case 25: Shot(dir, "15-apos-parar"); break;
                    default: t.Stop(); ExitApp(); return;
                }
                step++;
            };
            t.Start();
        }

        static FixStatus SampleFix(int state, string title, string head, string text, int daysAgo, double needDays, string nextGuide)
        {
            FixStatus st = new FixStatus();
            st.Fix = new FixEntry(); st.Fix.Title = title; st.Fix.When = DateTime.Now.AddDays(-daysAgo); st.Fix.Id = "amostra" + daysAgo;
            st.State = state; st.Headline = head; st.Text = text;
            st.AfterDays = daysAgo; st.NeedDays = needDays; st.NextGuide = nextGuide; st.NextLabel = nextGuide.Length > 0 ? "Ver o plano para resolver" : "";
            return st;
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
