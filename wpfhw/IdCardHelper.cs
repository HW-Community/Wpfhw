using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace wpfhw;

/// <summary>
/// 身份证编码规则校验工具（GB 11643）。
/// 本工具无法接入公安人口库，只能校验号码编码规则，不能核验身份证真实存在或属于本人。
/// 归属地联网接口为免费第三方服务，可能限流、超时或随时失效，结果仅用于展示，不参与合法性判定。
/// 身份证原文只在内存中处理，不写入文件、日志或数据库。
/// </summary>
public static class IdCardHelper
{
    private const string RegionApiFormat = "https://geo.datav.aliyun.com/areas_v3/bound/{0}.json";
    private static readonly TimeSpan RegionApiTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ApiRequestTimeout = TimeSpan.FromSeconds(8);

    private static readonly Regex IdCardPattern = new(@"^\d{17}[\dX]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly int[] Weights = { 7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };
    private static readonly char[] CheckCodes = { '1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2' };

    // 启动时一次性加载。字典用于展示名称，HashSet 额外包含已撤销的历史区县代码。
    private static readonly Dictionary<string, string> codeToName = new(StringComparer.Ordinal);
    private static readonly HashSet<string> regionCodes = new(StringComparer.Ordinal);
    private static readonly object InitLock = new();
    private static bool _initialized;

    private static readonly HttpClient Http = CreateHttpClient();

    public static bool IsRegionDataLoaded { get; private set; }
    public static int RegionCount => codeToName.Count;

    /// <summary>在应用启动时调用，一次性加载本地区划码表。</summary>
    public static void Initialize()
    {
        if (Volatile.Read(ref _initialized)) return;

        lock (InitLock)
        {
            if (_initialized) return;
            LoadRegionData();
            IsRegionDataLoaded = true;
            Volatile.Write(ref _initialized, true);
        }
    }

    public static string? GetLocalRegionName(string regionCode)
    {
        EnsureInitialized();
        return codeToName.TryGetValue(regionCode, out string? name) ? name : null;
    }

    public static bool IsKnownRegionCode(string regionCode)
    {
        EnsureInitialized();
        return regionCodes.Contains(regionCode);
    }

    /// <summary>
    /// 按 正则 → GB 11643 校验码 → 出生日期 的顺序校验，并在本地无区划时异步查询展示名称。
    /// 区划联网结果不影响 IsValid。
    /// </summary>
    public static async Task<IdCardValidationResult> ValidateAsync(string? rawIdCard, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        string idCard = (rawIdCard ?? string.Empty).Trim().ToUpperInvariant();
        if (!IdCardPattern.IsMatch(idCard))
        {
            return new IdCardValidationResult
            {
                IsValid = false,
                FailureReason = "格式错误：请输入 18 位身份证号码（末位可为 X）",
                RegionStatus = IdCardRegionStatus.NotQueried
            };
        }

        string regionCode = idCard[..6];
        bool checksumValid = ValidateChecksum(idCard);
        bool dateValid = TryParseBirthDate(idCard, out DateTime birthDate);
        int? age = dateValid ? CalculateAge(birthDate, DateTime.Today) : null;
        string? localName = GetLocalRegionName(regionCode);

        var result = new IdCardValidationResult
        {
            IsValid = checksumValid && dateValid,
            FailureReason = !checksumValid
                ? "第 18 位校验码不符合 GB 11643 规则"
                : !dateValid ? "第 7~14 位出生日期不合法" : null,
            RegionCode = regionCode,
            RegionName = localName,
            RegionStatus = localName != null ? IdCardRegionStatus.LocalMatch : IdCardRegionStatus.NotQueried,
            BirthDate = dateValid ? birthDate : null,
            Age = age,
            Gender = (idCard[16] - '0') % 2 == 1 ? "男" : "女",
            IsAdult = age is null ? null : age >= 18
        };

        // 校验未通过时不发起网络请求，避免无意义的等待。
        if (!result.IsValid || localName != null)
            return result;

        string? onlineName = await QueryRegionNameAsync(regionCode, cancellationToken).ConfigureAwait(false);
        result.RegionName = onlineName;
        result.RegionStatus = onlineName != null ? IdCardRegionStatus.OnlineMatch : IdCardRegionStatus.OnlineFailed;
        return result;
    }

    private static void EnsureInitialized()
    {
        if (!Volatile.Read(ref _initialized))
            Initialize();
    }

    private static void LoadRegionData()
    {
        try
        {
            using Stream? stream = OpenRegionDataStream();
            if (stream == null) return;

            var entries = JsonSerializer.Deserialize<List<AreaCodeEntry>>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (entries == null) return;

            foreach (AreaCodeEntry entry in entries)
            {
                if (entry.Code.Length != 6 || !entry.Code.All(c => c is >= '0' and <= '9') || string.IsNullOrWhiteSpace(entry.Name))
                    continue;

                codeToName[entry.Code] = entry.Name.Trim();
                regionCodes.Add(entry.Code);
            }
        }
        catch
        {
            // 本地数据损坏或缺失时保持空表，后续按“本地无此区划”处理。
        }
    }

    private static Stream? OpenRegionDataStream()
    {
        string localPath = Path.Combine(AppContext.BaseDirectory, "Data", "area_codes.json");
        if (File.Exists(localPath))
            return File.OpenRead(localPath);

        Assembly assembly = typeof(IdCardHelper).Assembly;
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("area_codes.json", StringComparison.OrdinalIgnoreCase));
        return resourceName == null ? null : assembly.GetManifestResourceStream(resourceName);
    }

