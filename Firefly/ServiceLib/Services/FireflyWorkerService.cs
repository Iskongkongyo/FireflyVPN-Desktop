using System.Net.Http.Headers;
using System.Net.Mime;

namespace ServiceLib.Services;

/// <summary>
/// Connects the desktop application to the anonymous-device Firefly Edge API.
/// </summary>
public class FireflyWorkerService
{
    private const string Tag = "FireflyWorker";

    public async Task<FireflySyncResult> SyncAsync(Config config, bool useProxy)
    {
        var defaultedApiUrl = config.ConstItem.FireflyApiUrl.TrimEx().IsNullOrEmpty();
        var configuredUrl = defaultedApiUrl
            ? Global.FireflyClientApiUrl
            : config.ConstItem.FireflyApiUrl.TrimEx();
        var apiBaseUri = GetApiBaseUri(configuredUrl);
        var normalizedApiUrl = apiBaseUri.AbsoluteUri.TrimEnd('/');
        var apiUrlChanged = config.ConstItem.FireflyApiUrl != normalizedApiUrl;

        if (apiUrlChanged)
        {
            config.ConstItem.FireflyApiUrl = normalizedApiUrl;
        }

        FireflyBootstrap? bootstrap;
        try
        {
            bootstrap = await FireflyNodeRequestRetryPolicy.ExecuteAsync(
                () => GetBootstrapAsync(apiBaseUri, useProxy));
        }
        catch (FireflyApiException exception)
        {
            return HandleDeviceApiFailure(exception);
        }
        if (bootstrap is null)
        {
            return FireflySyncResult.Disabled;
        }
        ValidateBootstrap(bootstrap);

        FireflyDeviceIdentity? identity;
        try
        {
            identity = await FireflyNodeRequestRetryPolicy.ExecuteAsync(
                () => EnrollCurrentDeviceAsync(apiBaseUri, useProxy));
        }
        catch (FireflyApiException exception) when (IsAccessBannedErrorCode(exception.ErrorCode))
        {
            return await HandleAccessBannedAsync(config, bootstrap, apiUrlChanged, exception);
        }
        catch (FireflyApiException exception)
        {
            return HandleDeviceApiFailure(exception);
        }
        if (identity is null)
        {
            return FireflySyncResult.Disabled;
        }

        List<FireflySubscription>? subscriptions;
        try
        {
            subscriptions = await FireflyNodeRequestRetryPolicy.ExecuteAsync(
                () => GetSubscriptionsAsync(apiBaseUri, identity, useProxy));
        }
        catch (FireflyApiException exception) when (IsAccessBannedErrorCode(exception.ErrorCode))
        {
            return await HandleAccessBannedAsync(config, bootstrap, apiUrlChanged, exception);
        }
        catch (FireflyApiException exception)
        {
            return HandleDeviceApiFailure(exception);
        }
        if (subscriptions is null)
        {
            return FireflySyncResult.Disabled;
        }

        var subscriptionSync = await SyncSubscriptions(config, apiBaseUri, subscriptions);
        var configChanged = apiUrlChanged
            | subscriptionSync.CatalogUpdate.StateChanged
            | ApplyNotice(config, bootstrap.Notice, out var notice);
        var appUpdate = SelectEligibleUpdate(bootstrap.PcAppUpdate ?? bootstrap.AppUpdate);

        if (configChanged && await ConfigHandler.SaveConfig(config) != 0)
        {
            Logging.SaveLog($"{Tag}: Failed to save Firefly Worker state.");
        }

        if (subscriptionSync.CatalogUpdate.NewGroupNames.Count > 0)
        {
            NoticeManager.Instance.SendMessageAndEnqueue(
                FormatNewSubscriptionGroupsMessage(subscriptionSync.CatalogUpdate.NewGroupNames));
        }

        return new FireflySyncResult(true, subscriptionSync.SubscriptionIds, appUpdate, notice);
    }

    /// <summary>
    /// Reads update metadata without registering a device or fetching a subscription.
    /// </summary>
    public async Task<FireflyAppUpdate?> GetDesktopUpdateAsync(Config config, bool useProxy)
    {
        var configuredUrl = config.ConstItem.FireflyApiUrl.TrimEx().IsNotEmpty()
            ? config.ConstItem.FireflyApiUrl.TrimEx()
            : Global.FireflyClientApiUrl;
        var bootstrap = await GetBootstrapAsync(GetApiBaseUri(configuredUrl), useProxy);
        ValidateBootstrap(bootstrap);
        return SelectEligibleUpdate(bootstrap.PcAppUpdate ?? bootstrap.AppUpdate);
    }

