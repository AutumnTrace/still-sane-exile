using Velopack;

namespace StillSaneExile.Desktop;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Must run first: handles install, update and uninstall hooks (and exits for them), and applies
        // an update that was downloaded but not yet installed.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
