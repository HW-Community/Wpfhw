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
                AppStorage.Log($"Load theme settings failed: {ex.Message}");
            }

            try
            {
                ThemeManager.ApplyTheme(mode);
            }
            catch (Exception ex)
            {
                AppStorage.Log($"Apply theme failed: {ex.Message}");
                try { ThemeManager.ApplyTheme(AppThemeMode.System); }
                catch (Exception fallbackEx)
                {
                    AppStorage.Log($"Fallback theme failed: {fallbackEx.Message}");
                }
            }

            var splash = new SplashScreen();
            splash.Show();
        }
    }
}