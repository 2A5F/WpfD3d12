using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Coplt.Dropping;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D9;
using Silk.NET.Direct3D11;
using Silk.NET.Direct3D12;
using Silk.NET.Direct3D11.Extensions.D3D11On12;
using Silk.NET.DXGI;
using WpfD3d12.Utilities;
using MessageCategory = Silk.NET.Direct3D12.MessageCategory;
using MessageID = Silk.NET.Direct3D12.MessageID;
using MessageSeverity = Silk.NET.Direct3D12.MessageSeverity;

namespace WpfD3d12.Modules.Graphics;

[Dropping(Unmanaged = true)]
public unsafe partial class GraphicsModule
{
    #region Consts

    public const int FrameCount = 3;

    #endregion

    #region Silk

#pragma warning disable CS0618
    public DXGI Dxgi { get; } = DXGI.GetApi();
    public D3D12 D3d12 { get; } = D3D12.GetApi();
    public D3D11 D3d11 { get; } = D3D11.GetApi();
    public D3D9 D3d9 { get; } = D3D9.GetApi();
    public D3D11On12 D3d11On12 { get; }
#pragma warning restore CS0618

    #endregion

    #region Fields Properties

    [Drop]
    private ComPtr<ID3D12Debug> m_debug_controller;
    [Drop]
    private ComPtr<IDXGIFactory6> m_factory;
    [Drop]
    private ComPtr<IDXGIAdapter1> m_adapter;
    [Drop]
    private ComPtr<ID3D12Device10> m_device;
    [Drop]
    private ComPtr<ID3D12InfoQueue1> m_info_queue;
    [Drop]
    private ComPtr<ID3D12CommandQueue> m_queue;
    [Drop]
    private ComPtr<ID3D12Fence> m_fence;
    [Drop]
    private ComPtr<ID3D11Device> m_device_11;
    [Drop]
    private ComPtr<ID3D11DeviceContext> m_context_11;
    [Drop]
    private ComPtr<ID3D11On12Device> m_11_on_12;
    [Drop]
    private FixedArray3<ComPtr<ID3D12CommandAllocator>> m_cmd_allocator;
    [Drop]
    private ComPtr<ID3D12GraphicsCommandList7> m_cmd_list;

    private EventWaitHandle m_event;
    private FixedArray3<ulong> m_fence_values = default;
    private ulong fence_value;
    private int m_cur_frame;
    private uint m_callback_cookie;

    public ref readonly ComPtr<IDXGIFactory6> Factory => ref m_factory;
    public ref readonly ComPtr<IDXGIAdapter1> Adapter => ref m_adapter;
    public ref readonly ComPtr<ID3D12Device10> Device => ref m_device;
    public ref readonly ComPtr<ID3D12CommandQueue> Queue => ref m_queue;
    public ref readonly ComPtr<ID3D12Fence> Fence => ref m_fence;
    public ref readonly ComPtr<ID3D11Device> Device11 => ref m_device_11;
    public ref readonly ComPtr<ID3D11DeviceContext> Context11 => ref m_context_11;
    public ref readonly ComPtr<ID3D11On12Device> Device11On12 => ref m_11_on_12;

    public ReadOnlySpan<ComPtr<ID3D12CommandAllocator>> CommandAllocator => m_cmd_allocator;
    public ref readonly ComPtr<ID3D12GraphicsCommandList7> CommandList => ref m_cmd_list;

    public bool DebugEnabled { get; }

    public Lock Lock { get; } = new();

    #endregion

    #region Ctor

