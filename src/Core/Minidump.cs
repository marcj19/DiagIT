using System;
using System.Collections.Generic;
using System.Text;

namespace DiagIT
{
    /// <summary>
    /// Lê o "triage dump" de um minidump de kernel 64 bits (C:\Windows\Minidump) para descobrir o driver culpado da tela azul:
    /// lista de drivers carregados (nome, endereço, tamanho) + trecho da pilha no momento da falha.
    /// Layout: cabeçalho DUMP_HEADER64 de 0x2000 bytes e, logo depois, TRIAGE_DUMP64. O tamanho de cada item da lista de drivers
    /// muda entre versões do Windows, então o leitor testa as combinações conhecidas e só aceita uma em que todos os itens fazem sentido.
    /// </summary>
    public static class Minidump
    {
        public class Module { public string Name = ""; public ulong Base; public uint Size; }

        const ulong KernelSpace = 0xFFFF800000000000UL;

        static uint U32(byte[] b, long o) { return o >= 0 && o + 4 <= b.Length ? BitConverter.ToUInt32(b, (int)o) : 0; }
        static ulong U64(byte[] b, long o) { return o >= 0 && o + 8 <= b.Length ? BitConverter.ToUInt64(b, (int)o) : 0; }

        static bool IsCore(string n)
        {
            n = n.ToLowerInvariant();
            return n.StartsWith("ntoskrnl") || n.StartsWith("ntkrnl") || n == "hal.dll";
        }

        // módulos do Windows que aparecem na pilha de quase toda falha e raramente são a causa
        static readonly string[] Generic = { "fltmgr.sys", "wdf01000.sys", "ci.dll", "cng.sys", "ksecdd.sys", "clfs.sys", "tm.sys", "pshed.dll", "kdcom.dll", "werkernel.sys", "watchdog.sys", "dxgkrnl.sys" };

        /// <summary>Nome em DUMP_STRING (Length + WCHAR[]); o comprimento é testado em caracteres e em bytes.</summary>
        static string ReadName(byte[] b, long o)
        {
            uint len = U32(b, o);
            if (len == 0 || len > 512) return null;
            foreach (uint chars in new uint[] { len, len / 2 })
            {
                if (chars < 3 || o + 4 + chars * 2 > b.Length) continue;
                string s = Encoding.Unicode.GetString(b, (int)o + 4, (int)chars * 2);
                bool ok = s.IndexOf('.') > 0;
                foreach (char ch in s) if (ch < 0x20 || ch > 0x7E) { ok = false; break; }
                if (ok) return s;
            }
            return null;
        }

        /// <summary>Lista de drivers do triage dump, validada; null se o formato não foi reconhecido.</summary>
        /// <summary>Layout reconhecido na última leitura (para diagnóstico do próprio leitor).</summary>
        public static string LastLayout = "";

        public static List<Module> Drivers(byte[] b, out long triageBase)
        {
            triageBase = 0;
            LastLayout = "";
            const int t = 0x2000;
            if (b.Length < t + 0x80) return null;
            uint drvOff = U32(b, t + 0x30), count = U32(b, t + 0x34), pool = U32(b, t + 0x38);
            if (count < 3 || count > 4000) return null;
            List<Module> best = null; int bestOk = 0;
            foreach (long baseOff in new long[] { 0, t })                       // offsets relativos ao arquivo ou ao triage dump
                foreach (int stride in new int[] { 0x90, 0xA0, 0x98, 0xA8, 0xB0, 0x88, 0x80, 0xC0 })
                {
                    long start = baseOff + drvOff;
                    if (start + (long)count * stride > b.Length) continue;
                    List<Module> l = new List<Module>();
                    int ok = 0;
                    for (int i = 0; i < count; i++)
                    {
                        long e = start + (long)i * stride;
                        uint nameOff = U32(b, e);
                        ulong dllBase = U64(b, e + 0x38);
                        uint size = U32(b, e + 0x48);
                        string name = ReadName(b, baseOff + nameOff) ?? ReadName(b, baseOff + pool + nameOff);
                        Module m = new Module(); m.Name = name ?? ""; m.Base = dllBase; m.Size = size;
                        l.Add(m);
                        if (name != null && dllBase >= KernelSpace && size >= 0x1000 && size < 0x40000000) ok++;
                    }
                    if (ok > bestOk) { bestOk = ok; best = l; triageBase = baseOff; LastLayout = "base 0x" + baseOff.ToString("X") + ", item 0x" + stride.ToString("X") + ", " + ok + "/" + count + " válidos"; }
                }
            // exige que quase todos os itens sejam válidos: um layout errado produz lixo na maioria deles
            if (best == null || bestOk < count * 0.9) return null;
            best.RemoveAll(delegate(Module m) { return m.Name.Length == 0 || m.Base < KernelSpace; });
            return best;
        }

