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
        public const int P_LIVE = 0, P_TESTS = 1, P_DIAG = 2, P_HW = 3, P_FIX = 4, P_SESS = 5, P_SET = 6, PAGES = 7;
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
        Label lblSideStatus, lblSideSensors;

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
            Text = "DiagIT — diagnóstico de hardware em jogos";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Font(9f);
            ClientSize = new Size(Theme.S(1240), Theme.S(780));
            MinimumSize = new Size(Theme.S(1000), Theme.S(660));
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
            BuildTray();
            runner.Finished += delegate(TestResult r) { Post(delegate { OnTestFinished(r); }); };

            Navigate(0);

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
                            "A última gravação terminou de forma abrupta (queda do PC ou travamento). Abra o Diagnóstico para ver as evidências.",
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
            side.Width = Theme.S(220);
            side.BackColor = Theme.Side;
            Controls.Add(side);

            Label logo = new Label();
            logo.Text = "DiagIT";
            logo.Font = Theme.Font(17f, FontStyle.Bold);
            logo.ForeColor = Theme.Text;
            logo.SetBounds(Theme.S(20), Theme.S(18), Theme.S(190), Theme.S(34));
            side.Controls.Add(logo);
            Label sub = new Label();
            sub.Text = "Diagnóstico de hardware  •  v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            sub.Font = Theme.Font(8f);
            sub.ForeColor = Theme.Muted;
            sub.SetBounds(Theme.S(22), Theme.S(50), Theme.S(190), Theme.S(18));
            side.Controls.Add(sub);

            string[] caps = { "Painel ao vivo", "Testes", "Diagnóstico", "Hardware", "Correções", "Sessões", "Configurações" };
            string[] gl = { "◉", "▶", "✚", "▦", "✔", "☰", "⚙" };
            navs = new NavButton[PAGES];
            for (int i = 0; i < PAGES; i++)
            {
                NavButton b = new NavButton();
                b.Caption = caps[i];
                b.Glyph = gl[i];
                b.SetBounds(0, Theme.S(92) + i * Theme.S(46), side.Width, Theme.S(44));
                int idx = i;
                b.Click += delegate { Navigate(idx); };
                side.Controls.Add(b);
                navs[i] = b;
            }

            lblSideStatus = new Label();
            lblSideStatus.Font = Theme.Font(9f, FontStyle.Bold);
            lblSideStatus.ForeColor = Theme.Muted;
            lblSideStatus.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            lblSideStatus.SetBounds(Theme.S(20), side.Height - Theme.S(86), Theme.S(190), Theme.S(36));
            side.Controls.Add(lblSideStatus);
            lblSideSensors = new Label();
            lblSideSensors.Font = Theme.Font(8f);
            lblSideSensors.ForeColor = Theme.Muted;
            lblSideSensors.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            lblSideSensors.SetBounds(Theme.S(20), side.Height - Theme.S(48), Theme.S(190), Theme.S(36));
            side.Controls.Add(lblSideSensors);
            side.Resize += delegate
            {
                lblSideStatus.Top = side.Height - Theme.S(86);
                lblSideSensors.Top = side.Height - Theme.S(48);
            };

            content = new Panel();
            content.Dock = DockStyle.Fill;
            content.Padding = new Padding(Theme.S(20));
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
            if (idx == curPage) return;
            curPage = idx;
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visible = i == idx;
                navs[i].Selected = i == idx;
                navs[i].Invalidate();
            }
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
            l.Font = Theme.Font(16f, FontStyle.Bold);
            l.ForeColor = Theme.Text;
            l.AutoSize = false;
            l.Height = Theme.S(36);
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
            tray.BalloonTipClicked += delegate { ShowFromTray(); Navigate(runner.Busy ? P_TESTS : P_DIAG); };
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
                Diagnosis d = Analyzer.Run(sds, events, disks, 30, TestStore.ListAll(), hw);
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
            if (runner.Busy)
            {
                lblSideStatus.ForeColor = Theme.Warn;
                lblSideStatus.Text = "● TESTANDO\n" + TestResult.KindLabel(runner.Current.Kind);
            }
            else if (w.Current != null)
            {
                lblSideStatus.ForeColor = Theme.Bad;
                lblSideStatus.Text = "● GRAVANDO\n" + Trim(w.Current.Game, 24);
            }
            else
            {
                lblSideStatus.ForeColor = Theme.Good;
                lblSideStatus.Text = "● Monitorando\naguardando um jogo";
            }
            bool cpuT = false;
            Sample ls = w.Last;
            if (ls != null) cpuT = !double.IsNaN(ls[F.CpuTemp]);
            lblSideSensors.Text = cpuT ? "Sensores: CPU, GPU e placa-mãe" : w.Sensors.Available ? "Sensores: só GPU\n(CPU requer administrador)" : "Sensores: não encontrados";
            lblSideSensors.ForeColor = cpuT ? Theme.Muted : Theme.Warn;
            tray.Text = w.Current != null ? "DiagIT — gravando " + Trim(w.Current.Game, 40) : "DiagIT — monitorando";

            if (!Visible || WindowState == FormWindowState.Minimized) return;
            if (curPage == P_LIVE) RefreshLive();
            else if (curPage == P_TESTS) RefreshTestRun();
            else if (curPage == P_SESS && selSess != null && selSess.Status == "running") UpdateSelectedSession(false);
        }

        static string Trim(string s, int n) { return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }

        // ------------------------------------------------------------------ DIAGNÓSTICO (página)
        Segmented cmbScope;
        Label lblVerdict, lblDiagStatus;
        CompStateList bars;
        FindingList flist;
        FlatButton btnAnalyze, btnExport;
        Diagnosis lastDiag;
        List<SessionData> lastDiagSessions = new List<SessionData>();

        void BuildDiag()
        {
            Panel p = pages[P_DIAG];
            p.Controls.Add(Header("Diagnóstico", ""));

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
            bars.RowClicked += delegate(int comp)
            {
                if (lastDiag == null || lastDiag.States[comp] == null || !lastDiag.States[comp].HasTest) return;
                Navigate(P_TESTS);
                HighlightTest(lastDiag.States[comp].Test);
            };
            left.Controls.Add(bars);

            lblVerdict = new Label();
            lblVerdict.Dock = DockStyle.Top;
            lblVerdict.Height = Theme.S(150);
            lblVerdict.Font = Theme.Font(10f, FontStyle.Bold);
            lblVerdict.ForeColor = Theme.Text;
            lblVerdict.Padding = new Padding(0, 0, 0, Theme.S(10));
            lblVerdict.Text = "Analisando seus eventos do Windows e partidas gravadas...";
            left.Controls.Add(lblVerdict);   // adicionado depois = encaixado primeiro (veredito em cima, barras abaixo)

            flist = new FindingList();
            flist.Dock = DockStyle.Fill;
            t.Controls.Add(flist, 1, 0);
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
            lblVerdict.Text = d.Verdict;
            lblVerdict.ForeColor = d.Verdict.StartsWith("PROBLEMA") ? Theme.Bad : Theme.Text;
            bars.States = d.States;
            bars.Invalidate();
            lblDiagStatus.Text = "Base: " + d.Tests.Count + " teste(s), " + d.Sessions + " sessão(ões) gravada(s), " + d.EventsTotal + " eventos do Windows (" + d.Days + " dias).";
            flist.SetFindings(d.Findings);
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
