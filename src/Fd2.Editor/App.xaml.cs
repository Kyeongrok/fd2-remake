using System.Windows;
using Fd2.Data;

namespace Fd2.Editor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new MainWindow(GameFolder.Find(e.Args)).Show();
    }
}
