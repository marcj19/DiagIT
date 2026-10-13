using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DiagIT
{
    /// <summary>"Estou com um problema": o usuário escolhe o sintoma e o DiagIT monta a verificação certa.</summary>
    public partial class MainForm
    {
        FlowPanel probFlow;
        // passo 1: sintoma
        LinkButton probBack;
        StepHeader probHead;
        SymptomGrid symGrid;
        ButtonRow probButtons;
        FlatButton probNext;
        // passo 2: plano
        LinkButton planBack;
        StepHeader planHead;
        TwoColumns planCols;
        ListCard planKnown, planBefore;
        PlanCard planCard;
        ButtonRow planButtons;
        FlatButton planGo;
        int planSymptom = -1;
        readonly List<object[]> planItems = new List<object[]>();

        static readonly string[] SymKey = { "bsod", "off", "crash", "stutter", "black", "fps" };
        static readonly TestKind[][] SymKinds =
        {
            new TestKind[] { TestKind.Ram, TestKind.Cpu, TestKind.Gpu, TestKind.Combined },
            new TestKind[] { TestKind.Combined, TestKind.Gpu, TestKind.Cpu },
            new TestKind[] { TestKind.Gpu, TestKind.Ram, TestKind.Cpu },
            new TestKind[] { TestKind.Disk, TestKind.Gpu, TestKind.Ram },
            new TestKind[] { TestKind.Gpu, TestKind.Combined },
            new TestKind[] { TestKind.Cpu, TestKind.Gpu }
        };
        static readonly string[] SymPlanTitle =
        {
            "Vamos descobrir a causa das telas azuis", "Vamos descobrir por que o PC desliga sozinho", "Vamos descobrir por que o jogo fecha sozinho",
            "Vamos descobrir a causa das travadinhas", "Vamos descobrir por que a imagem some", "Vamos descobrir por que o jogo está lento"
        };
        // prefixos dos achados que mostram cada sintoma nos registros
        static readonly string[][] SymEvidence =
        {
            new string[] { },
            new string[] { "O PC desligou/reiniciou sem aviso", "Partida interrompida abruptamente" },
            new string[] { "Jogos/apps travaram dentro do driver", "Vários programas diferentes travaram" },
            new string[] { "O sistema 'congelou'", "Tempo excessivo em drivers", "Disco com picos de latência", "Muita leitura de disco" },
            new string[] { "O driver de vídeo parou de responder", "Travamentos de GPU registrados" },
            new string[] { "Gargalo de CPU", "A CPU perde desempenho", "O clock da GPU cai", "A BIOS/firmware limitou" }
        };

        static string KindDesc(TestKind k)
        {
            switch (k)
            {
                case TestKind.Cpu: return "Todos os núcleos no máximo, conferindo cada cálculo.";
                case TestKind.Ram: return "Grava e confere padrões em toda a memória livre.";
                case TestKind.Gpu: return "Coloca a placa no máximo e confere cada cálculo e toda a memória dela.";
                case TestKind.Disk: return "Confere a saúde que o disco informa e a leitura de ponta a ponta.";
                default: return "Processador e placa de vídeo no máximo juntos: o maior consumo que o PC pode ter.";
            }
        }

        void BuildProblem()
        {
            Panel p = pages[P_PROBLEM];
            probFlow = new FlowPanel();
            probFlow.Dock = DockStyle.Fill;
            p.Controls.Add(probFlow);

            probBack = new LinkButton("Voltar ao início", "back");
            probBack.Click += delegate { Navigate(P_HOME); };
            probFlow.Controls.Add(probBack);
            probHead = new StepHeader();
            probHead.Set("PASSO 1 DE 3", "O que está acontecendo com o seu PC?",
                "Escolha o que mais parece com o seu caso. O DiagIT monta a verificação certa e já leva em conta o que ele registrou sozinho.");
            probFlow.Controls.Add(probHead);
            symGrid = new SymptomGrid();
            AddSym("monitorsad", Color.FromArgb(127, 178, 255), "Tela azul", "Aparece uma tela azul com uma carinha triste e o PC reinicia.");
            AddSym("power", Theme.BadText, "Desliga ou reinicia sozinho", "O PC apaga de repente, sem tela azul, e às vezes liga de novo.");
            AddSym("gamepadx", Color.FromArgb(201, 168, 255), "O jogo fecha sozinho", "Volta para a área de trabalho, às vezes com uma mensagem de erro.");
            AddSym("stutter", Theme.WarnText, "Travadinhas e engasgos", "A imagem congela por instantes durante o jogo e depois volta.");
            AddSym("monitoroff", Theme.Soft, "Tela preta ou imagem some", "O monitor apaga por alguns segundos e a imagem volta.");
            AddSym("gauge", Theme.GoodText, "Jogo lento (FPS baixo)", "O jogo roda mais devagar do que deveria, mesmo sem travar.");
            symGrid.SelectionChanged += delegate { probNext.Enabled = symGrid.Selected >= 0; };
            probFlow.Controls.Add(symGrid);
            probButtons = new ButtonRow();
            probNext = new FlatButton("Continuar", true);
            probNext.Click += delegate { if (symGrid.Selected >= 0) OpenPlan(symGrid.Selected, null); };
            probButtons.Controls.Add(probNext);
            LinkButton all = new LinkButton("Não sei dizer: fazer o check-up completo", null);
            all.Click += delegate { OpenPlan(-1, null); };
            probButtons.Controls.Add(all);
            probFlow.Controls.Add(probButtons);

            planBack = new LinkButton("Voltar", "back");
            planBack.Click += delegate { if (planSymptom >= 0) OpenProblem(); else Navigate(P_HOME); };
            probFlow.Controls.Add(planBack);
            planHead = new StepHeader();
            probFlow.Controls.Add(planHead);
            planKnown = new ListCard("O que já sabemos");
            planCard = new PlanCard();
            planCols = new TwoColumns(planKnown, planCard);
            probFlow.Controls.Add(planCols);
            planBefore = new ListCard("Antes de começar");
            probFlow.Controls.Add(planBefore);
            planButtons = new ButtonRow();
            planGo = new FlatButton("Começar verificação", true);
            planGo.Click += delegate
            {
                if (planItems.Count == 0) { Navigate(P_DIAG); return; }
                StartPlan(new List<object[]>(planItems), true);
            };
            planButtons.Controls.Add(planGo);
            LinkButton later = new LinkButton("Agora não", null);
            later.Click += delegate { Navigate(P_HOME); };
            planButtons.Controls.Add(later);
            probFlow.Controls.Add(planButtons);
            ShowProblemStep(false);
        }

        void AddSym(string icon, Color tint, string title, string desc)
        {
            SymptomGrid.Item it = new SymptomGrid.Item();
            it.Icon = icon; it.Tint = tint; it.Title = title; it.Desc = desc;
            symGrid.Items.Add(it);
        }

        void ShowProblemStep(bool plan)
        {
            probFlow.SuspendLayout();
            probBack.Visible = probHead.Visible = symGrid.Visible = probButtons.Visible = !plan;
            planBack.Visible = planHead.Visible = planCols.Visible = planBefore.Visible = planButtons.Visible = plan;
            probFlow.ResumeLayout();
            probFlow.AutoScrollPosition = new Point(0, 0);
            probFlow.Relayout();
        }

        /// <summary>Quantas vezes o sintoma aparece nos registros (para o selo "O DiagIT registrou N vezes").</summary>
        int SymptomCount(int sym)
        {
            Diagnosis d = lastDiag;
            if (d == null) return 0;
            int n = 0;
            foreach (Finding f in d.Findings)
            {
                if (f.Sev == Sev.Ok) continue;
                if (sym == 0 && f.BugCode >= 0) n += Math.Max(1, f.Count);
                if (sym == 2 && f.Title.Contains(" fechou com erro (")) n++;
                foreach (string pre in SymEvidence[sym])
                    if (f.Title.StartsWith(pre, StringComparison.Ordinal) && !pre.StartsWith("Partida") && !pre.StartsWith("Tempo") && !pre.StartsWith("Gargalo"))
                        n += Math.Max(1, Analyzer.Occurrences(f));
            }
            return n;
        }

        void OpenProblem()
        {
            int best = -1, bestN = 0;
            for (int i = 0; i < symGrid.Items.Count; i++)
            {
                int n = SymptomCount(i);
                symGrid.Items[i].Badge = n > 0 ? "O DiagIT registrou " + (n == 1 ? "1 vez" : n + " vezes") : null;
                if (n > bestN) { bestN = n; best = i; }
            }
            if (symGrid.Selected < 0) symGrid.Selected = best;
            probNext.Enabled = symGrid.Selected >= 0;
            ShowProblemStep(false);
            Navigate(P_PROBLEM);
            symGrid.Invalidate();
        }

        /// <summary>Plano da verificação para o sintoma escolhido (-1 = check-up; kinds força as peças).</summary>
        void OpenPlan(int sym, List<TestKind> kinds)
        {
            planSymptom = sym;
            Diagnosis d = lastDiag;
            List<TestKind> want = kinds != null ? new List<TestKind>(kinds) : sym >= 0 ? new List<TestKind>(SymKinds[sym]) : new List<TestKind>(KindOrder);
            // não repete o que já passou depois do problema (só para os sintomas; o check-up pedido é feito como pedido)
            List<TestKind> passed = new List<TestKind>();
            if (sym >= 0 && d != null)
                foreach (TestKind k in want.ToArray())
                {
                    CompState s = d.States[(int)KindComp(k)];
                    if (s != null && s.Status == CompStatus.Ok && s.Passed != null && !s.Retest) { want.Remove(k); passed.Add(k); }
                }
            planItems.Clear();
            planItems.AddRange(QuickPlan(want));

            planHead.Set(sym >= 0 ? "PASSO 2 DE 3" : "CHECK-UP", sym >= 0 ? SymPlanTitle[sym] : "Check-up do seu PC",
                sym >= 0 ? "O DiagIT não repete o que já foi testado. Ele coloca à prova só o que falta, e você não precisa ficar olhando."
                         : "Cada peça é colocada no máximo e o DiagIT confere cada resultado. Você não precisa ficar olhando.");

            // o que já sabemos
            planKnown.Rows.Clear();
            Finding ev = null;
            if (d != null && sym >= 0)
                foreach (Finding f in d.Findings)
                {
                    if (f.Sev == Sev.Ok) continue;
                    bool hit = sym == 0 ? f.BugCode >= 0 : sym == 2 && f.Title.Contains(" fechou com erro (");
                    foreach (string pre in SymEvidence[sym]) if (f.Title.StartsWith(pre, StringComparison.Ordinal)) hit = true;
                    if (hit) { ev = f; break; }
                }
            if (ev != null)
                planKnown.Add(ev.Confirmed ? CompStatus.Failed : CompStatus.Suspect, null, ev.Plain,
                    ev.Last > DateTime.MinValue ? "A última vez foi em " + ev.Last.ToString("dd/MM") + " às " + ev.Last.ToString("HH:mm") + "." : "");
            else if (sym >= 0)
                planKnown.Add(CompStatus.Untested, "info", "Nada registrado ainda", "O DiagIT não encontrou esse problema nos registros, mas os testes conferem as peças ligadas a ele.");
            if (passed.Count > 0)
            {
                List<string> n = new List<string>();
                DateTime last = DateTime.MinValue;
                foreach (TestKind k in passed)
                {
                    n.Add(KindName(k));
                    CompState s = d.States[(int)KindComp(k)];
                    if (s.Passed.Start > last) last = s.Passed.Start;
                }
                planKnown.Add(CompStatus.Ok, null, (n.Count == 1 ? n[0] + " já passou" : JoinCap(n) + " já passaram") + " nos testes", "Testados até " + last.ToString("dd/MM") + ", depois do problema. Nenhum erro.");
            }
            if (want.Count > 0)
            {
                List<string> n = new List<string>();
                foreach (TestKind k in want) n.Add(KindName(k));
                planKnown.Add(CompStatus.Untested, null, sym >= 0 ? "Falta testar" : "Vamos testar", JoinCap(n) + ".");
            }

            // o que vamos fazer
            planCard.Steps.Clear();
            foreach (object[] it in planItems)
            {
                PlanCard.Step st = new PlanCard.Step();
                TestKind k = (TestKind)it[0];
                st.Title = KindName(k); st.Desc = KindDesc(k);
                st.Duration = Math.Max(1, (int)it[1] / 60) + " min";
                planCard.Steps.Add(st);
            }
            planCard.Total = planItems.Count > 0 ? "Total: cerca de " + Math.Max(1, (int)Math.Round(PlanSeconds(planItems) / 60.0)) + " min" : "";
            planCard.Empty = "As peças ligadas a esse problema já passaram nos testes. O próximo passo é ver como resolver.";

            // antes de começar
            planBefore.Rows.Clear();
            string game;
            if (w.GameActive(out game)) planBefore.Add(CompStatus.Suspect, null, "Feche o jogo antes de começar", game);
            else planBefore.Add(CompStatus.Ok, null, "", "Nenhum jogo aberto");
            Sample ls = w.Last;
            if (ls != null && !double.IsNaN(ls[F.CpuTemp])) planBefore.Add(CompStatus.Ok, null, "", "Sensores de temperatura funcionando");
            else planBefore.Add(CompStatus.Suspect, null, "Sem a temperatura do processador", "Abra o DiagIT como administrador para medir tudo durante o teste.");
            bool power = planItems.Exists(delegate(object[] o) { return (TestKind)o[0] == TestKind.Combined; });
            if (power) planBefore.Add(CompStatus.Untested, "info", "", "Salve o que estiver aberto: o PC pode reiniciar se a fonte falhar.");
            planBefore.Callout = "É seguro. O DiagIT para sozinho se alguma peça esquentar demais. Se o PC desligar durante o teste, isso já é a resposta: ao religar, o DiagIT mostra o que aconteceu.";

            SetCaption(planGo, planItems.Count > 0 ? "Começar verificação" : "Ver como resolver");
            planGo.Enabled = planItems.Count == 0 || !runner.Busy;
            ShowProblemStep(true);
            Navigate(P_PROBLEM);
            planKnown.Invalidate(); planCard.Invalidate(); planBefore.Invalidate(); planHead.Invalidate();
        }

        static string JoinCap(List<string> n)
        {
            if (n.Count <= 1) return n.Count == 1 ? n[0] : "";
            List<string> rest = n.GetRange(1, n.Count - 1).ConvertAll<string>(delegate(string x) { return x.ToLowerInvariant().Replace("ram", "RAM"); });
            rest.Insert(0, n[0]);
            return string.Join(", ", rest.GetRange(0, rest.Count - 1).ToArray()) + " e " + rest[rest.Count - 1];
        }
    }
}
