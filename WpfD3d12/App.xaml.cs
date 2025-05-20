using WpfD3d12.Modules.Graphics;

namespace WpfD3d12;

public partial class App
{
    public static App Instance { get; private set; } = null!;

    public GraphicsModule Graphics { get; }
    public MainWindow Window { get; private set; } = null!;
    public bool Debug { get; } = true;
    public bool Running { get; internal set; } = true;

    internal App()
    {
        Instance = this;
        InitializeComponent();
        Graphics = new GraphicsModule();
    }
}
