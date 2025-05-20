using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Windows.Interop;
using Coplt.Dropping;
using Coplt.Mathematics;
using Silk.NET.Core.Native;
using Silk.NET.DXGI;
using Silk.NET.Direct3D11;
using Silk.NET.Direct3D12;
using Silk.NET.Direct3D9;
using WpfD3d12.Utilities;
using Format9 = Silk.NET.Direct3D9.Format;
using Format = Silk.NET.DXGI.Format;
using PresentParameters = Silk.NET.Direct3D9.PresentParameters;
using RenderTargetViewDesc = Silk.NET.Direct3D12.RenderTargetViewDesc;
using RtvDimension = Silk.NET.Direct3D12.RtvDimension;

namespace WpfD3d12.Modules.Graphics;

[Dropping(Unmanaged = true)]
public partial class D3DImageSwapChain : ISwapChain
{
    #region Consts

    public const int FrameCount = 3;

    #endregion

    #region Fields

    [Drop]
    private ComPtr<ID3D12DescriptorHeap> m_rtv_heap;
    [Drop]
    private ComPtr<IDirect3D9Ex> m_dx_9_ex;
    [Drop]
    private ComPtr<IDirect3DDevice9Ex> m_device_9_ex;
    [Drop]
    private FixedArray3<ComPtr<ID3D12Resource1>> m_12_buffers = default;
    [Drop]
    private FixedArray3<ComPtr<IDirect3DSurface9>> m_9_surfaces = default;
    [Drop]
    private FixedArray3<ComPtr<IDirect3DTexture9>> m_9_buffers = default;
    [Drop]
    private FixedArray3<ComPtr<ID3D11Texture2D>> m_11_buffers = default;
    [Drop]
    private ComPtr<ID3D12Fence> m_fence = default;

    private FixedArray3<ulong> m_fence_values = default;
    private int m_cur_frame;
    private readonly uint rtv_descriptor_size;

    private uint2 m_cur_size;
    private uint2 m_new_size;

    private readonly EventWaitHandle m_present_event;
    private readonly EventWaitHandle m_ready_event;
    private readonly Channel<int> m_present_queue = Channel.CreateUnbounded<int>();
    private readonly Lock m_present_lock = new();
    private bool m_disposed;

    #endregion

    #region Properties

    public D3D9 D3d9 => Graphics.D3d9;
    public GraphicsModule Graphics { get; }
    public D3DImage Image { get; }
    public Format Format { get; } = Format.FormatB8G8R8A8Unorm;

    public ref readonly ComPtr<IDirect3D9Ex> Dx9 => ref m_dx_9_ex;
    public ref readonly ComPtr<IDirect3DDevice9Ex> Device9 => ref m_device_9_ex;

    #endregion

    #region Ctor

    public D3DImageSwapChain(IntPtr WindowHandle, D3DImage Image, uint2 size) : this(App.Instance.Graphics, WindowHandle, Image, size) { }
    public unsafe D3DImageSwapChain(GraphicsModule Graphics, IntPtr WindowHandle, D3DImage Image, uint2 size)
    {
        this.Graphics = Graphics;
        this.Image = Image;

        m_new_size = m_cur_size = size;

        #region create event

        m_present_event = new(false, EventResetMode.AutoReset);
        m_ready_event = new(false, EventResetMode.AutoReset);

        #endregion

        #region create fence

        Graphics.Device.CreateFence(0u, FenceFlags.None, out m_fence).TryThrowHResult();
        if (Graphics.DebugEnabled) m_fence.SetName(in "SwapChain Fence".AsSpan()[0]).TryThrowHResult();

        #endregion

        #region create rtv heap

        DescriptorHeapDesc rtv_heap_desc = new()
        {
            Type = DescriptorHeapType.Rtv,
            NumDescriptors = FrameCount,
        };
        Graphics.Device.CreateDescriptorHeap(&rtv_heap_desc, out m_rtv_heap).TryThrowHResult();
        rtv_descriptor_size = Graphics.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.Rtv);

        #endregion

        #region create dx9

        {
            D3d9.Direct3DCreate9Ex(D3D9.SdkVersion, ref m_dx_9_ex).TryThrowHResult();
            uint flags = D3D9.CreateHardwareVertexprocessing | D3D9.CreateMultithreaded | D3D9.CreateFpuPreserve;
            var present = new PresentParameters(1, 1)
            {
                Windowed = true,
                SwapEffect = Swapeffect.Discard,
                HDeviceWindow = WindowHandle,
                PresentationInterval = D3D9.PresentIntervalImmediate,
            };
            m_dx_9_ex.Handle->CreateDeviceEx(D3D9.AdapterDefault, Devtype.Hal, WindowHandle, flags, &present, null, ref m_device_9_ex)
                .TryThrowHResult();
        }

        #endregion

        CreateRts();

