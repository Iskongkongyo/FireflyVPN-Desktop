namespace ServiceLib.Services;

/// <summary>
/// Synchronizes FireflyVPN desktop metadata from the public Cloudflare Worker
/// endpoint: GET /api/client.
/// </summary>
public class FireflyWorkerService
{
    private const string Tag = "FireflyWorker";

    public async Task<FireflySyncResult> SyncAsync(Config config, bool useProxy)
    {
        var defaultedApiUrl = config.ConstItem.FireflyApiUrl.TrimEx().IsNullOrEmpty();
        var apiUrl = defaultedApiUrl
            ? Global.FireflyClientApiUrl
            : config.ConstItem.FireflyApiUrl.TrimEx();
        if (defaultedApiUrl)
        {
            config.ConstItem.FireflyApiUrl = apiUrl;
        }

        if (!TryGetHttpsUri(apiUrl, out _))
        {
            throw new ArgumentException("Firefly Worker API must be an HTTPS URL.");
        }

        var state = await FireflyNodeRequestRetryPolicy.ExecuteAsync(() => DownloadClientStateAsync(apiUrl, useProxy));
        if (state is null)
        {
            return FireflySyncResult.Disabled;
        }

        var subscriptionIds = await SyncSubscriptions(config, state);
        var configChanged = defaultedApiUrl | ApplyNotice(config, state.Notice);
        configChanged |= ApplyUpdate(state.AppUpdatePc);

        if (configChanged && await ConfigHandler.SaveConfig(config) != 0)
        {
            Logging.SaveLog($"{Tag}: Failed to save Firefly Worker state.");
        }

        return new FireflySyncResult(true, subscriptionIds);
    }

    /// <summary>
    /// Reads the desktop-specific Worker update field without syncing
    /// subscriptions or emitting unrelated notices.
    /// </summary>
    public async Task<FireflyAppUpdate?> GetDesktopUpdateAsync(Config config, bool useProxy)
    {
        var apiUrl = config.ConstItem.FireflyApiUrl.TrimEx();
        if (apiUrl.IsNullOrEmpty())
        {
            apiUrl = Global.FireflyClientApiUrl;
        }

        if (!TryGetHttpsUri(apiUrl, out _))
        {
            throw new ArgumentException("Firefly Worker API must be an HTTPS URL.");
        }

        var update = (await DownloadClientStateAsync(apiUrl, useProxy)).AppUpdatePc;
        return update is not null
               && update.VersionCode > Global.FireflyVersionCode
               && TryGetHttpsUri(update.DownloadUrl, out _)
            ? update
            : null;
    }

    public static string FormatDesktopUpdateMessage(FireflyAppUpdate update)
    {
        var version = update.Version.TrimEx().IsNotEmpty() ? update.Version.TrimEx() : update.VersionCode.ToString();
        return string.Format(ResUI.TbFireflyUpdateAvailable, version, ToPlainText(update.Changelog), update.DownloadUrl).Trim();
    }

    public static FireflyUpdateNotification CreateUpdateNotification(FireflyAppUpdate update)
    {
        var version = update.Version.TrimEx().IsNotEmpty() ? update.Version.TrimEx() : update.VersionCode.ToString();
        return new FireflyUpdateNotification(version, ToPlainText(update.Changelog), update.DownloadUrl);
    }

    private static async Task<FireflyClientState> DownloadClientStateAsync(string apiUrl, bool useProxy)
    {
        var downloadService = new DownloadService();
        var content = await downloadService.TryDownloadString(apiUrl, useProxy, Global.AppName);
        return JsonUtils.Deserialize<FireflyClientState>(content)
               ?? throw new InvalidOperationException("Firefly Worker returned an invalid client response.");
    }

