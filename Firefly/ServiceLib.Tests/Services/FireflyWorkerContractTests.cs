using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Services;

public class FireflyWorkerContractTests
{
    [Fact]
    public void ConstItem_ShouldDefaultToTheFireflyClientEndpoint()
    {
        new ConstItem().FireflyApiUrl.Should().Be(Global.FireflyClientApiUrl);
    }

    [Fact]
    public void BootstrapResponse_ShouldDeserializeTheV2WorkerContract()
    {
        const string json = """
        {
          "ok": true,
          "data": {
            "crypto": {"version":2,"algorithm":"P256-HKDF-SHA256-A256GCM"},
            "notice": {"enabled":true,"id":"notice_1","title":"维护通知","content":"<b>今晚维护</b>","showOnce":true},
            "pcAppUpdate": {"versionCode":3,"versionName":"1.2.0-pc","downloadUrl":"https://downloads.example/FireflyVPN.zip","force":true,"changelog":"桌面端修复"},
            "settings": {"websiteUrl":"https://firefly.example","feedbackEmail":"help@example.com","feedbackUrl":"","githubUrl":""},
            "generatedAt":"2026-09-07T00:00:00.000Z"
          }
        }
        """;

        var response = JsonUtils.Deserialize<FireflyApiResponse<FireflyBootstrap>>(json);

        response.Should().NotBeNull();
        response!.Ok.Should().BeTrue();
        response.Data!.Crypto!.Version.Should().Be(2);
        response.Data.Crypto.Algorithm.Should().Be(FireflyCryptoV2.Algorithm);
        response.Data.Notice!.Id.Should().Be("notice_1");
        response.Data.PcAppUpdate!.VersionName.Should().Be("1.2.0-pc");
        response.Data.PcAppUpdate.Force.Should().BeTrue();
        response.Data.Settings!.WebsiteUrl.Should().Be("https://firefly.example");
    }

    [Fact]
    public void SubscriptionCatalog_ShouldDeserializeSafeContentPaths()
    {
        const string json = """
        {"ok":true,"data":[{"id":"main","name":"主线路","contentUrl":"/api/v2/subscriptions/main/content","cryptoVersion":2,"algorithm":"P256-HKDF-SHA256-A256GCM"}]}
        """;

        var response = JsonUtils.Deserialize<FireflyApiResponse<List<FireflySubscription>>>(json);

        response!.Data.Should().ContainSingle();
        response.Data![0].ContentUrl.Should().Be("/api/v2/subscriptions/main/content");
        response.Data[0].CryptoVersion.Should().Be(2);
    }

    [Theory]
    [InlineData("https://worker.example", "https://worker.example/")]
    [InlineData("https://worker.example/api/client", "https://worker.example/")]
    [InlineData("https://worker.example/api/v2/bootstrap", "https://worker.example/")]
    public void ApiBase_ShouldMigrateOldEndpointShapes(string input, string expected)
    {
        FireflyWorkerService.GetApiBaseUri(input).AbsoluteUri.Should().Be(expected);
    }

    [Fact]
    public void CatalogReconciliation_ShouldRemoveMissingManagedGroupsIncludingDisabledOnes()
    {
        var prefix = FireflyManagedSubscriptionPolicy.ManagedSubscriptionMemoPrefix;
        var existing = new List<SubItem>
        {
            new() { Id = "local-main", Memo = prefix + "main", Enabled = true },
            new() { Id = "local-backup", Memo = prefix + "backup", Enabled = true },
            new() { Id = "local-old", Memo = prefix + "old", Enabled = false },
            new() { Id = "personal", Memo = "user-owned", Enabled = true },
        };

        var obsolete = FireflyWorkerService.FindObsoleteManagedSubscriptions(
            existing, new HashSet<string>(StringComparer.Ordinal) { "main" });

        obsolete.Select(item => item.Id).Should().BeEquivalentTo("local-backup", "local-old");
    }

    [Fact]
    public void AccessBanReconciliation_ShouldRemoveEveryManagedGroupButKeepUserOwnedGroups()
    {
        var prefix = FireflyManagedSubscriptionPolicy.ManagedSubscriptionMemoPrefix;
        var existing = new List<SubItem>
        {
            new() { Id = "managed-main", Memo = prefix + "main" },
            new() { Id = "managed-disabled", Memo = prefix + "disabled", Enabled = false },
            new() { Id = "personal", Memo = "user-owned" },
        };

        var removed = FireflyWorkerService.FindObsoleteManagedSubscriptions(
            existing, new HashSet<string>(StringComparer.Ordinal));

        removed.Select(item => item.Id).Should().BeEquivalentTo("managed-main", "managed-disabled");
        removed.Should().NotContain(item => item.Id == "personal");
    }