    internal GraphicsModule()
    {
        D3d11On12 = new D3D11On12(D3d11.Context);

        #region create event

        m_event = new(false, EventResetMode.AutoReset);

        #endregion

        #region create dx12

        var dxgi_flags = 0u;
        var debug = App.Instance.Debug;
        if (debug)
        {
            if (((HResult)D3d12.GetDebugInterface(out m_debug_controller)).IsSuccess)
            {
                m_debug_controller.EnableDebugLayer();
                dxgi_flags |= DXGI.CreateFactoryDebug;
                DebugEnabled = true;
            }

            if (((HResult)m_debug_controller.QueryInterface(out ComPtr<ID3D12Debug5> debug5)).IsSuccess)
            {
                debug5.EnableDebugLayer();
                debug5.Dispose();
            }
        }

        Dxgi.CreateDXGIFactory2(dxgi_flags, out m_factory).TryThrowHResult();
        m_factory.EnumAdapterByGpuPreference(0, GpuPreference.HighPerformance, out m_adapter).TryThrowHResult();

        D3d12.CreateDevice(m_adapter, D3DFeatureLevel.Level121, out m_device).TryThrowHResult();
        if (DebugEnabled)
        {
            m_device.SetName(in "Main Device".AsSpan()[0]).TryThrowHResult();
            if (m_device.QueryInterface(out m_info_queue).AsHResult().IsSuccess)
            {
                uint cookie = 0;
                if (m_info_queue.Handle->RegisterMessageCallback(
                        (delegate* unmanaged[Cdecl]<MessageCategory, MessageSeverity, MessageID, byte*, void*, void>)&DebugCallback,
                        MessageCallbackFlags.FlagNone,
                        null,
                        &cookie
                    ).AsHResult().IsSuccess)
                {
                    m_callback_cookie = cookie;
                }
                else
                {
                    // todo logger warn
                }
            }
        }

        #region create queue

        CommandQueueDesc queue_desc = new()
        {
            Type = CommandListType.Direct,
            Priority = 0,
            Flags = CommandQueueFlags.None,
            NodeMask = 0
        };
        m_device.CreateCommandQueue(&queue_desc, out m_queue).TryThrowHResult();
        if (DebugEnabled) m_queue.SetName(in "Main Queue".AsSpan()[0]).TryThrowHResult();

        #endregion

        #region create fence

        m_device.CreateFence(0u, FenceFlags.None, out m_fence).TryThrowHResult();
        if (DebugEnabled) m_fence.SetName(in "Main Fence".AsSpan()[0]).TryThrowHResult();

        #endregion

        #region create command

        for (var i = 0; i < FrameCount; i++)
        {
            ref var ca = ref m_cmd_allocator[i];
            m_device.Handle->CreateCommandAllocator(CommandListType.Direct, out ca).TryThrowHResult();
        }

        m_device.Handle->CreateCommandList(0, CommandListType.Direct, m_cmd_allocator[0], default(ComPtr<ID3D12PipelineState>), out m_cmd_list)
            .TryThrowHResult();

        #endregion

        #endregion

        #region create dx11

        {
            var flags = 0u;
            flags |= 0x20; // D3D11_CREATE_DEVICE_BGRA_SUPPORT
            if (DebugEnabled) flags |= 0x2; // D3D11_CREATE_DEVICE_DEBUG 
            var feature = D3DFeatureLevel.Level121;
            var queue = (IUnknown*)m_queue.Handle;
            ID3D11Device* device;
            ID3D11DeviceContext* context;
            D3d11On12.On12CreateDevice((IUnknown*)m_device.Handle, flags, &feature, 1u, &queue, 1u, 0u, &device, &context, null).TryThrowHResult();
            m_device_11.Handle = device;
            m_context_11.Handle = context;
            m_device_11.QueryInterface(out m_11_on_12).TryThrowHResult();
        }

        #endregion
    }

    #endregion

    #region DebugCallback

    [Drop(Order = -1)]
    private void UnRegDebugCallback()
    {
        if (m_info_queue.Handle == null) return;
        m_info_queue.Handle->UnregisterMessageCallback(m_callback_cookie);
        m_callback_cookie = 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DebugCallback(MessageCategory Category, MessageSeverity Severity, MessageID id, byte* pDescription, void* pContext)
    {
        var msg = new string((sbyte*)pDescription);
        // todo logger
        Console.WriteLine(msg);
    }

    #endregion

    #region Fence

    public ulong AllocSignal() => Interlocked.Increment(ref fence_value);

    public ulong Signal()
    {
        var value = AllocSignal();
        m_queue.Signal(m_fence.Handle, value).TryThrowHResult();
        return value;
    }

    public void Wait(ulong value)
    {
        m_queue.Wait(m_fence.Handle, value).TryThrowHResult();
    }

    public void Wait(ulong value, EventWaitHandle handle)
    {
        m_fence.Wait(value, handle);
    }

    #endregion

    #region Submit

    public void Submit()
    {
        m_cmd_list.Handle->Close();
        var list = (ID3D12CommandList*)m_cmd_list.Handle;
        m_queue.Handle->ExecuteCommandLists(1, &list);
        m_fence_values[m_cur_frame] = Signal();
    }

    public void ReadyNextFrame()
    {
        m_cur_frame++;
        if (m_cur_frame >= FrameCount) m_cur_frame = 0;
        var value = m_fence_values[m_cur_frame];
        Wait(value, m_event);
        m_cmd_allocator[m_cur_frame].Handle->Reset().TryThrowHResult();
        m_cmd_list.Handle->Reset(m_cmd_allocator[m_cur_frame].Handle, null).TryThrowHResult();
    }

    #endregion
}