    private static async Task<List<string>> SyncSubscriptions(Config config, FireflyClientState state)
    {
        var existingItems = await AppManager.Instance.SubItems() ?? [];
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var syncedIds = new List<string>();

        foreach (var source in state.Subscriptions ?? [])
        {
            var sourceId = source.Id.TrimEx();
            var sourceUrl = source.Url.TrimEx();
            if (sourceId.IsNullOrEmpty() || !TryGetHttpsUri(sourceUrl, out _))
            {
                continue;
            }

            sourceIds.Add(sourceId);
            var memo = FireflyManagedSubscriptionPolicy.ManagedSubscriptionMemoPrefix + sourceId;
            var existing = existingItems.FirstOrDefault(item => item.Memo == memo);
            var subItem = existing ?? new SubItem
            {
                Id = string.Empty,
                UserAgent = Global.AppName,
                CustomCoreType = ECoreType.sing_box,
            };

            subItem.Remarks = source.Name.TrimEx().IsNotEmpty()
                ? source.Name.TrimEx()
                : $"Firefly {sourceId}";
            subItem.Url = sourceUrl;
            subItem.Enabled = source.Enabled;
            subItem.MoreUrl = string.Empty;
            subItem.ConvertTarget = string.Empty;
            subItem.Memo = memo;

            if (await ConfigHandler.AddSubItem(config, subItem) != 0)
            {
                throw new InvalidOperationException($"Failed to save Firefly subscription {sourceId}.");
            }
            syncedIds.Add(subItem.Id);
        }

        // Keep removed Worker subscriptions and their already-imported nodes, but disable them.
        foreach (var item in existingItems.Where(FireflyManagedSubscriptionPolicy.IsManagedSubscription))
        {
            var sourceId = item.Memo![FireflyManagedSubscriptionPolicy.ManagedSubscriptionMemoPrefix.Length..];
            if (sourceIds.Contains(sourceId) || item.Enabled == false)
            {
                continue;
            }

            item.Enabled = false;
            await ConfigHandler.AddSubItem(config, item);
        }

        return syncedIds;
    }

    private static bool ApplyNotice(Config config, FireflyNotice? notice)
    {
        if (notice?.HasNotice != true || notice.Content.TrimEx().IsNullOrEmpty())
        {
            return false;
        }

        var noticeId = notice.NoticeId.TrimEx();
        if (notice.ShowOnce && noticeId.IsNotEmpty() && noticeId == config.ConstItem.FireflyLastNoticeId)
        {
            return false;
        }

        var title = notice.Title.TrimEx();
        var content = ToPlainText(notice.Content);
        NoticeManager.Instance.SendMessageAndEnqueue($"{title}{(title.IsNotEmpty() ? Environment.NewLine : string.Empty)}{content}");

        if (noticeId.IsNotEmpty())
        {
            config.ConstItem.FireflyLastNoticeId = noticeId;
            return true;
        }
        return false;
    }

    private static bool ApplyUpdate(FireflyAppUpdate? update)
    {
        if (update is null || update.VersionCode <= Global.FireflyVersionCode || !TryGetHttpsUri(update.DownloadUrl, out _))
        {
            return false;
        }

        NoticeManager.Instance.NotifyFireflyUpdate(update);
        return false;
    }

    private static bool TryGetHttpsUri(string? value, out Uri? uri)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out uri)
               && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToPlainText(string? value)
    {
        var withoutTags = Regex.Replace(value ?? string.Empty, "<[^>]*>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"\s{2,}", " ").Trim();
    }
}

public sealed record FireflySyncResult(bool Enabled, IReadOnlyList<string> SubscriptionIds)
{
    public static FireflySyncResult Disabled { get; } = new(false, []);
}

public class FireflyClientState
{
    public List<FireflySubscription>? Subscriptions { get; set; }
    public FireflyNotice? Notice { get; set; }
    public FireflyAppUpdate? AppUpdate { get; set; }

    [JsonPropertyName("appUpdate_pc")]
    public FireflyAppUpdate? AppUpdatePc { get; set; }
    public FireflySettings? Settings { get; set; }
}

public class FireflySubscription
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool Encrypted { get; set; }
    public string Url { get; set; } = string.Empty;
}

public class FireflyNotice
{
    public bool HasNotice { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string NoticeId { get; set; } = string.Empty;
    public bool ShowOnce { get; set; } = true;
}

public class FireflyAppUpdate
{
    public string Version { get; set; } = string.Empty;
    public int VersionCode { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string Changelog { get; set; } = string.Empty;

    [JsonPropertyName("is_force")]
    public int IsForce { get; set; }
}

public class FireflySettings
{
    public string WebsiteUrl { get; set; } = string.Empty;
    public string FeedbackEmail { get; set; } = string.Empty;
    public string FeedbackUrl { get; set; } = string.Empty;
    public string GithubUrl { get; set; } = string.Empty;
    public int NodeRequestTimeoutMs { get; set; }
}
