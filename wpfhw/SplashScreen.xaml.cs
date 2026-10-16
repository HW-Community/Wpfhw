using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace wpfhw;

public partial class SplashScreen : Window
{
    public SplashScreen()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RunStoryboard("SplashEnter");
        await Task.Delay(900);

        var mainWindow = new MainWindow();
        mainWindow.Opacity = 0;
        mainWindow.Show();

        await RunStoryboard("SplashExit");
        Close();
    }

    private Task RunStoryboard(string key)
    {
        var board = (Storyboard)FindResource(key);
        var tcs = new TaskCompletionSource();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            board.Completed -= handler;
            tcs.TrySetResult();
        };
        board.Completed += handler;
        board.Begin(this, true);
        return Task.WhenAny(tcs.Task, Task.Delay(800));
    }
}
