using Microsoft.UI.Xaml;
namespace FluentTube;
public partial class App : Application
{
    private Window? window;
    public App()
    {
        UnhandledException += (_, e) => LogFailure(new Exception(e.Message, e.Exception));
        InitializeComponent();
    }
    private static void LogFailure(Exception error)
    {
        System.IO.Directory.CreateDirectory(LibraryStore.Folder);
        System.IO.File.WriteAllText(System.IO.Path.Combine(LibraryStore.Folder, "startup-error.txt"), error.ToString());
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            window = new MainWindow();
            window.Activate();
        }
        catch (Exception error) { LogFailure(error); throw; }
    }
}
