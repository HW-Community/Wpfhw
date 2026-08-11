using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Media.Animation;

namespace wpfhw;

public partial class MainWindow : Window
{
    private readonly HttpClient _httpClient;
    private ModSearchHit? _selectedMod;
    private List<ModVersion> _currentVersions = new();
    private string _currentGameVer = "";
    private string _currentLoader = "";
    private string _currentProjectType = "mod";
    private string _searchSource = "modrinth";
    private string _downloadPath = "";
    private ModFile? _pendingDownloadFile;
    private VersionDisplayItem? _pendingVersion;
    private CancellationTokenSource? _downloadCts;
    private int _currentOffset = 0;
    private const int PageSize = 30;
    private string _lastKeyword = "";
    private int _totalHits = 0;

    private static readonly HashSet<string> LoaderTypes = new() { "mod", "modpack" };

    public MainWindow()
    {
        InitializeComponent();

        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ModDownloader/1.0 (haodi0302@qq.com; Windows)");

        _currentProjectType = "mod";
        _downloadPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        UpdateNavStyle(navMod);
        cbbSearchSource.SelectedIndex = 0;

        UpdateLoaderVisibility();

        this.Closed += (s, e) =>
        {
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _httpClient.Dispose();
        };
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

        if (_searchSource == "mcbbs")
        {
            txtSearchKey.Tag = $"搜索{typeName}(中文)...";
        }
        else
        {
            txtSearchKey.Tag = $"搜索{typeName}...";
        }
    }

