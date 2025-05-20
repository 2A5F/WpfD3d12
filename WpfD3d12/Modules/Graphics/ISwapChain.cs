using Coplt.Mathematics;
using Silk.NET.Direct3D12;

namespace WpfD3d12.Modules.Graphics;

public interface ISwapChain : IDisposable
{
    public CpuDescriptorHandle CurrentRtv { get; }
    
    public void OnResize(uint2 size);
    public void Present();
    public void PresentNoWait();
    public void WaitFrameReady();
}