        _ = PresentThread(new(this), m_present_queue);
    }

    #endregion

    #region Drop

    [Drop]
    private void Drop()
    {
        m_disposed = true;
        m_present_queue.Writer.TryWrite(-1);
    }

    #endregion

    #region CreateRts

    private unsafe void CreateRts()
    {
        var size = m_cur_size;

        var handle = m_rtv_heap.GetCPUDescriptorHandleForHeapStart();

        Texture2DDesc desc11 = new()
        {
            Width = size.x,
            Height = size.y,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format,
            SampleDesc = new(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.RenderTarget,
            CPUAccessFlags = (uint)CpuAccessFlag.None,
            MiscFlags = (uint)ResourceMiscFlag.Shared,
        };

        for (var i = 0; i < FrameCount; i++)
        {
            ref var buffer = ref m_12_buffers[i];
            ref var buffer11 = ref m_11_buffers[i];
            ref var buffer9 = ref m_9_buffers[i];
            ref var surface9 = ref m_9_surfaces[i];

            Graphics.Device11.CreateTexture2D(&desc11, null, ref buffer11).TryThrowHResult();
            var r = buffer11.QueryInterface(out ComPtr<IDXGIResource> resource_);
            using var resource = resource_;
            r.TryThrowHResult();
            void* shared_handle;
            resource.GetSharedHandle(&shared_handle).TryThrowHResult();
            m_device_9_ex.CreateTexture(size.x, size.y, 1, D3D9.UsageRendertarget, Format9.A8R8G8B8, Pool.Default,
                ref buffer9, &shared_handle).TryThrowHResult();
            Graphics.Device.Handle->OpenSharedHandle(shared_handle, out buffer).TryThrowHResult();
            buffer9.Handle->GetSurfaceLevel(0, ref surface9).TryThrowHResult();

            if (Graphics.DebugEnabled) buffer.SetName(in $"SwapChain Frame {i}".AsSpan()[0]).TryThrowHResult();

            RenderTargetViewDesc desc = new()
            {
                Format = Format,
                ViewDimension = RtvDimension.Texture2D,
                Texture2D = new()
                {
                    MipSlice = 0,
                    PlaneSlice = 0,
                }
            };
            Graphics.Device.CreateRenderTargetView((ID3D12Resource*)buffer.Handle, &desc, handle);
            handle = new(handle.Ptr + rtv_descriptor_size);
        }
    }

    #endregion

    #region PresentThread

    private static async Task PresentThread(WeakReference<D3DImageSwapChain> swapchain, Channel<int> queue)
    {
        for (;;)
        {
            var frame = await queue.Reader.ReadAsync();
            if (frame is < 0 or >= FrameCount) return;
            if (!swapchain.TryGetTarget(out var self)) return;
            if (self.m_disposed) return;
            var fence_value = self.m_fence_values[frame];
            self.Graphics.Wait(fence_value, self.m_present_event);
            self.Image.Lock();
            unsafe
            {
                self.Image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, (IntPtr)self.m_9_surfaces[frame].Handle);
                Silk.NET.Direct3D9.SurfaceDesc desc = default;
                self.m_9_surfaces[frame].Handle->GetDesc(ref desc).TryThrowHResult();
                self.Image.AddDirtyRect(new(0, 0, (int)desc.Width, (int)desc.Height));
            }
            self.Image.Unlock();
            self.m_fence.Signal(fence_value).TryThrowHResult();
        }
    }

    #endregion

    #region CurrentRtv

    public CpuDescriptorHandle CurrentRtv
    {
        get
        {
            var handle = m_rtv_heap.GetCPUDescriptorHandleForHeapStart();
            return new(handle.Ptr + (nuint)m_cur_frame * rtv_descriptor_size);
        }
    }

    #endregion

    #region Ctrl

    public void OnResize(uint2 size)
    {
        Interlocked.Exchange(
            ref Unsafe.As<uint2, ulong>(ref m_new_size),
            Unsafe.BitCast<uint2, ulong>(new(size.x, size.y))
        );
    }

    public void Present()
    {
        using var _ = m_present_lock.EnterScope();
        PresentNoWait_InLock();
        WaitFrameReady_InLock();
    }

    public void PresentNoWait()
    {
        using var _ = m_present_lock.EnterScope();
        PresentNoWait_InLock();
    }

    public void WaitFrameReady()
    {
        using var _ = m_present_lock.EnterScope();
        WaitFrameReady_InLock();
    }

    private void PresentNoWait_InLock()
    {
        var signal = Graphics.Signal();
        m_fence_values[m_cur_frame] = signal;
        m_present_queue.Writer.TryWrite(m_cur_frame);
    }

    private void WaitFrameReady_InLock()
    {
        var cur_size = m_cur_size;
        var new_size = Unsafe.BitCast<ulong, uint2>(Interlocked.Read(ref Unsafe.As<uint2, ulong>(ref m_new_size)));
        if (!cur_size.Equals(new_size))
        {
            DoResize_InLock(new_size);
        }
        m_cur_frame++;
        if (m_cur_frame >= FrameCount) m_cur_frame = 0;
        var fence_value = m_fence_values[m_cur_frame];
        m_fence.Wait(fence_value, m_ready_event);
    }

    private void WaitAll_InLock()
    {
        ulong max = 0;
        for (var i = 0; i < FrameCount; i++)
        {
            var v = m_fence_values[i];
            if (v > max) max = v;
        }
        m_fence.Wait(max, m_ready_event);
    }

    private void DoResize_InLock(uint2 size)
    {
        WaitAll_InLock();
        for (var i = 0; i < FrameCount; i++)
        {
            m_12_buffers[i].Dispose();
            m_9_surfaces[i].Dispose();
            m_9_buffers[i].Dispose();
            m_11_buffers[i].Dispose();
        }
        CreateRts();
        m_cur_size = size;
    }

    #endregion
}
