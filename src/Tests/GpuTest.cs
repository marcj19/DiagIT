using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DiagIT
{
    /// <summary>
    /// Acesso mínimo ao Direct3D 11 (somente compute shaders), via tabela de métodos COM.
    /// Não depende de nenhuma biblioteca externa: usa d3d11.dll e d3dcompiler_47.dll do próprio Windows.
    /// </summary>
    public sealed class Dx11 : IDisposable
    {
        [DllImport("d3d11.dll")]
        static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, int[] levels, uint numLevels,
            uint sdk, out IntPtr device, out int level, out IntPtr context);

        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
        static extern int D3DCompile([MarshalAs(UnmanagedType.LPStr)] string src, UIntPtr len, string name, IntPtr defines, IntPtr include,
            string entry, string target, uint f1, uint f2, out IntPtr code, out IntPtr errors);

        [StructLayout(LayoutKind.Sequential)]
        public struct BufferDesc { public uint ByteWidth; public int Usage; public uint BindFlags; public uint CPUAccessFlags; public uint MiscFlags; public uint StructureByteStride; }
        [StructLayout(LayoutKind.Sequential)]
        public struct UavDesc { public int Format; public int ViewDimension; public uint FirstElement; public uint NumElements; public uint Flags; public uint Pad; }
        [StructLayout(LayoutKind.Sequential)]
        public struct Mapped { public IntPtr Data; public uint RowPitch; public uint DepthPitch; }

        delegate uint ReleaseFn(IntPtr self);
        delegate int CreateBufferFn(IntPtr self, ref BufferDesc d, IntPtr init, out IntPtr buf);
        delegate int CreateUavFn(IntPtr self, IntPtr res, ref UavDesc d, out IntPtr uav);
        delegate int CreateCsFn(IntPtr self, IntPtr code, UIntPtr len, IntPtr linkage, out IntPtr cs);
        delegate int RemovedFn(IntPtr self);
        delegate void SetUavsFn(IntPtr self, uint start, uint num, IntPtr[] uavs, IntPtr counts);
        delegate void SetCsFn(IntPtr self, IntPtr cs, IntPtr inst, uint n);
        delegate void SetCbsFn(IntPtr self, uint start, uint num, IntPtr[] bufs);
        delegate void DispatchFn(IntPtr self, uint x, uint y, uint z);
        delegate void CopyFn(IntPtr self, IntPtr dst, IntPtr src);
        delegate void UpdateFn(IntPtr self, IntPtr dst, uint sub, IntPtr box, IntPtr src, uint row, uint depth);
        delegate int MapFn(IntPtr self, IntPtr res, uint sub, int type, uint flags, out Mapped m);
        delegate void UnmapFn(IntPtr self, IntPtr res, uint sub);
        delegate void FlushFn(IntPtr self);
        delegate IntPtr BlobPtrFn(IntPtr self);
        delegate UIntPtr BlobSizeFn(IntPtr self);

        public IntPtr Dev, Ctx;
        readonly List<IntPtr> owned = new List<IntPtr>();

        static T Fn<T>(IntPtr obj, int index) where T : class
        {
            IntPtr vt = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, index * IntPtr.Size), typeof(T)) as T;
        }

        CreateBufferFn createBuffer; CreateUavFn createUav; CreateCsFn createCs; RemovedFn removed;
        SetUavsFn setUavs; SetCsFn setCs; SetCbsFn setCbs; DispatchFn dispatch; CopyFn copy; UpdateFn update; MapFn map; UnmapFn unmap; FlushFn flush;

        public static Dx11 Create()
        {
            IntPtr dev, ctx; int lvl;
            int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0, new int[] { 0xb000 }, 1, 7, out dev, out lvl, out ctx);
            if (hr < 0) throw new Exception("Direct3D 11 indisponível (0x" + hr.ToString("X8") + ")");
            Dx11 d = new Dx11();
            d.Dev = dev; d.Ctx = ctx;
            d.createBuffer = Fn<CreateBufferFn>(dev, 3);
            d.createUav = Fn<CreateUavFn>(dev, 8);
            d.createCs = Fn<CreateCsFn>(dev, 18);
            d.removed = Fn<RemovedFn>(dev, 39);
            d.map = Fn<MapFn>(ctx, 14);
            d.unmap = Fn<UnmapFn>(ctx, 15);
            d.dispatch = Fn<DispatchFn>(ctx, 41);
            d.copy = Fn<CopyFn>(ctx, 47);
            d.update = Fn<UpdateFn>(ctx, 48);
            d.setUavs = Fn<SetUavsFn>(ctx, 68);
            d.setCs = Fn<SetCsFn>(ctx, 69);
            d.setCbs = Fn<SetCbsFn>(ctx, 71);
            d.flush = Fn<FlushFn>(ctx, 111);
            return d;
        }

        public IntPtr Compile(string hlsl, string entry)
        {
            IntPtr code, errs;
            int hr = D3DCompile(hlsl, new UIntPtr((uint)hlsl.Length), "diag", IntPtr.Zero, IntPtr.Zero, entry, "cs_5_0", 1u << 14, 0, out code, out errs);
            if (hr < 0)
            {
                string msg = errs != IntPtr.Zero ? Marshal.PtrToStringAnsi(Fn<BlobPtrFn>(errs, 3)(errs)) : "";
                if (errs != IntPtr.Zero) Release(errs);
                throw new Exception("Falha ao compilar shader " + entry + ": " + msg);
            }
            if (errs != IntPtr.Zero) Release(errs);
            IntPtr cs;
            hr = createCs(Dev, Fn<BlobPtrFn>(code, 3)(code), Fn<BlobSizeFn>(code, 4)(code), IntPtr.Zero, out cs);
            Release(code);
            Check(hr, "CreateComputeShader");
            owned.Add(cs);
            return cs;
        }

        /// <summary>Buffer bruto com UAV (para leitura/escrita no shader). Retorna false se faltar memória.</summary>
        public bool RawBuffer(uint bytes, out IntPtr buf, out IntPtr uav)
        {
            uav = IntPtr.Zero;
            BufferDesc d = new BufferDesc();
            d.ByteWidth = bytes; d.Usage = 0; d.BindFlags = 0x80; d.MiscFlags = 0x20;
            int hr = createBuffer(Dev, ref d, IntPtr.Zero, out buf);
            if (hr < 0) { buf = IntPtr.Zero; return false; }
            UavDesc u = new UavDesc();
            u.Format = 39; u.ViewDimension = 1; u.FirstElement = 0; u.NumElements = bytes / 4; u.Flags = 1;
            hr = createUav(Dev, buf, ref u, out uav);
            if (hr < 0) { Release(buf); buf = IntPtr.Zero; return false; }
            owned.Add(buf); owned.Add(uav);
            return true;
        }

        public IntPtr ConstantBuffer(uint bytes)
        {
            BufferDesc d = new BufferDesc();
            d.ByteWidth = bytes; d.Usage = 0; d.BindFlags = 0x4;
            IntPtr b;
            Check(createBuffer(Dev, ref d, IntPtr.Zero, out b), "CreateBuffer(cb)");
            owned.Add(b);
            return b;
        }

        public IntPtr Staging(uint bytes) { return Staging(bytes, false); }

        public IntPtr Staging(uint bytes, bool write)
        {
            BufferDesc d = new BufferDesc();
            d.ByteWidth = bytes; d.Usage = 3; d.CPUAccessFlags = write ? 0x10000u : 0x20000u;
            IntPtr b;
            Check(createBuffer(Dev, ref d, IntPtr.Zero, out b), "CreateBuffer(staging)");
            owned.Add(b);
            return b;
        }

        public void Upload(IntPtr buf, uint[] data)
        {
            GCHandle h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { update(Ctx, buf, 0, IntPtr.Zero, h.AddrOfPinnedObject(), 0, 0); }
            finally { h.Free(); }
        }

        public void Run(IntPtr cs, IntPtr cb, IntPtr[] uavs, uint groups)
        {
            setCs(Ctx, cs, IntPtr.Zero, 0);
            setCbs(Ctx, 0, 1, new IntPtr[] { cb });
            setUavs(Ctx, 0, (uint)uavs.Length, uavs, IntPtr.Zero);
            dispatch(Ctx, groups, 1, 1);
        }

        /// <summary>Copia para a área de leitura e espera a GPU terminar. Retorna o HRESULT (falha = GPU travou/removida).</summary>
        public int Read(IntPtr src, IntPtr staging, uint[] dst)
        {
            copy(Ctx, staging, src);
            Mapped m;
            int hr = map(Ctx, staging, 0, 1, 0, out m);
            if (hr < 0) return hr;
            int[] tmp = new int[dst.Length];
            Marshal.Copy(m.Data, tmp, 0, dst.Length);
            for (int i = 0; i < dst.Length; i++) dst[i] = (uint)tmp[i];
            unmap(Ctx, staging, 0);
            return 0;
        }

        public void Flush() { flush(Ctx); }
        public void Copy(IntPtr dst, IntPtr src) { copy(Ctx, dst, src); }
        public int RemovedReason() { return removed(Dev); }

        static void Check(int hr, string what) { if (hr < 0) throw new Exception(what + " falhou (0x" + hr.ToString("X8") + ")"); }

        public static uint Release(IntPtr p) { return p == IntPtr.Zero ? 0 : Fn<ReleaseFn>(p, 2)(p); }

        public void Dispose()
        {
            for (int i = owned.Count - 1; i >= 0; i--) { try { Release(owned[i]); } catch { } }
            owned.Clear();
            try { Release(Ctx); } catch { }
            try { Release(Dev); } catch { }
            Ctx = IntPtr.Zero; Dev = IntPtr.Zero;
        }

        public static string Hr(int hr)
        {
            switch ((uint)hr)
            {
                case 0x887A0005: return "DXGI_ERROR_DEVICE_REMOVED (a GPU foi removida/reiniciada pelo Windows)";
                case 0x887A0006: return "DXGI_ERROR_DEVICE_HUNG (a GPU travou executando comandos)";
                case 0x887A0007: return "DXGI_ERROR_DEVICE_RESET (a GPU foi reiniciada)";
                case 0x887A0020: return "DXGI_ERROR_DRIVER_INTERNAL_ERROR (erro interno do driver de vídeo)";
                default: return "0x" + hr.ToString("X8");
            }
        }
    }

    /// <summary>
    /// Teste da placa de vídeo: (1) estresse de cálculo executado duas vezes e comparado bit a bit; (2) teste de padrões
    /// em quase toda a VRAM livre; (3) detecção de travamento da GPU (o mesmo "driver parou de responder" dos jogos).
    /// </summary>
    public class GpuTest : HwTest
    {
        public override TestKind Kind { get { return TestKind.Gpu; } }
        public class Options { public bool ComputeOnly; public long VramMaxMB; }

        const string HLSL = @"
cbuffer P : register(b0) { uint gPass; uint gSeed; uint gWords; uint gSpan; uint gIters; uint p0; uint p1; uint p2; };
RWByteAddressBuffer B0 : register(u0);
RWByteAddressBuffer B1 : register(u1);
RWByteAddressBuffer E  : register(u2);
uint H(uint x) { x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16; return x; }
void Err(uint idx, uint expect, uint got) {
  uint o; E.InterlockedAdd(0, 1, o);
  if (o < 16) { E.Store(16 + o * 12, idx); E.Store(20 + o * 12, expect); E.Store(24 + o * 12, got); }
}
[numthreads(256,1,1)] void Fill(uint3 id : SV_DispatchThreadID) {
  for (uint i = id.x; i < gWords; i += gSpan) B0.Store(i << 2, H(i ^ gSeed) ^ gPass);
}
[numthreads(256,1,1)] void Check(uint3 id : SV_DispatchThreadID) {
  for (uint i = id.x; i < gWords; i += gSpan) { uint e = H(i ^ gSeed) ^ gPass; uint v = B0.Load(i << 2); if (v != e) Err(i, e, v); }
}
[numthreads(256,1,1)] void Burn(uint3 id : SV_DispatchThreadID) {
  float4 a = float4(id.x * 1e-6 + 1.0, gSeed * 1e-9 + 0.5, 0.25, 0.125);
  float4 b = float4(0.999, 1.001, 0.5, 0.75);
  float4 c = float4(0.1, 0.2, 0.3, 0.4);
  [loop] for (uint k = 0; k < gIters; k++) {
    a = mad(a, b, c); b = mad(b, c, a) * 0.5; c = mad(c, a, b) * 0.25;
    a = frac(a) + 1.0; b = frac(b) + 0.5; c = frac(c) + 0.25;
  }
  B0.Store(id.x << 2, H(asuint(a.x) ^ (asuint(a.y) * 3) ^ (asuint(b.z) * 7) ^ (asuint(c.w) * 13) ^ id.x));
}
[numthreads(256,1,1)] void Compare(uint3 id : SV_DispatchThreadID) {
  for (uint i = id.x; i < gWords; i += gSpan) { uint x = B0.Load(i << 2); uint y = B1.Load(i << 2); if (x != y) Err(i, x, y); }
}";

        const uint BURN_GROUPS = 16384;           // 4,2 milhões de threads por despacho
        const uint CHUNK = 256u * 1024 * 1024;    // blocos de VRAM de 256 MB
        const uint SPAN_GROUPS = 8192;

        Dx11 dx;
        IntPtr csFill, csCheck, csBurn, csCompare, cb, errBuf, errUav, staging;
        uint[] cbData = new uint[8];
        uint[] errRead = new uint[64];

        public override void Run(TestContext ctx, TestResult r, int seconds)
        {
            Options opt = ctx.Options as Options ?? new Options();
            ctx.Phase = "Iniciando Direct3D";
            dx = Dx11.Create();
            try
            {
                csFill = dx.Compile(HLSL, "Fill");
                csCheck = dx.Compile(HLSL, "Check");
                csBurn = dx.Compile(HLSL, "Burn");
                csCompare = dx.Compile(HLSL, "Compare");
                cb = dx.ConstantBuffer(32);
                if (!dx.RawBuffer(256, out errBuf, out errUav)) throw new Exception("sem memória para o buffer de erros");
                staging = dx.Staging(256);

                IntPtr burnA, burnAU, burnB, burnBU;
                uint burnBytes = BURN_GROUPS * 256 * 4;
                if (!dx.RawBuffer(burnBytes, out burnA, out burnAU) || !dx.RawBuffer(burnBytes, out burnB, out burnBU))
                    throw new Exception("sem memória de vídeo para o teste de cálculo");

                // ---- blocos de VRAM
                List<IntPtr[]> vram = new List<IntPtr[]>();
                if (!opt.ComputeOnly)
                {
                    ctx.Phase = "Reservando memória de vídeo";
                    double total = NativeInfo.TotalVramMB();
                    double used = UsedVramMB(ctx);
                    double targetMb = double.IsNaN(total) ? 2048 : total - (double.IsNaN(used) ? 1024 : used) - 700;
                    if (opt.VramMaxMB > 0) targetMb = Math.Min(targetMb, opt.VramMaxMB);
                    long target = (long)Math.Max(0, targetMb) * 1024 * 1024;
                    long got = 0;
                    while (got + CHUNK <= target && !ctx.Cancel)
                    {
                        IntPtr b, u;
                        if (!dx.RawBuffer(CHUNK, out b, out u)) break;
                        vram.Add(new IntPtr[] { b, u });
                        got += CHUNK;
                    }
                    r.Set("vram_total_mb", total);
                    r.Set("vram_tested_mb", got / 1048576.0);
                    r.Detail("VRAM sob teste: " + (got / 1048576.0).ToString("N0") + " MB de " + (double.IsNaN(total) ? "?" : total.ToString("N0")) + " MB (o restante está em uso pelo Windows e programas abertos).");
                }

                // ---- calibra a carga para cada despacho durar ~150 ms (bem abaixo do limite de 2 s do Windows)
                ctx.Phase = "Calibrando carga";
                uint iters = 256;
                for (int k = 0; k < 6; k++)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    Burn(burnAU, 1, iters);
                    int hr = SyncErrors();
                    if (hr < 0) { Removed(ctx, r, hr); return; }
                    double ms = sw.Elapsed.TotalMilliseconds;
                    if (ms > 100 || iters >= 1u << 20) break;
                    iters = (uint)Math.Min(1u << 20, iters * Math.Max(2, Math.Min(8, 150 / Math.Max(1, ms))));
                }
                r.Set("burn_iters", iters);

                // ---- banda real do barramento PCIe (RAM -> VRAM e VRAM -> RAM), cronometrada
                ctx.Phase = "Medindo a banda PCIe";
                MeasurePcie(r);

                // ---- laço principal
                ctx.Phase = "Estressando GPU";
                long computeErr = 0, vramErr = 0, cycles = 0, vramPasses = 0;
                DateTime start = DateTime.Now, end = start.AddSeconds(seconds);
                while (!ctx.Cancel && DateTime.Now < end)
                {
                    uint seed = (uint)(cycles * 2654435761u);
                    Burn(burnAU, seed, iters);
                    Burn(burnBU, seed, iters);
                    ClearErrors();
                    SetCb(0, 0, burnBytes / 4, SPAN_GROUPS * 256, 0);
                    dx.Run(csCompare, cb, new IntPtr[] { burnAU, burnBU, errUav }, SPAN_GROUPS);
                    int hr = SyncErrors();
                    if (hr < 0) { Removed(ctx, r, hr); return; }
                    if (errRead[0] > 0)
                    {
                        computeErr += errRead[0];
                        ctx.AddError();
                        r.Detail("ERRO DE CÁLCULO DA GPU às " + DateTime.Now.ToString("HH:mm:ss") + ": " + errRead[0] + " resultados diferentes entre duas execuções idênticas.");
                    }
                    cycles++;

                    if (vram.Count > 0 && cycles % 4 == 0)
                    {
                        ctx.Phase = "Testando VRAM (passada " + (vramPasses + 1) + ")";
                        for (int i = 0; i < vram.Count && !ctx.Cancel; i++)
                        {
                            uint pass = (uint)(vramPasses * 0x9E3779B9u) ^ (uint)i;
                            uint vseed = (uint)(i * 0x85EBCA6Bu + 17);
                            SetCb(pass, vseed, CHUNK / 4, SPAN_GROUPS * 256, 0);
                            dx.Run(csFill, cb, new IntPtr[] { vram[i][1], burnBU, errUav }, SPAN_GROUPS);
                        }
                        ClearErrors();
                        for (int i = 0; i < vram.Count && !ctx.Cancel; i++)
                        {
                            uint pass = (uint)(vramPasses * 0x9E3779B9u) ^ (uint)i;
                            uint vseed = (uint)(i * 0x85EBCA6Bu + 17);
                            SetCb(pass, vseed, CHUNK / 4, SPAN_GROUPS * 256, 0);
                            dx.Run(csCheck, cb, new IntPtr[] { vram[i][1], burnBU, errUav }, SPAN_GROUPS);
                        }
                        hr = SyncErrors();
                        if (hr < 0) { Removed(ctx, r, hr); return; }
                        if (errRead[0] > 0)
                        {
                            vramErr += errRead[0];
                            ctx.AddError();
                            if (vramErr <= 64)
                                for (int e = 0; e < Math.Min(4, (int)errRead[0]); e++)
                                    r.Detail("ERRO DE VRAM: posição " + errRead[4 + e * 3] + " gravado " + errRead[5 + e * 3].ToString("X8") + " lido " + errRead[6 + e * 3].ToString("X8") +
                                        " (bits " + (errRead[5 + e * 3] ^ errRead[6 + e * 3]).ToString("X8") + ")");
                        }
                        vramPasses++;
                        ctx.Phase = "Estressando GPU";
                    }
                    ctx.Progress = Math.Min(1, (DateTime.Now - start).TotalSeconds / seconds);
                }

                r.Set("compute_errors", computeErr);
                r.Set("vram_errors", vramErr);
                r.Set("cycles", cycles);
                r.Set("vram_passes", vramPasses);
                double min = (DateTime.Now - start).TotalMinutes;
                r.Detail("Ciclos de cálculo verificados: " + cycles + ". Passadas completas na VRAM: " + vramPasses + ".");
                if (computeErr + vramErr > 0)
                {
                    r.Status = "failed";
                    r.Errors = (int)Math.Min(int.MaxValue, computeErr + vramErr);
                    r.Summary = (computeErr > 0 ? computeErr + " erro(s) de cálculo da GPU" : "") + (computeErr > 0 && vramErr > 0 ? " e " : "") +
                        (vramErr > 0 ? vramErr + " erro(s) na memória de vídeo (VRAM)" : "") +
                        ". Uma GPU saudável sempre repete o mesmo resultado; isso indica GPU/VRAM instável (temperatura, energia, overclock/undervolt) ou com defeito.";
                }
                else if (!ctx.Cancel || cycles > 0)
                {
                    r.Status = ctx.Cancel ? "aborted" : "passed";
                    r.Summary = "Nenhum erro: GPU em carga total por " + min.ToString("0.#") + " min sem travar, " + cycles + " cálculos idênticos conferidos" +
                        (vramPasses > 0 ? " e " + vramPasses + " passadas em " + (r.Get("vram_tested_mb") / 1024).ToString("0.0") + " GB de VRAM sem erro." : ".");
                }
            }
            finally
            {
                dx.Dispose();
            }
        }

        const uint PCIE_BLOCK = 128u * 1024 * 1024;

        void MeasurePcie(TestResult r)
        {
            try
            {
                IntPtr up = dx.Staging(PCIE_BLOCK, true), down = dx.Staging(PCIE_BLOCK, false);
                IntPtr vb, vu;
                if (!dx.RawBuffer(PCIE_BLOCK, out vb, out vu)) return;
                dx.Copy(vb, up); dx.Copy(down, vb);
                if (SyncErrors() < 0) return;
                const int N = 12;
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < N; i++) dx.Copy(vb, up);
                if (SyncErrors() < 0) return;
                double upGbs = N * (double)PCIE_BLOCK / sw.Elapsed.TotalSeconds / 1e9;
                sw.Restart();
                for (int i = 0; i < N; i++) dx.Copy(down, vb);
                if (SyncErrors() < 0) return;
                double downGbs = N * (double)PCIE_BLOCK / sw.Elapsed.TotalSeconds / 1e9;
                r.Set("pcie_up_gbs", upGbs);
                r.Set("pcie_down_gbs", downGbs);
                r.Detail("Banda PCIe medida: " + upGbs.ToString("0.0") + " GB/s da RAM para a GPU e " + downGbs.ToString("0.0") + " GB/s da GPU para a RAM " +
                    "(referência: PCIe 3.0 x8 ≈ 6–7 GB/s; PCIe 4.0 x8 ≈ 12–14 GB/s; PCIe 3.0 x16 ≈ 12–13 GB/s).");
            }
            catch (Exception e) { r.Detail("Banda PCIe não medida: " + e.Message); }
        }

        void Removed(TestContext ctx, TestResult r, int hr)
        {
            int reason = 0;
            try { reason = dx.RemovedReason(); } catch { }
            r.Status = "failed";
            r.Set("device_removed", 1);
            r.Errors++;
            ctx.AddError();
            r.Summary = "A GPU TRAVOU durante o teste: " + Dx11.Hr(hr) + (reason != 0 && reason != hr ? " — motivo informado: " + Dx11.Hr(reason) : "") +
                ". É o mesmo tipo de falha que fecha jogos/gera tela preta. Compare com a temperatura e o consumo registrados para separar defeito, driver ou energia.";
            r.Detail("Travamento às " + DateTime.Now.ToString("HH:mm:ss") + ".");
        }

        static double UsedVramMB(TestContext ctx)
        {
            Sample s = ctx.W != null ? ctx.W.Last : null;
            return s != null ? s[F.GpuMem] : double.NaN;
        }

        void SetCb(uint pass, uint seed, uint words, uint span, uint iters)
        {
            cbData[0] = pass; cbData[1] = seed; cbData[2] = words; cbData[3] = span; cbData[4] = iters;
            dx.Upload(cb, cbData);
        }

        void Burn(IntPtr uav, uint seed, uint iters)
        {
            SetCb(0, seed, 0, 0, iters);
            dx.Run(csBurn, cb, new IntPtr[] { uav, IntPtr.Zero, errUav }, BURN_GROUPS);
        }

        void ClearErrors() { dx.Upload(errBuf, new uint[64]); }

        int SyncErrors()
        {
            dx.Flush();
            return dx.Read(errBuf, staging, errRead);
        }
    }
}