    [Theory]
    [InlineData("account_banned", true)]
    [InlineData("device_banned", true)]
    [InlineData("account_deleted", false)]
    [InlineData("device_revoked", false)]
    [InlineData("unauthorized", false)]
    public void AccessBan_ShouldRecognizeOnlyBackendBanCodes(string errorCode, bool expected)
    {
        FireflyWorkerService.IsAccessBannedErrorCode(errorCode).Should().Be(expected);
    }

    [Fact]
    public async Task AccessBan_ShouldNotRetryAnAuthoritativeBackendDecision()
    {
        var attempts = 0;

        var exception = await Assert.ThrowsAsync<FireflyApiException>(() =>
            FireflyNodeRequestRetryPolicy.ExecuteAsync<string>(() =>
            {
                attempts++;
                return Task.FromException<string?>(
                    new FireflyApiException("device_banned", HttpStatusCode.Forbidden));
            }));

        exception.ErrorCode.Should().Be("device_banned");
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task DeviceConflict_ShouldNotBeReportedAsARetryableNetworkFailure()
    {
        var attempts = 0;

        var exception = await Assert.ThrowsAsync<FireflyApiException>(() =>
            FireflyNodeRequestRetryPolicy.ExecuteAsync<string>(() =>
            {
                attempts++;
                return Task.FromException<string?>(
                    new FireflyApiException("device_conflict", HttpStatusCode.Conflict));
            }));

        exception.ErrorCode.Should().Be("device_conflict");
        attempts.Should().Be(1);
    }

    [Fact]
    public void DisabledSync_ShouldSuppressTheMisleadingMissingConfigurationWarning()
    {
        FireflySyncResult.Disabled.SuppressMissingServerWarning.Should().BeTrue();
    }

    [Fact]
    public void NodeFetchFailure_ShouldCombineDistinctGroupNames()
    {
        var message = FireflyNodeRequestRetryPolicy.FormatFinalFailureMessage(
            ["香港节点", "美国节点", "香港节点", ""]);

        message.Should().Be("香港节点、美国节点分组节点信息获取失败，已重试 3 次，请检查网络后重试。");
    }

    [Fact]
    public void NodeFetchFailure_WithoutAGroupName_ShouldKeepTheOriginalMessage()
    {
        var message = FireflyNodeRequestRetryPolicy.FormatFinalFailureMessage([null, " "]);

        message.Should().Be("节点信息获取失败，已重试 3 次，请检查网络后重试。");
        FireflyNodeRequestRetryPolicy.FinalFailureNotificationDuration
            .Should().Be(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void FirstSuccessfulSubscriptionCatalog_ShouldOnlyEstablishTheBaseline()
    {
        var settings = new ConstItem();

        var update = FireflyWorkerService.TrackSubscriptionCatalog(settings,
        [
            new FireflySubscription { Id = "hong-kong", Name = "香港节点" },
            new FireflySubscription { Id = "united-states", Name = "美国节点" },
        ]);

        update.NewGroupNames.Should().BeEmpty();
        settings.FireflySubscriptionCatalogInitialized.Should().BeTrue();
        settings.FireflyKnownSubscriptionIds.Should().BeEquivalentTo("hong-kong", "united-states");
        settings.FireflyUnreadSubscriptionIds.Should().BeEmpty();
    }

    [Fact]
    public void LaterSubscriptionCatalog_ShouldTrackAndFormatNewGroupsByStableId()
    {
        var settings = new ConstItem
        {
            FireflySubscriptionCatalogInitialized = true,
            FireflyKnownSubscriptionIds = ["hong-kong"],
            FireflyUnreadSubscriptionIds = [],
        };

        var update = FireflyWorkerService.TrackSubscriptionCatalog(settings,
        [
            new FireflySubscription { Id = "hong-kong", Name = "香港节点（新名称）" },
            new FireflySubscription { Id = "united-states", Name = "美国节点" },
            new FireflySubscription { Id = "japan", Name = "日本节点" },
        ]);

        update.NewGroupNames.Should().Equal("美国节点", "日本节点");
        settings.FireflyUnreadSubscriptionIds.Should().BeEquivalentTo("united-states", "japan");
        FireflyWorkerService.FormatNewSubscriptionGroupsMessage(update.NewGroupNames)
            .Should().Be("新增：美国节点、日本节点分组");
    }

    [Fact]
    public void ExistingSubscriptionIdWithARenamedGroup_ShouldNotBeTreatedAsNew()
    {
        var settings = new ConstItem
        {
            FireflySubscriptionCatalogInitialized = true,
            FireflyKnownSubscriptionIds = ["hong-kong"],
            FireflyUnreadSubscriptionIds = [],
        };

        var update = FireflyWorkerService.TrackSubscriptionCatalog(settings,
        [
            new FireflySubscription { Id = "hong-kong", Name = "香港节点（新名称）" },
        ]);

        update.NewGroupNames.Should().BeEmpty();
        settings.FireflyUnreadSubscriptionIds.Should().BeEmpty();
    }

    [Fact]
    public void DesktopUpdate_ShouldRequireANewerVersionAndHttpsDownload()
    {
        var valid = new FireflyAppUpdate
        {
            VersionCode = Global.FireflyVersionCode + 1,
            VersionName = "next",
            DownloadUrl = "https://downloads.example/firefly.exe",
        };

        FireflyWorkerService.SelectEligibleUpdate(valid).Should().BeSameAs(valid);
        FireflyWorkerService.SelectEligibleUpdate(new FireflyAppUpdate
        {
            VersionCode = Global.FireflyVersionCode,
            DownloadUrl = valid.DownloadUrl,
        }).Should().BeNull();
        FireflyWorkerService.SelectEligibleUpdate(new FireflyAppUpdate
        {
            VersionCode = Global.FireflyVersionCode + 1,
            DownloadUrl = "http://downloads.example/firefly.exe",
        }).Should().BeNull();

        var forcedWithoutSafeDownload = new FireflyAppUpdate
        {
            VersionCode = Global.FireflyVersionCode + 1,
            DownloadUrl = "http://downloads.example/firefly.exe",
            Force = true,
        };
        FireflyWorkerService.SelectEligibleUpdate(forcedWithoutSafeDownload)
            .Should().BeSameAs(forcedWithoutSafeDownload);
        forcedWithoutSafeDownload.DownloadUrl.Should().BeEmpty();

        var markdownWrapped = new FireflyAppUpdate
        {
            VersionCode = Global.FireflyVersionCode + 1,
            DownloadUrl = "[download](https://downloads.example/firefly.exe)",
        };
        FireflyWorkerService.SelectEligibleUpdate(markdownWrapped).Should().BeSameAs(markdownWrapped);
        markdownWrapped.DownloadUrl.Should().Be("https://downloads.example/firefly.exe");
    }

    [Fact]
    public void DesktopUpdateMessage_ShouldFormatEveryUpdateField()
    {
        var update = new FireflyAppUpdate
        {
            VersionCode = Global.FireflyVersionCode + 1,
            VersionName = "2.0.0-pc",
            DownloadUrl = "https://downloads.example/firefly.exe",
            Changelog = "<b>修复启动通知</b>",
        };

        var message = FireflyWorkerService.FormatDesktopUpdateMessage(update);

        message.Should().Contain(update.VersionName);
        message.Should().Contain("修复启动通知");
        message.Should().Contain(update.DownloadUrl);
    }

    [Fact]
    public void DesktopNotifications_ShouldPreserveHtmlForSafeUiRendering()
    {
        var update = FireflyWorkerService.CreateUpdateNotification(new FireflyAppUpdate
        {
            VersionCode = Global.FireflyVersionCode + 1,
            VersionName = "2.0.0-pc",
            DownloadUrl = "https://downloads.example/firefly.exe",
            Changelog = "<p><strong>重要更新</strong><br><em>请及时安装</em></p>",
            Force = true,
        });
        var notice = FireflyWorkerService.CreateNoticeNotification(new FireflyNotice
        {
            Title = "维护通知",
            Content = "<ul><li>节点维护</li><li>稍后恢复</li></ul>",
        });

        update.Changelog.Should().Contain("<strong>");
        update.IsForced.Should().BeTrue();
        notice.Content.Should().Contain("<li>");
    }

    [Fact]
    public void AccessBanNotification_ShouldUseTheRequiredPersistentMessage()
    {
        FireflyWorkerService.CreateAccessBannedNotification().Content.Should().Be(
            "抱歉，您已被管理员封禁！当前无法获取节点等信息，软件其他功能则不受影响！");
    }
}
