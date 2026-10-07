using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Media.Animation;

namespace wpfhw;

/// <summary>中译缓存条目：Modrinth 项目 -> 中文标题/描述（来自 MC百科）</summary>
public class ModTranslation
{
    public string ChineseTitle { get; set; } = string.Empty;
    public string ChineseDesc { get; set; } = string.Empty;
    public string? MatchEnglishName { get; set; }
}

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions SharedJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    private readonly HttpClient _httpClient;
    private readonly HttpClient _mcHttpClient;
    private readonly AppSettings _settings;
    private ModSearchHit? _selectedMod;
    private List<ModVersion> _currentVersions = new();
    private string _currentGameVer = "";
    private string _currentLoader = "";
    private string _currentProjectType = "mod";
    private string _downloadProjectType = "mod";
    private string _pendingDownloadProjectType = "mod";
    private string _downloadPath = "";
    private ModFile? _pendingDownloadFile;
    private VersionDisplayItem? _pendingVersion;
    private const int MaxDuplicateSuffix = 1000;
    private const int MaxFileNameLength = 200;
    private CancellationTokenSource? _downloadCts;
    private CancellationTokenSource? _searchCts;
    private int _currentOffset = 0;
    private const int PageSize = 30;
    private string _lastKeyword = "";
    private int _totalHits = 0;

    // 下载并发管理
    private SemaphoreSlim _downloadSemaphore = null!;
    private int _activeDownloadCount;
    private readonly object _panelLock = new();
    private int _panelOwnerId = -1;       // 当前占用下载面板的下载任务 id，-1 表示空闲
    private int _downloadIdCounter;

    // 设置面板返回时要回到的面板
    private enum MainPanel { Search, VersionDetail, DownloadConfirm, Downloading, Game }
    private MainPanel _lastPanel = MainPanel.Search;
    private bool _suppressSettingsSave;

    private readonly MinecraftInstaller _mcInstaller;
    private List<McVersionInfo> _allMcVersions = new();
    private McVersionInfo? _selectedMcVersion;
    private McLoaderKind _selectedLoader = McLoaderKind.Vanilla;
    private McInstallMode _selectedInstallMode = McInstallMode.VersionsFolder;
    private string _minecraftPath = "";
    private bool _gameFilterRelease = true;
    private bool _gameFilterSnapshot;
    private bool _gameFilterLegacy;
    private enum GameListState { Idle, Loading, Loaded }
    private GameListState _gameListState = GameListState.Idle;
    private CancellationTokenSource? _loaderListCts;
    private CancellationTokenSource? _mcInstallCts;

    /// <summary>中译缓存：key = Modrinth ProjectId（小写）</summary>
    private readonly Dictionary<string, ModTranslation> _translations = new();

    /// <summary>待匹配的中译：key = 英文名小写 -> 中译。搜 Modrinth 前先填这个，命中 Modrinth 结果时迁移到 _translations。</summary>
    private readonly Dictionary<string, ModTranslation> _pendingByEnglish = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> LoaderTypes = new() { "mod", "modpack" };
    private const string NavGame = "game";
    private static readonly HashSet<string> ProjectTypes = new() { "mod", "resourcepack", "shader", "datapack", "modpack" };
    private static readonly HashSet<string> NavTypes = new() { "mod", "resourcepack", "shader", "datapack", "modpack", NavGame };
    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public MainWindow()
    {
        InitializeComponent();

        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(BmclApiClient.UserAgent);

        _mcHttpClient = new HttpClient();
        _mcHttpClient.Timeout = TimeSpan.FromMinutes(5);
        _mcHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(BmclApiClient.UserAgent);

        _mcInstaller = new MinecraftInstaller(_mcHttpClient);

        _settings = AppSettings.Load();
        _currentProjectType = NavTypes.Contains(_settings.LastProjectType)
            ? _settings.LastProjectType
            : "mod";
        _downloadPath = ResolveExistingDirectory(_settings.DownloadPath)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        _minecraftPath = ResolveWritableDirectory(_settings.MinecraftPath)
            ?? ResolveWritableDirectory(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"))
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        if (_settings.WindowWidth >= 640) Width = _settings.WindowWidth;
        if (_settings.WindowHeight >= 480) Height = _settings.WindowHeight;

        // 下载并发信号量（不设上限，运行时通过 Release/Wait 动态调整）
        _maxDownloadThreads = _settings.MaxDownloadThreads;
        _downloadSemaphore = new SemaphoreSlim(_maxDownloadThreads);
        txtThreadCount.Text = _maxDownloadThreads.ToString();

        UpdateNavStyle(GetNavButton(_currentProjectType));
        UpdateLoaderVisibility();
        UpdateSearchPlaceholder();
        ApplyTheme(_settings.ThemeMode);
        ApplyDownloadOptionsToUi();
        txtMinecraftPath.Text = _minecraftPath;
        HighlightLoaderButton();
        HighlightInstallModeButton();
        HighlightGameFilters();
        SaveSettings();
        if (_currentProjectType == NavGame)
            ShowGamePanel();

        _downloadCts = new CancellationTokenSource();

        Closed += (_, _) =>
        {
            SaveSettings();
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _loaderListCts?.Cancel();
            _loaderListCts?.Dispose();
            _mcInstallCts?.Cancel();
            _mcInstallCts?.Dispose();
            _httpClient.Dispose();
            _mcHttpClient.Dispose();
        };
    }

    private Button GetNavButton(string projectType) => projectType switch
    {
        "resourcepack" => navResource,
        "shader" => navShader,
        "datapack" => navData,
        "modpack" => navPack,
        NavGame => navGame,
        _ => navMod
    };

    private void SaveSettings()
    {
        _settings.DownloadPath = _downloadPath;
        _settings.MinecraftPath = _minecraftPath;
        _settings.LastProjectType = _currentProjectType;
        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;
        _settings.ThemeMode = _currentThemeMode;
        _settings.MaxDownloadThreads = _maxDownloadThreads;
        _settings.Save();
    }

    private void ApplyDownloadOptionsToUi()
    {
        _suppressSettingsSave = true;
        try
        {
            txtDefaultDownloadPath.Text = _downloadPath;
            chkCreateTypeSubfolder.IsChecked = _settings.CreateTypeSubfolder;
            chkOverwriteExisting.IsChecked = _settings.OverwriteExistingFiles;
            chkOpenFolderAfterDownload.IsChecked = _settings.OpenFolderAfterDownload;
            chkSkipDownloadConfirm.IsChecked = _settings.SkipDownloadConfirm;
        }
        finally
        {
            _suppressSettingsSave = false;
        }
    }

    private static string? ResolveExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return Directory.Exists(path) ? path : null;
    }

    private static string? ResolveWritableDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            return path;

        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        return Directory.Exists(fallback) ? fallback : GetDesktopDirectory();
    }

    private static string GetDesktopDirectory()
        => Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    private void SetDownloadPath(string path)
    {
        _downloadPath = path;
        txtDefaultDownloadPath.Text = path;
        SaveSettings();
    }

    private AppThemeMode _currentThemeMode = AppThemeMode.System;
    private int _maxDownloadThreads = 3;

    /// <summary>应用主题并高亮对应按钮。</summary>
    private void ApplyTheme(AppThemeMode mode)
    {
        _currentThemeMode = mode;
        ThemeManager.ApplyTheme(mode);
        UpdateThemeButtons();
    }

    private void UpdateThemeButtons()
    {
        foreach (var btn in new[] { btnThemeLight, btnThemeDark, btnThemeSystem })
        {
            btn.SetResourceReference(Control.BackgroundProperty, "ThemeCardBackground");
            btn.SetResourceReference(Control.ForegroundProperty, "ThemePrimaryText");
            btn.FontWeight = FontWeights.Normal;
        }

        Button active = _currentThemeMode switch
        {
            AppThemeMode.Light => btnThemeLight,
            AppThemeMode.Dark => btnThemeDark,
            _ => btnThemeSystem
        };
        active.SetResourceReference(Control.BackgroundProperty, "ThemeAccent");
        active.Foreground = Brushes.White;
        active.FontWeight = FontWeights.SemiBold;
    }

    /// <summary>动态调整下载并发数。</summary>
    private void UpdateMaxDownloadThreads(int newValue)
    {
        newValue = Math.Clamp(newValue, 1, 16);
        if (newValue == _maxDownloadThreads) return;

        int old = _maxDownloadThreads;
        _maxDownloadThreads = newValue;
        txtThreadCount.Text = newValue.ToString();

        int diff = newValue - old;
        if (diff > 0)
        {
            // 增加并发：释放对应数量的槽位
            try { _downloadSemaphore.Release(diff); }
            catch (SemaphoreFullException) { }
        }
        else if (diff < 0)
        {
            // 减少并发：后台逐步回收多余槽位（不阻塞 UI，等进行中的下载自然结束）
            int toRemove = -diff;
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < toRemove; i++)
                    await _downloadSemaphore.WaitAsync();
            });
        }

        SaveSettings();
    }

    private string ResolveIconUrl(string url)
    {
        if (!IconCache.IsRemoteUrl(url)) return url;

        string? local = IconCache.TryGetLocalPath(url);
        if (local != null)
            return new Uri(local).AbsoluteUri;

        _ = CacheIconInBackground(url);
        return url;
    }

    private async Task CacheIconInBackground(string url)
    {
        try
        {
            await IconCache.GetLocalPathAsync(url, _httpClient, CancellationToken.None);
        }
        catch
        {
        }
    }

    private ModSearchHit WithCachedIcon(ModSearchHit hit)
    {
        hit.IconUrl = ResolveIconUrl(hit.IconUrl);
        return hit;
    }

    private bool HasLoaders() => LoaderTypes.Contains(_currentProjectType);

    private void UpdateLoaderVisibility()
    {
        if (HasLoaders())
        {
            cbbLoader.Visibility = Visibility.Visible;
        }
        else
        {
            cbbLoader.Visibility = Visibility.Collapsed;
            cbbLoader.SelectedIndex = 0;
        }
    }

    private void UpdateSearchPlaceholder()
    {
        string typeName = _currentProjectType switch
        {
            "mod" => "模组",
            "resourcepack" => "资源包",
            "shader" => "光影",
            "datapack" => "数据包",
            "modpack" => "整合包",
            _ => "模组"
        };
        txtSearchKey.Tag = $"搜索{typeName}(支持中文)...";
    }

    #region ========== 窗口控制 ==========

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    #endregion

    #region ========== 设置面板 ==========

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        // 记录当前面板，便于返回
        if (panelVersionDetail.Visibility == Visibility.Visible) _lastPanel = MainPanel.VersionDetail;
        else if (panelDownloadConfirm.Visibility == Visibility.Visible) _lastPanel = MainPanel.DownloadConfirm;
        else if (panelDownloading.Visibility == Visibility.Visible) _lastPanel = MainPanel.Downloading;
        else if (panelGame.Visibility == Visibility.Visible) _lastPanel = MainPanel.Game;
        else _lastPanel = MainPanel.Search;

        panelSearch.Visibility = Visibility.Collapsed;
        panelVersionDetail.Visibility = Visibility.Collapsed;
        panelDownloadConfirm.Visibility = Visibility.Collapsed;
        panelDownloading.Visibility = Visibility.Collapsed;
        panelGame.Visibility = Visibility.Collapsed;
        panelSettings.Visibility = Visibility.Visible;

        UpdateThemeButtons();
        ApplyDownloadOptionsToUi();
    }

    private void BtnBackFromSettings_Click(object sender, RoutedEventArgs e)
    {
        panelSettings.Visibility = Visibility.Collapsed;
        switch (_lastPanel)
        {
            case MainPanel.VersionDetail:
                panelVersionDetail.Visibility = Visibility.Visible;
                break;
            case MainPanel.DownloadConfirm:
                panelDownloadConfirm.Visibility = Visibility.Visible;
                break;
            case MainPanel.Downloading:
                panelDownloading.Visibility = Visibility.Visible;
                break;
            case MainPanel.Game:
                panelGame.Visibility = Visibility.Visible;
                break;
            default:
                panelSearch.Visibility = Visibility.Visible;
                break;
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        AppThemeMode mode = btn.Tag?.ToString() switch
        {
            "Light" => AppThemeMode.Light,
            "Dark" => AppThemeMode.Dark,
            _ => AppThemeMode.System
        };
        ApplyTheme(mode);
        SaveSettings();
    }

    private void BtnThreadMinus_Click(object sender, RoutedEventArgs e)
        => UpdateMaxDownloadThreads(_maxDownloadThreads - 1);

    private void BtnThreadPlus_Click(object sender, RoutedEventArgs e)
        => UpdateMaxDownloadThreads(_maxDownloadThreads + 1);

    private void BtnGithub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/HW-Community/Wpfhw",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void BtnBrowseDefaultDownloadPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择默认下载文件夹",
            FolderName = _downloadPath
        };

        if (dialog.ShowDialog() == true)
            SetDownloadPath(dialog.FolderName);
    }

    private void BtnResetDownloadPath_Click(object sender, RoutedEventArgs e)
        => SetDownloadPath(GetDesktopDirectory());

    private void DownloadOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsSave) return;
        SyncDownloadOptionsFromUi();
        SaveSettings();
    }

    private void SyncDownloadOptionsFromUi()
    {
        _settings.CreateTypeSubfolder = chkCreateTypeSubfolder.IsChecked == true;
        _settings.OverwriteExistingFiles = chkOverwriteExisting.IsChecked == true;
        _settings.OpenFolderAfterDownload = chkOpenFolderAfterDownload.IsChecked == true;
        _settings.SkipDownloadConfirm = chkSkipDownloadConfirm.IsChecked == true;
    }

    #endregion

    #region ========== 导航栏 ==========

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        _currentProjectType = btn.Tag?.ToString() ?? "mod";
        UpdateNavStyle(btn);

        if (_currentProjectType == NavGame)
        {
            ShowGamePanel();
            return;
        }

        HideGamePanel();
        UpdateLoaderVisibility();
        UpdateSearchPlaceholder();

        txtSearchKey.Text = "";
        cbbGameVersion.SelectedIndex = 0;
        cbbLoader.SelectedIndex = 0;

        txtStatusMsg.Text = "已切换，点击搜索";
        lstModResult.Items.Clear();
        _currentOffset = 0;
        _totalHits = 0;
    }

    private void UpdateNavStyle(Button active)
    {
        foreach (var child in navPanel.Children)
        {
            if (child is Button btn)
            {
                btn.SetResourceReference(Control.ForegroundProperty, "ThemeSecondaryText");
                btn.FontWeight = FontWeights.Normal;
                btn.Background = Brushes.Transparent;
            }
        }

        active.SetResourceReference(Control.ForegroundProperty, "ThemeAccent");
        active.FontWeight = FontWeights.SemiBold;
        active.SetResourceReference(Control.BackgroundProperty, "ThemeWindowBackground");
    }

    #endregion

    #region ========== 搜索面板（Modrinth + MC百科中译）==========

    private void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        _currentOffset = 0;
        _lastKeyword = txtSearchKey.Text.Trim();
        DoSearch();
    }

    private void TxtSearchKey_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnSearch_Click(sender, new RoutedEventArgs());
        }
    }

    private async void DoSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        _pendingByEnglish.Clear();
        lstModResult.Items.Clear();

        string keyword = _lastKeyword;
        bool isChinese = ContainsChinese(keyword);

        if (isChinese)
        {
            // 中文关键词：先 MC百科 找对应 mod 的英文名，建立英文名->中译映射；
            // 然后把这些英文名逐个丢给 Modrinth 搜索，合并结果。
            txtStatusMsg.Text = "正在通过MC百科匹配英文名...";
            statusDot.Visibility = Visibility.Visible;
            try
            {
                var mcHits = await SearchMCModEnNames(keyword, ct);
                if (mcHits.Count == 0)
                {
                    txtStatusMsg.Text = "MC百科未匹配到结果，将直接用关键词尝试 Modrinth 搜索";
                    // 最后兜底直接搜 Modrinth
                    await DoModrinthSearch(keyword, ct);
                    return;
                }

                // 填充 pendingByEnglish
                foreach (var h in mcHits)
                {
                    if (string.IsNullOrWhiteSpace(h.EnglishName)) continue;
                    var key = NormalizeEnglishName(h.EnglishName);
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    _pendingByEnglish[key] = new ModTranslation
                    {
                        ChineseTitle = h.Title,
                        ChineseDesc = h.Description,
                        MatchEnglishName = h.EnglishName
                    };
                }

                await SearchModrinthByEnglishNames(mcHits, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                txtStatusMsg.Text = $"MC百科匹配失败：{ex.Message}，尝试直接搜 Modrinth";
                try { await DoModrinthSearch(keyword, ct); } catch { }
            }
            finally
            {
                statusDot.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            await DoModrinthSearch(keyword, ct);
        }
    }

    /// <summary>中文关键词搜 MC百科，返回候选英文名列表。实际最多取前 8 个有英文名的结果去 Modrinth 匹配。</summary>
    private async Task<List<MCModSearchHit>> SearchMCModEnNames(string keyword, CancellationToken ct)
    {
        string queryEnc = Uri.EscapeDataString(keyword);
        string url = $"https://search.mcmod.cn/s?key={queryEnc}&filter=0&page=1";

        using var resp = await _httpClient.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        string html = await resp.Content.ReadAsStringAsync(ct);

        var all = ParseMCModSearchHtml(html);
        var withEn = all.Where(h => !string.IsNullOrWhiteSpace(h.EnglishName)).Take(8).ToList();
        return withEn;
    }

    /// <summary>根据 MC百科 拿到的多个英文名，逐个去 Modrinth 搜索，合并去重结果。</summary>
    private async Task SearchModrinthByEnglishNames(List<MCModSearchHit> mcHits, CancellationToken ct)
    {
        _translations.Clear();
        _totalHits = 0;
        var seenIds = new HashSet<string>();

        statusDot.Visibility = Visibility.Visible;
        int hitIndex = 0;

        txtStatusMsg.Text = $"匹配到 {mcHits.Count} 个中文候选，正在 Modrinth 对齐...";

        foreach (var mc in mcHits)
        {
            ct.ThrowIfCancellationRequested();
            hitIndex++;

            string query = mc.EnglishName ?? mc.Title;
            if (string.IsNullOrWhiteSpace(query)) continue;

            try
            {
                var facetGroups = new List<string>
                {
                    $"[\"project_type:{_currentProjectType}\"]"
                };
                if (!string.IsNullOrWhiteSpace(_currentGameVer))
                    facetGroups.Add($"[\"versions:{_currentGameVer}\"]");
                if (!string.IsNullOrWhiteSpace(_currentLoader) && HasLoaders())
                    facetGroups.Add($"[\"categories:{_currentLoader}\"]");
                string rawFacet = "[" + string.Join(",", facetGroups) + "]";
                string facetEnc = Uri.EscapeDataString(rawFacet);
                string queryEnc = Uri.EscapeDataString(query);

                string searchUrl = $"https://api.modrinth.com/v2/search?query={queryEnc}&facets={facetEnc}&limit=3&offset=0";
                string json = await _httpClient.GetStringAsync(searchUrl, ct);
                var sr = JsonSerializer.Deserialize<ModSearchResponse>(json, SharedJsonOptions);

                if (sr?.Hits != null && sr.Hits.Any())
                {
                    var hit = sr.Hits[0];
                    if (seenIds.Add(hit.ProjectId))
                    {
                        // 绑定中译
                        _translations[hit.ProjectId.ToLowerInvariant()] = new ModTranslation
                        {
                            ChineseTitle = mc.Title,
                            ChineseDesc = mc.Description,
                            MatchEnglishName = mc.EnglishName
                        };

                        lstModResult.Items.Add(WithCachedIcon(ApplyTranslation(hit)));
                        _totalHits++;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // 某个英文名没搜到或失败，跳过继续下一个
            }
        }

        if (_totalHits == 0)
        {
            txtStatusMsg.Text = $"MC百科匹配到候选，但 Modrinth 未对齐到对应项目。请尝试用英文关键词搜索。";
        }
        else
        {
            txtStatusMsg.Text = $"共找到 {_totalHits} 个中文结果（MC百科中译已应用，可直接下载 Modrinth 版本）";
        }

        statusDot.Visibility = Visibility.Collapsed;
    }

    /// <summary>标准 Modrinth 搜索（英文或兜底路径）。完成后尝试用 MC百科补中译。</summary>
    private async Task DoModrinthSearch(string keyword, CancellationToken ct)
    {
        string gameVer = GetComboBoxValue(cbbGameVersion, "全部版本");
        string loader = GetComboBoxValue(cbbLoader, "全部");

        _currentGameVer = gameVer;
        _currentLoader = loader;

        try
        {
            statusDot.Visibility = Visibility.Visible;
            txtStatusMsg.Text = "正在搜索 Modrinth...";
            lstModResult.Items.Clear();

            var facetGroups = new List<string>
            {
                $"[\"project_type:{_currentProjectType}\"]"
            };

            if (!string.IsNullOrWhiteSpace(gameVer))
                facetGroups.Add($"[\"versions:{gameVer}\"]");

            if (!string.IsNullOrWhiteSpace(loader) && HasLoaders())
                facetGroups.Add($"[\"categories:{loader}\"]");

            string rawFacet = "[" + string.Join(",", facetGroups) + "]";
            string facetEnc = Uri.EscapeDataString(rawFacet);
            string queryEnc = Uri.EscapeDataString(keyword);

            string sortParam = string.IsNullOrWhiteSpace(keyword) ? "&index=downloads" : "";
            string requestUrl = $"https://api.modrinth.com/v2/search?query={queryEnc}&facets={facetEnc}&limit={PageSize}&offset={_currentOffset}{sortParam}";

            string jsonText = await _httpClient.GetStringAsync(requestUrl, ct);

            var searchResult = JsonSerializer.Deserialize<ModSearchResponse>(jsonText, SharedJsonOptions);

            _totalHits = searchResult?.TotalHits ?? 0;

            if (searchResult?.Hits != null && searchResult.Hits.Any())
            {
                // 先用 pendingByEnglish（如果有）应用中译
                foreach (var m in searchResult.Hits)
                {
                    TryMatchPendingByEnglish(m);
                    lstModResult.Items.Add(WithCachedIcon(ApplyTranslation(m)));
                }
                txtStatusMsg.Text = $"找到 {_totalHits} 个结果，第 {_currentOffset / PageSize + 1} 页（中译仅在通过MC百科路径搜索时应用）";
            }
            else
            {
                txtStatusMsg.Text = "未查询到相关结果";
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException httpErr)
        {
            txtStatusMsg.Text = $"网络错误：{httpErr.StatusCode}";
        }
        catch (Exception ex)
        {
            txtStatusMsg.Text = $"搜索异常：{ex.Message}";
        }
        finally
        {
            statusDot.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>尝试用 pendingByEnglish 通过标题近似匹配填充中译缓存。</summary>
    private void TryMatchPendingByEnglish(ModSearchHit mod)
    {
        if (_pendingByEnglish.Count == 0) return;
        string normalized = NormalizeEnglishName(mod.Title);
        if (_pendingByEnglish.TryGetValue(normalized, out var tr))
        {
            _translations.TryAdd(mod.ProjectId.ToLowerInvariant(), tr);
            return;
        }
        // 再尝试 slug 匹配
        if (!string.IsNullOrWhiteSpace(mod.Slug)
            && _pendingByEnglish.TryGetValue(NormalizeEnglishName(mod.Slug), out var tr2))
        {
            _translations.TryAdd(mod.ProjectId.ToLowerInvariant(), tr2);
        }
    }

    /// <summary>对 ModSearchHit 应用中译，返回展示用对象（用匿名或原对象包装 Title/Description 字段）。
    /// 因为 WPF 绑定到 ModSearchHit.Title，我们这里不改类，而是返回一个新建的 ModSearchHit 并覆盖标题/描述。
    /// </summary>
    private ModSearchHit ApplyTranslation(ModSearchHit src)
    {
        if (_translations.TryGetValue(src.ProjectId.ToLowerInvariant(), out var tr)
            && !string.IsNullOrWhiteSpace(tr.ChineseTitle))
        {
            // 返回一个副本，用中文覆盖
            return new ModSearchHit
            {
                ProjectId = src.ProjectId,
                Slug = src.Slug,
                Title = tr.ChineseTitle,
                Description = string.IsNullOrWhiteSpace(tr.ChineseDesc) ? src.Description : tr.ChineseDesc,
                IconUrl = src.IconUrl,
                Downloads = src.Downloads,
                Followers = src.Followers,
                Author = src.Author,
                LatestVersion = src.LatestVersion,
                License = src.License,
                Categories = src.Categories,
                DisplayCategories = src.DisplayCategories,
                Versions = src.Versions,
                DateCreated = src.DateCreated,
                DateModified = src.DateModified,
                GameVersions = src.GameVersions,
                Loaders = src.Loaders,
                OriginalTitle = src.Title, // 保留原始英文名，以防以后需要
                OriginalDescription = src.Description
            };
        }
        return src;
    }

    /// <summary>MC百科搜索页 HTML 解析。只抓取 result-item 中 mcmod.cn/class/数字.html 的链接。</summary>
    private static List<MCModSearchHit> ParseMCModSearchHtml(string html)
    {
        var result = new List<MCModSearchHit>();

        var itemMatches = Regex.Matches(html,
            @"<div class=""result-item"">.*?<div class=""head"">.*?<a[^>]*href=""(https://www\.mcmod\.cn/class/\d+\.html)""[^>]*>(.*?)</a>.*?<div class=""body"">(.*?)</div>",
            RegexOptions.Singleline);

        foreach (Match m in itemMatches)
        {
            string url = m.Groups[1].Value;
            string rawTitle = m.Groups[2].Value;
            string desc = m.Groups[3].Value;

            string title = Regex.Replace(rawTitle, "<[^>]+>", "").Trim();
            desc = Regex.Replace(desc, "<[^>]+>", "").Trim();
            desc = Regex.Replace(desc, @"\s+", " ");
            if (desc.Length > 200) desc = desc[..200] + "...";

            var hit = new MCModSearchHit
            {
                Url = url,
                McModId = MCModSearchHit.ExtractIdFromUrl(url),
                RawTitle = title,
                Title = title,
                EnglishName = MCModSearchHit.ExtractEnglishName(title),
                Description = desc
            };
            result.Add(hit);
        }
        return result;
    }

    private static bool ContainsChinese(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        return Regex.IsMatch(s, @"[\u4e00-\u9fa5]");
    }

    private static string NormalizeEnglishName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        // 去掉特殊字符、空格，全部小写；mod名称对比主要看字母数字
        return Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "");
    }

    private static string GetComboBoxValue(ComboBox cbb, string defaultValue)
    {
        if (cbb.SelectedItem is ComboBoxItem item && item.Content != null)
        {
            string content = item.Content.ToString()?.Trim() ?? "";
            return content == defaultValue ? "" : content;
        }

        if (cbb.IsEditable && !string.IsNullOrWhiteSpace(cbb.Text))
        {
            string text = cbb.Text.Trim();
            return text == defaultValue ? "" : text;
        }

        return "";
    }

    private void LstModResult_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstModResult.SelectedItem is not ModSearchHit modHit) return;
        _selectedMod = modHit;
        ShowVersionDetail(modHit);
    }

    private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentOffset >= PageSize)
        {
            _currentOffset -= PageSize;
            DoSearch();
        }
    }

    private void BtnNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentOffset + PageSize < _totalHits)
        {
            _currentOffset += PageSize;
            DoSearch();
        }
    }

    #endregion

    #region ========== 版本详情面板 ==========

    private async void ShowVersionDetail(ModSearchHit modHit)
    {
        _downloadProjectType = ProjectTypes.Contains(modHit.ProjectType)
            ? modHit.ProjectType
            : (ProjectTypes.Contains(_currentProjectType) ? _currentProjectType : "mod");
        panelSearch.Visibility = Visibility.Collapsed;
        panelVersionDetail.Visibility = Visibility.Visible;
        panelDownloadConfirm.Visibility = Visibility.Collapsed;
        panelDownloading.Visibility = Visibility.Collapsed;
        panelGame.Visibility = Visibility.Collapsed;

        panelVersionDetail.DataContext = new { SelectedMod = modHit };
        btnOpenExternal.Content = "访问 Modrinth";

        try
        {
            string url = $"https://api.modrinth.com/v2/project/{modHit.ProjectId}/version";
            string json = await _httpClient.GetStringAsync(url);

            _currentVersions = JsonSerializer.Deserialize<List<ModVersion>>(json, SharedJsonOptions) ?? new();

            BuildVersionTags();
            FilterVersionsByTag(_currentGameVer);
        }
        catch (Exception ex)
        {
            txtStatusMsg.Text = $"加载失败：{ex.Message}";
        }
    }

    private void BuildVersionTags()
    {
        panelVersionTags.Children.Clear();

        var allGameVersions = _currentVersions
            .SelectMany(v => v.GameVersions)
            .Distinct()
            .OrderByDescending(v => v)
            .ToList();

        var btnAll = CreateTagButton("全部", string.IsNullOrWhiteSpace(_currentGameVer));
        btnAll.Click += (s, e) => FilterVersionsByTag("全部");
        panelVersionTags.Children.Add(btnAll);

        foreach (var ver in allGameVersions)
        {
            bool isActive = ver == _currentGameVer;
            var btn = CreateTagButton(ver, isActive);
            string capturedVer = ver;
            btn.Click += (s, e) => FilterVersionsByTag(capturedVer);
            panelVersionTags.Children.Add(btn);
        }
    }

    private Button CreateTagButton(string text, bool isActive)
    {
        var btn = new Button
        {
            Content = text,
            Width = 70,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Style = (Style)FindResource("VersionTag")
        };

        if (isActive)
        {
            btn.SetResourceReference(Control.BackgroundProperty, "ThemeAccent");
            btn.Foreground = Brushes.White;
            btn.FontWeight = FontWeights.SemiBold;
        }

        return btn;
    }

    private void FilterVersionsByTag(string gameVerTag)
    {
        var filtered = gameVerTag == "全部"
            ? _currentVersions
            : _currentVersions.Where(v => v.GameVersions.Contains(gameVerTag)).ToList();

        var displayItems = filtered
            .Select(v => new VersionDisplayItem(v))
            .ToList();

        itemsVersionGroups.ItemsSource = displayItems;
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        panelVersionDetail.Visibility = Visibility.Collapsed;
        panelSearch.Visibility = Visibility.Visible;
        panelGame.Visibility = Visibility.Collapsed;
        _currentVersions.Clear();
        itemsVersionGroups.ItemsSource = null;
        lstModResult.SelectedIndex = -1;
    }

    private void BtnOpenModrinth_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMod == null) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = $"https://modrinth.com/{_currentProjectType}/{_selectedMod.ProjectId}",
            UseShellExecute = true
        });
    }

    private void BtnCopyName_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMod == null) return;
        System.Windows.Clipboard.SetText(_selectedMod.Title);
        txtStatusMsg.Text = "已复制名称";
    }

    #endregion

    #region ========== 下载确认面板 ==========

    private void BtnDownloadVersion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not VersionDisplayItem item) return;

        var mainFile = item.Version.Files.FirstOrDefault(f => f.IsPrimary)
            ?? item.Version.Files.FirstOrDefault();

        if (mainFile == null)
        {
            txtStatusMsg.Text = "该版本无可下载文件";
            return;
        }

        _pendingVersion = item;
        _pendingDownloadFile = mainFile;
        _pendingDownloadProjectType = _downloadProjectType;

        if (_settings.SkipDownloadConfirm)
        {
            StartDownload(mainFile, _pendingDownloadProjectType);
            return;
        }

        txtDownloadPath.Text = _downloadPath;
        txtDownloadFileName.Text = mainFile.FileName;
        txtDownloadVersionInfo.Text = $"版本: {item.Version.VersionNumber} | MC版本: {string.Join(", ", item.Version.GameVersions.Take(3))}";

        panelVersionDetail.Visibility = Visibility.Collapsed;
        panelDownloadConfirm.Visibility = Visibility.Visible;
    }

    private void BtnBackFromDownload_Click(object sender, RoutedEventArgs e)
    {
        panelDownloadConfirm.Visibility = Visibility.Collapsed;
        panelVersionDetail.Visibility = Visibility.Visible;
        _pendingDownloadFile = null;
        _pendingVersion = null;
        _pendingDownloadProjectType = "mod";
    }

    private void BtnBrowseDownloadPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择下载保存位置",
            FolderName = _downloadPath
        };

        if (dialog.ShowDialog() == true)
        {
            SetDownloadPath(dialog.FolderName);
            txtDownloadPath.Text = dialog.FolderName;
        }
    }

    private void BtnStartDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDownloadFile == null) return;
        StartDownload(_pendingDownloadFile, _pendingDownloadProjectType);
    }

    private void StartDownload(ModFile file, string projectType)
    {
        if (!TryResolveSaveDirectory(projectType, out string saveDir))
        {
            txtStatusMsg.Text = "无法创建下载目录，请检查默认下载文件夹权限";
            return;
        }

        string fileName = SanitizeFileName(file.FileName);
        var request = new DownloadRequest(
            Interlocked.Increment(ref _downloadIdCounter),
            file.Url,
            fileName,
            saveDir,
            _settings.OverwriteExistingFiles,
            _settings.OpenFolderAfterDownload);

        _pendingDownloadFile = null;
        _pendingVersion = null;
        _pendingDownloadProjectType = "mod";
        panelDownloadConfirm.Visibility = Visibility.Collapsed;
        panelVersionDetail.Visibility = Visibility.Visible;
        txtStatusMsg.Text = $"已加入下载队列：{fileName}";
        _ = Task.Run(() => RunDownloadAsync(request, _downloadCts!.Token));
    }

    private bool TryResolveSaveDirectory(string projectType, out string saveDir)
    {
        string root = Path.GetFullPath(_downloadPath);
        saveDir = root;
        if (_settings.CreateTypeSubfolder && ProjectTypes.Contains(projectType))
        {
            string candidate = Path.GetFullPath(Path.Combine(root, projectType));
            if (IsSubPathOf(candidate, root))
                saveDir = candidate;
        }

        if (TryCreateDirectory(saveDir))
            return true;
        if (TryCreateDirectory(root))
        {
            saveDir = root;
            return true;
        }

        string desktop = GetDesktopDirectory();
        if (TryCreateDirectory(desktop))
        {
            saveDir = desktop;
            txtStatusMsg.Text = "默认下载目录不可用，已改存到桌面";
            return true;
        }

        saveDir = "";
        return false;
    }

    private static bool IsSubPathOf(string candidate, string root)
    {
        string rootPrefix = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string fullCandidate = Path.GetFullPath(candidate);
        return fullCandidate.Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
            || fullCandidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        string name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
            return "download.bin";

        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }

        string sanitized = new string(chars).Trim('.', ' ');
        if (string.IsNullOrWhiteSpace(sanitized))
            return "download.bin";

        int dot = sanitized.IndexOf('.');
        string stem = dot >= 0 ? sanitized[..dot] : sanitized;
        if (ReservedFileNames.Contains(stem))
            sanitized = "_" + sanitized;

        if (sanitized.Length > MaxFileNameLength)
        {
            string ext = Path.GetExtension(sanitized);
            string body = Path.GetFileNameWithoutExtension(sanitized);
            int keep = Math.Max(8, MaxFileNameLength - ext.Length - 9);
            if (keep > body.Length) keep = body.Length;
            sanitized = body[..keep] + "_" + Math.Abs(sanitized.GetHashCode()).ToString("x8")[..8] + ext;
        }

        return sanitized;
    }

    private static string CommitDownloadedFile(string tempPath, string saveDir, string fileName, bool overwriteExisting)
    {
        string dest = Path.Combine(saveDir, fileName);
        if (overwriteExisting)
        {
            File.Move(tempPath, dest, overwrite: true);
            return dest;
        }

        string name = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int index = 0; index <= MaxDuplicateSuffix; index++)
        {
            dest = index == 0
                ? Path.Combine(saveDir, fileName)
                : Path.Combine(saveDir, $"{name} ({index}){ext}");
            try
            {
                File.Move(tempPath, dest);
                return dest;
            }
            catch (IOException ex) when (IsFileAlreadyExists(ex))
            {
            }
        }

        throw new IOException("无法分配不冲突的文件名，目录中同名文件过多");
    }

    private static bool IsFileAlreadyExists(IOException ex)
        => (ex.HResult & 0xFFFF) is 80 or 183;

    private static bool OpenContainingFolder(string filePath)
    {
        try
        {
            string fullPath = Path.GetFullPath(filePath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{fullPath}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception
            or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task RunDownloadAsync(DownloadRequest request, CancellationToken ct)
    {
        bool ownsPanel = false;
        bool semaphoreAcquired = false;
        string? tempPath = null;
        string displayName = request.FileName;
        try
        {
            await _downloadSemaphore.WaitAsync(ct);
            semaphoreAcquired = true;
            Interlocked.Increment(ref _activeDownloadCount);

            lock (_panelLock)
            {
                if (_panelOwnerId == -1)
                {
                    _panelOwnerId = request.DownloadId;
                    ownsPanel = true;
                }
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (ownsPanel)
                {
                    panelVersionDetail.Visibility = Visibility.Collapsed;
                    panelSearch.Visibility = Visibility.Collapsed;
                    panelDownloadConfirm.Visibility = Visibility.Collapsed;
                    panelSettings.Visibility = Visibility.Collapsed;
                    panelGame.Visibility = Visibility.Collapsed;
                    panelDownloading.Visibility = Visibility.Visible;

                    txtDownloadingFile.Text = displayName;
                    progressBarFill.Width = 0;
                    txtDownloadPercent.Text = "0%";
                }
                else
                {
                    txtStatusMsg.Text = $"正在后台下载：{displayName}（并发 {_activeDownloadCount}）";
                }
            });

            using var response = await _httpClient.GetAsync(
                request.FileUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            long receivedBytes = 0;
            byte[] buffer = new byte[8192];

            tempPath = Path.Combine(request.SaveDir, $".{Guid.NewGuid():N}.part");
            using var streamRemote = await response.Content.ReadAsStreamAsync(ct);
            using (var streamLocal = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, true))
            {
                int readCount;
                while ((readCount = await streamRemote.ReadAsync(buffer, ct)) > 0)
                {
                    await streamLocal.WriteAsync(buffer.AsMemory(0, readCount), ct);
                    receivedBytes += readCount;

                    if (ownsPanel && totalBytes > 0)
                    {
                        double percent = receivedBytes * 100.0 / totalBytes;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            UpdateProgressBar(percent);
                            txtDownloadPercent.Text = $"{percent:F1}%";
                        });
                    }
                }
            }

            string savePath = CommitDownloadedFile(
                tempPath, request.SaveDir, displayName, request.OverwriteExisting);
            tempPath = null;

            await Dispatcher.InvokeAsync(() =>
            {
                if (ownsPanel)
                {
                    txtDownloadingFile.Text = $"下载完成！{Path.GetFileName(savePath)}";
                    txtDownloadPercent.Text = "100%";
                    UpdateProgressBar(100);
                }
                else
                {
                    txtStatusMsg.Text = $"下载完成：{Path.GetFileName(savePath)}";
                }
            });

            if (request.OpenFolderAfterDownload && !OpenContainingFolder(savePath))
            {
                await Dispatcher.InvokeAsync(() =>
                    txtStatusMsg.Text = "下载完成，但无法打开所在文件夹");
            }

            if (ownsPanel)
                await Task.Delay(1500);
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (ownsPanel)
                {
                    txtDownloadingFile.Text = "下载已取消";
                    txtDownloadPercent.Text = "";
                }
                else
                {
                    txtStatusMsg.Text = $"下载已取消：{displayName}";
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (ownsPanel)
                {
                    txtDownloadingFile.Text = $"下载失败：{ex.Message}";
                    txtDownloadPercent.Text = "";
                }
                else
                {
                    txtStatusMsg.Text = $"下载失败：{displayName} - {ex.Message}";
                }
            });
        }
        finally
        {
            if (tempPath != null)
            {
                try { File.Delete(tempPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            if (semaphoreAcquired)
            {
                Interlocked.Decrement(ref _activeDownloadCount);
                _downloadSemaphore.Release();
            }

            if (ownsPanel)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    lock (_panelLock)
                    {
                        _panelOwnerId = -1;
                    }

                    if (panelSettings.Visibility != Visibility.Visible
                        && panelGame.Visibility != Visibility.Visible)
                    {
                        panelDownloading.Visibility = Visibility.Collapsed;
                        panelVersionDetail.Visibility = Visibility.Visible;
                    }
                });
            }
        }
    }

    private void UpdateProgressBar(double percent)
    {
        if (progressBarFill.Parent is not FrameworkElement parent) return;

        double targetWidth = percent / 100.0 * parent.ActualWidth;
        if (targetWidth < 0) targetWidth = 0;

        progressBarFill.Width = targetWidth;
    }

    /// <summary>单次下载任务的快照参数，避免后台线程读取可变 UI 或远程文件对象。</summary>
    private sealed record DownloadRequest(
        int DownloadId,
        string FileUrl,
        string FileName,
        string SaveDir,
        bool OverwriteExisting,
        bool OpenFolderAfterDownload);

    #endregion

    #region ========== Minecraft 游戏安装 ==========

    private void ShowGamePanel()
    {
        panelSearch.Visibility = Visibility.Collapsed;
        panelVersionDetail.Visibility = Visibility.Collapsed;
        panelDownloadConfirm.Visibility = Visibility.Collapsed;
        panelDownloading.Visibility = Visibility.Collapsed;
        panelSettings.Visibility = Visibility.Collapsed;
        panelGame.Visibility = Visibility.Visible;
        txtMinecraftPath.Text = _minecraftPath;
        if (_gameListState == GameListState.Idle)
            _ = LoadMinecraftVersionsAsync();
    }

    private void HideGamePanel()
    {
        panelGame.Visibility = Visibility.Collapsed;
        panelSearch.Visibility = Visibility.Visible;
    }

    private async Task LoadMinecraftVersionsAsync()
    {
        if (_gameListState == GameListState.Loading) return;
        _gameListState = GameListState.Loading;
        txtGameStatus.Text = "正在从 BMCLAPI 拉取版本列表...";
        try
        {
            var manifest = await _mcInstaller.Api.GetManifestAsync(CancellationToken.None);
            _allMcVersions = manifest.Versions;
            _gameListState = GameListState.Loaded;
            RefreshGameVersionList();
            txtGameSourceHint.Text = $"BMCLAPI · 共 {_allMcVersions.Count} 个版本";
            txtGameStatus.Text = string.IsNullOrEmpty(manifest.Latest.Release)
                ? "版本列表已就绪"
                : $"最新正式版 {manifest.Latest.Release}";
        }
        catch (Exception ex)
        {
            _gameListState = GameListState.Idle;
            txtGameStatus.Text = $"版本列表加载失败：{ex.GetBaseException().Message}";
        }
    }

    private void RefreshGameVersionList()
    {
        string keyword = txtGameSearch.Text.Trim();
        var filtered = _allMcVersions.Where(v =>
        {
            bool typeOk = (v.IsRelease && _gameFilterRelease)
                          || (v.IsSnapshot && _gameFilterSnapshot)
                          || (v.IsLegacy && _gameFilterLegacy);
            if (!typeOk) return false;
            return string.IsNullOrEmpty(keyword)
                   || v.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        lstGameVersions.ItemsSource = filtered;
        if (_selectedMcVersion != null)
        {
            var match = filtered.FirstOrDefault(v => v.Id == _selectedMcVersion.Id);
            if (match != null)
                lstGameVersions.SelectedItem = match;
        }
    }

    private void TxtGameSearch_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshGameVersionList();

    private void GameFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        switch (btn.Tag?.ToString())
        {
            case "release":
                _gameFilterRelease = !_gameFilterRelease;
                if (!_gameFilterRelease && !_gameFilterSnapshot && !_gameFilterLegacy)
                    _gameFilterRelease = true;
                break;
            case "snapshot":
                _gameFilterSnapshot = !_gameFilterSnapshot;
                break;
            case "old":
                _gameFilterLegacy = !_gameFilterLegacy;
                break;
        }
        if (!_gameFilterRelease && !_gameFilterSnapshot && !_gameFilterLegacy)
            _gameFilterRelease = true;
        HighlightGameFilters();
        RefreshGameVersionList();
    }

    private void HighlightGameFilters()
    {
        SetSegmentButton(btnGameFilterRelease, _gameFilterRelease);
        SetSegmentButton(btnGameFilterSnapshot, _gameFilterSnapshot);
        SetSegmentButton(btnGameFilterLegacy, _gameFilterLegacy);
    }

    private void SetSegmentButton(Button btn, bool active)
    {
        if (active)
        {
            btn.SetResourceReference(Control.BackgroundProperty, "ThemeAccent");
            btn.Foreground = Brushes.White;
            btn.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            btn.SetResourceReference(Control.BackgroundProperty, "ThemeCardBackground");
            btn.SetResourceReference(Control.ForegroundProperty, "ThemePrimaryText");
            btn.FontWeight = FontWeights.Normal;
        }
    }

    private void LstGameVersions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstGameVersions.SelectedItem is not McVersionInfo version) return;
        _selectedMcVersion = version;
        txtSelectedGameVersion.Text = version.Id;
        txtSelectedGameMeta.Text = $"{version.TypeLabel}  ·  {version.TimeDisplay}  ·  BMCLAPI";
        txtCustomVersionName.Text = "";
        _ = LoadLoaderVersionsAsync();
    }

    private void LoaderKind_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _selectedLoader = btn.Tag?.ToString() switch
        {
            "Forge" => McLoaderKind.Forge,
            "Fabric" => McLoaderKind.Fabric,
            "NeoForge" => McLoaderKind.NeoForge,
            "OptiFine" => McLoaderKind.OptiFine,
            _ => McLoaderKind.Vanilla
        };
        HighlightLoaderButton();
        _ = LoadLoaderVersionsAsync();
    }

    private void HighlightLoaderButton()
    {
        SetSegmentButton(btnLoaderVanilla, _selectedLoader == McLoaderKind.Vanilla);
        SetSegmentButton(btnLoaderForge, _selectedLoader == McLoaderKind.Forge);
        SetSegmentButton(btnLoaderFabric, _selectedLoader == McLoaderKind.Fabric);
        SetSegmentButton(btnLoaderNeoForge, _selectedLoader == McLoaderKind.NeoForge);
        SetSegmentButton(btnLoaderOptiFine, _selectedLoader == McLoaderKind.OptiFine);
    }

    private async Task LoadLoaderVersionsAsync()
    {
        cbbLoaderVersion.ItemsSource = null;
        cbbLoaderVersion.Visibility = _selectedLoader == McLoaderKind.Vanilla
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (_selectedMcVersion == null)
        {
            txtLoaderHint.Text = "先在左侧选择 Minecraft 版本";
            return;
        }

        if (_selectedLoader == McLoaderKind.Vanilla)
        {
            txtLoaderHint.Text = "将安装原版客户端，不附加加载器";
            return;
        }

        _loaderListCts?.Cancel();
        _loaderListCts?.Dispose();
        _loaderListCts = new CancellationTokenSource();
        var ct = _loaderListCts.Token;
        string mcId = _selectedMcVersion.Id;
        txtLoaderHint.Text = "正在查询可用加载器版本...";

        try
        {
            List<LoaderOption> options = _selectedLoader switch
            {
                McLoaderKind.Forge => (await _mcInstaller.Api.GetForgeAsync(mcId, ct))
                    .OrderByDescending(x => x.Build)
                    .Select(x => new LoaderOption(x.Version, "", x.Display))
                    .ToList(),
                McLoaderKind.Fabric => (await _mcInstaller.Api.GetFabricAsync(mcId, ct))
                    .Select(x => new LoaderOption(x.Loader.Version, "", x.Loader.Display))
                    .ToList(),
                McLoaderKind.NeoForge => (await _mcInstaller.Api.GetNeoForgeAsync(mcId, ct))
                    .OrderByDescending(x => x.Version, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new LoaderOption(x.Version, "", x.Display))
                    .ToList(),
                McLoaderKind.OptiFine => (await _mcInstaller.Api.GetOptiFineAsync(mcId, ct))
                    .OrderBy(x => x.IsPreview)
                    .ThenByDescending(x => x.Patch)
                    .Select(x => new LoaderOption(x.Patch, x.Type, x.Display))
                    .ToList(),
                _ => new List<LoaderOption>()
            };

            if (ct.IsCancellationRequested) return;
            cbbLoaderVersion.ItemsSource = options;
            if (options.Count > 0)
            {
                cbbLoaderVersion.SelectedIndex = 0;
                txtLoaderHint.Text = $"该版本共 {options.Count} 个加载器构建";
            }
            else
            {
                txtLoaderHint.Text = "该 Minecraft 版本没有对应加载器";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            txtLoaderHint.Text = $"加载器列表获取失败：{ex.Message}";
        }
    }

    private void InstallMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _selectedInstallMode = btn.Tag?.ToString() switch
        {
            "CoreOnly" => McInstallMode.CoreOnly,
            "CustomDirectory" => McInstallMode.CustomDirectory,
            _ => McInstallMode.VersionsFolder
        };
        HighlightInstallModeButton();
    }

    private void HighlightInstallModeButton()
    {
        SetSegmentButton(btnModeCore, _selectedInstallMode == McInstallMode.CoreOnly);
        SetSegmentButton(btnModeVersions, _selectedInstallMode == McInstallMode.VersionsFolder);
        SetSegmentButton(btnModeCustom, _selectedInstallMode == McInstallMode.CustomDirectory);
    }

    private void BtnBrowseMinecraftPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = _selectedInstallMode switch
            {
                McInstallMode.CoreOnly => "选择核心保存文件夹",
                McInstallMode.CustomDirectory => "选择空白安装目录",
                _ => "选择 .minecraft 或 versions 文件夹"
            },
            FolderName = _minecraftPath
        };

        if (dialog.ShowDialog() == true)
        {
            _minecraftPath = ResolveWritableDirectory(dialog.FolderName) ?? dialog.FolderName;
            txtMinecraftPath.Text = _minecraftPath;
            SaveSettings();
        }
    }

    private async void BtnInstallMinecraft_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMcVersion == null)
        {
            txtGameStatus.Text = "请先选择一个 Minecraft 版本";
            return;
        }

        if (string.IsNullOrWhiteSpace(_minecraftPath))
        {
            txtGameStatus.Text = "请选择安装目录";
            return;
        }

        string loaderVersion = "";
        string loaderExtra = "";
        if (_selectedLoader != McLoaderKind.Vanilla)
        {
            if (cbbLoaderVersion.SelectedItem is not LoaderOption option
                || string.IsNullOrWhiteSpace(option.Value))
            {
                txtGameStatus.Text = "请选择加载器版本";
                return;
            }
            try
            {
                loaderVersion = MinecraftInstaller.SanitizeVersionSegment(option.Value);
                loaderExtra = string.IsNullOrWhiteSpace(option.Extra)
                    ? ""
                    : MinecraftInstaller.SanitizeVersionSegment(option.Extra);
            }
            catch (Exception ex)
            {
                txtGameStatus.Text = ex.Message;
                return;
            }
        }

        string versionName;
        try
        {
            versionName = string.IsNullOrWhiteSpace(txtCustomVersionName.Text)
                ? ""
                : MinecraftInstaller.SanitizeVersionSegment(txtCustomVersionName.Text);
        }
        catch (Exception ex)
        {
            txtGameStatus.Text = ex.Message;
            return;
        }

        var request = new MinecraftInstallRequest
        {
            GameVersion = _selectedMcVersion,
            Loader = _selectedLoader,
            LoaderVersion = loaderVersion,
            LoaderExtra = loaderExtra,
            Mode = _selectedInstallMode,
            TargetPath = _minecraftPath,
            VersionName = versionName,
            MaxConcurrency = _maxDownloadThreads,
            OpenFolderWhenDone = _settings.OpenFolderAfterDownload
        };

        _mcInstallCts?.Cancel();
        _mcInstallCts?.Dispose();
        _mcInstallCts = new CancellationTokenSource();
        btnInstallMinecraft.IsEnabled = false;
        panelGame.Visibility = Visibility.Collapsed;
        panelDownloading.Visibility = Visibility.Visible;
        txtDownloadingFile.Text = $"准备安装 {MinecraftInstaller.BuildDefaultVersionName(request)}";
        progressBarFill.Width = 0;
        txtDownloadPercent.Text = "0%";

        var progress = new Progress<MinecraftInstallProgress>(p =>
        {
            txtDownloadingFile.Text = p.Message;
            txtDownloadPercent.Text = $"{p.Percent:F0}%";
            UpdateProgressBar(p.Percent);
        });

        try
        {
            await _mcInstaller.InstallAsync(request, progress, _mcInstallCts.Token);
            txtDownloadingFile.Text = "安装完成";
            txtDownloadPercent.Text = "100%";
            UpdateProgressBar(100);
            if (request.OpenFolderWhenDone)
            {
                string reveal = Directory.EnumerateFiles(_minecraftPath, "*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault() ?? _minecraftPath;
                OpenContainingFolder(reveal);
            }
            await Task.Delay(1200);
        }
        catch (OperationCanceledException)
        {
            txtDownloadingFile.Text = "安装已取消";
        }
        catch (Exception ex)
        {
            txtDownloadingFile.Text = $"安装失败：{ex.GetBaseException().Message}";
            txtGameStatus.Text = $"安装失败：{ex.GetBaseException().Message}";
            await Task.Delay(1800);
        }
        finally
        {
            btnInstallMinecraft.IsEnabled = true;
            panelDownloading.Visibility = Visibility.Collapsed;
            panelGame.Visibility = Visibility.Visible;
        }
    }

    #endregion
}

public class VersionDisplayItem
{
    public ModVersion Version { get; }
    public string VersionNumber => Version.VersionNumber;
    public string LoadersDisplay => string.Join(", ", Version.Loaders);
    public string GameVersionsDisplay => string.Join(", ", Version.GameVersions);
    public string VersionInfo => $"支持MC: {GameVersionsDisplay} | Loaders: {LoadersDisplay}";
    public bool IsPreview => Version.VersionNumber.Contains("beta", StringComparison.OrdinalIgnoreCase)
        || Version.VersionNumber.Contains("alpha", StringComparison.OrdinalIgnoreCase)
        || Version.VersionNumber.Contains("rc", StringComparison.OrdinalIgnoreCase);
    public bool IsRelease => !IsPreview;

    public VersionDisplayItem(ModVersion version)
    {
        Version = version;
    }
}
