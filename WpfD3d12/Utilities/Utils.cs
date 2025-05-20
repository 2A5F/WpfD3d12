using System.Diagnostics;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace WpfD3d12.Utilities;

public static partial class Utils
{
    public static HResult AsHResult(this int result) => result;

    [StackTraceHidden]
    public static void TryThrow(this HResult result)
    {
        if (!result.IsSuccess) result.Throw();
    }

    [StackTraceHidden]
    public static void TryThrowHResult(this int result) => result.AsHResult().TryThrow();
    
    public static unsafe void Wait(this ComPtr<ID3D12Fence> fence, ulong value, EventWaitHandle handle)
    {
        if (fence.GetCompletedValue() >= value) return;
        fence.SetEventOnCompletion(value, (void*)handle.SafeWaitHandle.DangerousGetHandle()).TryThrowHResult();
        handle.WaitOne();
    }
}
