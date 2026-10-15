using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DiagIT
{
    /// <summary>
    /// Percebe com o app aberto que a placa ou o driver de vídeo mudou (DDU, instalador do fabricante, Windows Update).
    /// Antes a placa era lida uma vez só, na abertura: quem abria o DiagIT no meio da reinstalação via "Adaptador de Vídeo Básico" para sempre.
    /// </summary>
    public partial class MainForm
    {
        string videoSig, videoPending;
        volatile bool videoChecking;
        int videoTicks;

        /// <summary>Chamado a cada segundo; lê a placa de vídeo a cada ~20 s (leitura barata).</summary>
        void WatchVideoDriver()
        {
            MachineInfo m = machine;
            if (m == null) return;
            if (videoSig == null) { videoSig = m.VideoSignature; return; }
            if (++videoTicks % 20 != 0 || videoChecking) return;
            videoChecking = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { CheckVideoDriver(); }
                catch (Exception e) { Log.Write("WatchVideoDriver: " + e.Message); }
                finally { videoChecking = false; }
            });
        }

        void CheckVideoDriver()
        {
            List<VideoAdapter> now = VideoAdapters.ReadAll();
            string sig = VideoAdapters.Signature(now);
            if (sig == videoSig) { videoPending = null; return; }
            // uma instalação passa por vários estados em sequência (básico, driver antigo, driver novo): só vale o que se repete em duas leituras
            if (sig != videoPending) { videoPending = sig; return; }

            MachineInfo old = machine;
            string before = old != null && old.Gpu.Length > 0 && !old.GpuMissingDriver ? old.Gpu + " " + old.GpuDriver : "sem driver de vídeo";
            videoSig = sig; videoPending = null;
            machine = MachineInfo.Read(w.Perf.TotalVramMB);
            hwAt = DateTime.MinValue;   // a próxima análise lê o hardware de novo
            VideoAdapter a = VideoAdapters.Primary(now);
            Log.Write("Placa de vídeo mudou: " + before + " → " + VideoAdapters.Describe(a));
            if (a == null) return;      // sem driver: é o meio do DDU, não há o que avisar ainda

            bool marked = MarkInstallStepDone();
            Balloon("Driver de vídeo trocado", VideoAdapters.Describe(a) + (marked ? ". Marquei \"Instale o driver baixado\" no passo a passo." : "."), ToolTipIcon.Info);
            if (!analyzing)
                RunAnalysis(0, true, delegate(Diagnosis d, List<SessionData> sds) { ShowDiagnosis(d, sds); });
        }

        static bool SoftwareInstalled(params string[] tokens)
        {
            foreach (string path in new string[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path))
                    {
                        if (k == null) continue;
                        foreach (string n in k.GetSubKeyNames())
                            using (Microsoft.Win32.RegistryKey s = k.OpenSubKey(n))
                            {
                                string dn = s == null ? null : s.GetValue("DisplayName") as string;
                                if (dn == null) continue;
                                foreach (string t in tokens) if (dn.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                            }
                    }
                }
                catch { }
            }
            return false;
        }

        /// <summary>Responde "o driver de vídeo ficou certo?": qual está em uso, de onde veio e se o Windows Update pode trocá-lo.</summary>
        void VerifyVideoDriver()
        {
            List<VideoAdapter> l = VideoAdapters.ReadAll();
            VideoAdapter a = VideoAdapters.Primary(l);
            if (a == null)
            {
                MessageBox.Show(this, l.Count == 0 ? "Não encontrei nenhuma placa de vídeo." :
                    "O Windows está usando o Adaptador de Vídeo Básico: ainda não há driver do fabricante instalado.\n\nFaça o passo \"Instale o driver baixado\".",
                    "Conferir o driver de vídeo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            VideoVendor v = Rules.VideoFor(a.Name);
            bool panel = v.Brand == "AMD" ? SoftwareInstalled("AMD Software") : v.Brand == "NVIDIA" ? SoftwareInstalled("NVIDIA Graphics Driver", "NVIDIA App") :
                         v.Brand == "Intel" ? SoftwareInstalled("Intel(R) Graphics", "Intel Graphics Software") : false;
            DateTime wuAt = DateTime.MinValue;
            List<WinEvent> ev = events;
            if (ev != null)
                foreach (WinEvent e in ev)
                {
                    if (e.Provider != "Microsoft-Windows-WindowsUpdateClient" || e.Id != 19) continue;
                    System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(e.Prop(0) + "\n" + e.Msg, @"\s+-\s+Display\s+-\s+([0-9]+(?:\.[0-9]+)+)");
                    if (m.Success && m.Groups[1].Value == a.DriverVersion) { wuAt = e.Time; break; }
                }
            bool blocked = Fixes.WuDriversBlocked();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("Placa: " + a.Name);
            sb.AppendLine("Driver em uso: " + a.DriverVersion + (a.DriverDate > DateTime.MinValue ? " (de " + a.DriverDate.ToString("dd/MM/yyyy") + ")" : ""));
            if (v.Brand.Length > 0) sb.AppendLine("Programa do fabricante (" + v.Panel + ") instalado: " + (panel ? "sim" : "não"));
            sb.AppendLine("Origem: " + (wuAt > DateTime.MinValue ? "Windows Update (" + wuAt.ToString("dd/MM HH:mm") + ")" : "instalador do fabricante ou instalação manual"));
            sb.AppendLine("Windows Update impedido de trocar o driver: " + (blocked ? "sim" : "NÃO"));
            sb.AppendLine();
            if (wuAt > DateTime.MinValue && !panel)
                sb.Append("Este driver veio do Windows Update, não do site do fabricante. Instale o driver baixado do site e confira de novo.");
            else if (!blocked)
                sb.Append("O driver está certo, mas o Windows ainda pode trocá-lo sozinho. No passo \"Impeça o Windows de trocar o driver\", clique em \"Impedir agora\" (precisa do DiagIT como administrador).");
            else
            {
                bool tracking = CloseDriverGuideIfDone();
                sb.Append(tracking
                    ? "Tudo certo com o driver, e o teste da placa já passou depois da instalação. Registrei a troca e o DiagIT passa a acompanhar: nos Resultados, o que aconteceu antes do driver novo aparece como \"antes da troca\". Jogue normalmente; se os travamentos voltarem, ele avisa."
                    : "Tudo certo com o driver. Agora rode o teste da placa (3 minutos): quando ele passar, o DiagIT registra a troca e passa a acompanhar sozinho.");
                if (tracking) StartAnalyzeClick(false);
            }
            MessageBox.Show(this, sb.ToString(), "Conferir o driver de vídeo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Passo a passo do driver limpo em andamento, com o driver do fabricante já instalado e o teste da placa aprovado depois disso:
        /// registra a correção na hora em que o driver entrou (23:02, não "agora") e passa a acompanhar. Assim ninguém precisa lembrar de
        /// clicar em "Concluir", e o que aconteceu antes do driver novo não é confundido com problema atual. Devolve true se registrou.
        /// </summary>
        bool CloseDriverGuideIfDone()
        {
            try
            {
                string[] s = GuideInProgress();
                if (s == null || s.Length < 3 || s[0] != "driver-video-limpo") return false;
                Guide g = Rules.GuideById("driver-video-limpo");
                if (g == null) return false;
                int idx = -1;
                for (int i = 0; i < g.Steps.Count; i++) if (g.Steps[i].Title.StartsWith("Instale o driver", StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                if (idx < 0 || idx >= s[2].Length || s[2][idx] != '1') return false;
                VideoAdapter a = VideoAdapters.Primary(VideoAdapters.ReadAll());
                DateTime at = VideoAdapters.InstalledAt(a);
                if (a == null || at == DateTime.MinValue || (DateTime.Now - at).TotalDays > 3) return false;
                bool tested = false;
                foreach (TestResult t in TestStore.ListAll()) if (t.Kind == TestKind.Gpu && t.Status == "passed" && t.Start > at) tested = true;
                if (!tested) return false;
                FixLog.Add("guia:" + g.Id, g.Done, g.Scope, at);
                ClearGuideProgress();
                Post(delegate { if (curGuide != null && curGuide.Id == g.Id) curGuide = null; });
                Balloon("Driver de vídeo: acompanhando", "Registrei a troca do driver de " + at.ToString("dd/MM HH:mm") + " e o teste da placa passou. O DiagIT avisa se os travamentos voltarem.", ToolTipIcon.Info);
                return true;
            }
            catch (Exception e) { Log.Write("CloseDriverGuideIfDone: " + e.Message); return false; }
        }

        /// <summary>Se o passo a passo do driver limpo está em andamento, marca "Instale o driver baixado" como feito.</summary>
        bool MarkInstallStepDone()
        {
            string[] s = GuideInProgress();
            if (s == null || s.Length < 3 || s[0] != "driver-video-limpo") return false;
            Guide g = Rules.GuideById("driver-video-limpo");
            if (g == null) return false;
            int idx = -1;
            for (int i = 0; i < g.Steps.Count; i++) if (g.Steps[i].Title.StartsWith("Instale o driver", StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
            if (idx < 0) return false;
            char[] bits = s[2].PadRight(g.Steps.Count, '0').ToCharArray();
            if (bits[idx] == '1') return false;
            bits[idx] = '1';
            int step = idx;
            for (int k = 0; k < bits.Length; k++) if (bits[k] != '1') { step = k; break; }
            try { File.WriteAllText(GuideProgressFile, "driver-video-limpo|" + step + "|" + new string(bits)); }
            catch { return false; }
            // se o passo a passo está aberto na tela, atualiza também
            Post(delegate
            {
                if (curGuide != null && curGuide.Id == "driver-video-limpo" && guideDone.Length > idx)
                {
                    guideDone[idx] = true;
                    guideStep = step;
                    LayoutGuide();
                }
            });
            return true;
        }
    }
}
