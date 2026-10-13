using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DiagIT
{
    public partial class MainForm : Form
    {
        // páginas: as 5 primeiras são o caminho de quem é leigo; as demais ficam em "Avançado"
        public const int P_HOME = 0, P_PROBLEM = 1, P_TESTS = 2, P_DIAG = 3, P_FIX = 4, P_LIVE = 5, P_HW = 6, P_SESS = 7, P_SET = 8, P_RUN = 9, P_GUIDE = 10, PAGES = 11;
        readonly Watcher w;
        readonly AppSettings cfg;
        readonly TestRunner runner;
        HwSnapshot hw;
        DateTime hwAt = DateTime.MinValue;
        NotifyIcon tray;
        bool reallyExit, allowVisible, trayHintShown;

        Panel side, content;
        NavButton[] navs;
        Panel[] pages;
        int curPage = -1;
        System.Windows.Forms.Timer ui;

        // sidebar
        SideStatus sideStatus;

        // cache de análise
        List<WinEvent> events;
        DateTime eventsAt = DateTime.MinValue;
        List<DiskHealth> disks = new List<DiskHealth>();
        MachineInfo machine;
        volatile bool analyzing;

        public MainForm(AppSettings cfg, Watcher w, TestRunner runner, bool startHidden, List<SessionInfo> recovered, TestResult crashedTest)
        {
            this.cfg = cfg;
            this.w = w;
            this.runner = runner;
            allowVisible = !startHidden;

            AutoScaleMode = AutoScaleMode.None;
            Text = "DiagIT " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + " — check-up do seu PC";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Font(9f);
            ClientSize = new Size(Theme.S(1280), Theme.S(820));
            MinimumSize = new Size(Theme.S(1000), Theme.S(680));
            StartPosition = FormStartPosition.CenterScreen;
            Icon = AppIcon.Get();

            BuildShell();
            BuildLive();
            BuildTests();
            BuildDiag();
            BuildHardware();
            BuildFixes();
            BuildSessions();
            BuildSettings();
            BuildHome();      // depois dos testes: o Início usa a lista de testes para montar o check-up
            BuildProblem();
            BuildRun();
            BuildGuide();
            BuildTray();
            runner.Finished += delegate(TestResult r) { Post(delegate { OnTestFinished(r); }); };

            Navigate(P_HOME);

            ui = new System.Windows.Forms.Timer();
            ui.Interval = 1000;
            ui.Tick += delegate { OnUiTick(); };
            ui.Start();

            w.SessionStarted += delegate(SessionInfo s) { Post(delegate { OnSessionStarted(s); }); };
            w.SessionEnded += delegate(SessionInfo s) { Post(delegate { OnSessionEnded(s); }); };

            IntPtr h = Handle;   // garante handle para BeginInvoke mesmo iniciando oculto
            w.WindowVisible = !startHidden;

            machine = null;
            ThreadPool.QueueUserWorkItem(delegate
            {
                MachineInfo m = MachineInfo.Read(w.Perf.TotalVramMB);
                machine = m;
            });

            // análise inicial em segundo plano
            Thread t = new Thread(delegate()
            {
                Thread.Sleep(2500);
                RunAnalysis(0, true, delegate(Diagnosis d, List<SessionData> sds)
                {
                    ShowDiagnosis(d, sds);
                    if (crashedTest != null)
                    {
                        Balloon("O PC caiu durante um teste", crashedTest.Summary, ToolTipIcon.Warning);
                        Navigate(P_TESTS);
                        ShowTestResult(crashedTest);
                    }
                    if (recovered != null && recovered.Count > 0)
                        Balloon("Partida anterior interrompida",
                            "A última gravação terminou de forma abrupta (queda do PC ou travamento). Abra os Resultados para ver as evidências.",
                            ToolTipIcon.Warning);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void Post(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { }
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(allowVisible ? value : false);
        }

        public void ShowFromTray()
        {
            allowVisible = true;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            w.WindowVisible = true;
        }

        public void ExitApp()
        {
            reallyExit = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit && cfg.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                w.WindowVisible = false;
                if (!trayHintShown)
                {
                    trayHintShown = true;
                    Balloon("DiagIT continua em segundo plano",
                        "Ele segue monitorando e gravando suas partidas. Clique no ícone da bandeja para reabrir; botão direito para sair.", ToolTipIcon.Info);
                }
                return;
            }
            ui.Stop();
            try { tray.Visible = false; tray.Dispose(); } catch { }
            w.Stop();
            base.OnFormClosing(e);
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { }   // barra de título escura
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (w != null) w.WindowVisible = Visible && WindowState != FormWindowState.Minimized;
        }

        void Balloon(string title, string text, ToolTipIcon icon)
        {
            if (!cfg.Notify) return;
            Post(delegate
            {
                try { tray.ShowBalloonTip(8000, title, text, icon); } catch { }
            });
        }

        // ------------------------------------------------------------------ estrutura (barra lateral + páginas)
        void BuildShell()
        {
            side = new Panel();
            side.Dock = DockStyle.Left;
            side.Width = Theme.S(244);
            side.BackColor = Theme.Side;
            Controls.Add(side);

            BrandMark brand = new BrandMark();
            brand.SetBounds(0, Theme.S(14), side.Width, Theme.S(56));
            side.Controls.Add(brand);

            string[] caps = new string[PAGES];
            string[] ics = new string[PAGES];
            caps[P_HOME] = "Início"; ics[P_HOME] = "home";
            caps[P_PROBLEM] = "Estou com um problema"; ics[P_PROBLEM] = "help";
            caps[P_TESTS] = "Verificar peças"; ics[P_TESTS] = "search";
            caps[P_DIAG] = "Resultados"; ics[P_DIAG] = "clipboard";
            caps[P_FIX] = "Resolver"; ics[P_FIX] = "wrench";
            caps[P_LIVE] = "Ao vivo"; ics[P_LIVE] = "activity";
            caps[P_HW] = "Hardware"; ics[P_HW] = "chip";
            caps[P_SESS] = "Partidas gravadas"; ics[P_SESS] = "gamepad";
            caps[P_SET] = "Configurações"; ics[P_SET] = "gear";

            navs = new NavButton[PAGES];
            int y = Theme.S(90);
            int[] main = { P_HOME, P_PROBLEM, P_TESTS, P_DIAG, P_FIX };
            foreach (int i in main) { navs[i] = NavItem(caps[i], ics[i], i, y); y += Theme.S(46); }
            Label adv = new Label();
            adv.Text = "AVANÇADO";
            adv.Font = Theme.Font(8f, FontStyle.Bold);
            adv.ForeColor = Color.FromArgb(111, 123, 137);
            adv.BackColor = Theme.Side;
            adv.SetBounds(Theme.S(26), y + Theme.S(14), Theme.S(180), Theme.S(20));
            side.Controls.Add(adv);
            y += Theme.S(40);
            int[] more = { P_LIVE, P_HW, P_SESS };
            foreach (int i in more) { navs[i] = NavItem(caps[i], ics[i], i, y); y += Theme.S(46); }

            sideStatus = new SideStatus();
            sideStatus.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            sideStatus.SetBounds(0, side.Height - Theme.S(150), side.Width, Theme.S(84));
            sideStatus.Click += delegate { Navigate(runner.Busy ? P_RUN : w.Current != null ? P_LIVE : P_SESS); };
            side.Controls.Add(sideStatus);
            navs[P_SET] = NavItem(caps[P_SET], ics[P_SET], P_SET, side.Height - Theme.S(58));
            navs[P_SET].Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            side.Resize += delegate
            {
                sideStatus.Top = side.Height - Theme.S(150);
                navs[P_SET].Top = side.Height - Theme.S(58);
            };

            content = new Panel();
            content.Dock = DockStyle.Fill;
            content.Padding = new Padding(Theme.S(36), Theme.S(28), Theme.S(28), Theme.S(20));
            content.BackColor = Theme.Bg;
            Controls.Add(content);
            content.BringToFront();

            pages = new Panel[PAGES];
            for (int i = 0; i < PAGES; i++)
            {
                pages[i] = new Panel();
                pages[i].Dock = DockStyle.Fill;
                pages[i].BackColor = Theme.Bg;
                pages[i].Visible = false;
                content.Controls.Add(pages[i]);
            }
        }

        NavButton NavItem(string caption, string icon, int page, int y)
        {
            NavButton b = new NavButton();
            b.Caption = caption;
            b.Icon = icon;
            b.SetBounds(0, y, side.Width, Theme.S(44));
            b.Click += delegate { if (page == P_PROBLEM) OpenProblem(); else Navigate(page); };
            side.Controls.Add(b);
            return b;
        }

        bool offscreenMode;
        public void StartOffscreen()
        {
            offscreenMode = true;
            allowVisible = true;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-4000, -4000);
        }
        protected override bool ShowWithoutActivation { get { return offscreenMode; } }

        public void NavigateTo(int idx) { Navigate(Math.Max(0, Math.Min(PAGES - 1, idx))); }

        void Navigate(int idx)
        {
            if (idx == P_TESTS && runner.Busy) idx = P_RUN;   // durante a verificação, "Verificar peças" mostra o andamento
            if (idx == curPage) return;
            curPage = idx;
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visible = i == idx;
                if (navs[i] == null) continue;
                navs[i].Selected = i == idx || (idx == P_RUN && i == P_TESTS) || (idx == P_GUIDE && i == P_FIX);
                navs[i].Invalidate();
            }
            if (idx == P_HOME) RefreshHome();
            if (idx == P_RUN) RefreshRun();
            if (idx == P_LIVE) RefreshLive();
            if (idx == P_TESTS) ReloadTestHistory();
            if (idx == P_HW)
            {
                if (hw == null || (DateTime.Now - hwAt).TotalMinutes > 2) RefreshHardware();
                else ShowHardware(hw);
            }
            if (idx == P_FIX) RefreshFixes();
            if (idx == P_SESS) ReloadSessions();
            if (idx == P_SET) RefreshSensorsBox();
        }

        static Label Header(string text, string sub)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.Display(18f);
            l.ForeColor = Theme.Text;
            l.AutoSize = false;
            l.Height = Theme.S(48);
            l.Dock = DockStyle.Top;
            return l;
        }

        static Label Body(string text, bool muted)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = muted ? Theme.Muted : Theme.Text;
            l.Font = Theme.Font(9f);
            l.AutoSize = false;
            return l;
        }

        // ------------------------------------------------------------------ bandeja
        void BuildTray()
        {
            tray = new NotifyIcon();
            tray.Icon = AppIcon.Get();
            tray.Text = "DiagIT — monitorando";
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("Abrir DiagIT", null, delegate { ShowFromTray(); });
            ToolStripItem rec = m.Items.Add("Iniciar gravação manual", null, delegate { ToggleManual(); });
            m.Opening += delegate { rec.Text = w.Current != null ? "Parar gravação" : "Iniciar gravação manual"; };
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Sair", null, delegate { ExitApp(); });
            tray.ContextMenuStrip = m;
            tray.DoubleClick += delegate { ShowFromTray(); };
            tray.BalloonTipClicked += delegate { ShowFromTray(); Navigate(runner.Busy ? P_RUN : P_DIAG); };
            tray.Visible = true;
        }

        void ToggleManual()
        {
            if (w.Current != null) w.EndSession("Gravação manual encerrada");
            else w.StartManual();
            RefreshLive();
        }

        void OnSessionStarted(SessionInfo s)
        {
            Balloon("Jogo detectado: " + s.Game, "O DiagIT começou a gravar esta partida.", ToolTipIcon.Info);
            if (curPage == P_SESS) ReloadSessions();
            RefreshLive();
        }

        void OnSessionEnded(SessionInfo s)
        {
            if (curPage == P_SESS) ReloadSessions();
            RefreshLive();
            if (s.Note == "teste" || s.Game.StartsWith("[Teste]")) return;
            List<SessionInfo> one = new List<SessionInfo>();
            one.Add(s);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    EnsureEvents(false);
                    List<SessionData> sds = LoadData(one);
                    Diagnosis d = Analyzer.Run(sds, events, disks, 30, TestStore.ListAll(), hw);
                    int crit = 0, warn = 0;
                    foreach (Finding f in d.Findings) { if (f.Sev == Sev.Crit) crit++; else if (f.Sev == Sev.Warn) warn++; }
                    string msg = crit + warn == 0
                        ? "Nenhum problema encontrado nesta partida."
                        : crit + " alerta(s) crítico(s) e " + warn + " de atenção. " + d.Verdict;
                    Balloon("Partida encerrada: " + s.Game, msg + (s.Note.Length > 0 ? "\n" + s.Note : ""),
                        crit + warn == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
                }
                catch (Exception e) { Log.Write("Análise pós-sessão: " + e.Message); }
            });
        }

        // ------------------------------------------------------------------ análise (compartilhada)
        void EnsureEvents(bool force)
        {
            if (force || events == null || (DateTime.Now - eventsAt).TotalMinutes > 10)
            {
                events = EventScanner.ScanAll(30, delegate(string m) { SetDiagStatus(m); });
                eventsAt = DateTime.Now;
                disks = DiskInfo.Read();
            }
        }

        static List<SessionData> LoadData(List<SessionInfo> infos)
        {
            List<SessionData> r = new List<SessionData>();
            foreach (SessionInfo si in infos)
            {
                SessionData sd = new SessionData();
                sd.Info = si;
                sd.S = SessionStore.LoadSamples(si);
                r.Add(sd);
            }
            return r;
        }

        List<SessionInfo> ScopeSessions(int scope, SessionInfo selected)
        {
            List<SessionInfo> all = SessionStore.ListAll();
            List<SessionInfo> r = new List<SessionInfo>();
            DateTime cut = DateTime.Now.AddDays(-30);
            if (scope == 1) { if (all.Count > 0) r.Add(all[0]); }
            else if (scope == 2) { if (selected != null) r.Add(selected); }
            else foreach (SessionInfo s in all) if (s.Start >= cut) r.Add(s);
            return r;
        }

        void RunAnalysis(int scope, bool refreshEvents, Action<Diagnosis, List<SessionData>> done)
        {
            if (analyzing) return;
            analyzing = true;
            SessionInfo sel = selSess;
            try
            {
                SetDiagStatus("Lendo eventos do Windows...");
                EnsureEvents(refreshEvents);
                SetDiagStatus("Lendo o hardware (SMART, telas azuis, configuração)...");
                if (hw == null || refreshEvents || (DateTime.Now - hwAt).TotalMinutes > 5) { hw = HwFacts.Collect(w.Sensors); hwAt = DateTime.Now; }
                SetDiagStatus("Analisando...");
                List<SessionData> sds = LoadData(ScopeSessions(scope, sel));
                // o acompanhamento das correções olha todas as partidas dos 30 dias, mesmo com outro escopo selecionado
                List<SessionData> all = scope == 0 ? sds : LoadData(ScopeSessions(0, sel));
                Diagnosis d = Analyzer.Run(sds, events, disks, 30, TestStore.ListAll(), hw, FixLog.Load());
                if (scope != 0) { Diagnosis full = Analyzer.Run(all, events, disks, 30, TestStore.ListAll(), hw, FixLog.Load()); d.Fixes = full.Fixes; }
                Post(delegate { done(d, sds); });
                SetDiagStatus("Análise concluída às " + DateTime.Now.ToString("HH:mm:ss"));
            }
            catch (Exception e)
            {
                Log.Write("RunAnalysis: " + e);
                SetDiagStatus("Erro na análise: " + e.Message);
            }
            finally { analyzing = false; }
        }

        void SetDiagStatus(string s)
        {
            Post(delegate { if (lblDiagStatus != null) lblDiagStatus.Text = s; });
        }

        // ------------------------------------------------------------------ tique da interface
        void OnUiTick()
        {
            // status da barra lateral
            bool cpuT = false;
            Sample ls = w.Last;
            if (ls != null) cpuT = !double.IsNaN(ls[F.CpuTemp]);
            if (runner.Busy)
                sideStatus.Set(Theme.Accent, "Verificação em andamento", "Testando: " + TestResult.KindLabel(runner.Current.Kind) + ". Os jogos ficam bloqueados até terminar.");
            else if (w.Current != null)
                sideStatus.Set(Theme.Bad, "Gravando a partida", Trim(w.Current.Game, 30));
            else if (!cpuT)
                sideStatus.Set(Theme.Warn, "Monitorando suas partidas", w.Sensors.Available ? "Sem a temperatura do processador: abra como administrador." : "Sensores não encontrados.");
            else
                sideStatus.Set(Theme.Good, "Monitorando suas partidas", "O DiagIT grava sozinho quando você joga.");
            tray.Text = w.Current != null ? "DiagIT — gravando " + Trim(w.Current.Game, 40) : "DiagIT — monitorando";

            if (!Visible || WindowState == FormWindowState.Minimized) return;
            if (curPage == P_LIVE) RefreshLive();
            else if (curPage == P_RUN) RefreshRun();
            else if (curPage == P_TESTS) RefreshTestRun();
            else if (curPage == P_SESS && selSess != null && selSess.Status == "running") UpdateSelectedSession(false);
        }

        static string Trim(string s, int n) { return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }

        // ------------------------------------------------------------------ DIAGNÓSTICO (página)
        Segmented cmbScope;
        Label lblDiagStatus;
        VerdictBlock lblVerdict;
        CompStateList bars;
        FindingList flist;
        FlatButton btnAnalyze, btnExport;
        Diagnosis lastDiag;
        List<SessionData> lastDiagSessions = new List<SessionData>();

        void BuildDiag()
        {
            Panel p = pages[P_DIAG];
            p.Controls.Add(Header("Resultados", ""));

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = Theme.S(48);
            p.Controls.Add(top);
            top.BringToFront();

            cmbScope = new Segmented(new string[] { "30 dias", "Última sessão", "Sessão selecionada" }, 0);
            cmbScope.Location = new Point(0, Theme.S(8));
            top.Controls.Add(cmbScope);

            btnAnalyze = new FlatButton("Analisar agora", true);
            btnAnalyze.Location = new Point(cmbScope.Right + Theme.S(14), Theme.S(6));
            btnAnalyze.Click += delegate { StartAnalyzeClick(true); };
            top.Controls.Add(btnAnalyze);

            btnExport = new FlatButton("Exportar relatório HTML", false);
            btnExport.Location = new Point(btnAnalyze.Right + Theme.S(8), Theme.S(6));
            btnExport.Click += delegate { ExportReport(); };
            top.Controls.Add(btnExport);

            lblDiagStatus = new Label();
            lblDiagStatus.ForeColor = Theme.Muted;
            lblDiagStatus.AutoSize = false;
            lblDiagStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblDiagStatus.SetBounds(btnExport.Right + Theme.S(12), Theme.S(6), Theme.S(400), Theme.S(32));
            top.Controls.Add(lblDiagStatus);

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 2;
            t.RowCount = 1;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(470)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.Controls.Add(t);
            t.BringToFront();

            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.Padding = new Padding(0, 0, Theme.S(14), 0);
            t.Controls.Add(left, 0, 0);

            bars = new CompStateList();
            bars.Dock = DockStyle.Fill;
            bars.RowClicked += delegate(int comp) { OpenComponent(comp); };
            left.Controls.Add(bars);

            lblVerdict = new VerdictBlock();
            lblVerdict.Dock = DockStyle.Top;
            lblVerdict.Set("Analisando seu PC...", "Lendo os registros do Windows, os testes e as partidas gravadas.");
            left.Controls.Add(lblVerdict);   // adicionado depois = encaixado primeiro (veredito em cima, barras abaixo)

            flist = new FindingList();
            flist.Dock = DockStyle.Fill;
            flist.CardAction += delegate(Finding f, string action) { OnFindingAction(f, action); };
            t.Controls.Add(flist, 1, 0);
        }

        /// <summary>Clique numa peça: leva ao teste quando ele é o próximo passo; senão mostra o achado que explica o estado.</summary>
        void OpenComponent(int comp)
        {
            if (lastDiag == null || lastDiag.States[comp] == null) return;
            CompState s = lastDiag.States[comp];
            bool testNext = s.HasTest && s.Status != CompStatus.Failed && (s.Passed == null || s.Retest);
            Finding show = s.Cause ?? s.Basis;
            if (!testNext && show != null)
            {
                Navigate(P_DIAG);
                if (flist.Reveal(show)) return;
            }
            if (!s.HasTest) return;
            Navigate(P_TESTS);
            HighlightTest(s.Test);
        }

        void StartAnalyzeClick(bool refreshEvents)
        {
            if (analyzing) return;
            btnAnalyze.Enabled = false;
            int scope = cmbScope.SelectedIndex;
            lblVerdict.Text = "Analisando...";
            Thread t = new Thread(delegate()
            {
                RunAnalysis(scope, refreshEvents, delegate(Diagnosis d, List<SessionData> sds) { ShowDiagnosis(d, sds); });
                Post(delegate { btnAnalyze.Enabled = true; });
            });
            t.IsBackground = true;
            t.Start();
        }

        void ShowDiagnosis(Diagnosis d, List<SessionData> sds)
        {
            lastDiag = d;
            lastDiagSessions = sds;
            string vt, vs;
            Analyzer.PlainVerdict(d, out vt, out vs);
            lblVerdict.Set(vt, vs);
            bars.States = d.States;
            bars.Invalidate();
            lblDiagStatus.Text = "Base: " + d.Tests.Count + " teste(s), " + d.Sessions + " sessão(ões) gravada(s), " + d.EventsTotal + " eventos do Windows (" + d.Days + " dias).";
            flist.SetFindings(d.Findings);
            NotifyFixChanges(d);
            RefreshHome();
            if (goResultsAfterRun) { goResultsAfterRun = false; if (curPage == P_RUN) Navigate(P_DIAG); }
        }

        bool goResultsAfterRun;

        /// <summary>"Já fiz isso" / "Passo a passo" num achado.</summary>
        void OnFindingAction(Finding f, string action)
        {
            if (action == "guide" && f.Guide != null) { OpenGuide(f.Guide, f); return; }
            if (action != "fix") return;
            string scope = Analyzer.ScopeOf(f);
            if (MessageBox.Show(this, "Marcar como feito: \"" + Analyzer.FirstSentence(f.Advice) + "\"\n\nO DiagIT vai acompanhar se os " + Analyzer.ScopeName(scope) +
                " voltam e mostrar o resultado no Início.", "Já fiz isso", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            FixLog.Add("achado:" + (f.BugCode >= 0 ? "0x" + f.BugCode.ToString("X") : f.Title.Length > 40 ? f.Title.Substring(0, 40) : f.Title),
                "Segui o conselho para: " + f.Plain, scope);
            StartAnalyzeClick(false);
        }

        /// <summary>Avisa quando uma correção passa a "resolvido" ou "não resolveu".</summary>
        void NotifyFixChanges(Diagnosis d)
        {
            if (d.Fixes.Count == 0) return;
            List<FixEntry> all = FixLog.Load();
            bool changed = false;
            foreach (FixStatus st in d.Fixes)
            {
                FixEntry e = all.Find(delegate(FixEntry x) { return x.Id == st.Fix.Id; });
                if (e == null || e.LastState == st.State) continue;
                if (st.State == 1) Balloon("Resolvido: " + e.Title, st.Text, ToolTipIcon.Info);
                else if (st.State == 2) Balloon("Ainda não resolveu: " + e.Title, st.Text, ToolTipIcon.Warning);
                e.LastState = st.State; changed = true;
            }
            if (changed) FixLog.Save(all);
        }

        void ExportReport()
        {
            if (lastDiag == null) { MessageBox.Show(this, "Rode a análise primeiro.", "DiagIT"); return; }
            try
            {
                MachineInfo m = machine ?? new MachineInfo();
                string path = Report.Save(Report.Build(lastDiag, m, lastDiagSessions));
                Process.Start(path);
            }
            catch (Exception e) { MessageBox.Show(this, "Não foi possível gerar o relatório:\n" + e.Message, "DiagIT"); }
        }
    }
}
