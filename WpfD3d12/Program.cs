namespace WpfD3d12;

public static class Program
{
    [STAThread]
    private static void Main()
    {
        var application = new App();
        application.InitializeComponent();
        application.Run();
    }
}
