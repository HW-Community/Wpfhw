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
    private readonly AppSettings _settings;
    private ModSearchHit? _selectedMod;
    private List<ModVersion> _currentVersions = new();
    private string _currentGameVer = "";
    private string _currentLoader = "";
    private string _currentProjectType = "mod";
    private string _downloadPath = "";
    private ModFile? _pendingDownloadFile;
    private VersionDisplayItem? _pendingVersion;
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
    private enum MainPanel { Search, VersionDetail, DownloadConfirm, Downloading }
    private MainPanel _lastPanel = MainPanel.Search;

    /// <summary>中译缓存：key = Modrinth ProjectId（小写）</summary>
    private readonly Dictionary<string, ModTranslation> _translations = new();

    /// <summary>待匹配的中译：key = 英文名小写 -> 中译。搜 Modrinth 前先填这个，命中 Modrinth 结果时迁移到 _translations。</summary>
    private readonly Dictionary<string, ModTranslation> _pendingByEnglish = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> LoaderTypes = new() { "mod", "modpack" };
    private static readonly HashSet<string> ProjectTypes = new() { "mod", "resourcepack", "shader", "datapack", "modpack" };

    public MainWindow()
    {
        InitializeComponent();

        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ModDownloader/1.0 (haodi0302@qq.com; Windows)");

        _settings = AppSettings.Load();
        _currentProjectType = ProjectTypes.Contains(_settings.LastProjectType)
            ? _settings.LastProjectType
            : "mod";
        _downloadPath = !string.IsNullOrWhiteSpace(_settings.DownloadPath) && Directory.Exists(_settings.DownloadPath)
            ? _settings.DownloadPath
            : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

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
        SaveSettings();

        _downloadCts = new CancellationTokenSource();

        Closed += (_, _) =>
        {
            SaveSettings();
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _httpClient.Dispose();
        };
    }

    private Button GetNavButton(string projectType) => projectType switch
    {
        "resourcepack" => navResource,
        "shader" => navShader,
        "datapack" => navData,
        "modpack" => navPack,
        _ => navMod
    };

    private void SaveSettings()
    {
        _settings.DownloadPath = _downloadPath;
        _settings.LastProjectType = _currentProjectType;
        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;
        _settings.ThemeMode = _currentThemeMode;
        _settings.MaxDownloadThreads = _maxDownloadThreads;
        _settings.Save();
    }

    private ThemeMode _currentThemeMode = ThemeMode.System;
    private int _maxDownloadThreads = 3;

    /// <summary>应用主题并高亮对应按钮。</summary>
    private void ApplyTheme(ThemeMode mode)
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
            ThemeMode.Light => btnThemeLight,
            ThemeMode.Dark => btnThemeDark,
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
        else _lastPanel = MainPanel.Search;

        panelSearch.Visibility = Visibility.Collapsed;
        panelVersionDetail.Visibility = Visibility.Collapsed;
        panelDownloadConfirm.Visibility = Visibility.Collapsed;
        panelDownloading.Visibility = Visibility.Collapsed;
        panelSettings.Visibility = Visibility.Visible;

        UpdateThemeButtons();
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
            default:
                panelSearch.Visibility = Visibility.Visible;
                break;
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        ThemeMode mode = btn.Tag?.ToString() switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System
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

    #endregion

    #region ========== 导航栏 ==========

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        _currentProjectType = btn.Tag?.ToString() ?? "mod";
        UpdateNavStyle(btn);
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
        panelSearch.Visibility = Visibility.Collapsed;
        panelVersionDetail.Visibility = Visibility.Visible;
        panelDownloadConfirm.Visibility = Visibility.Collapsed;
        panelDownloading.Visibility = Visibility.Collapsed;

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

        _pendingVersion = item;
        var mainFile = item.Version.Files.FirstOrDefault(f => f.IsPrimary)
            ?? item.Version.Files.FirstOrDefault();

        if (mainFile == null)
        {
            txtStatusMsg.Text = "该版本无可下载文件";
            return;
        }

        _pendingDownloadFile = mainFile;
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
            _downloadPath = dialog.FolderName;
            txtDownloadPath.Text = _downloadPath;
            SaveSettings();
        }
    }

    private void BtnStartDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDownloadFile == null) return;

        var file = _pendingDownloadFile;
        string saveDir = _downloadPath;
        int downloadId = Interlocked.Increment(ref _downloadIdCounter);

        // 立即返回版本详情，允许用户继续排队下载
        panelDownloadConfirm.Visibility = Visibility.Collapsed;
        panelVersionDetail.Visibility = Visibility.Visible;
        _pendingDownloadFile = null;
        _pendingVersion = null;

        txtStatusMsg.Text = $"已加入下载队列：{file.FileName}";

        // 启动并发下载任务（受信号量限制）
        _ = Task.Run(() => RunDownloadAsync(downloadId, file, saveDir, _downloadCts!.Token));
    }

    private async Task RunDownloadAsync(int downloadId, ModFile file,
        string saveDir, CancellationToken ct)
    {
        bool ownsPanel = false;
        bool semaphoreAcquired = false;
        try
        {
            await _downloadSemaphore.WaitAsync(ct);
            semaphoreAcquired = true;
            Interlocked.Increment(ref _activeDownloadCount);

            // 尝试占用下载进度面板
            lock (_panelLock)
            {
                if (_panelOwnerId == -1)
                {
                    _panelOwnerId = downloadId;
                    ownsPanel = true;
                }
            }

            string fullSavePath = Path.Combine(saveDir, file.FileName);

            await Dispatcher.InvokeAsync(() =>
            {
                if (ownsPanel)
                {
                    panelVersionDetail.Visibility = Visibility.Collapsed;
                    panelSearch.Visibility = Visibility.Collapsed;
                    panelDownloadConfirm.Visibility = Visibility.Collapsed;
                    panelSettings.Visibility = Visibility.Collapsed;
                    panelDownloading.Visibility = Visibility.Visible;

                    txtDownloadingFile.Text = file.FileName;
                    progressBarFill.Width = 0;
                    txtDownloadPercent.Text = "0%";
                }
                else
                {
                    txtStatusMsg.Text = $"正在后台下载：{file.FileName}（并发 {_activeDownloadCount}）";
                }
            });

            using var response = await _httpClient.GetAsync(
                file.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            long receivedBytes = 0;
            byte[] buffer = new byte[8192];

            using var streamRemote = await response.Content.ReadAsStreamAsync(ct);
            using var streamLocal = new FileStream(
                fullSavePath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true);

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

            await Dispatcher.InvokeAsync(() =>
            {
                if (ownsPanel)
                {
                    txtDownloadingFile.Text = $"下载完成！{file.FileName}";
                    txtDownloadPercent.Text = "100%";
                    UpdateProgressBar(100);
                }
                else
                {
                    txtStatusMsg.Text = $"下载完成：{file.FileName}";
                }
            });

            await Task.Delay(ownsPanel ? 1500 : 0, ct);
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
                    txtStatusMsg.Text = $"下载已取消：{file.FileName}";
                }
            });

            string fullSavePath = Path.Combine(saveDir, file.FileName);
            if (File.Exists(fullSavePath))
            {
                try { File.Delete(fullSavePath); } catch { }
            }
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
                    txtStatusMsg.Text = $"下载失败：{file.FileName} - {ex.Message}";
                }
            });
        }
        finally
        {
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

                    if (panelSettings.Visibility != Visibility.Visible)
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
