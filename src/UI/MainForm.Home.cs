using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DiagIT
{
    public partial class MainForm
    {
        FlowPanel homeFlow;
        HeroCard hero;
        FlatButton heroPrimary, heroSecondary;
        PartsGrid parts;
        readonly List<StepRow> homeSteps = new List<StepRow>();
        SectionTitle fixesTitle;
        FixList fixList;

        static readonly TestKind[] KindOrder = { TestKind.Cpu, TestKind.Ram, TestKind.Gpu, TestKind.Disk, TestKind.Combined };

        static string KindName(TestKind k)
        {
            switch (k)
            {
                case TestKind.Cpu: return "Processador";
                case TestKind.Ram: return "Memória RAM";
                case TestKind.Gpu: return "Placa de vídeo";
                case TestKind.Disk: return "Armazenamento";
                default: return "Fonte de energia";
            }
        }

        static Comp KindComp(TestKind k)
        {
            switch (k)
            {
                case TestKind.Cpu: return Comp.Cpu;
                case TestKind.Ram: return Comp.Ram;
                case TestKind.Gpu: return Comp.Gpu;
                case TestKind.Disk: return Comp.Disk;
                default: return Comp.Psu;
            }
        }

        static string JoinLower(List<string> n)
        {
            List<string> l = n.ConvertAll<string>(delegate(string x) { return x.ToLowerInvariant().Replace("ram", "RAM"); });
            if (l.Count <= 1) return l.Count == 1 ? l[0] : "";
            return string.Join(", ", l.GetRange(0, l.Count - 1).ToArray()) + " e " + l[l.Count - 1];
        }

        static void SetCaption(FlatButton b, string text)
        {
            b.Text = text;
            b.Width = Math.Max(Theme.S(90), TextRenderer.MeasureText(text, b.Font).Width + Theme.S(40));
        }

        void BuildHome()
        {
            Panel p = pages[P_HOME];
            homeFlow = new FlowPanel();
            homeFlow.Dock = DockStyle.Fill;
            p.Controls.Add(homeFlow);

            hero = new HeroCard();
            heroPrimary = new FlatButton("Fazer o check-up", true);
            heroSecondary = new FlatButton("Estou com um problema", false);
            heroPrimary.Click += delegate { HeroPrimaryClick(); };
            heroSecondary.Click += delegate { OpenProblem(); };
            hero.AddButton(heroPrimary);
            hero.AddButton(heroSecondary);
            homeFlow.Controls.Add(hero);

            homeFlow.Controls.Add(new SectionTitle("Suas peças", true));
            parts = new PartsGrid();
            parts.TileClicked += delegate(int c) { OpenComponent(c); };
            homeFlow.Controls.Add(parts);
            homeFlow.Controls.Add(new SectionTitle("O que fazer agora", false));
            fixesTitle = new SectionTitle("Suas correções", false);
            fixList = new FixList();
            fixList.RemoveClicked += delegate(FixStatus st)
            {
                if (MessageBox.Show(this, "Parar de acompanhar \"" + st.Fix.Title + "\"?", "Suas correções", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                FixLog.Remove(st.Fix.Id);
                if (lastDiag != null) lastDiag.Fixes.Remove(st);
                RefreshHome();
            };
            RefreshHome();
        }

        /// <summary>Testes que faltam para confirmar o estado das peças (não testadas ou com problema depois da última aprovação).</summary>
        List<TestKind> PendingKinds(Diagnosis d)
        {
            List<TestKind> r = new List<TestKind>();
            foreach (TestKind k in KindOrder)
            {
                if (d == null) { r.Add(k); continue; }
                CompState s = d.States[(int)KindComp(k)];
                if (s == null) continue;
                if (s.Status == CompStatus.Untested || (s.Status == CompStatus.Suspect && (s.Passed == null || s.Retest))) r.Add(k);
            }
            return r;
        }

        int PlanMinutes(List<TestKind> kinds) { return Math.Max(1, (int)Math.Round(PlanSeconds(QuickPlan(kinds)) / 60.0)); }

        void HeroPrimaryClick()
        {
            List<TestKind> pend = PendingKinds(lastDiag);
            if (pend.Count > 0) OpenPlan(-1, pend);
            else Navigate(P_DIAG);
        }

        static string CleanCpu(string n)
        {
            if (string.IsNullOrEmpty(n)) return "";
            n = n.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "");
            n = Regex.Replace(n, @"\s+\d+-Core Processor|\s+with Radeon Graphics|\s+Processor|\s+CPU\s+@.*$", "", RegexOptions.IgnoreCase);
            return Regex.Replace(n, @"\s+", " ").Trim();
        }

        string[] PartModels()
        {
            string[] m = new string[8];
            MachineInfo mi = machine;
            if (mi != null)
            {
                m[(int)Comp.Cpu] = CleanCpu(mi.Cpu);
                m[(int)Comp.Gpu] = mi.Gpu.Replace("(R)", "").Replace("(TM)", "").Trim();
                if (mi.RamGB > 0) m[(int)Comp.Ram] = mi.RamGB.ToString("0") + " GB" + (hw != null && hw.RamModules > 1 ? " (" + hw.RamModules + " pentes)" : "");
            }
            if (hw != null && hw.Drives.Count > 0) m[(int)Comp.Disk] = hw.Drives.Count == 1 ? hw.Drives[0].Model : hw.Drives.Count + " discos";
            return m;
        }

        void RefreshHome()
        {
            if (hero == null) return;
            Diagnosis d = lastDiag;
            if (d == null)
            {
                hero.Meta = "Check-up do seu PC";
                hero.Title = "Analisando seu PC...";
                hero.Sub = "Lendo os registros do Windows, os testes e as partidas gravadas. Leva alguns segundos.";
                hero.Basis = "";
                for (int i = 0; i < 8; i++) hero.Status[i] = CompStatus.Untested;
            }
            else
            {
                string t, sub;
                Analyzer.PlainVerdict(d, out t, out sub);
                hero.Title = t; hero.Sub = sub;
                hero.Meta = "Check-up de " + (d.When.Date == DateTime.Today ? "hoje" : d.When.ToString("dd/MM")) + ", " + d.When.ToString("HH:mm");
                hero.Basis = "Baseado em " + d.Tests.Count + (d.Tests.Count == 1 ? " teste, " : " testes, ") + d.Sessions + (d.Sessions == 1 ? " partida gravada" : " partidas gravadas") +
                    " e " + d.Days + " dias de registros do Windows.";
                for (int i = 0; i < 8; i++) hero.Status[i] = d.States[i] == null ? CompStatus.Untested : d.States[i].Status;
            }
            List<TestKind> pend = PendingKinds(d);
            bool anyOk = d != null && Array.Exists(d.States, delegate(CompState s) { return s != null && s.Status == CompStatus.Ok; });
            if (pend.Count > 0) SetCaption(heroPrimary, (anyOk ? "Continuar o check-up" : "Fazer o check-up") + " · cerca de " + PlanMinutes(pend) + " min");
            else SetCaption(heroPrimary, "Ver os resultados");
            heroPrimary.Enabled = d != null && !runner.Busy;

            parts.States = d != null ? d.States : new CompState[8];
            parts.Models = PartModels();

            // "O que fazer agora"
            foreach (StepRow r in homeSteps) { homeFlow.Controls.Remove(r); r.Dispose(); }
            homeSteps.Clear();
            if (d != null)
            {
                int n = 0;
                string[] saved = GuideInProgress();
                Guide inProg = saved != null ? Rules.GuideById(saved[0]) : null;
                if (inProg != null)
                {
                    int st = 0; if (saved.Length > 1) int.TryParse(saved[1], out st);
                    st = Math.Max(0, Math.Min(inProg.Steps.Count - 1, st));
                    StepRow r = new StepRow(++n, "Continuar: " + inProg.Title, "Passo " + (st + 1) + " de " + inProg.Steps.Count + ": " + Rules.Fill(inProg.Steps[st].Title) + ".", "Continuar", true);
                    string gid = inProg.Id;
                    r.Button.Click += delegate { OpenGuide(gid, null); };
                    homeSteps.Add(r);
                }
                if (pend.Count > 0)
                {
                    List<string> names = new List<string>();
                    foreach (TestKind k in pend) names.Add(KindName(k));
                    StepRow r = new StepRow(++n, "Testar " + JoinLower(names), "Cerca de " + PlanMinutes(pend) + " minutos. Feche os jogos antes; você pode sair de perto do PC.", "Começar", true);
                    List<TestKind> kinds = pend;
                    r.Button.Click += delegate { OpenPlan(-1, kinds); };
                    homeSteps.Add(r);
                }
                int[] order = { (int)Comp.Software, (int)Comp.Cpu, (int)Comp.Gpu, (int)Comp.Ram, (int)Comp.Disk, (int)Comp.Psu, (int)Comp.Cooling, (int)Comp.Board };
                foreach (int c in order)
                {
                    if (n >= 3) break;
                    CompState s = d.States[c];
                    if (s == null || s.Cause == null) continue;
                    bool covered = s.HasTest && pend.Contains(s.Test) && s.Status != CompStatus.Failed;
                    if (covered || (s.Status != CompStatus.Failed && s.Status != CompStatus.Suspect)) continue;
                    string sub = c == (int)Comp.Software && s.Cause.BugCode >= 0 ? s.Detail : s.Cause.Advice;
                    string guide = s.Cause.Guide;
                    Guide gd = guide != null ? Rules.GuideById(guide) : null;
                    if (gd != null) sub = gd.Title + ": " + Analyzer.FirstSentence(Rules.Fill(gd.Summary));
                    else if (sub.StartsWith("Sem teste automático", StringComparison.Ordinal)) sub = s.Cause.Advice;
                    StepRow r = new StepRow(++n, s.Cause.Plain, gd != null ? sub : Analyzer.FirstSentence(sub), guide != null ? "Resolver passo a passo" : "Ver como resolver", guide != null);
                    int comp = c;
                    Finding cause = s.Cause;
                    r.Button.Click += delegate { if (guide != null) OpenGuide(guide, cause); else OpenComponent(comp); };
                    homeSteps.Add(r);
                }
                // tela azul com as peças aprovadas só em testes curtos: o defeito pode ser intermitente
                bool bsod = d.Findings.Exists(delegate(Finding f) { return f.BugCode >= 0 && f.W.Count > 0; });
                bool longDone = d.Tests.Exists(delegate(TestResult t) { return t.Status == "passed" && t.PlannedSec >= 1800 && t.Start > DateTime.Now.AddDays(-30); });
                if (n < 3 && bsod && !longDone && pend.Count == 0)
                {
                    StepRow r = new StepRow(++n, "Fazer o teste longo esta noite", "As peças passaram nos testes rápidos, mas defeitos que aparecem de vez em quando só se mostram depois de horas. Cerca de 3 horas; o PC não dorme até terminar.", "Agendar para agora", false);
                    r.Button.Click += delegate { StartLongPlan(3); };
                    homeSteps.Add(r);
                }
                if (n == 0)
                    homeSteps.Add(new StepRow(1, "Tudo certo por aqui", "O DiagIT continua de olho nas suas partidas e avisa se algo mudar.", null, false));
            }
            foreach (StepRow r in homeSteps) homeFlow.Controls.Add(r);
            // correções acompanhadas, depois dos passos (a ordem do FlowPanel é a de inclusão)
            homeFlow.Controls.Remove(fixesTitle); homeFlow.Controls.Remove(fixList);
            fixList.Items.Clear();
            if (d != null) fixList.Items.AddRange(d.Fixes);
            if (fixList.Items.Count > 0) { homeFlow.Controls.Add(fixesTitle); homeFlow.Controls.Add(fixList); }
            fixList.Invalidate();
            homeFlow.Relayout();
        }
    }
}
