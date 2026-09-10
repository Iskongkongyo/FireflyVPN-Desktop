namespace ServiceLib.Events;

public static class AppEvents
{
    public static readonly EventChannel<RxVoid> AddServerViaClipboardRequested = new();
    public static readonly EventChannel<string> HasUpdateNotified = new();

    public static readonly EventChannel<ServerSpeedItem> DispatcherStatisticsRequested = new();

    public static readonly EventChannel<SnackNotification> SendSnackMsgRequested = new();
    public static readonly EventChannel<string> SendMsgViewRequested = new();
    public static readonly EventChannel<FireflyNodeFetchNotification> FireflyNodeFetchNotificationRequested = new();
    public static readonly EventChannel<FireflyUpdateNotification> FireflyUpdateNotificationRequested = new();

    public static readonly EventChannel<RxVoid> AppExitRequested = new();
    public static readonly EventChannel<bool> ShutdownRequested = new();

    public static readonly EventChannel<ESysProxyType> SysProxyChangeRequested = new();
}

public sealed record SnackNotification(string Content, TimeSpan? Expiration = null);

/// <summary>
/// A persistent desktop notification for a Worker node-fetch operation.
/// </summary>
public sealed record FireflyNodeFetchNotification(Guid Id, string Content, bool IsActive, bool IsStartup);

/// <summary>
/// A persistent Worker announcement. Content remains HTML so the desktop UI
/// can render a safe rich-text subset without executing active content.
/// </summary>
public sealed record FireflyNoticeNotification(string Title, string Content);

/// <summary>
/// A persistent warning shown after the Worker explicitly bans the current
/// anonymous account or device.
/// </summary>
public sealed record FireflyAccessBannedNotification(string Content);

/// <summary>
/// A user-actionable desktop update notification. The download URL is validated
/// by FireflyWorkerService before this event is published.
/// </summary>
public sealed record FireflyUpdateNotification(
    string Version,
    string Changelog,
    string DownloadUrl,
    bool IsForced);
