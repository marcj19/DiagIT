using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace DiagIT
{
    /// <summary>
    /// Ajustes de Windows com Aplicar e Desfazer à vista, na página Resolver: "Deixar o Windows mais rápido" (desempenho)
    /// e "Testes de estabilidade" (um de cada vez, acompanhados pelo "Resolveu?").
    /// </summary>
    public partial class MainForm
    {
        readonly Dictionary<string, Label> tweakLabels = new Dictionary<string, Label>();
        Label lblStartup, lblTemp, lblGameMode;

        /// <summary>Aplica ou desfaz um ajuste, sempre dizendo o que vai acontecer e como voltar atrás.</summary>
        void RunTweak(string tid, bool on, bool track)
        {
            Tweak tw = Tweaks.ById(tid);
            if (tw == null) return;
            if (tw.Admin && !NeedAdmin()) return;
            bool isOn = false;
            try { isOn = tw.IsOn(); } catch { }
            if (on && isOn) { MessageBox.Show(this, "Este ajuste já está ligado.\n\n" + tw.Name, "Ajuste de Windows", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!on && !isOn) { MessageBox.Show(this, "Este ajuste já está no padrão do Windows. Nada a desfazer.", "Ajuste de Windows", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (on && MessageBox.Show(this, tw.What + "\n\nÉ reversível: o botão Desfazer deste ajuste volta tudo ao padrão." +
                (tw.Reboot ? "\n\nO Windows só aplica depois de reiniciar." : "") + "\n\nAplicar agora?",
                tw.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string msg;
            bool ok = Tweaks.Set(tid, on, track, out msg);
            Console_(msg);
            MessageBox.Show(this, msg, "Ajuste de Windows", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            RefreshTweakStates();
            if (ok && track) StartAnalyzeClick(false);
        }

        /// <summary>Cartão de ajuste: estado atual e botões Aplicar e Desfazer.</summary>
        int TweakCard(Panel parent, int y, string tid, string title, string desc, bool track, string applyLabel)
        {
            Label st;
            y = FixCard(parent, y, title, desc, out st, new string[] { applyLabel, "Desfazer" },
                new EventHandler[] { delegate { RunTweak(tid, true, track); }, delegate { RunTweak(tid, false, track); } });
            tweakLabels[tid] = st;
            return y;
        }

        int BuildTweakCards(Panel scroll, int y)
        {
            y = Section(scroll, "Deixar o Windows mais rápido", y);
            y = TweakCard(scroll, y, "energia", "Plano de energia: Alto desempenho",
                "Mantém o processador pronto para trabalhar. Em PC de mesa o ganho é pequeno e o consumo sobe; não use em notebook na bateria. Desfazer volta ao plano anterior.", false, "Aplicar");
            y = TweakCard(scroll, y, "gamedvr", "Gravação de jogo em segundo plano (Xbox Game Bar)",
                "O Windows pode gravar o jogo em segundo plano, usando placa de vídeo e disco. Desligar não perde nada que você use.", false, "Aplicar");
            y = FixCard(scroll, y, "Programas que iniciam com o Windows",
                "Cada programa que inicia junto deixa o PC mais lento para ligar e ocupa memória. Desative os que você não usa na lista do Windows (o DiagIT não desativa nada sozinho).",
                out lblStartup, new string[] { "Abrir a lista de inicialização" },
                new EventHandler[] { delegate { OpenSettingsPage("ms-settings:startupapps"); } });
            y = FixCard(scroll, y, "Arquivos temporários",
                "Apaga arquivos temporários do seu usuário com mais de 3 dias. Não dá para desfazer; arquivos em uso são pulados.",
                out lblTemp, new string[] { "Limpar agora" }, new EventHandler[] { delegate { CleanTempClick(); } });
            y = FixCard(scroll, y, "Modo de Jogo do Windows",
                "O Modo de Jogo prioriza o jogo em primeiro plano. Costuma vir ligado: confira e, se estiver desligado, ligue na tela do Windows.",
                out lblGameMode, new string[] { "Abrir a configuração" }, new EventHandler[] { delegate { OpenSettingsPage("ms-settings:gaming-gamemode"); } });

            y = Section(scroll, "Testes de estabilidade (um de cada vez)", y + Theme.S(8));
            Label note = Body("Só para quando um problema de vídeo voltou. Aplique um, jogue alguns dias e veja o resultado em Início > Resolveu?. Se não ajudar, desfaça e passe ao próximo.", true);
            note.SetBounds(0, y, Theme.S(860), Theme.S(36));
            scroll.Controls.Add(note);
            y += Theme.S(40);
            y = TweakCard(scroll, y, "mpo", "Desligar as sobreposições de vídeo do Windows (MPO)",
                "Contorno conhecido para tela preta, piscadas e reset do vídeo; a chave não é documentada pela Microsoft. Precisa reiniciar.", true, "Aplicar teste");
            y = TweakCard(scroll, y, "hags", "Desligar o agendamento de GPU por hardware (HAGS)",
                "Devolve ao Windows a organização do trabalho da placa. Em alguns conjuntos de placa e driver causa travamentos. Precisa reiniciar.", true, "Aplicar teste");
            y = TweakCard(scroll, y, "tdr", "Dar 10 segundos ao vídeo antes do reset (TDR)",
                "A Microsoft não recomenda como correção: pode esconder um defeito real. Use só como teste curto. Precisa reiniciar.", true, "Aplicar teste");
            y = TweakCard(scroll, y, "cpu-boost", "Desligar o turbo do processador",
                "Serve para saber se o problema é calor ou instabilidade do turbo. Perde um pouco de desempenho. Não precisa reiniciar.", true, "Aplicar teste");
            y = Section(scroll, "Correções do Windows", y + Theme.S(8));
            return y;
        }

        static void OpenSettingsPage(string uri)
        {
            try { Process.Start(uri); }
            catch (Exception e) { MessageBox.Show("Não foi possível abrir a configuração do Windows: " + e.Message, "DiagIT"); }
        }

        void CleanTempClick()
        {
            int files; long bytes;
            Tweaks.TempInfo(out files, out bytes);
            if (files == 0) { MessageBox.Show(this, "Não há arquivos temporários com mais de 3 dias para apagar.", "Arquivos temporários", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (MessageBox.Show(this, "Apagar " + files.ToString("N0") + " arquivos temporários com mais de 3 dias (" + (bytes / 1048576.0).ToString("N0") +
                " MB) da pasta temporária do seu usuário?\n\nNão dá para desfazer. Arquivos em uso são pulados.", "Arquivos temporários",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int n; long b;
                Tweaks.CleanTemp(out n, out b);
                string res = "Apaguei " + n.ToString("N0") + " arquivos temporários (" + (b / 1048576.0).ToString("N0") + " MB).";
                Log.Write(res);
                Console_(res);
                Post(delegate
                {
                    MessageBox.Show(this, res, "Arquivos temporários", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshTweakStates();
                });
            });
        }

        /// <summary>Lê o estado atual de cada ajuste em segundo plano e mostra nos cartões.</summary>
        void RefreshTweakStates()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, string> text = new Dictionary<string, string>();
                Dictionary<string, bool> on = new Dictionary<string, bool>();
                foreach (Tweak t in Tweaks.All)
                {
                    bool v = false;
                    try { v = t.IsOn(); } catch { }
                    on[t.Id] = v;
                    text[t.Id] = v ? "Ligado agora." : "Desligado: padrão do Windows.";
                }
                string pg, pn;
                Tweaks.ActivePlan(out pg, out pn);
                text["energia"] = "Plano de energia atual: " + (string.IsNullOrEmpty(pn) ? "não foi possível ler" : pn) + ".";
                List<string> startup = Tweaks.StartupPrograms();
                string sTxt = startup.Count + (startup.Count == 1 ? " programa inicia" : " programas iniciam") + " com o Windows" +
                    (startup.Count > 0 ? ": " + string.Join(", ", startup.GetRange(0, Math.Min(6, startup.Count)).ToArray()) + (startup.Count > 6 ? " e mais " + (startup.Count - 6) : "") : "") + " (contagem aproximada).";
                int tf; long tb;
                Tweaks.TempInfo(out tf, out tb);
                string tTxt = tf == 0 ? "Nada para limpar." : tf.ToString("N0") + " arquivos (" + (tb / 1048576.0).ToString("N0") + " MB) com mais de 3 dias.";
                int gm = Tweaks.GameMode();
                string gTxt = gm == 1 ? "Modo de Jogo: ligado." : gm == 0 ? "Modo de Jogo: DESLIGADO. Vale ligar." : "Modo de Jogo: sem marcação (padrão do Windows).";
                Post(delegate
                {
                    foreach (KeyValuePair<string, Label> kv in tweakLabels)
                    {
                        string s;
                        if (!text.TryGetValue(kv.Key, out s)) continue;
                        kv.Value.Text = s;
                        kv.Value.ForeColor = on.ContainsKey(kv.Key) && on[kv.Key] ? Theme.Good : Theme.Muted;
                    }
                    if (lblStartup != null) { lblStartup.Text = sTxt; lblStartup.ForeColor = startup.Count > 12 ? Theme.Warn : Theme.Muted; }
                    if (lblTemp != null) { lblTemp.Text = tTxt; lblTemp.ForeColor = Theme.Muted; }
                    if (lblGameMode != null) { lblGameMode.Text = gTxt; lblGameMode.ForeColor = gm == 0 ? Theme.Warn : Theme.Muted; }
                });
            });
        }
    }
}