    /// <summary>
    /// Downloads and authenticates one Crypto V2 subscription response. A fresh
    /// challenge is generated for every retry because challenges are one-shot.
    /// </summary>
    public async Task<string> DownloadSubscriptionAsync(
        Config config,
        string subscriptionId,
        bool useProxy,
        string? groupName = null,
        bool deferFailureNotification = false)
    {
        var apiBaseUri = GetApiBaseUri(config.ConstItem.FireflyApiUrl.TrimEx().IsNotEmpty()
            ? config.ConstItem.FireflyApiUrl.TrimEx()
            : Global.FireflyClientApiUrl);
        var identity = await FireflyDeviceIdentityStore.LoadAsync()
            ?? await EnrollCurrentDeviceAsync(apiBaseUri, useProxy);

        var content = await FireflyNodeRequestRetryPolicy.ExecuteAsync(
            async () =>
            {
                var challenge = FireflyCryptoV2.CreateChallenge();
                var envelope = await GetSubscriptionEnvelopeAsync(
                    apiBaseUri, subscriptionId, identity, challenge, useProxy);
                return FireflyCryptoV2.Decrypt(envelope, identity, subscriptionId, challenge);
            },
            groupName,
            !deferFailureNotification);

        return content
            ?? throw new InvalidOperationException("Unable to retrieve the Firefly subscription.");
    }

    public async Task<FireflyUsageReportResult> ReportUsageAsync(
        Config config,
        string sessionId,
        long uploadBytes,
        long downloadBytes,
        bool useProxy = false)
    {
        if (uploadBytes < 0 || downloadBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(uploadBytes));
        }

