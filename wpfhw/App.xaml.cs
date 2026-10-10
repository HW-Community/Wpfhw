using System.Windows;

namespace wpfhw
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppStorage.Initialize();
            base.OnStartup(e);
            ThemeManager.ApplyTheme(AppSettings.Load().ThemeMode);

            var splash = new SplashScreen();
            splash.Show();
        }
    }
}