        static Module At(List<Module> mods, ulong addr)
        {
            if (addr < KernelSpace) return null;
            foreach (Module m in mods) if (addr >= m.Base && addr < m.Base + m.Size) return m;
            return null;
        }

        /// <summary>
        /// Preenche Culprit/CulpritHow/StackModules do dump. Ordem de confiança: endereço nos parâmetros da tela azul
        /// (quando o código diz qual parâmetro aponta o código que falhou) → instrução da falha (contexto) → primeiro driver na pilha.
        /// </summary>
        public static void Analyze(CrashDump d, byte[] b)
        {
            long tb;
            List<Module> mods = Drivers(b, out tb);
            if (mods == null || mods.Count == 0) return;
            d.ModulesRead = mods.Count;
            const int t = 0x2000;

            // 1) parâmetro da tela azul que aponta o código que falhou
            BugRule rule = Rules.Bug((int)(d.Code & 0xFFFF));
            foreach (int pi in rule.AddrParams)
            {
                Module m = At(mods, d.P[pi - 1]);
                if (m != null && !IsCore(m.Name)) { d.Culprit = m.Name; d.CulpritHow = "parâmetro " + pi + " da tela azul"; break; }
            }

            // 2) instrução em execução na hora da falha (CONTEXT.Rip, deslocamento 0xF8)
            uint ctx = U32(b, t + 0x0C);
            Module rip = At(mods, U64(b, tb + ctx + 0xF8));
            if (d.Culprit == null && rip != null && !IsCore(rip.Name) && Array.IndexOf(Generic, rip.Name.ToLowerInvariant()) < 0)
            { d.Culprit = rip.Name; d.CulpritHow = "instrução que falhou"; }

            // 3) pilha: endereços de retorno que caem dentro de algum driver
            uint stackOff = U32(b, t + 0x28), stackSize = U32(b, t + 0x2C);
            List<string> seen = new List<string>();
            string firstOther = null, firstAny = null;
            if (stackSize > 0 && stackSize < 0x100000)
                for (long o = tb + stackOff; o + 8 <= tb + stackOff + stackSize && o + 8 <= b.Length; o += 8)
                {
                    Module m = At(mods, U64(b, o));
                    if (m == null) continue;
                    if (!seen.Contains(m.Name)) seen.Add(m.Name);
                    if (IsCore(m.Name)) continue;
                    if (firstAny == null) firstAny = m.Name;
                    if (firstOther == null && Array.IndexOf(Generic, m.Name.ToLowerInvariant()) < 0) firstOther = m.Name;
                }
            d.StackModules = seen;
            if (d.Culprit == null && (firstOther ?? firstAny) != null) { d.Culprit = firstOther ?? firstAny; d.CulpritHow = "pilha no momento da falha"; }
            if (d.Culprit == null && seen.Count > 0 && IsCore(seen[0])) { d.Culprit = seen[0]; d.CulpritHow = "só o núcleo do Windows na pilha"; }
        }
    }
}