    private static bool ValidateChecksum(string idCard)
    {
        int sum = 0;
        for (int i = 0; i < 17; i++)
            sum += (idCard[i] - '0') * Weights[i];
        return idCard[17] == CheckCodes[sum % 11];
    }

    private static bool TryParseBirthDate(string idCard, out DateTime birthDate)
    {
        bool parsed = DateTime.TryParseExact(
            idCard.Substring(6, 8),
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out birthDate);

        if (!parsed) return false;

        birthDate = birthDate.Date;
        return birthDate <= DateTime.Today;
    }

    private static int CalculateAge(DateTime birthDate, DateTime today)
    {
        int age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age)) age--;
        return age;
    }

    /// <summary>联网查询只传 6 位区划码，绝不发送完整身份证号。</summary>
    private static async Task<string?> QueryRegionNameAsync(string regionCode, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RegionApiTimeout);

        try
        {
            string url = string.Format(CultureInfo.InvariantCulture, RegionApiFormat, regionCode);
            using HttpResponseMessage response = await Http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests || !response.IsSuccessStatusCode)
                return null;

            await using Stream stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: timeoutCts.Token).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("features", out JsonElement features) ||
                features.ValueKind != JsonValueKind.Array ||
                features.GetArrayLength() == 0)
                return null;

            JsonElement properties = features[0].GetProperty("properties");
            if (!properties.TryGetProperty("name", out JsonElement nameElement))
                return null;

            string? name = nameElement.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (OperationCanceledException)
        {
            // 超时或调用方取消。
            return null;
        }
        catch (HttpRequestException)
        {
            // 断网、DNS 失败、接口报错等。
            return null;
        }
        catch (JsonException)
        {
            // 接口返回内容不是预期 JSON。
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = ApiRequestTimeout
        };
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Wpfhw-IdCardValidator/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private sealed class AreaCodeEntry
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}

public enum IdCardRegionStatus
{
    NotQueried,
    LocalMatch,
    OnlineMatch,
    OnlineFailed
}

public sealed class IdCardValidationResult
{
    public bool IsValid { get; set; }
    public string? FailureReason { get; set; }
    public string RegionCode { get; set; } = string.Empty;
    public string? RegionName { get; set; }
    public IdCardRegionStatus RegionStatus { get; set; }
    public DateTime? BirthDate { get; set; }
    public int? Age { get; set; }
    public string Gender { get; set; } = string.Empty;
    public bool? IsAdult { get; set; }

    public string RegionStatusText => RegionStatus switch
    {
        IdCardRegionStatus.LocalMatch => "本地库匹配",
        IdCardRegionStatus.OnlineMatch => "本地无，联网查到",
        IdCardRegionStatus.OnlineFailed => "本地无，联网失败",
        _ => "校验未通过，未联网查询"
    };

    public string? RegionStatusDetail => RegionStatus switch
    {
        IdCardRegionStatus.OnlineFailed => "本地库无此区划，联网查询失败",
        _ => null
    };
}
