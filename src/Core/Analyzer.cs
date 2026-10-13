using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DiagIT
{
    public enum Comp { Cpu, Gpu, Ram, Disk, Psu, Cooling, Software, Board }
    public enum Sev { Crit, Warn, Info, Ok }

    public class Finding
    {
        public Sev Sev;
        public string Source = "";
        public string Title = "";
        public string Evidence = "";
        public string Advice = "";
        public Dictionary<Comp, double> W = new Dictionary<Comp, double>();
        /// <summary>Problema comprovado (teste reprovado, erro registrado pelo próprio hardware), não apenas indício.</summary>
        public bool Confirmed;
        public int Primary = -1;

        public Finding Weigh(Comp c, double w) { W[c] = w; return this; }

        public string Label
        {
            get
            {
                if (Confirmed) return "CONFIRMADO";
                if (Sev == Sev.Ok) return "OK";
                if (Sev == Sev.Info) return "INFO";
                double m = 0; foreach (double v in W.Values) m = Math.Max(m, v);
                return m >= 0.3 || Sev == Sev.Crit ? "PROVÁVEL" : "PISTA";
            }
        }
    }

    public class SessionData
    {
        public SessionInfo Info;
        public List<Sample> S = new List<Sample>();
    }

    public class Diagnosis
    {
        public List<Finding> Findings = new List<Finding>();
        public double[] Score = new double[8];
        public int Sessions, SamplesTotal, EventsTotal, Days;
        public double HoursRecorded;
        public bool HasCpuTemp, HasGpuTemp, HasPower;
        public string Verdict = "";
        public DateTime When = DateTime.Now;
        public CompState[] States = new CompState[8];
        public List<TestResult> Tests = new List<TestResult>();
        public HwSnapshot Hw;
    }

    public static class CompInfo
    {
        public static readonly string[] Names =
        {
            "Processador (CPU)", "Placa de vídeo (GPU)", "Memória RAM", "Armazenamento (SSD/HD)",
            "Fonte de energia (PSU)", "Refrigeração / temperatura", "Drivers / software", "Placa-mãe / PCIe"
        };
    }

    public enum CompStatus { Untested, Ok, Suspect, Failed }

    public class CompState
    {
        public CompStatus Status = CompStatus.Untested;
        public string Headline = "";
        public string Detail = "";
        public bool HasTest;
        public TestKind Test;
    }

    public static partial class Analyzer
    {
        static readonly CultureInfo Pt = new CultureInfo("pt-BR");

        static string D(DateTime t) { return t.ToString("dd/MM/yyyy HH:mm", Pt); }
        static string N0(double v) { return v.ToString("0", Pt); }
        static string N1(double v) { return v.ToString("0.0", Pt); }

        static bool Has(string s, params string[] any)
        {
            if (s == null) return false;
            foreach (string a in any) if (s.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static double Scale(int n, double full) { return Math.Min(1.0, n / full); }

        static string Recent(List<WinEvent> ev, int max)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;
            foreach (WinEvent e in ev)
            {
                if (i++ >= max) break;
                sb.AppendLine("  • " + D(e.Time) + " — " + e.Provider + " (ID " + e.Id + ")");
            }
            return sb.ToString().TrimEnd();
        }

        static List<WinEvent> Where(List<WinEvent> all, Predicate<WinEvent> p)
        {
            return all.FindAll(p);
        }

        // --------------------------------------------------------------------------------
        public static Diagnosis Run(List<SessionData> sessions, List<WinEvent> events, List<DiskHealth> disks, int days)
        {
            return Run(sessions, events, disks, days, null, null);
        }

        public static Diagnosis Run(List<SessionData> sessions, List<WinEvent> events, List<DiskHealth> disks, int days, List<TestResult> tests, HwSnapshot hw)
        {
            Diagnosis d = new Diagnosis();
            d.Hw = hw;
            if (tests != null) d.Tests = tests;
            bool nativeSmart = false;
            if (hw != null) foreach (DriveHealth dh in hw.Drives) if (dh.Read) nativeSmart = true;
            if (nativeSmart) disks = new List<DiskHealth>();
            d.Days = days;
            d.Sessions = sessions.Count;
            d.EventsTotal = events.Count;
            DateTime cutoff = DateTime.Now.AddDays(-days);
            events = Where(events, delegate(WinEvent e) { return e.Time >= cutoff; });

            List<Sample> all = new List<Sample>();
            foreach (SessionData sd in sessions) all.AddRange(sd.S);
            d.SamplesTotal = all.Count;
            foreach (SessionData sd in sessions) d.HoursRecorded += sd.Info.Duration.TotalHours;

            EventRules(d, events);
            SnapshotRules(d);
            DiskRules(d, disks, events);
            TelemetryRules(d, sessions, all, events);
            TestRules(d, days);
            FactRules(d);
            Finish(d);
            BuildStates(d);
            return d;
        }

        // ================================================================ JANELAS DE INSTALAÇÃO
        /// <summary>Períodos em que drivers/atualizações/o próprio Windows estavam sendo instalados (eventos nesses períodos são esperados).</summary>
        static List<DateTime[]> InstallWindows(List<WinEvent> ev)
        {
            List<DateTime[]> w = new List<DateTime[]>();
            DateTime inst = SystemInfo.WindowsInstallDate();
            if (inst > DateTime.MinValue) w.Add(new DateTime[] { inst.AddMinutes(-30), inst.AddHours(2) });
            foreach (WinEvent e in ev)
            {
                bool isInst = e.Provider == "MsiInstaller" || e.Provider == "Microsoft-Windows-WindowsUpdateClient" || e.Provider == "Microsoft-Windows-UserPnp";
                if (!isInst) continue;
                // só conta instalações que mexem no vídeo (driver/software de GPU) ou atualizações do Windows que incluem drivers
                string m = e.Msg + " " + string.Join(" ", e.P);
                bool video = Has(m, "Radeon", "AMD Software", "Adrenalin", "- Display", "Display -", "GeForce", "NVIDIA Graphics", "amdkmdag", "nvlddmkm", "VEN_10DE") && !Has(m, "VideoExtension", "Extension");
                if (!video) continue;
                w.Add(new DateTime[] { e.Time.AddMinutes(-20), e.Time.AddMinutes(30) });
            }
            return w;
        }

        static bool DuringInstall(List<DateTime[]> w, DateTime t)
        {
            foreach (DateTime[] p in w) if (t >= p[0] && t <= p[1]) return true;
            return false;
        }

        // ================================================================ EVENTOS DO WINDOWS
        static void EventRules(Diagnosis d, List<WinEvent> ev)
        {
            List<DateTime[]> installs = InstallWindows(ev);
            // ---------- WHEA
            List<WinEvent> whea = Where(ev, delegate(WinEvent e) { return Has(e.Provider, "WHEA"); });
            if (whea.Count > 0)
            {
                List<WinEvent> fatal = new List<WinEvent>(), cpu = new List<WinEvent>(), mem = new List<WinEvent>(),
                    pcie = new List<WinEvent>(), unk = new List<WinEvent>();
                foreach (WinEvent e in whea)
                {
                    bool isFatal = e.Id == 1 || e.Id == 18 || e.Id == 20 || e.Level == 1;
                    if (isFatal) fatal.Add(e);
                    if (Has(e.Msg, "PCI Express", "PCIe")) pcie.Add(e);
                    else if (Has(e.Msg, "Memory", "Memória", "DIMM")) mem.Add(e);
                    else if (Has(e.Msg, "Processor", "processador", "Cache", "Núcleo", "Core")) cpu.Add(e);
                    else unk.Add(e);
                }
                string breakdown = "Origem informada pelo hardware: " +
                    cpu.Count + " no processador, " + mem.Count + " na memória, " + pcie.Count + " no barramento PCIe, " +
                    unk.Count + " não classificados.";

                if (fatal.Count > 0)
                {
                    double w = Math.Min(1, 0.6 + fatal.Count / 5.0 * 0.4);
                    Finding f = new Finding();
                    f.Sev = Sev.Crit; f.Source = "Eventos do Windows";
                    f.Title = "Erros FATAIS de hardware registrados pelo Windows (WHEA) — " + fatal.Count + "×";
                    f.Evidence = "Último em " + D(fatal[0].Time) + ". " + breakdown + "\n" + Recent(fatal, 4) +
                        (fatal[0].Msg.Length > 0 ? "\nTrecho: " + Trunc(fatal[0].Msg, 220) : "");
                    f.Advice = "Erro WHEA fatal é o próprio hardware avisando que algo falhou (CPU, memória, PCIe ou alimentação). " +
                        "Remova qualquer overclock/EXPO/XMP e Curve Optimizer, atualize a BIOS e teste a RAM (MemTest86) e a fonte.";
                    f.Weigh(Comp.Cpu, 0.40 * w).Weigh(Comp.Ram, 0.30 * w).Weigh(Comp.Board, 0.20 * w).Weigh(Comp.Psu, 0.20 * w);
                    if (pcie.Count > 0) f.Weigh(Comp.Gpu, 0.30 * w);
                    d.Findings.Add(f);
                }
                List<WinEvent> corrected = Where(whea, delegate(WinEvent e) { return !fatal.Contains(e); });
                if (corrected.Count > 0)
                {
                    double w = Scale(corrected.Count, 15);
                    Finding f = new Finding();
                    f.Sev = corrected.Count >= 20 ? Sev.Crit : Sev.Warn; f.Source = "Eventos do Windows";
                    f.Title = "Erros de hardware corrigidos (WHEA) — " + corrected.Count + "×";
                    f.Evidence = "O hardware detectou e corrigiu erros sozinho. " + breakdown + "\n" + Recent(corrected, 4);
                    f.Advice = "Poucos erros corrigidos isolados são tolerados, mas repetidos indicam instabilidade (RAM/CPU com overclock, " +
                        "tensão baixa, slot PCIe com mau contato). Teste com tudo em configuração padrão.";
                    f.Weigh(Comp.Cpu, 0.30 * w).Weigh(Comp.Ram, 0.30 * w).Weigh(Comp.Board, 0.15 * w).Weigh(Comp.Psu, 0.08 * w);
                    if (pcie.Count > 0) f.Weigh(Comp.Gpu, 0.20 * w).Weigh(Comp.Board, 0.25 * w);
                    d.Findings.Add(f);
                }
            }

            // ---------- Driver de vídeo parou de responder (TDR)
            List<WinEvent> tdr = Where(ev, delegate(WinEvent e)
            {
                return ((e.Provider == "Display" && e.Id == 4101) ||
                       Has(e.Provider, "amdkmdag", "amdwddmg", "atikmdag", "nvlddmkm", "igfx")) && !DuringInstall(installs, e.Time);
            });
            if (tdr.Count > 0)
            {
                double w = Math.Max(0.3, Scale(tdr.Count, 5));
                Finding f = new Finding();
                f.Sev = tdr.Count >= 5 ? Sev.Crit : Sev.Warn; f.Source = "Eventos do Windows";
                f.Title = "O driver de vídeo parou de responder e foi reiniciado — " + tdr.Count + "×";
                f.Evidence = "Último em " + D(tdr[0].Time) + ". Cada ocorrência costuma aparecer como tela preta/congelada por alguns segundos.\n" + Recent(tdr, 5);
                f.Advice = "Causas comuns: driver corrompido (reinstale com DDU em modo seguro), GPU superaquecendo, " +
                    "undervolt/overclock instável, cabos PCIe mal encaixados ou fonte sem folga. Em AMD, desative também o " +
                    "overclock automático/ajustes de desempenho no Adrenalin para testar.";
                f.Weigh(Comp.Gpu, 0.35 * w).Weigh(Comp.Software, 0.30 * w).Weigh(Comp.Psu, 0.12 * w).Weigh(Comp.Cooling, 0.12 * w);
                d.Findings.Add(f);
            }

            // ---------- LiveKernelEvent (watchdog do kernel: GPU/driver etc.)
            // Propriedades do WER 1001: [2]=nome do evento, [5]=P1 (codigo do LiveKernelEvent)
            List<WinEvent> lke = Where(ev, delegate(WinEvent e)
            {
                return e.Provider == "Windows Error Reporting" && e.Id == 1001 && e.Prop(2) == "LiveKernelEvent";
            });
            if (lke.Count > 0)
            {
                List<WinEvent> strong = Where(lke, delegate(WinEvent e)
                {
                    string c = e.Prop(5).ToLowerInvariant();
                    return c == "141" || c == "117" || c == "193" || c == "10e";
                });
                List<WinEvent> soft = Where(lke, delegate(WinEvent e)
                {
                    string c = e.Prop(5).ToLowerInvariant();
                    return c == "1a8" || c == "1b8";
                });
                Dictionary<string, int> codes = new Dictionary<string, int>();
                foreach (WinEvent e in lke) { string k = e.Prop(5); int n; codes.TryGetValue(k, out n); codes[k] = n + 1; }
                List<string> cl = new List<string>();
                foreach (KeyValuePair<string, int> kv in codes) cl.Add("0x" + kv.Key + " ×" + kv.Value);
                int episodes = 0; DateTime lastT = DateTime.MinValue;
                List<WinEvent> asc = new List<WinEvent>(lke); asc.Reverse();
                foreach (WinEvent e in asc) { if ((e.Time - lastT).TotalMinutes > 5) episodes++; lastT = e.Time; }

                if (strong.Count > 0)
                {
                    double w = Math.Max(0.3, Scale(strong.Count, 4));
                    Finding f = new Finding();
                    f.Sev = strong.Count >= 3 ? Sev.Crit : Sev.Warn; f.Source = "Eventos do Windows";
                    f.Title = "Travamentos de GPU registrados pelo Windows (LiveKernelEvent 141/117/193) — " + strong.Count + "×";
                    f.Evidence = "A GPU parou de responder e o Windows a recuperou sem tela azul. Último em " + D(strong[0].Time) + ".\n" + Recent(strong, 4);
                    f.Advice = "Driver limpo (DDU), temperatura, energia da GPU, cabos PCIe e slot.";
                    f.Weigh(Comp.Gpu, 0.40 * w).Weigh(Comp.Software, 0.20 * w).Weigh(Comp.Psu, 0.12 * w).Weigh(Comp.Cooling, 0.10 * w);
                    d.Findings.Add(f);
                }
                if (soft.Count > 0)
                {
                    // separa episódios durante instalação de drivers/atualizações (esperados) dos que aconteceram em uso normal
                    int instEp = 0, runEp = 0; DateTime lt = DateTime.MinValue; bool lastInst = false;
                    List<WinEvent> softAsc = new List<WinEvent>(soft); softAsc.Reverse();
                    foreach (WinEvent e in softAsc)
                    {
                        if ((e.Time - lt).TotalMinutes > 5) { lastInst = DuringInstall(installs, e.Time); if (lastInst) instEp++; else runEp++; }
                        lt = e.Time;
                    }
                    episodes = instEp + runEp;
                    double w = runEp == 0 ? 0.1 : Math.Max(0.25, Scale(runEp, 4));
                    Finding f = new Finding();
                    f.Sev = runEp >= 2 ? Sev.Warn : Sev.Info; f.Source = "Eventos do Windows";
                    f.Title = "Watchdog do kernel disparou (LiveKernelEvent 0x1a8/0x1b8) — " + runEp + " episódio(s) em uso normal" +
                        (instEp > 0 ? " e " + instEp + " durante instalação de drivers/atualizações" : "");
                    f.Evidence = "Códigos: " + string.Join(", ", cl.ToArray()) + ". Último em " + D(soft[0].Time) + ". " +
                        "Não há documentação oficial desses códigos; relatos de usuários os ligam a falha de modo de vídeo/timeout do agendador da GPU, " +
                        "mas também a driver de Wi-Fi e a overclock/XMP instável. O dump fica em C:\\Windows\\LiveKernelReports\\WATCHDOG." +
                        (instEp > 0 ? " Episódios durante a instalação de drivers são esperados (o driver de vídeo reinicia a tela ao ser instalado) e pesam pouco." : "") + "\n" + Recent(soft, 4);
                    f.Advice = "Costumam aparecer junto de congelamentos ou telas pretas. Atualize/reinstale o driver de vídeo (DDU) e o de Wi-Fi, " +
                        "desative XMP/EXPO para testar e anote o horário exato quando o problema acontecer para cruzar com as evidências do DiagIT.";
                    f.Weigh(Comp.Gpu, 0.22 * w).Weigh(Comp.Software, 0.28 * w).Weigh(Comp.Ram, 0.10 * w);
                    d.Findings.Add(f);
                }
            }

            // ---------- Desligamentos inesperados
            List<WinEvent> kp = Where(ev, delegate(WinEvent e) { return e.Provider == "Microsoft-Windows-Kernel-Power" && e.Id == 41; });
            List<WinEvent> noBsod = Where(kp, delegate(WinEvent e) { return ParseCode(e.Prop(0)) == 0; });
            if (kp.Count == 0)
            {
                List<WinEvent> e6008 = Where(ev, delegate(WinEvent e) { return e.Provider == "EventLog" && e.Id == 6008; });
                noBsod = e6008;
            }
            if (noBsod.Count > 0)
            {
                double w = Scale(noBsod.Count, 4);
                Finding f = new Finding();
                f.Sev = noBsod.Count >= 3 ? Sev.Crit : Sev.Warn; f.Source = "Eventos do Windows";
                f.Title = "O PC desligou/reiniciou sem aviso e sem tela azul — " + noBsod.Count + "×";
                f.Evidence = "Registro de 'desligamento inesperado' (Kernel-Power 41 com código de erro 0). Último em " + D(noBsod[0].Time) + ".\n" + Recent(noBsod, 5);
                f.Advice = "Esse é o padrão clássico de fonte falhando/sem potência, superaquecimento (proteção térmica corta tudo), " +
                    "ou falha de hardware que trava o sistema inteiro. Descarte antes: queda de energia, régua/filtro de linha e o botão " +
                    "de energia sendo pressionado. Se acontece em jogo, a fonte e a temperatura são as primeiras suspeitas.";
                f.Weigh(Comp.Psu, 0.32 * w).Weigh(Comp.Cooling, 0.22 * w).Weigh(Comp.Gpu, 0.14 * w)
                 .Weigh(Comp.Cpu, 0.10 * w).Weigh(Comp.Ram, 0.10 * w).Weigh(Comp.Board, 0.10 * w);
                d.Findings.Add(f);
            }

            // ---------- Telas azuis
            Dictionary<int, List<WinEvent>> bc = new Dictionary<int, List<WinEvent>>();
            foreach (WinEvent e in ev)
            {
                int code = -1;
                if (e.Provider == "Microsoft-Windows-WER-SystemErrorReporting" && e.Id == 1001)
                {
                    Match m = Regex.Match(e.Prop(0) + " " + e.Msg, @"0x([0-9a-fA-F]{1,8})");
                    if (m.Success) code = (int)(Convert.ToInt64(m.Groups[1].Value, 16) & 0xFFFF);
                }
                else if (e.Provider == "Microsoft-Windows-Kernel-Power" && e.Id == 41)
                {
                    long c = ParseCode(e.Prop(0));
                    if (c > 0) code = (int)(c & 0xFFFF);
                }
                if (code <= 0) continue;
                List<WinEvent> l;
                if (!bc.TryGetValue(code, out l)) { l = new List<WinEvent>(); bc[code] = l; }
                // Kernel-Power 41 e o 1001 descrevem a mesma tela azul; evita contar em dobro (<10 min)
                bool dup = false;
                foreach (WinEvent x in l) if (Math.Abs((x.Time - e.Time).TotalMinutes) < 10) dup = true;
                if (!dup) l.Add(e);
            }
            foreach (KeyValuePair<int, List<WinEvent>> kv in bc)
            {
                string name, advice; Dictionary<Comp, double> w;
                Bugcheck(kv.Key, out name, out advice, out w);
                double s = Math.Min(1, (kv.Value.Count + 1) / 4.0);
                Finding f = new Finding();
                f.Sev = kv.Value.Count >= 2 ? Sev.Crit : Sev.Warn; f.Source = "Eventos do Windows";
                f.Title = "Tela azul 0x" + kv.Key.ToString("X") + " (" + name + ") — " + kv.Value.Count + "×";
                f.Evidence = "Último em " + D(kv.Value[0].Time) + ".\n" + Recent(kv.Value, 4);
                f.Advice = advice;
                foreach (KeyValuePair<Comp, double> c in w) f.Weigh(c.Key, c.Value * s);
                d.Findings.Add(f);
            }

            // ---------- Limitação da CPU pelo firmware
            List<WinEvent> thr = Where(ev, delegate(WinEvent e) { return e.Provider == "Microsoft-Windows-Kernel-Processor-Power" && e.Id == 37; });
            if (thr.Count > 0)
            {
                double w = Scale(thr.Count, 40);
                Finding f = new Finding();
                f.Sev = thr.Count >= 40 ? Sev.Warn : Sev.Info; f.Source = "Eventos do Windows";
                f.Title = "A BIOS/firmware limitou a velocidade da CPU — " + thr.Count + " registros";
                f.Evidence = "O Windows registrou que a frequência dos núcleos foi reduzida pelo firmware (térmico, limite de energia ou VRM). Último em " + D(thr[0].Time) + ".";
                f.Advice = "Verifique a temperatura da CPU sob carga, a pasta térmica/cooler e o limite de energia (PPT/TDC/EDC) na BIOS. " +
                    "Em placas de entrada, VRM quente também causa isso.";
                f.Weigh(Comp.Cooling, 0.32 * w).Weigh(Comp.Cpu, 0.10 * w).Weigh(Comp.Psu, 0.08 * w).Weigh(Comp.Board, 0.08 * w);
                d.Findings.Add(f);
            }

            // ---------- Memória (teste do Windows)
            List<WinEvent> md = Where(ev, delegate(WinEvent e) { return Has(e.Provider, "MemoryDiagnostics"); });
            List<WinEvent> mdBad = Where(md, delegate(WinEvent e) { return e.Id == 1102 || Has(e.Msg, "detected hardware errors", "erros de hardware"); });
            if (mdBad.Count > 0)
            {
                Finding f = new Finding();
                f.Sev = Sev.Crit; f.Source = "Eventos do Windows";
                f.Title = "O diagnóstico de memória do Windows encontrou ERROS";
                f.Confirmed = true; f.Primary = (int)Comp.Ram;
                f.Evidence = "Em " + D(mdBad[0].Time) + ": " + Trunc(mdBad[0].Msg, 200);
                f.Advice = "Teste cada pente separadamente (e cada slot) com MemTest86. Pente com erro precisa ser trocado.";
                f.Weigh(Comp.Ram, 0.85);
                d.Findings.Add(f);
            }
            else if (md.Count > 0)
            {
                Finding f = new Finding();
                f.Sev = Sev.Ok; f.Source = "Eventos do Windows";
                f.Title = "Teste de memória do Windows sem erros (" + D(md[0].Time) + ")";
                f.Evidence = "O teste rápido do Windows não é exaustivo; erros intermitentes podem passar. MemTest86 por várias horas é mais confiável.";
                d.Findings.Add(f);
            }

            // ---------- Travamentos de aplicativos
            List<WinEvent> crashes = Where(ev, delegate(WinEvent e) { return e.Provider == "Application Error" && e.Id == 1000; });
            if (crashes.Count > 0)
            {
                Dictionary<string, int> perApp = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int av = 0, gpuMod = 0;
                foreach (WinEvent e in crashes)
                {
                    string app = e.Prop(0);
                    int n; perApp.TryGetValue(app, out n); perApp[app] = n + 1;
                    string code = e.Prop(6).ToLowerInvariant();
                    if (code == "c0000005" || code == "c0000409" || code == "c0000374") av++;
                    if (Has(e.Prop(3), "atiu9p", "atidxx", "amdxx", "aticfx", "amdvlk", "nvwgf2", "nvd3d", "igd")) gpuMod++;
                }
                List<string> top = new List<string>();
                foreach (KeyValuePair<string, int> kv in perApp) top.Add(kv.Key + " ×" + kv.Value);
                top.Sort();
                if (perApp.Count >= 3 && av >= 3)
                {
                    double w = Scale(perApp.Count, 6);
                    Finding f = new Finding();
                    f.Sev = Sev.Warn; f.Source = "Eventos do Windows";
                    f.Title = "Vários programas diferentes travaram com erro de memória — " + perApp.Count + " apps";
                    f.Evidence = "Falhas de acesso à memória (c0000005/c0000409) em: " + string.Join(", ", top.GetRange(0, Math.Min(8, top.Count)).ToArray()) + ".";
                    f.Advice = "Um único programa travando é bug dele; vários programas diferentes travando assim sugere memória RAM instável " +
                        "(ou overclock/EXPO mal estável). Rode MemTest86 e teste com o perfil de memória desativado.";
                    f.Weigh(Comp.Ram, 0.32 * w).Weigh(Comp.Cpu, 0.15 * w).Weigh(Comp.Software, 0.10 * w);
                    d.Findings.Add(f);
                }
                if (gpuMod >= 2)
                {
                    double w = Scale(gpuMod, 5);
                    Finding f = new Finding();
                    f.Sev = Sev.Warn; f.Source = "Eventos do Windows";
                    f.Title = "Jogos/apps travaram dentro do driver de vídeo — " + gpuMod + "×";
                    f.Evidence = "O módulo que falhou pertence ao driver gráfico (atiu9p64, atidxx64, amdxx64, nvwgf2umx...).";
                    f.Advice = "Reinstale o driver com DDU. Se persistir com driver limpo, suspeite da própria GPU, VRAM ou energia.";
                    f.Weigh(Comp.Gpu, 0.22 * w).Weigh(Comp.Software, 0.30 * w);
                    d.Findings.Add(f);
                }
            }
        }

        static string Trunc(string s, int n)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        static long ParseCode(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            try
            {
                s = s.Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Convert.ToInt64(s.Substring(2), 16);
                return Convert.ToInt64(s, CultureInfo.InvariantCulture);
            }
            catch { return 0; }
        }

        static void Bugcheck(int code, out string name, out string advice, out Dictionary<Comp, double> w)
        {
            w = new Dictionary<Comp, double>();
            advice = "Procure o código no site da Microsoft e atualize drivers/BIOS; se repetir, analise o dump com WinDbg ou BlueScreenView.";
            switch (code)
            {
                case 0x116: case 0x117: case 0x119: case 0x141:
                    name = "falha de vídeo / TDR";
                    advice = "A GPU ou o driver de vídeo travou de forma irrecuperável. Driver limpo (DDU), checar temperatura, cabos PCIe/fonte e estabilidade da GPU.";
                    w[Comp.Gpu] = 0.45; w[Comp.Software] = 0.25; w[Comp.Psu] = 0.10; w[Comp.Cooling] = 0.10; break;
                case 0x10E:
                    name = "VIDEO_MEMORY_MANAGEMENT_INTERNAL";
                    w[Comp.Gpu] = 0.40; w[Comp.Software] = 0.20; break;
                case 0x124:
                    name = "WHEA_UNCORRECTABLE_ERROR";
                    advice = "Erro de hardware não corrigível. Remova overclock/EXPO/Curve Optimizer, atualize BIOS, teste RAM e fonte.";
                    w[Comp.Cpu] = 0.35; w[Comp.Ram] = 0.20; w[Comp.Board] = 0.15; w[Comp.Psu] = 0.15; break;
                case 0x9C:
                    name = "MACHINE_CHECK_EXCEPTION";
                    w[Comp.Cpu] = 0.40; w[Comp.Ram] = 0.15; w[Comp.Psu] = 0.15; w[Comp.Cooling] = 0.10; break;
                case 0x1A:
                    name = "MEMORY_MANAGEMENT";
                    advice = "Quase sempre RAM defeituosa/instável ou driver corrompendo memória. Rode MemTest86 e teste os pentes separadamente.";
                    w[Comp.Ram] = 0.45; w[Comp.Software] = 0.15; w[Comp.Disk] = 0.10; break;
                case 0x4E:
                    name = "PFN_LIST_CORRUPT";
                    w[Comp.Ram] = 0.40; w[Comp.Software] = 0.20; break;
                case 0x50:
                    name = "PAGE_FAULT_IN_NONPAGED_AREA";
                    w[Comp.Ram] = 0.35; w[Comp.Software] = 0.25; w[Comp.Disk] = 0.10; break;
                case 0x7A:
                    name = "KERNEL_DATA_INPAGE_ERROR";
                    advice = "O Windows não conseguiu ler dados do disco. Verifique cabos/porta, SMART do SSD/HD e o estado de saúde do disco.";
                    w[Comp.Disk] = 0.50; w[Comp.Ram] = 0.15; w[Comp.Board] = 0.10; break;
                case 0x7B:
                    name = "INACCESSIBLE_BOOT_DEVICE";
                    w[Comp.Disk] = 0.50; w[Comp.Software] = 0.15; break;
                case 0xF4:
                    name = "CRITICAL_OBJECT_TERMINATION";
                    w[Comp.Disk] = 0.40; w[Comp.Software] = 0.15; w[Comp.Ram] = 0.10; break;
                case 0x101:
                    name = "CLOCK_WATCHDOG_TIMEOUT";
                    advice = "Um núcleo da CPU parou de responder. Remova overclock/undervolt/Curve Optimizer, atualize BIOS; verifique temperatura e energia.";
                    w[Comp.Cpu] = 0.40; w[Comp.Board] = 0.10; w[Comp.Psu] = 0.10; w[Comp.Cooling] = 0.10; break;
                case 0x133:
                    name = "DPC_WATCHDOG_VIOLATION";
                    advice = "Normalmente driver (chipset, armazenamento, rede, áudio) travando. Atualize BIOS, chipset e firmware do SSD.";
                    w[Comp.Software] = 0.40; w[Comp.Disk] = 0.20; break;
                case 0x1E: case 0x3B: case 0x7E:
                    name = code == 0x3B ? "SYSTEM_SERVICE_EXCEPTION" : code == 0x1E ? "KMODE_EXCEPTION_NOT_HANDLED" : "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED";
                    w[Comp.Software] = 0.30; w[Comp.Ram] = 0.15; w[Comp.Cpu] = 0.10; break;
                case 0xA: case 0xD1: case 0xC5:
                    name = code == 0xA ? "IRQL_NOT_LESS_OR_EQUAL" : code == 0xD1 ? "DRIVER_IRQL_NOT_LESS_OR_EQUAL" : "DRIVER_CORRUPTED_EXPOOL";
                    w[Comp.Software] = 0.35; w[Comp.Ram] = 0.20; break;
                case 0x139:
                    name = "KERNEL_SECURITY_CHECK_FAILURE";
                    w[Comp.Software] = 0.25; w[Comp.Ram] = 0.20; w[Comp.Disk] = 0.10; break;
                case 0xEF:
                    name = "CRITICAL_PROCESS_DIED";
                    w[Comp.Software] = 0.20; w[Comp.Disk] = 0.25; w[Comp.Ram] = 0.10; break;
                case 0xC2:
                    name = "BAD_POOL_CALLER";
                    w[Comp.Software] = 0.30; w[Comp.Ram] = 0.20; break;
                case 0x7F:
                    name = "UNEXPECTED_KERNEL_MODE_TRAP";
                    w[Comp.Ram] = 0.30; w[Comp.Cpu] = 0.20; w[Comp.Cooling] = 0.10; w[Comp.Psu] = 0.10; break;
                default:
                    name = "código desconhecido";
                    w[Comp.Software] = 0.20; w[Comp.Ram] = 0.10; break;
            }
        }

        // ================================================================ ESTADO ATUAL DO SISTEMA
        static void SnapshotRules(Diagnosis d)
        {
            try
            {
                double commit = NativeInfo.CommitPercent();
                double limitGb = NativeInfo.CommitLimitGB();
                if (commit >= 90)
                {
                    Finding f = new Finding();
                    f.Sev = commit >= 96 ? Sev.Warn : Sev.Info; f.Source = "Estado atual do sistema";
                    f.Title = "Memória virtual (commit) quase esgotada: " + N0(commit) + "% do limite de " + N1(limitGb) + " GB";
                    f.Evidence = "Quando o commit chega a 100%, o Windows recusa novas alocações: jogos fecham sozinhos, o sistema congela ou aparece o aviso " +
                        "\"memória insuficiente\" — mesmo com RAM física livre. Costuma ocorrer com muitos programas abertos (jogo + launcher + navegador) e arquivo de paginação pequeno.";
                    f.Advice = "Aumente o arquivo de paginação: Configurações > Sistema > Sobre > Configurações avançadas > Desempenho > Avançado > Memória virtual " +
                        "(ex.: tamanho inicial 16384 MB e máximo 32768 MB em um SSD), e feche launchers/navegadores durante o jogo.";
                    f.Weigh(Comp.Software, 0.22).Weigh(Comp.Ram, 0.12);
                    d.Findings.Add(f);
                }
            }
            catch { }
        }

        // ================================================================ DISCOS
        static void DiskRules(Diagnosis d, List<DiskHealth> disks, List<WinEvent> ev)
        {
            List<WinEvent> de = Where(ev, delegate(WinEvent e)
            {
                if (Has(e.Provider, "disk", "stornvme", "storahci", "iaStor", "nvme") && !Has(e.Provider, "diskcounters")) return true;
                if (Has(e.Provider, "Ntfs") && (e.Id == 50 || e.Id == 55 || e.Id == 57 || e.Id == 137 || e.Id == 140)) return true;
                return false;
            });
            if (de.Count > 0)
            {
                Dictionary<string, int> ids = new Dictionary<string, int>();
                bool severe = false;
                foreach (WinEvent e in de)
                {
                    string k = e.Provider + " " + e.Id;
                    int n; ids.TryGetValue(k, out n); ids[k] = n + 1;
                    if (e.Id == 7 || e.Id == 11 || e.Id == 52 || e.Id == 55) severe = true;
                }
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, int> kv in ids) parts.Add(kv.Key + " ×" + kv.Value);
                double w = Scale(de.Count, 5);
                Finding f = new Finding();
                f.Sev = severe || de.Count >= 5 ? Sev.Crit : Sev.Warn; f.Source = "Eventos do Windows";
                f.Title = "Erros de armazenamento registrados pelo Windows — " + de.Count + "×";
                f.Evidence = "Tipos: " + string.Join(", ", parts.ToArray()) + ". Último em " + D(de[0].Time) +
                    (de[0].Msg.Length > 0 ? ".\nTrecho: " + Trunc(de[0].Msg, 200) : ".");
                f.Advice = "Erros de disco/controlador (IDs 7, 11, 51, 55, 129, 153) indicam setores ruins, cabo/porta com mau contato ou SSD " +
                    "falhando. Faça backup agora, rode 'chkdsk' e confira o SMART (CrystalDiskInfo). Troque o cabo SATA / reencaixe o NVMe.";
                f.Weigh(Comp.Disk, 0.50 * w).Weigh(Comp.Board, 0.10 * w).Weigh(Comp.Psu, 0.05 * w);
                if (severe) { f.Confirmed = true; f.Primary = (int)Comp.Disk; }
                d.Findings.Add(f);
            }

            if (disks == null || disks.Count == 0) return;
            bool allGood = true;
            foreach (DiskHealth h in disks)
            {
                List<string> issues = new List<string>();
                double wDisk = 0; Sev sv = Sev.Info;
                if (h.Health == 1) { issues.Add("Windows reporta o estado como ALERTA"); wDisk = Math.Max(wDisk, 0.60); sv = Sev.Warn; }
                if (h.Health == 2) { issues.Add("Windows reporta o disco como NÃO SAUDÁVEL"); wDisk = Math.Max(wDisk, 0.90); sv = Sev.Crit; }
                if (!double.IsNaN(h.Wear) && h.Wear >= 90) { issues.Add("desgaste de " + N0(h.Wear) + "%"); wDisk = Math.Max(wDisk, h.Wear >= 98 ? 0.7 : 0.4); if (sv != Sev.Crit) sv = Sev.Warn; }
                if ((!double.IsNaN(h.ReadErrUncorrected) && h.ReadErrUncorrected > 0) || (!double.IsNaN(h.WriteErrUncorrected) && h.WriteErrUncorrected > 0))
                {
                    issues.Add("erros de leitura/escrita não corrigidos (" + N0(Math.Max(0, h.ReadErrUncorrected)) + "/" + N0(Math.Max(0, h.WriteErrUncorrected)) + ")");
                    wDisk = Math.Max(wDisk, 0.55); if (sv != Sev.Crit) sv = Sev.Warn;
                }
                if (issues.Count == 0) continue;
                allGood = false;
                Finding f = new Finding();
                f.Sev = sv; f.Source = "SMART / saúde do disco";
                f.Title = "Disco com problemas: " + h.Name;
                f.Evidence = string.Join("; ", issues.ToArray()) + ".";
                f.Advice = "Faça backup imediatamente e considere substituir a unidade.";
                f.Weigh(Comp.Disk, wDisk);
                d.Findings.Add(f);
            }
            if (allGood)
            {
                List<string> names = new List<string>();
                foreach (DiskHealth h in disks)
                    names.Add(h.Name + (double.IsNaN(h.Wear) ? "" : " (desgaste " + N0(h.Wear) + "%)") + (double.IsNaN(h.PowerOnHours) ? "" : " " + N0(h.PowerOnHours) + " h ligado"));
                Finding f = new Finding();
                f.Sev = Sev.Ok; f.Source = "SMART / saúde do disco";
                f.Title = "Discos reportam estado saudável";
                f.Evidence = string.Join("; ", names.ToArray()) + ".";
                d.Findings.Add(f);
            }
        }

        // ================================================================ TELEMETRIA DAS SESSÕES
        static double Median(List<double> l)
        {
            if (l.Count == 0) return double.NaN;
            l.Sort();
            return l[l.Count / 2];
        }

        static double Actual(Sample s)
        {
            if (!double.IsNaN(s.V[F.CpuClock])) return s.V[F.CpuClock];
            if (!double.IsNaN(s.V[F.CpuMhz]) && !double.IsNaN(s.V[F.CpuPerf])) return s.V[F.CpuMhz] * s.V[F.CpuPerf] / 100.0;
            return double.NaN;
        }

        static void TelemetryRules(Diagnosis d, List<SessionData> sessions, List<Sample> all, List<WinEvent> events)
        {
            if (all.Count < 5)
            {
                Finding f = new Finding();
                f.Sev = Sev.Info; f.Source = "Telemetria";
                f.Title = sessions.Count == 0 ? "Ainda não há partidas gravadas" : "Poucos dados nas sessões selecionadas";
                f.Evidence = "A análise usa apenas o histórico de eventos do Windows até que o app grave pelo menos uma partida.";
                f.Advice = "Deixe o DiagIT aberto (pode ficar na bandeja) e jogue normalmente. Ele detecta o jogo sozinho e grava.";
                d.Findings.Add(f);
                return;
            }

            double[] cpuT = Stat.Vals(all, F.CpuTemp), gpuT = Stat.Vals(all, F.GpuTemp), gpuH = Stat.Vals(all, F.GpuHot);
            d.HasCpuTemp = cpuT.Length > 0; d.HasGpuTemp = gpuT.Length > 0;
            double[] v12 = Stat.Vals(all, F.V12);
            d.HasPower = v12.Length > 0;

            if (!d.HasCpuTemp && !d.HasGpuTemp)
            {
                Finding f = new Finding();
                f.Sev = Sev.Warn; f.Source = "Telemetria";
                f.Title = "Sem leitura de temperatura — a análise térmica está cega";
                f.Evidence = "O Windows não expõe temperatura de CPU/GPU AMD sem um leitor de sensores. Superaquecimento é uma das causas mais comuns de travamentos em jogos.";
                f.Advice = "Instale o HWiNFO64 (modo 'sensors only') e ative 'Shared Memory Support' em Settings; o DiagIT passa a ler tudo automaticamente. " +
                    "Alternativa: colocar LibreHardwareMonitorLib.dll na pasta 'sensores' ao lado do app e rodar como administrador.";
                d.Findings.Add(f);
            }

            // ---------- Temperatura CPU
            if (d.HasCpuTemp)
            {
                double mx = Stat.Max(cpuT), p95 = Stat.Percentile(cpuT, 0.95), f85 = Stat.FracAbove(cpuT, 85);
                Finding f = new Finding(); f.Source = "Telemetria";
                if (mx >= 95)
                {
                    f.Sev = Sev.Crit; f.Title = "CPU chegou ao limite térmico — máximo " + N0(mx) + " °C";
                    f.Advice = "Limpe o cooler, reaplique pasta térmica, confirme que o cooler está bem assentado e que a ventoinha da CPU gira. Considere cooler melhor.";
                    f.Weigh(Comp.Cooling, 0.50).Weigh(Comp.Cpu, 0.12);
                }
                else if (p95 >= 90 || mx >= 92)
                {
                    f.Sev = Sev.Warn; f.Title = "CPU muito quente em jogo — máximo " + N0(mx) + " °C (95% do tempo abaixo de " + N0(p95) + " °C)";
                    f.Advice = "Está no limite do aceitável. Revise fluxo de ar do gabinete, pasta térmica e curva das ventoinhas.";
                    f.Weigh(Comp.Cooling, 0.35).Weigh(Comp.Cpu, 0.08);
                }
                else if (f85 > 0.2)
                {
                    f.Sev = Sev.Warn; f.Title = "CPU passa " + N0(f85 * 100) + "% do tempo acima de 85 °C";
                    f.Advice = "Aceitável para alguns processadores, mas deixa pouca margem. Melhore a refrigeração se houver travamentos.";
                    f.Weigh(Comp.Cooling, 0.20);
                }
                else
                {
                    f.Sev = Sev.Ok; f.Title = "Temperatura da CPU normal — máximo " + N0(mx) + " °C";
                }
                f.Evidence = "Máx " + N0(mx) + " °C • p95 " + N0(p95) + " °C • média " + N0(Stat.Avg(cpuT)) + " °C (" + cpuT.Length + " leituras).";
                d.Findings.Add(f);
            }

            // ---------- Temperatura GPU
            if (d.HasGpuTemp || gpuH.Length > 0)
            {
                double mx = d.HasGpuTemp ? Stat.Max(gpuT) : double.NaN;
                double hx = gpuH.Length > 0 ? Stat.Max(gpuH) : double.NaN;
                Finding f = new Finding(); f.Source = "Telemetria";
                bool crit = (!double.IsNaN(mx) && mx >= 95) || (!double.IsNaN(hx) && hx >= 110);
                bool warn = (!double.IsNaN(mx) && mx >= 90) || (!double.IsNaN(hx) && hx >= 105);
                bool mild = (!double.IsNaN(mx) && mx >= 85) || (!double.IsNaN(hx) && hx >= 100);
                if (crit)
                {
                    f.Sev = Sev.Crit; f.Title = "GPU em temperatura crítica";
                    f.Advice = "A GPU está se protegendo de superaquecimento. Limpe poeira, melhore o fluxo de ar, confira as ventoinhas da placa e, se for antiga, troque a pasta térmica/pads.";
                    f.Weigh(Comp.Cooling, 0.50).Weigh(Comp.Gpu, 0.20);
                }
                else if (warn)
                {
                    f.Sev = Sev.Warn; f.Title = "GPU muito quente em jogo";
                    f.Advice = "Ajuste a curva de ventoinha, limpe poeira e verifique o fluxo de ar. Limitar FPS ou usar undervolt reduz a temperatura.";
                    f.Weigh(Comp.Cooling, 0.35).Weigh(Comp.Gpu, 0.10);
                }
                else if (mild)
                {
                    f.Sev = Sev.Info; f.Title = "GPU quente, mas dentro do limite";
                    f.Weigh(Comp.Cooling, 0.10);
                }
                else
                {
                    f.Sev = Sev.Ok; f.Title = "Temperatura da GPU normal";
                }
                f.Evidence = (double.IsNaN(mx) ? "" : "Núcleo máx " + N0(mx) + " °C • ") + (double.IsNaN(hx) ? "" : "hotspot máx " + N0(hx) + " °C • ") +
                    "leituras: " + Math.Max(gpuT.Length, gpuH.Length) + ".";
                d.Findings.Add(f);

                // ventoinha parada com GPU quente
                double[] fan = Stat.Vals(all, F.GpuFan);
                if (fan.Length > 0 && d.HasGpuTemp)
                {
                    int hotStopped = 0, hot = 0;
                    foreach (Sample s in all)
                    {
                        if (double.IsNaN(s.V[F.GpuTemp]) || double.IsNaN(s.V[F.GpuFan])) continue;
                        if (s.V[F.GpuTemp] > 70) { hot++; if (s.V[F.GpuFan] < 100) hotStopped++; }
                    }
                    if (hot > 10 && hotStopped > hot * 0.1)
                    {
                        Finding g = new Finding();
                        g.Sev = Sev.Crit; g.Source = "Telemetria";
                        g.Title = "Ventoinha da GPU parada com a placa acima de 70 °C";
                        g.Evidence = hotStopped + " de " + hot + " leituras quentes com a ventoinha em ~0 RPM.";
                        g.Advice = "Ventoinha travada ou com defeito, ou modo zero-RPM mal configurado. Verifique manualmente se gira e ajuste a curva.";
                        g.Weigh(Comp.Cooling, 0.40).Weigh(Comp.Gpu, 0.30);
                        d.Findings.Add(g);
                    }
                }
            }

            // ---------- Throttling de CPU (queda progressiva sob carga)
            List<double> early = new List<double>(), late = new List<double>();
            List<double> loadFreqs = new List<double>();
            List<Sample> loaded = new List<Sample>();
            foreach (Sample s in all)
                if (!double.IsNaN(s.V[F.Cpu]) && s.V[F.Cpu] >= 85 && !double.IsNaN(Actual(s))) loaded.Add(s);
            if (loaded.Count >= 40)
            {
                int q = loaded.Count / 4;
                for (int i = 0; i < q; i++) early.Add(Actual(loaded[i]));
                for (int i = loaded.Count - q; i < loaded.Count; i++) late.Add(Actual(loaded[i]));
                double me = Median(early), ml = Median(late);
                if (me > 0 && ml / me < 0.88)
                {
                    Finding f = new Finding();
                    f.Sev = Sev.Warn; f.Source = "Telemetria";
                    f.Title = "A CPU perde desempenho sob carga prolongada (" + N0((1 - ml / me) * 100) + "% menos clock)";
                    f.Evidence = "Com carga ≥85%, a frequência passou de ~" + N0(me) + " MHz no início para ~" + N0(ml) + " MHz no fim.";
                    f.Advice = "Típico de throttling térmico ou de limite de energia (VRM/BIOS). Compare com a temperatura da CPU e reveja pasta térmica e refrigeração.";
                    f.Weigh(Comp.Cooling, 0.30).Weigh(Comp.Cpu, 0.08).Weigh(Comp.Psu, 0.05).Weigh(Comp.Board, 0.08);
                    d.Findings.Add(f);
                }
            }

            // ---------- Throttling da GPU
            List<Sample> gl = new List<Sample>();
            foreach (Sample s in all) if (!double.IsNaN(s.V[F.Gpu]) && s.V[F.Gpu] >= 90 && !double.IsNaN(s.V[F.GpuClock])) gl.Add(s);
            if (gl.Count >= 40)
            {
                int q = gl.Count / 4;
                List<double> a = new List<double>(), b = new List<double>();
                for (int i = 0; i < q; i++) a.Add(gl[i].V[F.GpuClock]);
                for (int i = gl.Count - q; i < gl.Count; i++) b.Add(gl[i].V[F.GpuClock]);
                double ma = Median(a), mb = Median(b);
                if (ma > 0 && mb / ma < 0.90)
                {
                    Finding f = new Finding();
                    f.Sev = Sev.Warn; f.Source = "Telemetria";
                    f.Title = "O clock da GPU cai com o tempo sob carga total (" + N0((1 - mb / ma) * 100) + "%)";
                    f.Evidence = "Clock mediano de ~" + N0(ma) + " MHz no início para ~" + N0(mb) + " MHz no fim com GPU ≥90%.";
                    f.Advice = "A GPU está reduzindo clock por temperatura ou limite de energia. Veja a temperatura e o consumo; confira cabos PCIe e a fonte.";
                    f.Weigh(Comp.Cooling, 0.25).Weigh(Comp.Gpu, 0.10).Weigh(Comp.Psu, 0.10);
                    d.Findings.Add(f);
                }
            }

            // ---------- Tensão de +12V
            if (v12.Length > 0)
            {
                double mn = Stat.Percentile(v12, 0.01), mx = Stat.Max(v12), avg = Stat.Avg(v12);
                Finding f = new Finding(); f.Source = "Telemetria";
                if (mn < 11.0)
                {
                    f.Sev = Sev.Crit; f.Title = "Tensão de +12 V caiu muito — mínimo " + N1(mn) + " V";
                    f.Advice = "Queda abaixo de 11,4 V sob carga é sinal clássico de fonte fraca/envelhecida. Confirme com multímetro e, se possível, teste com outra fonte.";
                    f.Weigh(Comp.Psu, 0.60);
                }
                else if (mn < 11.4 || mx > 12.6)
                {
                    f.Sev = Sev.Warn; f.Title = "Tensão de +12 V fora da tolerância de ±5% (mín " + N1(mn) + " V, máx " + N1(mx) + " V)";
                    f.Advice = "O sensor da placa-mãe tem imprecisão de alguns %, mas desvios grandes merecem teste com multímetro ou outra fonte.";
                    f.Weigh(Comp.Psu, 0.35);
                }
                else
                {
                    f.Sev = Sev.Ok; f.Title = "Tensão de +12 V estável (média " + N1(avg) + " V)";
                }
                f.Evidence = "Mín (1%) " + N1(mn) + " V • média " + N1(avg) + " V • máx " + N1(mx) + " V. Leituras de placa-mãe são aproximadas.";
                d.Findings.Add(f);
            }

            // ---------- Travamentos do próprio sistema (lacunas de amostragem)
            int stalls = 0; double worst = 0; DateTime worstAt = DateTime.MinValue;
            foreach (Sample s in all)
            {
                double g = s.V[F.Gap];
                if (double.IsNaN(g)) continue;
                if (g > 6 && g < 120) { stalls++; if (g > worst) { worst = g; worstAt = s.Time; } }
            }
            if (stalls >= 2)
            {
                double w = Scale(stalls, 6);
                Finding f = new Finding();
                f.Sev = stalls >= 5 ? Sev.Warn : Sev.Info; f.Source = "Telemetria";
                f.Title = "O sistema 'congelou' por alguns segundos " + stalls + "× durante as partidas";
                f.Evidence = "O monitor ficou sem conseguir amostrar por mais de 6 s (pior: " + N0(worst) + " s em " + D(worstAt) + "). Isso só ocorre quando o PC inteiro trava.";
                f.Advice = "Congelamentos de sistema vêm de drivers (GPU, armazenamento, chipset), disco lento/falhando ou superaquecimento. Veja as outras evidências na mesma data.";
                f.Weigh(Comp.Software, 0.18 * w).Weigh(Comp.Gpu, 0.10 * w).Weigh(Comp.Cpu, 0.08 * w).Weigh(Comp.Disk, 0.10 * w);
                d.Findings.Add(f);
            }

            // ---------- DPC / interrupções
            List<double> dpc = new List<double>();
            foreach (Sample s in all) if (!double.IsNaN(s.V[F.Dpc]) && !double.IsNaN(s.V[F.Intr])) dpc.Add(s.V[F.Dpc] + s.V[F.Intr]);
            if (dpc.Count > 20)
            {
                double[] arr = dpc.ToArray();
                double avg = Stat.Avg(arr), p95 = Stat.Percentile(arr, 0.95);
                if (avg > 6 || p95 > 12)
                {
                    Finding f = new Finding();
                    f.Sev = Sev.Warn; f.Source = "Telemetria";
                    f.Title = "Tempo excessivo em drivers (DPC/interrupções): média " + N1(avg) + "%, p95 " + N1(p95) + "%";
                    f.Evidence = "Valores acima de ~5% sustentados causam engasgos de áudio/vídeo e microtravamentos mesmo com CPU livre.";
                    f.Advice = "Atualize chipset, BIOS, driver de rede/Wi-Fi/áudio/GPU. Use o LatencyMon para descobrir qual driver é o culpado.";
                    f.Weigh(Comp.Software, 0.38).Weigh(Comp.Board, 0.05);
                    d.Findings.Add(f);
                }
            }

            // ---------- RAM
            double[] avail = Stat.Vals(all, F.Avail), commit = Stat.Vals(all, F.Commit), hard = Stat.Vals(all, F.Hard);
            if (avail.Length > 0)
            {
                double mn = Stat.Min(avail), lowFrac = Stat.FracBelow(avail, 1024);
                double cp = commit.Length > 0 ? Stat.Percentile(commit, 0.95) : 0;
                if (lowFrac > 0.05 || cp > 92)
                {
                    Finding f = new Finding();
                    f.Sev = Sev.Warn; f.Source = "Telemetria";
                    f.Title = cp > 92 ? "Memória virtual (commit) quase no limite durante o jogo — p95 " + N0(cp) + "%" : "Pouca memória RAM livre durante o jogo (mínimo " + N0(mn) + " MB livres)";
                    f.Evidence = N0(lowFrac * 100) + "% do tempo com menos de 1 GB livre; memória comprometida p95 = " + N0(cp) + "%.";
                    f.Advice = "Não é defeito de hardware, e sim falta de capacidade: aumente o arquivo de paginação (ex.: 16384–32768 MB em SSD), feche programas em segundo plano ou adicione RAM.";
                    f.Weigh(Comp.Ram, 0.10).Weigh(Comp.Software, 0.05);
                    d.Findings.Add(f);
                }
            }
            if (hard.Length > 20 && Stat.Percentile(hard, 0.95) > 500)
            {
                Finding f = new Finding();
                f.Sev = Sev.Info; f.Source = "Telemetria";
                f.Title = "Muita leitura de disco por falta de memória (falhas de página altas)";
                f.Evidence = "p95 de " + N0(Stat.Percentile(hard, 0.95)) + " páginas/s lidas do disco. Gera microtravamentos.";
                f.Advice = "Mais RAM ou menos programas abertos resolvem; instalar o jogo em SSD também ajuda.";
                f.Weigh(Comp.Disk, 0.06).Weigh(Comp.Ram, 0.08);
                d.Findings.Add(f);
            }

            // ---------- Disco
            double[] lat = Stat.Vals(all, F.DiskLat);
            if (lat.Length > 20)
            {
                double p99 = Stat.Percentile(lat, 0.99), mx = Stat.Max(lat);
                if (mx >= 1000 || p99 >= 100)
                {
                    Finding f = new Finding();
                    f.Sev = mx >= 2000 ? Sev.Crit : Sev.Warn; f.Source = "Telemetria";
                    f.Title = "Disco com picos de latência — máx " + N0(mx) + " ms, p99 " + N0(p99) + " ms";
                    f.Evidence = "Um SSD saudável responde em poucos ms. Picos acima de 100–1000 ms congelam o jogo enquanto ele lê arquivos.";
                    f.Advice = "Verifique SMART/saúde do disco, espaço livre (SSD cheio fica lento), cabos SATA e firmware. Se for HDD, mova o jogo para SSD.";
                    f.Weigh(Comp.Disk, mx >= 2000 ? 0.45 : 0.28).Weigh(Comp.Board, 0.04);
                    d.Findings.Add(f);
                }
            }
            double[] dt = Stat.Vals(all, F.DiskTemp);
            if (dt.Length > 0 && Stat.Max(dt) >= 70)
            {
                Finding f = new Finding();
                f.Sev = Stat.Max(dt) >= 80 ? Sev.Warn : Sev.Info; f.Source = "Telemetria";
                f.Title = "SSD/HD quente — máximo " + N0(Stat.Max(dt)) + " °C";
                f.Evidence = "NVMe costuma reduzir velocidade (throttle) por volta de 80 °C.";
                f.Advice = "Instale dissipador no SSD ou melhore o fluxo de ar perto do slot M.2.";
                f.Weigh(Comp.Cooling, 0.10).Weigh(Comp.Disk, 0.10);
                d.Findings.Add(f);
            }

            // ---------- VRAM
            double[] vram = Stat.Vals(all, F.GpuMemPct);
            if (vram.Length > 20 && Stat.FracAbove(vram, 95) > 0.10)
            {
                Finding f = new Finding();
                f.Sev = Sev.Info; f.Source = "Telemetria";
                f.Title = "VRAM da GPU quase cheia em " + N0(Stat.FracAbove(vram, 95) * 100) + "% do tempo";
                f.Evidence = "Quando a VRAM enche, o jogo usa a RAM e engasga.";
                f.Advice = "Reduza a qualidade das texturas ou a resolução. Não indica defeito.";
                f.Weigh(Comp.Gpu, 0.05);
                d.Findings.Add(f);
            }

            // ---------- Gargalo de CPU (informativo)
            double[] gpuU = Stat.Vals(all, F.Gpu), core = Stat.Vals(all, F.CoreMax);
            if (gpuU.Length > 30 && core.Length > 30 && Stat.Avg(gpuU) < 75 && Stat.Percentile(core, 0.9) >= 92)
            {
                Finding f = new Finding();
                f.Sev = Sev.Info; f.Source = "Telemetria";
                f.Title = "Gargalo de CPU: um núcleo no limite enquanto a GPU não é usada por completo";
                f.Evidence = "GPU média " + N0(Stat.Avg(gpuU)) + "% com o núcleo mais ocupado a 90%+ da maior parte do tempo.";
                f.Advice = "Explica FPS baixo/instável sem ser defeito. Reduzir configurações dependentes de CPU (distância, multidões) ajuda.";
                d.Findings.Add(f);
            }

            // ---------- Sessões interrompidas
            foreach (SessionData sd in sessions)
            {
                if (sd.Info.Status != "interrupted") continue;
                if (sd.Info.Game.StartsWith("[Teste]")) continue;   // queda durante teste é tratada pelo resultado do teste
                List<Sample> tail = sd.S.Count > 5 ? sd.S.GetRange(sd.S.Count - 5, 5) : sd.S;
                double lc = Stat.Avg(Stat.Vals(tail, F.Cpu)), lg = Stat.Avg(Stat.Vals(tail, F.Gpu));
                double lct = Stat.Max(Stat.Vals(tail, F.CpuTemp)), lgt = Stat.Max(Stat.Vals(tail, F.GpuTemp));
                double lgh = Stat.Max(Stat.Vals(tail, F.GpuHot));
                double lgp = Stat.Avg(Stat.Vals(tail, F.GpuPower));
                DateTime end = sd.Info.End ?? sd.Info.Start;

                WinEvent power = null;
                foreach (WinEvent e in events)
                {
                    if (e.Provider == "Microsoft-Windows-Kernel-Power" && e.Id == 41 && e.Time >= end.AddMinutes(-2) && e.Time <= end.AddMinutes(45))
                    { power = e; }
                }
                bool confirmed = power != null;
                bool highLoad = (!double.IsNaN(lc) && lc >= 80) || (!double.IsNaN(lg) && lg >= 90);
                bool lowLoad = !double.IsNaN(lc) && !double.IsNaN(lg) && lc < 40 && lg < 40;
                double k = confirmed ? 1.0 : 0.4;

                Finding f = new Finding();
                f.Sev = confirmed ? Sev.Crit : Sev.Warn; f.Source = "Telemetria";
                f.Title = "Partida interrompida abruptamente: " + sd.Info.Game + " (" + D(sd.Info.Start) + ")";
                StringBuilder ev = new StringBuilder();
                ev.Append("A gravação parou de repente às " + D(end) + ", sem fechamento normal. ");
                ev.Append(confirmed ? "O Windows registrou reinício inesperado logo depois (Kernel-Power 41 em " + D(power.Time) + "). " :
                    "Não há registro de reinício do Windows — o aplicativo pode ter sido encerrado à força. ");
                ev.Append("\nÚltimas leituras antes da queda: ");
                ev.Append("CPU " + Stat.Fmt(lc, "%", 0) + ", GPU " + Stat.Fmt(lg, "%", 0));
                if (!double.IsNaN(lct)) ev.Append(", temp. CPU " + Stat.Fmt(lct, "°C", 0));
                if (!double.IsNaN(lgt)) ev.Append(", temp. GPU " + Stat.Fmt(lgt, "°C", 0));
                if (!double.IsNaN(lgh)) ev.Append(", hotspot " + Stat.Fmt(lgh, "°C", 0));
                if (!double.IsNaN(lgp)) ev.Append(", GPU " + Stat.Fmt(lgp, "W", 0));
                ev.Append(".");
                f.Evidence = ev.ToString();

                f.Weigh(Comp.Psu, 0.30 * k).Weigh(Comp.Cooling, 0.15 * k).Weigh(Comp.Gpu, 0.14 * k)
                 .Weigh(Comp.Cpu, 0.10 * k).Weigh(Comp.Ram, 0.10 * k).Weigh(Comp.Board, 0.08 * k);
                string hint = "";
                if ((!double.IsNaN(lct) && lct >= 90) || (!double.IsNaN(lgt) && lgt >= 90) || (!double.IsNaN(lgh) && lgh >= 105))
                {
                    f.W[Comp.Cooling] = Math.Max(f.W[Comp.Cooling], 0.55 * k);
                    hint = "A temperatura estava altíssima no momento da queda: proteção térmica é a causa mais provável. ";
                }
                else if (highLoad)
                {
                    f.W[Comp.Psu] = Math.Max(f.W[Comp.Psu], 0.42 * k);
                    hint = "A queda ocorreu sob carga pesada com temperaturas normais: fonte incapaz de entregar potência (ou transiente da GPU) é o suspeito nº 1. ";
                }
                else if (lowLoad)
                {
                    f.W[Comp.Software] = 0.20 * k; f.W[Comp.Ram] = Math.Max(f.W[Comp.Ram], 0.18 * k);
                    hint = "A queda ocorreu com carga baixa: aponta mais para RAM/driver/software do que para fonte ou refrigeração. ";
                }
                f.Advice = hint + "Se o PC reinicia sem aviso, teste com outra fonte, confira conectores 8-pin/12VHPWR e a temperatura.";
                d.Findings.Add(f);
            }

            // ---------- Sessões concluídas com o jogo fechando por erro
            foreach (SessionData sd in sessions)
            {
                if (sd.Info.Note.StartsWith("O jogo fechou com erro"))
                {
                    Finding f = new Finding();
                    f.Sev = Sev.Info; f.Source = "Telemetria";
                    f.Title = sd.Info.Game + " fechou com erro (" + D(sd.Info.End ?? sd.Info.Start) + ")";
                    f.Evidence = sd.Info.Note + ".";
                    f.Advice = "Falha isolada de um jogo costuma ser bug do próprio jogo ou driver; se repete em vários jogos, veja RAM e GPU.";
                    f.Weigh(Comp.Software, 0.05);
                    d.Findings.Add(f);
                }
            }
        }

        // ================================================================ FECHAMENTO
        static void Finish(Diagnosis d)
        {
            double[] prod = new double[8];
            for (int i = 0; i < prod.Length; i++) prod[i] = 1;
            foreach (Finding f in d.Findings)
            {
                foreach (KeyValuePair<Comp, double> kv in f.W)
                    prod[(int)kv.Key] *= 1 - Math.Max(0, Math.Min(0.92, kv.Value));
            }
            for (int i = 0; i < prod.Length; i++) d.Score[i] = Math.Round((1 - prod[i]) * 100);

            d.Findings.Sort(delegate(Finding a, Finding b)
            {
                int c = a.Sev.CompareTo(b.Sev);
                if (c != 0) return c;
                double sa = 0, sb = 0;
                foreach (double v in a.W.Values) sa = Math.Max(sa, v);
                foreach (double v in b.W.Values) sb = Math.Max(sb, v);
                return sb.CompareTo(sa);
            });

            int best = 0;
            for (int i = 1; i < d.Score.Length; i++) if (d.Score[i] > d.Score[best]) best = i;
            double top = d.Score[best];
            int crit = d.Findings.FindAll(delegate(Finding f) { return f.Sev == Sev.Crit; }).Count;
            if (top < 20 && crit == 0)
            {
                d.Verdict = "Nenhum componente se destaca como defeituoso com os dados atuais. Continue gravando partidas, principalmente quando o problema acontecer.";
            }
            else
            {
                string conf = top >= 70 ? "forte" : top >= 45 ? "moderado" : "fraco";
                List<string> rest = new List<string>();
                for (int i = 0; i < d.Score.Length; i++)
                    if (i != best && d.Score[i] >= 30) rest.Add(CompInfo.Names[i] + " (" + d.Score[i] + "%)");
                d.Verdict = "Principal suspeito: " + CompInfo.Names[best] + " — indício " + conf + " (" + top + "%)." +
                    (rest.Count > 0 ? " Também sob suspeita: " + string.Join(", ", rest.ToArray()) + "." : "") +
                    " Isto é uma estimativa por evidências, não um laudo: confirme com os testes sugeridos abaixo.";
            }
        }
    }
}
