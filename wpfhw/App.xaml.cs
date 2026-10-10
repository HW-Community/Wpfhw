using System.Diagnostics;
using System.Windows;

namespace wpfhw
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppStorage.Initialize();
            base.OnStartup(e);

            AppThemeMode mode = AppThemeMode.System;
            try
            {
                mode = AppSettings.Load().ThemeMode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Load theme settings failed: {ex.Message}");
                mode = AppThemeMode.Light;
            }

            try
            {
                ThemeManager.ApplyTheme(mode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Apply theme failed: {ex.Message}");
                try { ThemeManager.ApplyTheme(AppThemeMode.Light); }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"Fallback theme failed: {fallbackEx.Message}");
                }
            }

            var splash = new SplashScreen();
            splash.Show();
        }
    }
}