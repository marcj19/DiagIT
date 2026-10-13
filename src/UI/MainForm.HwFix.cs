using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DiagIT
{
    public partial class MainForm
    {
        // ================================================================ HARDWARE
        FactList factList;
        Label lblHwStatus;
        FlatButton btnHwRefresh;

        void BuildHardware()
        {
            Panel p = pages[P_HW];
            Panel scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            Theme.Dark(scroll);
            p.Controls.Add(scroll);

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = Theme.S(44);
            p.Controls.Add(top);
            btnHwRefresh = new FlatButton("Atualizar leitura", false);
            btnHwRefresh.Location = new Point(0, Theme.S(4));
            btnHwRefresh.Click += delegate { RefreshHardware(); };
            top.Controls.Add(btnHwRefresh);
            lblHwStatus = Body("", true);
            lblHwStatus.SetBounds(btnHwRefresh.Right + Theme.S(12), Theme.S(4), Theme.S(700), Theme.S(32));
            lblHwStatus.TextAlign = ContentAlignment.MiddleLeft;
            top.Controls.Add(lblHwStatus);
            p.Controls.Add(Header("Hardware", ""));

            factList = new FactList();
            factList.Dock = DockStyle.Top;
            scroll.Controls.Add(factList);
        }

        void RefreshHardware()
        {
            btnHwRefresh.Enabled = false;
            lblHwStatus.Text = "Lendo hardware, discos e registros de travamento...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HwSnapshot h = null;
                try { h = HwFacts.Collect(w.Sensors); }
                catch (Exception e) { Log.Write("Hardware: " + e); }
                Post(delegate
                {
                    btnHwRefresh.Enabled = true;
                    if (h == null) { lblHwStatus.Text = "Falha ao ler o hardware (veja o log)."; return; }
                    hw = h; hwAt = DateTime.Now;
                    ShowHardware(h);
                });
            });
        }

        void ShowHardware(HwSnapshot h)
        {
            factList.Width = factList.Parent.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Theme.S(4);
            factList.SetFacts(h.Facts);
            int bad = 0, warn = 0;
            foreach (Fact f in h.Facts) { if (f.Level == 2) bad++; else if (f.Level == 1) warn++; }
            lblHwStatus.Text = "Lido às " + h.When.ToString("HH:mm:ss") + " — " + (bad + warn == 0 ? "nenhum item fora do normal." :
                bad + " problema(s) e " + warn + " ponto(s) de atenção.") + (SysUtil.IsAdmin() ? "" : "  (sem administrador: parte das leituras indisponível)");
            lblHwStatus.ForeColor = bad > 0 ? Theme.Bad : warn > 0 ? Theme.Warn : Theme.Muted;
        }

        // ================================================================ CORREÇÕES
        TextBox fixConsole;
        readonly List<FlatButton> fixButtons = new List<FlatButton>();
        Label lblFixPage, lblFixDump, lblFixDriver, lblFixPawn, lblFixBoot, lblFixOld, lblFixWu;
        volatile bool fixRunning;

        void BuildFixes()
        {
            Panel p = pages[P_FIX];
            fixConsole = new TextBox();
            fixConsole.Multiline = true;
            fixConsole.ReadOnly = true;
            fixConsole.ScrollBars = ScrollBars.Vertical;
            fixConsole.BackColor = Color.FromArgb(8, 10, 14);
            fixConsole.ForeColor = Color.FromArgb(180, 220, 190);
            fixConsole.BorderStyle = BorderStyle.None;
            fixConsole.Font = new Font("Consolas", 9f);
            fixConsole.Dock = DockStyle.Bottom;
            fixConsole.Height = Theme.S(190);
            fixConsole.Text = "A saída das ferramentas aparece aqui.";
            Theme.Dark(fixConsole);

            Panel scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            Theme.Dark(scroll);
            p.Controls.Add(scroll);
            p.Controls.Add(fixConsole);
            p.Controls.Add(Header("Resolver", ""));

            int y = 0;
            int pageInit, pageMax;
            Fixes.RecommendedPagefile(out pageInit, out pageMax);

            y = FixCard(scroll, y, "Memória virtual (arquivo de paginação)",
                "Perto de 100% de uso, jogos fecham sozinhos ou o PC congela mesmo com RAM livre. Recomendado: tamanho fixo de " +
                pageInit.ToString("N0") + " a " + pageMax.ToString("N0") + " MB no disco do sistema. Com o Windows em SSD, não deixe arquivo no HD (o jogo engasga). Requer reiniciar.",
                out lblFixPage,
                new string[] { "Aplicar recomendado", "Voltar ao automático", "Tirar do HD" },
                new EventHandler[]
                {
                    delegate
                    {
                        if (!NeedAdmin()) return;
                        if (MessageBox.Show(this, "Definir a memória virtual em " + pageInit.ToString("N0") + "–" + pageMax.ToString("N0") + " MB no disco do sistema?\nSerá necessário reiniciar o computador.",
                            "Memória virtual", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                        string msg; Fixes.SetPagefile(pageInit, pageMax, out msg); Console_(msg); RefreshFixes();
                    },
                    delegate
                    {
                        if (!NeedAdmin()) return;
                        string msg; Fixes.RestorePagefileAuto(out msg); Console_(msg); RefreshFixes();
                    },
                    delegate
                    {
                        if (!NeedAdmin()) return;
                        List<string> hdd = Fixes.PagefileOnHdd();
                        if (hdd.Count == 0) { Console_("Nenhum arquivo de paginação em HD (ou o Windows não está em SSD). Nada a alterar."); return; }
                        if (MessageBox.Show(this, "Remover o arquivo de paginação do HD (" + string.Join(", ", hdd.ToArray()) + ":) e manter só no SSD do Windows, com tamanho gerenciado pelo sistema?\nSerá necessário reiniciar o computador.",
                            "Memória virtual", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                        string msg; Fixes.MovePagefileOffHdd(out msg); Console_(msg); RefreshFixes();
                    }
                });

            Label dummy;
            y = FixCard(scroll, y, "Reparar arquivos do Windows (SFC + DISM)",
                "Verifica e repara arquivos corrompidos do sistema, que podem causar travamentos e telas azuis. Leva de 15 a 30 minutos; dá para usar o PC enquanto roda.",
                out dummy, new string[] { "Verificar e reparar" },
                new EventHandler[]
                {
                    delegate
                    {
                        if (!NeedAdmin()) return;
                        if (MessageBox.Show(this, "Executar SFC /scannow e depois DISM /RestoreHealth? Leva de 15 a 30 minutos.", "Reparar Windows",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                        RunTools(new string[][]
                        {
                            new string[] { Fixes.Sys("sfc.exe"), "/scannow", "u" },
                            new string[] { Fixes.Sys("Dism.exe"), "/Online /Cleanup-Image /RestoreHealth", "" }
                        });
                    }
                });

            y = FixCard(scroll, y, "Verificar o sistema de arquivos do disco C:",
                "Procura erros no sistema de arquivos sem precisar reiniciar (chkdsk /scan). Para os setores físicos, use o teste de Armazenamento.",
                out dummy, new string[] { "Verificar disco" },
                new EventHandler[]
                {
                    delegate
                    {
                        if (!NeedAdmin()) return;
                        RunTools(new string[][] { new string[] { Fixes.Sys("chkdsk.exe"), Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\') + " /scan", "" } });
                    }
                });

            y = FixCard(scroll, y, "Teste completo de memória do Windows",
                "Testa 100% da RAM fora do Windows (na inicialização), cobrindo a parte que o teste do DiagIT não alcança. Leva de 15 a 30 minutos e reinicia o PC; o resultado aparece em Resultados.",
                out dummy, new string[] { "Agendar teste" },
                new EventHandler[] { delegate { string msg; Fixes.ScheduleMemoryDiagnostic(out msg); Console_(msg); } });

            y = FixCard(scroll, y, "Limpar entradas antigas do boot",
                "Remove do gerenciador de boot os restos de instalações anteriores do Windows (os que aparecem como \"outro Windows\"). Antes faz um backup completo da configuração de boot.",
                out lblFixBoot, new string[] { "Limpar entradas antigas" },
                new EventHandler[]
                {
                    delegate
                    {
                        if (!NeedAdmin()) return;
                        if (MessageBox.Show(this, "Remover do boot as entradas de instalações antigas do Windows? O Windows atual e as entradas de recuperação não são tocados, e um backup é salvo antes.",
                            "Boot", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                        fixConsole.Clear();
                        Fixes.CleanBootEntries(delegate(string s) { Console_(s); });
                        RefreshFixes();
                    }
                });

            y = FixCard(scroll, y, "Remover a instalação anterior do Windows (Windows.old)",
                "Libera o espaço ocupado pela instalação antiga. Antes, copie qualquer arquivo seu que esteja em Windows.old\\Users. Na Limpeza de Disco, clique em \"Limpar arquivos do sistema\" e marque \"Instalações anteriores do Windows\".",
                out lblFixOld, new string[] { "Abrir pasta", "Abrir Limpeza de Disco" },
                new EventHandler[]
                {
                    delegate { try { Process.Start(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "Windows.old")); } catch { } },
                    delegate { try { Process.Start(Fixes.Sys("cleanmgr.exe"), "/d " + Path.GetPathRoot(Environment.SystemDirectory).Substring(0, 2)); } catch { } }
                });

            y = FixCard(scroll, y, "Registro do motivo das telas azuis",
                "Com o registro ativo, cada tela azul deixa um arquivo com o código exato do erro, que o DiagIT lê automaticamente.",
                out lblFixDump, new string[] { "Ativar registro" },
                new EventHandler[]
                {
                    delegate
                    {
                        if (!NeedAdmin()) return;
                        string msg; Fixes.EnableMinidumps(out msg); Console_(msg); RefreshFixes();
                    }
                });

            y = FixCard(scroll, y, "Driver de vídeo",
                "Driver corrompido é a causa mais comum de \"o driver de vídeo parou de responder\". Instalação limpa: baixe o driver no site do fabricante da sua placa, use o DDU (Display Driver Uninstaller) em modo de segurança para remover o atual e instale o novo.",
                out lblFixDriver, new string[] { "Abrir página de drivers", "Passo a passo da instalação limpa" },
                new EventHandler[]
                {
                    delegate
                    {
                        VideoVendor v = Rules.VideoFor(machine != null ? machine.Gpu : hw != null ? hw.GpuName : "");
                        try { Process.Start(v.DriverSite.Length > 0 ? v.DriverSite : "ms-settings:windowsupdate-optionalupdates"); } catch { }
                    },
                    delegate
                    {
                        VideoVendor v = Rules.VideoFor(machine != null ? machine.Gpu : hw != null ? hw.GpuName : "");
                        string brand = v.Brand.Length > 0 ? v.Brand : "do fabricante da sua placa";
                        Console_("INSTALAÇÃO LIMPA DO DRIVER DE VÍDEO\r\n" +
                            "1) Baixe o driver mais recente " + (v.Brand.Length > 0 ? "da " : "") + brand + " (botão ao lado) e o DDU em guru3d.com (Display Driver Uninstaller).\r\n" +
                            "2) Desconecte a internet (evita que o Windows instale um driver sozinho).\r\n" +
                            "3) Reinicie em Modo de Segurança, abra o DDU, escolha GPU / " + v.DduName + " e clique em \"Limpar e reiniciar\".\r\n" +
                            "4) Instale o driver baixado, reconecte a internet.\r\n" +
                            "5) Rode o teste de Placa de vídeo no DiagIT para confirmar.");
                    }
                });

            y = FixCard(scroll, y, "Impedir o Windows Update de trocar drivers",
                "Evita que o Windows Update troque o driver de vídeo que você instalou (causa travamentos e erro de versão no painel do driver). " +
                "Depois disso, atualize os drivers pelo site do fabricante.",
                out lblFixWu, new string[] { "Impedir trocas de driver", "Permitir de novo" },
                new EventHandler[]
                {
                    delegate
                    {
                        if (!NeedAdmin()) return;
                        if (MessageBox.Show(this, "Impedir que o Windows Update instale drivers?\nAs atualizações do Windows continuam normais; drivers passam a ser atualizados só por você.",
                            "Windows Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                        string msg; Fixes.SetWuDriversBlocked(true, out msg); Console_(msg); RefreshFixes();
                    },
                    delegate
                    {
                        if (!NeedAdmin()) return;
                        string msg; Fixes.SetWuDriversBlocked(false, out msg); Console_(msg); RefreshFixes();
                    }
                });

            y = FixCard(scroll, y, "Driver de sensores (PawnIO)",
                "Necessário para o DiagIT ler a temperatura da CPU, as tensões da fonte e as ventoinhas da placa-mãe.",
                out lblFixPawn, new string[] { "Instalar / atualizar PawnIO" },
                new EventHandler[]
                {
                    delegate
                    {
                        if (MessageBox.Show(this, "Instalar o driver PawnIO (namazso.PawnIO) pelo winget? O Windows pedirá permissão de administrador.", "PawnIO",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                        RunTools(new string[][] { new string[] { "winget", "install --id namazso.PawnIO --source winget --exact --accept-package-agreements --accept-source-agreements", "" } });
                    }
                });
        }

        int FixCard(Panel parent, int y, string title, string desc, out Label status, string[] buttons, EventHandler[] actions)
        {
            Panel c = new Panel();
            c.BackColor = Theme.Panel;
            c.SetBounds(0, y, Theme.S(900), Theme.S(118));
            c.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            Label t = Body(title, false);
            t.Font = Theme.Font(10.5f, FontStyle.Bold);
            t.BackColor = Theme.Panel;
            t.SetBounds(Theme.S(14), Theme.S(10), Theme.S(860), Theme.S(22));
            c.Controls.Add(t);
            Label d = Body(desc, true);
            d.BackColor = Theme.Panel;
            d.SetBounds(Theme.S(14), Theme.S(34), Theme.S(860), Theme.S(36));
            c.Controls.Add(d);
            Label st = Body("", false);
            st.BackColor = Theme.Panel;
            st.Font = Theme.Font(8.5f, FontStyle.Bold);
            st.SetBounds(Theme.S(14), Theme.S(72), Theme.S(520), Theme.S(36));
            c.Controls.Add(st);
            status = st;
            int bx = Theme.S(886);
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                FlatButton b = new FlatButton(buttons[i], i == 0);
                bx -= b.Width;
                b.Location = new Point(bx, Theme.S(76));
                b.Click += actions[i];
                c.Controls.Add(b);
                fixButtons.Add(b);
                bx -= Theme.S(8);
            }
            parent.Controls.Add(c);
            parent.Resize += delegate
            {
                int w = Math.Max(Theme.S(700), parent.ClientSize.Width - Theme.S(4));
                c.Width = w;
                t.Width = w - Theme.S(28); d.Width = w - Theme.S(28);
                int x = w - Theme.S(14);
                List<FlatButton> bs = new List<FlatButton>();
                foreach (Control ctl in c.Controls) if (ctl is FlatButton) bs.Add((FlatButton)ctl);
                for (int i = 0; i < bs.Count; i++) { x -= bs[i].Width; bs[i].Left = x; x -= Theme.S(8); }
                st.Width = Math.Max(Theme.S(200), x - Theme.S(20));
            };
            return y + c.Height + Theme.S(10);
        }

        bool NeedAdmin()
        {
            if (SysUtil.IsAdmin()) return true;
            if (MessageBox.Show(this, "Esta correção precisa de privilégios de administrador. Reiniciar o DiagIT como administrador agora?", "Resolver",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                if (SysUtil.RelaunchElevated("--elevated --page=" + P_FIX)) ExitApp();
            }
            return false;
        }

        void Console_(string s)
        {
            Post(delegate
            {
                if (fixConsole.Text == "A saída das ferramentas aparece aqui.") fixConsole.Clear();
                fixConsole.AppendText(s.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n") + "\r\n");
            });
        }

        void RunTools(string[][] steps)
        {
            if (fixRunning) { MessageBox.Show(this, "Já existe uma correção em andamento.", "Resolver"); return; }
            fixRunning = true;
            foreach (FlatButton b in fixButtons) b.Enabled = false;
            fixConsole.Clear();
            Thread t = new Thread(delegate()
            {
                try
                {
                    foreach (string[] st in steps)
                    {
                        Console_("> " + Path.GetFileName(st[0]) + " " + st[1]);
                        int code = Fixes.RunTool(st[0], st[1], st[2] == "u", delegate(string line) { Console_(line); }, null);
                        Console_("(terminou com código " + code + ")\r\n");
                    }
                }
                catch (Exception e) { Console_("Erro: " + e.Message); }
                finally
                {
                    fixRunning = false;
                    Post(delegate { foreach (FlatButton b in fixButtons) b.Enabled = true; RefreshFixes(); });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        void RefreshFixes()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string page = Fixes.PagefileStatus();
                List<string> pfHdd = Fixes.PagefileOnHdd();
                if (pfHdd.Count > 0) page = "ATENÇÃO: arquivo de paginação no HD (" + string.Join(", ", pfHdd.ToArray()) + ":). " + page;
                bool wuBlocked = Fixes.WuDriversBlocked();
                double commit = NativeInfo.CommitPercent();
                bool dumps = true;
                try
                {
                    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl"))
                    {
                        object v = k == null ? null : k.GetValue("CrashDumpEnabled");
                        dumps = v == null || Convert.ToInt32(v) != 0;
                    }
                }
                catch { }
                string drv = "";
                try
                {
                    foreach (System.Management.ManagementObject o in new System.Management.ManagementObjectSearcher("SELECT Name,DriverVersion,DriverDate FROM Win32_VideoController").Get())
                    {
                        string n = Convert.ToString(o["Name"]);
                        if (n.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        DateTime d = System.Management.ManagementDateTimeConverter.ToDateTime(Convert.ToString(o["DriverDate"]));
                        VideoVendor vv = Rules.VideoFor(n);
                        drv = n + " — driver " + o["DriverVersion"] + " de " + d.ToString("dd/MM/yyyy") + " (" + (int)(DateTime.Now - d).TotalDays + " dias)" +
                            (vv.Brand.Length > 0 ? ". O botão abre o site da " + vv.Brand + "." : ". Marca não reconhecida: o botão abre as atualizações opcionais do Windows.");
                        break;
                    }
                }
                catch { }
                bool pawn = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"PawnIO\PawnIOLib.dll"));
                BootInfo boot = SystemInfo.ReadBoot();
                DateTime oldAt;
                string old = SystemInfo.WindowsOld(out oldAt);
                Post(delegate
                {
                    lblFixPage.Text = page;
                    lblFixPage.ForeColor = commit >= 90 ? Theme.Bad : commit >= 80 || pfHdd.Count > 0 ? Theme.Warn : Theme.Good;
                    lblFixWu.Text = wuBlocked ? "Ativo: o Windows Update não instala drivers." : "Desativado: o Windows Update pode trocar drivers.";
                    lblFixWu.ForeColor = wuBlocked ? Theme.Good : Theme.Muted;
                    lblFixDump.Text = dumps ? "Ativo." : "DESATIVADO — o motivo das telas azuis não está sendo guardado.";
                    lblFixDump.ForeColor = dumps ? Theme.Good : Theme.Warn;
                    lblFixDriver.Text = drv;
                    lblFixDriver.ForeColor = Theme.Muted;
                    lblFixPawn.Text = pawn ? "Instalado." + (SysUtil.IsAdmin() ? "" : " (o DiagIT precisa rodar como administrador para usá-lo)") : "NÃO instalado — temperatura da CPU e tensões indisponíveis.";
                    lblFixPawn.ForeColor = pawn ? Theme.Good : Theme.Warn;
                    if (!boot.Read) { lblFixBoot.Text = "Requer administrador para ler."; lblFixBoot.ForeColor = Theme.Muted; }
                    else
                    {
                        int n = boot.Stale().Count;
                        lblFixBoot.Text = n == 0 ? "Nenhuma entrada antiga." : n + " entrada(s) antiga(s) encontrada(s).";
                        lblFixBoot.ForeColor = n == 0 ? Theme.Good : Theme.Warn;
                    }
                    lblFixOld.Text = old == null ? "Não há instalação anterior guardada." : "Existe " + old + " (de " + oldAt.ToString("dd/MM/yyyy") + ").";
                    lblFixOld.ForeColor = old == null ? Theme.Good : Theme.Warn;
                });
            });
        }
    }
}
