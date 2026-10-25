using System.Windows;

namespace wpfhw
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppStorage.Initialize();
            base.OnStartup(e);

            // 区划码表只在启动时加载一次；失败时保留空表，联网查询仍可兜底展示。
            try
            {
                IdCardHelper.Initialize();
            }
            catch (Exception ex)
            {
                AppStorage.Log($"Load area codes failed: {ex.Message}");
            }

            AppThemeMode mode = AppThemeMode.System;
            try
            {
                mode = AppSettings.Load().ThemeMode;
            }
            catch (Exception ex)
            {
                AppStorage.Log($"Load theme settings failed: {ex.Message}");
                mode = AppThemeMode.Light;
            }

            try
            {
                ThemeManager.ApplyTheme(mode);
            }
            catch (Exception ex)
            {
                AppStorage.Log($"Apply theme failed: {ex.Message}");
                try { ThemeManager.ApplyTheme(AppThemeMode.Light); }
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