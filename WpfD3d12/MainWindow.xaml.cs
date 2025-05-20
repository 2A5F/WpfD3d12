using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using Coplt.Mathematics;
using WpfD3d12.Modules.Graphics;

namespace WpfD3d12;

public partial class MainWindow
{
    public GraphicsModule Graphics { get; }
    public D3DImageSwapChain SwapChain { get; }
    public IntPtr WindowHandle { get; }

    public MainWindow()
    {
        Graphics = App.Instance.Graphics;
        InitializeComponent();
        WindowHandle = new WindowInteropHelper(this).EnsureHandle();

        SwapChain = new D3DImageSwapChain(WindowHandle, D3DImage, new uint2((uint)Output.Width, (uint)Output.Height));
        new Thread(MainLoop).Start();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SizeToContent = SizeToContent.Manual;
        Output.Width = double.NaN;
        Output.Height = double.NaN;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var size = Output.RenderSize;
        if (size.Width == 0 || size.Height == 0) return;
        SwapChain.OnResize(new((uint)size.Width, (uint)size.Height));
    }

    private unsafe void MainLoop()
    {
        while (App.Instance.Running)
        {
            var color = new float4(0.83f, 0.8f, 0.97f, 1f);
            Graphics.CommandList.Handle->ClearRenderTargetView(
                SwapChain.CurrentRtv,
                ref Unsafe.As<float4, float>(ref color),
                0, null
            );

            Graphics.Submit();
            SwapChain.Present();
            Graphics.ReadyNextFrame();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        App.Instance.Running = false;
    }
}