    #region ========== 窗口控制 ==========

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
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
        txtSearchKey.Tag = _currentProjectType switch
        {
            "resourcepack" => "搜索资源包...",
            "shader" => "搜索光影...",
            "datapack" => "搜索数据包...",
            "modpack" => "搜索整合包...",
            _ => "搜索模组..."
        };
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
                btn.Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102));
                btn.FontWeight = FontWeights.Normal;
                btn.Background = Brushes.Transparent;
            }
        }

        active.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 255));
        active.FontWeight = FontWeights.SemiBold;
        active.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    }

    #endregion

    #region ========== 搜索源选择 ==========

    private void CbbSearchSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cbbSearchSource.SelectedItem is not ComboBoxItem item) return;
        _searchSource = item.Tag?.ToString() ?? "modrinth";
        UpdateSearchPlaceholder();
    }

    #endregion

    #region ========== 搜索面板 ==========

    private void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        _currentOffset = 0;
        _lastKeyword = txtSearchKey.Text.Trim();
        DoSearch();
    }

    private async void DoSearch()
    {
        if (_searchSource == "mcbbs")
        {
            await DoMCBBSSearch();
        }
        else
        {
            await DoModrinthSearch();
        }
    }

    private async Task DoModrinthSearch()
    {
        string keyword = _lastKeyword;
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
            {
                facetGroups.Add($"[\"versions:{gameVer}\"]");
            }

            if (!string.IsNullOrWhiteSpace(loader) && HasLoaders())
            {
                facetGroups.Add($"[\"categories:{loader}\"]");
            }

            string rawFacet = "[" + string.Join(",", facetGroups) + "]";
            string facetEnc = Uri.EscapeDataString(rawFacet);
            string queryEnc = Uri.EscapeDataString(keyword);

            string sortParam = string.IsNullOrWhiteSpace(keyword) ? "&index=downloads" : "";
            string requestUrl = $"https://api.modrinth.com/v2/search?query={queryEnc}&facets={facetEnc}&limit={PageSize}&offset={_currentOffset}{sortParam}";

            string jsonText = await _httpClient.GetStringAsync(requestUrl);

            var jsonOption = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var searchResult = JsonSerializer.Deserialize<ModSearchResponse>(jsonText, jsonOption);

            _totalHits = searchResult?.TotalHits ?? 0;

            if (searchResult?.Hits != null && searchResult.Hits.Any())
            {
                foreach (var mod in searchResult.Hits)
                {
                    lstModResult.Items.Add(mod);
                }
                txtStatusMsg.Text = $"找到 {_totalHits} 个结果，第 {_currentOffset / PageSize + 1} 页";
            }
            else
            {
                txtStatusMsg.Text = "未查询到相关结果";
            }
        }
        catch (TaskCanceledException)
        {
            txtStatusMsg.Text = "请求超时！国内直连 Modrinth 不稳定";
        }
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

    private async Task DoMCBBSSearch()
    {
        string keyword = _lastKeyword;

        try
        {
            statusDot.Visibility = Visibility.Visible;
            txtStatusMsg.Text = "正在搜索 MC百科...";
            lstModResult.Items.Clear();

            string mcbbsType = _currentProjectType switch
            {
                "mod" => "mod",
                "resourcepack" => "resource",
                "shader" => "shader",
                "datapack" => "data",
                "modpack" => "modpack",
                _ => "mod"
            };

            string queryEnc = Uri.EscapeDataString(keyword);
            string requestUrl = $"https://search.mcmod.cn/api/search?keyword={queryEnc}&type={mcbbsType}&page={_currentOffset / PageSize + 1}&size={PageSize}";

            string jsonText = await _httpClient.GetStringAsync(requestUrl);

            var jsonOption = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var response = JsonSerializer.Deserialize<MCBBSearchResponse>(jsonText, jsonOption);

            _totalHits = response?.Data?.Total ?? 0;

            if (response?.Data?.List != null && response.Data.List.Any())
            {
                foreach (var item in response.Data.List)
                {
                    var hit = new ModSearchHit
                    {
                        ProjectId = item.Id,
                        Title = item.Name,
                        Description = item.Description,
                        IconUrl = item.Icon,
                        Downloads = item.Downloads
                    };
                    lstModResult.Items.Add(hit);
                }
                txtStatusMsg.Text = $"找到 {_totalHits} 个中文结果，第 {_currentOffset / PageSize + 1} 页 (来自MC百科)";
            }
            else
            {
                txtStatusMsg.Text = "未查询到相关中文结果";
            }
        }
        catch (TaskCanceledException)
        {
            txtStatusMsg.Text = "请求超时！MC百科连接不稳定";
        }
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

        if (_searchSource == "mcbbs")
        {
            ShowMCBBDetail(modHit);
        }
        else
        {
            ShowVersionDetail(modHit);
        }
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

            var jsonOption = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _currentVersions = JsonSerializer.Deserialize<List<ModVersion>>(json, jsonOption) ?? new();

            BuildVersionTags();
            FilterVersionsByTag(_currentGameVer);
        }
        catch (Exception ex)
        {
            txtStatusMsg.Text = $"加载失败：{ex.Message}";
        }
    }

    private void ShowMCBBDetail(ModSearchHit modHit)
    {
        panelSearch.Visibility = Visibility.Collapsed;
        panelVersionDetail.Visibility = Visibility.Visible;
        panelDownloadConfirm.Visibility = Visibility.Collapsed;
        panelDownloading.Visibility = Visibility.Collapsed;

        panelVersionDetail.DataContext = new { SelectedMod = modHit };
        btnOpenExternal.Content = "访问 MC百科";

        _currentVersions.Clear();
        itemsVersionGroups.ItemsSource = null;

        panelVersionTags.Children.Clear();
        var btnHint = CreateTagButton("MC百科结果", false);
        btnHint.IsEnabled = false;
        panelVersionTags.Children.Add(btnHint);

        txtStatusMsg.Text = "此结果来自MC百科，请点击\"访问MC百科\"查看详情或切换到Modrinth搜索";
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
            btn.Background = new SolidColorBrush(Color.FromRgb(0, 122, 255));
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

        if (_searchSource == "mcbbs")
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"https://search.mcmod.cn/s?key={Uri.EscapeDataString(_selectedMod.Title)}",
                UseShellExecute = true
            });
        }
        else
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"https://modrinth.com/{_currentProjectType}/{_selectedMod.ProjectId}",
                UseShellExecute = true
            });
        }
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
        }
    }

    private async void BtnStartDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDownloadFile == null) return;

        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        panelDownloadConfirm.Visibility = Visibility.Collapsed;
        panelDownloading.Visibility = Visibility.Visible;

        string fullSavePath = Path.Combine(_downloadPath, _pendingDownloadFile.FileName);
        txtDownloadingFile.Text = _pendingDownloadFile.FileName;

        progressBarFill.Width = 0;
        txtDownloadPercent.Text = "0%";

        try
        {
            using var response = await _httpClient.GetAsync(
                _pendingDownloadFile.Url, HttpCompletionOption.ResponseHeadersRead, ct);
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

                if (totalBytes > 0)
                {
                    double percent = receivedBytes * 100.0 / totalBytes;
                    UpdateProgressBar(percent);
                    txtDownloadPercent.Text = $"{percent:F1}%";
                }
            }

            txtDownloadingFile.Text = $"下载完成！{_pendingDownloadFile.FileName}";
            txtDownloadPercent.Text = "100%";
            UpdateProgressBar(100);

            await Task.Delay(3000, ct);

            if (!ct.IsCancellationRequested)
            {
                panelDownloading.Visibility = Visibility.Collapsed;
                panelVersionDetail.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
            txtDownloadingFile.Text = "下载已取消";
            txtDownloadPercent.Text = "";

            if (File.Exists(fullSavePath))
            {
                try { File.Delete(fullSavePath); } catch { }
            }
        }
        catch (Exception ex)
        {
            txtDownloadingFile.Text = $"下载失败：{ex.Message}";
            txtDownloadPercent.Text = "";
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
