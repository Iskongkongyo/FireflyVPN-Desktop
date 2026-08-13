namespace ServiceLib.Events;

public static class AppEvents
{
    public static readonly EventChannel<RxVoid> AddServerViaClipboardRequested = new();
    public static readonly EventChannel<string> HasUpdateNotified = new();

    public static readonly EventChannel<ServerSpeedItem> DispatcherStatisticsRequested = new();

    public static readonly EventChannel<string> SendSnackMsgRequested = new();
    public static readonly EventChannel<string> SendMsgViewRequested = new();
    public static readonly EventChannel<FireflyNodeFetchNotification> FireflyNodeFetchNotificationRequested = new();
    public static readonly EventChannel<FireflyUpdateNotification> FireflyUpdateNotificationRequested = new();

    public static readonly EventChannel<RxVoid> AppExitRequested = new();
    public static readonly EventChannel<bool> ShutdownRequested = new();

    public static readonly EventChannel<ESysProxyType> SysProxyChangeRequested = new();
}

/// <summary>
/// A persistent desktop notification for a Worker node-fetch operation.
/// </summary>
public sealed record FireflyNodeFetchNotification(Guid Id, string Content, bool IsActive);

/// <summary>
/// A user-actionable desktop update notification. The download URL is validated
/// by FireflyWorkerService before this event is published.
/// </summary>
public sealed record FireflyUpdateNotification(string Version, string Changelog, string DownloadUrl);