        var apiBaseUri = GetApiBaseUri(config.ConstItem.FireflyApiUrl.TrimEx().IsNotEmpty()
            ? config.ConstItem.FireflyApiUrl.TrimEx()
            : Global.FireflyClientApiUrl);
        var identity = await FireflyDeviceIdentityStore.LoadAsync()
            ?? throw new InvalidOperationException("The Firefly device has not been enrolled.");
        return await SendWrappedAsync<FireflyUsageReportResult>(
            apiBaseUri,
            "api/v2/usage/report",
            HttpMethod.Post,
            useProxy,
            identity,
            new { sessionId, uploadBytes, downloadBytes });
    }

    public static string FormatDesktopUpdateMessage(FireflyAppUpdate update)
    {
        var version = update.VersionName.TrimEx().IsNotEmpty()
            ? update.VersionName.TrimEx()
            : update.VersionCode.ToString();
        return string.Format(ResUI.TbFireflyUpdateAvailable,
            version, Environment.NewLine, ToPlainText(update.Changelog), update.DownloadUrl).Trim();
    }

    public static FireflyUpdateNotification CreateUpdateNotification(FireflyAppUpdate update)
    {
        var version = update.VersionName.TrimEx().IsNotEmpty()
            ? update.VersionName.TrimEx()
            : update.VersionCode.ToString();
        return new FireflyUpdateNotification(version, update.Changelog.TrimEx(), update.DownloadUrl, update.Force);
    }

    public static FireflyNoticeNotification CreateNoticeNotification(FireflyNotice notice)
    {
        return new FireflyNoticeNotification(notice.Title.TrimEx(), notice.Content.TrimEx());
    }

    public static FireflyAccessBannedNotification CreateAccessBannedNotification()
    {
        return new FireflyAccessBannedNotification(
            "抱歉，您已被管理员封禁！当前无法获取节点等信息，软件其他功能则不受影响！");
    }

    internal static bool IsAccessBannedErrorCode(string? errorCode)
    {
        return errorCode is "account_banned" or "device_banned";
    }

    public static Uri GetApiBaseUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host.IsNullOrEmpty()
            || uri.UserInfo.IsNotEmpty()
            || uri.Query.IsNotEmpty()
            || uri.Fragment.IsNotEmpty())
        {
            throw new ArgumentException("Firefly Worker API must be an HTTPS URL.");
        }

        var absolute = uri.AbsoluteUri.TrimEnd('/');
        foreach (var obsoleteSuffix in new[] { "/api/client", "/api/v2/bootstrap" })
        {
            if (absolute.EndsWith(obsoleteSuffix, StringComparison.OrdinalIgnoreCase))
            {
                absolute = absolute[..^obsoleteSuffix.Length];
                break;
            }
        }
        return new Uri(absolute.TrimEnd('/') + "/", UriKind.Absolute);
    }

    private static async Task<FireflyBootstrap> GetBootstrapAsync(Uri apiBaseUri, bool useProxy)
    {
        return await SendWrappedAsync<FireflyBootstrap>(
            apiBaseUri, "api/v2/bootstrap", HttpMethod.Get, useProxy);
    }

    private static async Task<FireflyDeviceIdentity> EnrollCurrentDeviceAsync(
        Uri apiBaseUri,
        bool useProxy,
        bool allowCredentialRecovery = true)
    {
        var deviceMaterial = FireflyDeviceIdentityProvider.Collect();
        FireflyDeviceIdentity? identity;
        try
        {
            identity = await FireflyDeviceIdentityStore.LoadAsync();
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            // A restored config database cannot open another machine's DPAPI or
            // Keychain payload. Start a new anonymous identity in that case.
            Logging.SaveLog($"{Tag}: Replacing an unreadable local device credential.", exception);
            await FireflyDeviceIdentityStore.ResetAsync();
            identity = null;
        }
        if (identity is not null && identity.IdentityHash != deviceMaterial.IdentityHash)
        {
            // A changed hardware identity must not keep using credentials bound
            // to the previous device.
            await FireflyDeviceIdentityStore.ResetAsync();
            identity = null;
        }
        if (identity is null || identity.DeviceToken.IsNullOrEmpty())
        {
            var (PublicKeySpki, PrivateKeyPkcs8) = FireflyCryptoV2.CreateKeyPair();
            identity = new FireflyDeviceIdentity
            {
                IdentityHash = deviceMaterial.IdentityHash,
                PublicKeySpki = PublicKeySpki,
                PrivateKeyPkcs8 = PrivateKeyPkcs8,
            };
        }

        var deviceName = Environment.MachineName.TrimEx();
        if (deviceName.Length > 64)
        {
            deviceName = deviceName[..64];
        }

        FireflyEnrollmentResult result;
        try
        {
            result = await SendWrappedAsync<FireflyEnrollmentResult>(
                apiBaseUri,
                "api/v2/devices/enroll",
                HttpMethod.Post,
                useProxy,
                identity.DeviceToken.IsNotEmpty() ? identity : null,
                new
                {
                    deviceId = deviceMaterial.IdentityHash,
                    platform = deviceMaterial.Platform,
                    deviceName,
                    publicKey = identity.PublicKeySpki,
                    cryptoVersion = FireflyCryptoV2.Version,
                },
                includeDeviceIdHeader: false);
        }
        catch (FireflyApiException exception) when (
            allowCredentialRecovery
            && exception.ErrorCode == "unauthorized"
            && identity.DeviceToken.IsNotEmpty())
        {
            // A server-side credential reset leaves this installation with an
            // unusable token. Generate a fresh key so the reinstall/rebind path
            // can atomically replace both the key and token once.
            await FireflyDeviceIdentityStore.ResetAsync();
            return await EnrollCurrentDeviceAsync(apiBaseUri, useProxy, false);
        }

        if (!Regex.IsMatch(result.DeviceId, "^[a-f0-9]{64}$")
            || result.CryptoVersion != FireflyCryptoV2.Version
            || (!result.AlreadyEnrolled && result.DeviceToken.IsNullOrEmpty()))
        {
            throw new InvalidOperationException("The Worker returned an invalid enrollment response.");
        }

        identity.AccountId = result.AccountId;
        identity.DeviceId = result.DeviceId;
        if (result.DeviceToken.IsNotEmpty())
        {
            identity.DeviceToken = result.DeviceToken;
        }
        if (identity.DeviceToken.IsNullOrEmpty())
        {
            throw new InvalidOperationException("The Worker did not return a device token.");
        }
        await FireflyDeviceIdentityStore.SaveAsync(identity);
        return identity;
    }

    private static async Task<List<FireflySubscription>> GetSubscriptionsAsync(
        Uri apiBaseUri,
        FireflyDeviceIdentity identity,
        bool useProxy)
    {
        return await SendWrappedAsync<List<FireflySubscription>>(
            apiBaseUri, "api/v2/subscriptions", HttpMethod.Get, useProxy, identity);
    }

    private static async Task<FireflyCryptoV2Envelope> GetSubscriptionEnvelopeAsync(
        Uri apiBaseUri,
        string subscriptionId,
        FireflyDeviceIdentity identity,
        string challenge,
        bool useProxy)
    {
        if (!Regex.IsMatch(subscriptionId, "^[a-z0-9][a-z0-9_-]{0,63}$"))
        {
            throw new ArgumentException("Invalid Firefly subscription identifier.", nameof(subscriptionId));
        }

        var headers = new Dictionary<string, string>
        {
            ["X-Firefly-Crypto-Version"] = FireflyCryptoV2.Version.ToString(),
            ["X-Firefly-Challenge"] = challenge,
        };
        return await SendRawAsync<FireflyCryptoV2Envelope>(
            apiBaseUri,
            $"api/v2/subscriptions/{Uri.EscapeDataString(subscriptionId)}/content",
            HttpMethod.Get,
            useProxy,
            identity,
            null,
            headers);
    }

    private static async Task<T> SendWrappedAsync<T>(
        Uri apiBaseUri,
        string path,
        HttpMethod method,
        bool useProxy,
        FireflyDeviceIdentity? identity = null,
        object? body = null,
        IReadOnlyDictionary<string, string>? headers = null,
        bool includeDeviceIdHeader = true)
    {
        var response = await SendRawAsync<FireflyApiResponse<T>>(
            apiBaseUri, path, method, useProxy, identity, body, headers, includeDeviceIdHeader);
        if (!response.Ok || response.Data is null)
        {
            throw new InvalidOperationException("The Worker returned an invalid API response.");
        }
        return response.Data;
    }

    private static async Task<T> SendRawAsync<T>(
        Uri apiBaseUri,
        string path,
        HttpMethod method,
        bool useProxy,
        FireflyDeviceIdentity? identity = null,
        object? body = null,
        IReadOnlyDictionary<string, string>? headers = null,
        bool includeDeviceIdHeader = true)
    {
        async Task<T> SendOnce(bool proxy)
        {
            using var client = await CreateHttpClientAsync(proxy);
            using var request = new HttpRequestMessage(method, new Uri(apiBaseUri, path));
            request.Headers.UserAgent.TryParseAdd(Global.AppName);
            if (identity is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.DeviceToken);
                if (includeDeviceIdHeader)
                {
                    request.Headers.TryAddWithoutValidation("X-Firefly-Device-ID", identity.DeviceId);
                }
            }
            if (headers is not null)
            {
                foreach (var header in headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            if (body is not null)
            {
                request.Content = new StringContent(
                    JsonUtils.Serialize(body, false), Encoding.UTF8, MediaTypeNames.Application.Json);
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var failure = JsonUtils.Deserialize<FireflyApiFailure>(content);
                throw new FireflyApiException(
                    failure?.Error.TrimEx().IsNotEmpty() == true
                        ? failure.Error.TrimEx()
                        : ((int)response.StatusCode).ToString(),
                    response.StatusCode);
            }

            return JsonUtils.Deserialize<T>(content)
                ?? throw new InvalidOperationException("The Worker returned invalid JSON.");
        }

        if (!useProxy)
        {
            return await SendOnce(false);
        }

        try
        {
            return await SendOnce(true);
        }
        catch (HttpRequestException exception) when (
            exception.StatusCode is null || (int)exception.StatusCode >= 500)
        {
            return await SendOnce(false);
        }
        catch (TaskCanceledException)
        {
            return await SendOnce(false);
        }
    }

    private static async Task<HttpClient> CreateHttpClientAsync(bool useProxy)
    {
        var webProxy = await new DownloadService().GetWebProxy(useProxy);
        var handler = new SocketsHttpHandler
        {
            Proxy = webProxy,
            UseProxy = webProxy is not null,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };
        var certificateChainPolicy = CertPemManager.Instance.BuildCertificateChainPolicy();
        if (certificateChainPolicy is not null)
        {
            handler.SslOptions.CertificateChainPolicy = certificateChainPolicy;
            handler.SslOptions.RemoteCertificateValidationCallback = null;
        }
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20),
            MaxResponseContentBufferSize = 4 * 1024 * 1024,
        };
    }

    private static async Task<FireflySubscriptionSyncResult> SyncSubscriptions(
        Config config,
        Uri apiBaseUri,
        IEnumerable<FireflySubscription> subscriptions)
    {
        var existingItems = await AppManager.Instance.SubItems() ?? [];
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var syncedIds = new List<string>();
        var syncedSources = new List<FireflySubscription>();

        foreach (var source in subscriptions)
        {
            var sourceId = source.Id.TrimEx();
            if (!Regex.IsMatch(sourceId, "^[a-z0-9][a-z0-9_-]{0,63}$")
                || source.CryptoVersion != FireflyCryptoV2.Version
                || source.Algorithm != FireflyCryptoV2.Algorithm
                || !Uri.TryCreate(apiBaseUri, source.ContentUrl, out var sourceUri)
                || sourceUri.Scheme != Uri.UriSchemeHttps
                || !sourceUri.Authority.Equals(apiBaseUri.Authority, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!sourceIds.Add(sourceId))
            {
                continue;
            }
            var memo = FireflyManagedSubscriptionPolicy.ManagedSubscriptionMemoPrefix + sourceId;
            var existing = existingItems.FirstOrDefault(item => item.Memo == memo);
            var subItem = existing ?? new SubItem
            {
                Id = string.Empty,
                UserAgent = Global.AppName,
            };

            subItem.Remarks = source.Name.TrimEx().IsNotEmpty()
                ? source.Name.TrimEx()
                : $"Firefly {sourceId}";
            subItem.Url = sourceUri.AbsoluteUri;
            subItem.Enabled = true;
            subItem.MoreUrl = string.Empty;
            subItem.ConvertTarget = string.Empty;
            subItem.Memo = memo;
            // Worker content may be a URI list, Clash YAML, Xray JSON, or
            // sing-box JSON. Keep parser auto-detection enabled for both new
            // and previously provisioned groups.
            subItem.CustomCoreType = null;

            if (await ConfigHandler.AddSubItem(config, subItem) != 0)
            {
                throw new InvalidOperationException($"Failed to save Firefly subscription {sourceId}.");
            }
            syncedIds.Add(subItem.Id);
            syncedSources.Add(source);
        }

        foreach (var item in FindObsoleteManagedSubscriptions(existingItems, sourceIds))
        {
            // The Worker catalog is authoritative for managed groups. Removing
            // the local subscription also removes its stale imported nodes and
            // repairs SubIndexId when the deleted group was selected.
            await ConfigHandler.DeleteSubItem(config, item.Id);
        }

        var catalogUpdate = TrackSubscriptionCatalog(config.ConstItem, syncedSources);
        return new FireflySubscriptionSyncResult(syncedIds, catalogUpdate);
    }

    internal static FireflySubscriptionCatalogUpdate TrackSubscriptionCatalog(
        ConstItem constItem,
        IEnumerable<FireflySubscription> subscriptions)
    {
        var currentSources = subscriptions
            .Select(source => new
            {
                Id = source.Id.TrimEx(),
                Name = source.Name.TrimEx(),
            })
            .Where(source => source.Id.IsNotEmpty())
            .GroupBy(source => source.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var currentIds = currentSources
            .Select(source => source.Id)
            .ToHashSet(StringComparer.Ordinal);
        var knownIds = (constItem.FireflyKnownSubscriptionIds ?? [])
            .Where(id => id.IsNotEmpty())
            .ToHashSet(StringComparer.Ordinal);
        var previousUnreadIds = (constItem.FireflyUnreadSubscriptionIds ?? [])
            .Where(id => id.IsNotEmpty())
            .ToHashSet(StringComparer.Ordinal);

        var newSources = constItem.FireflySubscriptionCatalogInitialized
            ? currentSources.Where(source => !knownIds.Contains(source.Id)).ToList()
            : [];
        var unreadIds = constItem.FireflySubscriptionCatalogInitialized
            ? previousUnreadIds
                .Where(currentIds.Contains)
                .Concat(newSources.Select(source => source.Id))
                .ToHashSet(StringComparer.Ordinal)
            : [];
        var stateChanged = !constItem.FireflySubscriptionCatalogInitialized
            || !knownIds.SetEquals(currentIds)
            || !previousUnreadIds.SetEquals(unreadIds);

        constItem.FireflySubscriptionCatalogInitialized = true;
        constItem.FireflyKnownSubscriptionIds = currentIds.Order(StringComparer.Ordinal).ToList();
        constItem.FireflyUnreadSubscriptionIds = unreadIds.Order(StringComparer.Ordinal).ToList();

        var newGroupNames = newSources
            .Select(source => source.Name.IsNotEmpty() ? source.Name : $"Firefly {source.Id}")
            .ToList();
        return new FireflySubscriptionCatalogUpdate(newGroupNames, stateChanged);
    }

    internal static string FormatNewSubscriptionGroupsMessage(IEnumerable<string> groupNames)
    {
        var names = groupNames
            .Select(name => name.TrimEx())
            .Where(name => name.IsNotEmpty())
            .Distinct(StringComparer.Ordinal);
        return $"新增：{string.Join('、', names)}分组";
    }

    internal static List<SubItem> FindObsoleteManagedSubscriptions(
        IEnumerable<SubItem> existingItems,
        ISet<string> currentSourceIds)
    {
        return existingItems
            .Where(FireflyManagedSubscriptionPolicy.IsManagedSubscription)
            .Where(item => !currentSourceIds.Contains(
                item.Memo![FireflyManagedSubscriptionPolicy.ManagedSubscriptionMemoPrefix.Length..]))
            .ToList();
    }

    private static async Task<FireflySyncResult> HandleAccessBannedAsync(
        Config config,
        FireflyBootstrap bootstrap,
        bool apiUrlChanged,
        FireflyApiException exception)
    {
        Logging.SaveLog($"{Tag}: Access denied by Worker ({exception.ErrorCode}); removing managed subscriptions.");

        var existingItems = await AppManager.Instance.SubItems() ?? [];
        var managedItems = FindObsoleteManagedSubscriptions(
            existingItems,
            new HashSet<string>(StringComparer.Ordinal));
        foreach (var item in managedItems)
        {
            await ConfigHandler.DeleteSubItem(config, item.Id);
        }

        var configChanged = apiUrlChanged | ApplyNotice(config, bootstrap.Notice, out var notice);
        if ((configChanged || managedItems.Count > 0) && await ConfigHandler.SaveConfig(config) != 0)
        {
            Logging.SaveLog($"{Tag}: Failed to save state after managed subscriptions were removed.");
        }

        return new FireflySyncResult(
            false,
            [],
            SelectEligibleUpdate(bootstrap.PcAppUpdate ?? bootstrap.AppUpdate),
            notice,
            true,
            true);
    }

    private static FireflySyncResult HandleDeviceApiFailure(FireflyApiException exception)
    {
        Logging.SaveLog($"{Tag}: Device API rejected the request ({exception.ErrorCode}).", exception);
        var message = exception.ErrorCode switch
        {
            "device_conflict" => "设备凭据与服务器记录冲突，请稍后重试或联系管理员重置设备凭据。",
            "device_revoked" => "当前设备凭据已被撤销，暂时无法获取节点信息。",
            "account_deleted" => "当前设备所属账号已被注销，暂时无法获取节点信息。",
            "unauthorized" => "设备凭据验证失败，请重新启动软件后重试。",
            _ => $"设备注册或鉴权失败（{exception.ErrorCode}），请稍后重试。",
        };
        NoticeManager.Instance.SendMessageAndEnqueue(message);
        return FireflySyncResult.Disabled;
    }

    private static void ValidateBootstrap(FireflyBootstrap bootstrap)
    {
        if (bootstrap.Crypto?.Version != FireflyCryptoV2.Version
            || bootstrap.Crypto.Algorithm != FireflyCryptoV2.Algorithm)
        {
            throw new InvalidOperationException("The Worker does not support the required Crypto V2 protocol.");
        }
    }

    private static bool ApplyNotice(Config config, FireflyNotice? notice, out FireflyNotice? displayNotice)
    {
        displayNotice = null;
        if (notice?.Enabled != true || notice.Content.TrimEx().IsNullOrEmpty())
        {
            return false;
        }

        var noticeId = notice.Id.TrimEx();
        if (notice.ShowOnce && noticeId.IsNotEmpty() && noticeId == config.ConstItem.FireflyLastNoticeId)
        {
            return false;
        }

        var title = notice.Title.TrimEx();
        var content = ToPlainText(notice.Content);
        NoticeManager.Instance.SendMessage(
            $"{title}{(title.IsNotEmpty() ? Environment.NewLine : string.Empty)}{content}");
        displayNotice = notice;

        if (noticeId.IsNotEmpty())
        {
            config.ConstItem.FireflyLastNoticeId = noticeId;
            return true;
        }
        return false;
    }

    internal static FireflyAppUpdate? SelectEligibleUpdate(FireflyAppUpdate? update)
    {
        if (update is null || update.VersionCode <= Global.FireflyVersionCode)
        {
            return null;
        }

        if (TryGetHttpsUri(update.DownloadUrl, out var downloadUri))
        {
            update.DownloadUrl = downloadUri!.AbsoluteUri;
            return update;
        }

        // A malformed optional update is ignored. A forced update must still
        // block the client and clearly report the backend configuration error;
        // silently ignoring it would bypass the server's mandatory policy.
        if (!update.Force)
        {
            return null;
        }

        update.DownloadUrl = string.Empty;
        return update;
    }

    private static bool TryGetHttpsUri(string? value, out Uri? uri)
    {
        var candidate = value.TrimEx();
        var markdownLink = Regex.Match(
            candidate,
            @"^\[[^\]]*\]\((https://[^\s()]+)\)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (markdownLink.Success)
        {
            candidate = markdownLink.Groups[1].Value;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out uri)
               && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToPlainText(string? value)
    {
        var withoutTags = Regex.Replace(value ?? string.Empty, "<[^>]*>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"\s{2,}", " ").Trim();
    }
}

internal sealed record FireflySubscriptionSyncResult(
    List<string> SubscriptionIds,
    FireflySubscriptionCatalogUpdate CatalogUpdate);

internal sealed record FireflySubscriptionCatalogUpdate(
    IReadOnlyList<string> NewGroupNames,
    bool StateChanged);

public sealed record FireflySyncResult(
    bool Enabled,
    IReadOnlyList<string> SubscriptionIds,
    FireflyAppUpdate? AppUpdate = null,
    FireflyNotice? Notice = null,
    bool AccessBanned = false,
    bool SuppressMissingServerWarning = false)
{
    public static FireflySyncResult Disabled { get; } = new(false, [], null, null, false, true);
}

public sealed class FireflyApiException(string errorCode, HttpStatusCode statusCode) : HttpRequestException($"Firefly API request failed ({errorCode}).", null, statusCode)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class FireflyApiResponse<T>
{
    public bool Ok { get; set; }
    public T? Data { get; set; }
}

public sealed class FireflyApiFailure
{
    public bool Ok { get; set; }
    public string Error { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
}

public sealed class FireflyBootstrap
{
    public FireflyCryptoDescriptor? Crypto { get; set; }
    public FireflyNotice? Notice { get; set; }
    public FireflyAppUpdate? PcAppUpdate { get; set; }
    // Kept for compatibility with Worker deployments that still expose the
    // former shared update field.
    public FireflyAppUpdate? AppUpdate { get; set; }
    public FireflySettings? Settings { get; set; }
    public string GeneratedAt { get; set; } = string.Empty;
}

public sealed class FireflyCryptoDescriptor
{
    public int Version { get; set; }
    public string Algorithm { get; set; } = string.Empty;
}

public sealed class FireflySubscription
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ContentUrl { get; set; } = string.Empty;
    public int CryptoVersion { get; set; }
    public string Algorithm { get; set; } = string.Empty;
}

public sealed class FireflyNotice
{
    public bool Enabled { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool ShowOnce { get; set; } = true;
}

public sealed class FireflyAppUpdate
{
    public int VersionCode { get; set; }
    public string VersionName { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public bool Force { get; set; }
    public string Changelog { get; set; } = string.Empty;

    [JsonIgnore]
    public string Version
    {
        get => VersionName;
        set => VersionName = value;
    }

    [JsonIgnore]
    public int IsForce
    {
        get => Force ? 1 : 0;
        set => Force = value != 0;
    }
}

public sealed class FireflySettings
{
    public string WebsiteUrl { get; set; } = string.Empty;
    public string FeedbackEmail { get; set; } = string.Empty;
    public string FeedbackUrl { get; set; } = string.Empty;
    public string GithubUrl { get; set; } = string.Empty;
}

public sealed class FireflyEnrollmentResult
{
    public string AccountId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceToken { get; set; } = string.Empty;
    public int CryptoVersion { get; set; }
    public bool AlreadyEnrolled { get; set; }
}

public sealed class FireflyUsageReportResult
{
    public bool Duplicate { get; set; }
    public string SessionId { get; set; } = string.Empty;
}
