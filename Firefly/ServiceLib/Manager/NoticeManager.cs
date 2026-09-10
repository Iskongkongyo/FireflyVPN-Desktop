namespace ServiceLib.Manager;

public class NoticeManager
{
    private static readonly Lazy<NoticeManager> _instance = new(() => new());
    public static NoticeManager Instance => _instance.Value;

    public void Enqueue(string? content, TimeSpan? expiration = null)
    {
        if (content.IsNullOrEmpty())
        {
            return;
        }
        AppEvents.SendSnackMsgRequested.Publish(new(content, expiration));
    }

    public void SendMessage(string? content)
    {
        if (content.IsNullOrEmpty())
        {
            return;
        }
        AppEvents.SendMsgViewRequested.Publish(content);
    }

    public void SendMessageEx(string? content)
    {
        if (content.IsNullOrEmpty())
        {
            return;
        }
        content = $"{DateTime.Now:yyyy/MM/dd HH:mm:ss} {content}";
        SendMessage(content);
    }

    public void SendMessageAndEnqueue(string? msg, TimeSpan? expiration = null)
    {
        Enqueue(msg, expiration);
        SendMessage(msg);
    }

    public Guid BeginFireflyNodeFetch(bool isStartup = false)
    {
        const string content = "正在请求节点......";
        var id = Guid.NewGuid();
        SendMessage(content);
        AppEvents.FireflyNodeFetchNotificationRequested.Publish(new(id, content, true, isStartup));
        return id;
    }

    public void EndFireflyNodeFetch(Guid id, bool isStartup = false)
    {
        AppEvents.FireflyNodeFetchNotificationRequested.Publish(new(id, string.Empty, false, isStartup));
    }

    public void NotifyFireflyUpdate(FireflyAppUpdate update)
    {
        var notification = FireflyWorkerService.CreateUpdateNotification(update);
        SendMessage(FireflyWorkerService.FormatDesktopUpdateMessage(update));
        AppEvents.FireflyUpdateNotificationRequested.Publish(notification);
    }

    /// <summary>
    /// Sends each error and warning in <paramref name="validatorResult"/> to the message panel
    /// and enqueues a summary snack notification (capped at 10 messages).
    /// Returns <c>true</c> when there were any messages so the caller can decide on early-return
    /// based on <see cref="NodeValidatorResult.Success"/>.
    /// </summary>
    public bool NotifyValidatorResult(NodeValidatorResult validatorResult)
    {
        var msgs = new List<string>([.. validatorResult.Errors, .. validatorResult.Warnings]);
        if (msgs.Count == 0)
        {
            return false;
        }
        foreach (var msg in msgs)
        {
            SendMessage(msg);
        }
        Enqueue(Utils.List2String(msgs.Take(10).ToList(), true));
        return true;
    }
}
