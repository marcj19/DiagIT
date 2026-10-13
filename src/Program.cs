using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace DiagIT
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Paths.MigrateLegacy();
            // ---- modos sem interface
            if (args.Length >= 2 && args[0] == "--selftest")
                return SelfTest.Run(args[1]);
            if (args.Length >= 2 && args[0] == "--make-icon")
            {
                IconWriter.Write(args[1]);
                return 0;
            }
            if (args.Length >= 4 && args[0] == "--run-test")
                return SelfTest.RunHw(args[1], int.Parse(args[2]), args[3], args.Length >= 5 ? long.Parse(args[4]) : 0);
            if (args.Length >= 2 && args[0] == "--validate-all")
                return SelfTest.ValidateAll(args[1], args.Length >= 3 ? int.Parse(args[2]) : 1);
            if (args.Length >= 2 && args[0] == "--fix-boot")
            {
                // mesma correção do botão "Limpar entradas antigas do boot" (página Resolver)
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                bool ok = Fixes.CleanBootEntries(delegate(string s) { sb.AppendLine(s); });
                BootInfo after = SystemInfo.ReadBoot();
                sb.AppendLine("Depois: " + after.DisplayOrder.Count + " opção(ões) no menu, " + after.Stale().Count + " entrada(s) antiga(s).");
                foreach (BootEntry e in after.Entries) if (e.IsLoader && !e.IsRamdisk) sb.AppendLine("  " + e.Id + " | " + e.Description + " | " + e.OsDevice + (after.DisplayOrder.Contains(e.Id) ? " | no menu" : ""));
                System.IO.File.WriteAllText(args[1], sb.ToString(), System.Text.Encoding.UTF8);
                return ok ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--facts")
                return SelfTest.Facts(args[1]);
            if (args.Length >= 2 && args[0] == "--probe")
                return SelfTest.Probe(args[1]);
            if (args.Length >= 3 && args[0] == "--dump-info")
                return SelfTest.DumpInfo(args[1], args[2]);
            if (args.Length >= 2 && args[0] == "--diag-test")
                return SelfTest.DiagScenarios(args[1]);
            if (args.Length >= 2 && args[0] == "--report-test")
                return SelfTest.ReportTest(args[1]);
            if (args.Length >= 2 && args[0] == "--autostart")
            {
                Paths.Ensure();
                return SysUtil.SetAutostart(args[1] == "on") ? 0 : 1;
            }

            bool elevated = Array.IndexOf(args, "--elevated") >= 0;
            bool background = Array.IndexOf(args, "--background") >= 0;
            bool multi = Array.IndexOf(args, "--multi") >= 0;          // só para testes: permite 2ª instância
            bool offscreen = Array.IndexOf(args, "--offscreen") >= 0;  // só para testes: abre fora da tela, sem roubar o foco

            // abre como administrador (necessário para temperatura da CPU, placa-mãe, SMART e arquivos de tela azul)
            Paths.Ensure();
            if (!elevated && !multi && Array.IndexOf(args, "--no-elevate") < 0 && !SysUtil.IsAdmin() && AppSettings.Load().AlwaysAdmin)
            {
                List<string> a = new List<string>();
                foreach (string s in args) a.Add(s.Contains(" ") ? "\"" + s + "\"" : s);
                a.Add("--elevated");
                if (SysUtil.RelaunchElevated(string.Join(" ", a.ToArray()))) return 0;
            }

            // ---- instância única
            Mutex mutex = null;
            EventWaitHandle showEvt = null;
            bool owner = false;
            try
            {
                mutex = new Mutex(true, multi ? "Local\\DiagIT_Test_" + Environment.TickCount : "Local\\DiagIT_Single", out owner);
                if (!owner && elevated)
                {
                    // acabamos de reiniciar como administrador: espera a instância anterior terminar
                    owner = mutex.WaitOne(10000);
                }
                showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, multi ? "Local\\DiagIT_TestShow_" + Environment.TickCount : "Local\\DiagIT_Show");
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("O DiagIT já está em execução como administrador.\nProcure o ícone na bandeja do sistema (perto do relógio).",
                    "DiagIT", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            if (!owner)
            {
                try { if (showEvt != null) showEvt.Set(); } catch { }
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Write("UI: " + e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log.Write("Fatal: " + e.ExceptionObject); };

            Paths.Ensure();
            AppSettings cfg = AppSettings.Load();
            List<SessionInfo> recovered = SessionStore.RecoverInterrupted();
            if (recovered.Count > 0) Log.Write("Sessões interrompidas recuperadas: " + recovered.Count);
            TestResult crashed = TestStore.RecoverCrashed();
            if (crashed != null)
            {
                Log.Write("Teste interrompido por queda do PC: " + crashed.Id);
                // a sessão do teste não é "partida interrompida": o teste já registra a queda
                recovered.RemoveAll(delegate(SessionInfo s) { return s.Id == crashed.SessionId; });
            }

            Watcher w = new Watcher(cfg);
            w.Start();
            TestRunner runner = new TestRunner(w);

            MainForm form = new MainForm(cfg, w, runner, background || cfg.StartMinimized, recovered, crashed);

            // outra execução do app pede para mostrar a janela
            Thread waiter = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        showEvt.WaitOne();
                        form.BeginInvoke((Action)delegate { form.ShowFromTray(); });
                    }
                    catch { return; }
                }
            });
            waiter.IsBackground = true;
            waiter.Start();

            if (offscreen) form.StartOffscreen();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--screenshots") form.ScheduleScreenshots(args[i + 1], Array.IndexOf(args, "--with-run") >= 0);   // só para desenvolvimento
            foreach (string a in args) if (a.StartsWith("--page=")) { int pg; if (int.TryParse(a.Substring(7), out pg)) form.NavigateTo(pg); }
            Microsoft.Win32.SystemEvents.SessionEnding += delegate { w.EndSession("Windows encerrando"); };

            Application.Run(form);
            try { mutex.ReleaseMutex(); } catch { }
            return 0;
        }
    }
}
