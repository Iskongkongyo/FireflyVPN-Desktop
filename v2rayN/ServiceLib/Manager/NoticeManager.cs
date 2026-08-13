namespace ServiceLib.Manager;

public class NoticeManager
{
    private static readonly Lazy<NoticeManager> _instance = new(() => new());
    public static NoticeManager Instance => _instance.Value;

    public void Enqueue(string? content)
    {
        if (content.IsNullOrEmpty())
        {
            return;
        }
        AppEvents.SendSnackMsgRequested.Publish(content);
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

    public void SendMessageAndEnqueue(string? msg)
    {
        Enqueue(msg);
        SendMessage(msg);
    }

    public Guid BeginFireflyNodeFetch()
    {
        const string content = "正在获取节点信息......";
        var id = Guid.NewGuid();
        SendMessage(content);
        AppEvents.FireflyNodeFetchNotificationRequested.Publish(new(id, content, true));
        return id;
    }

    public void EndFireflyNodeFetch(Guid id)
    {
        AppEvents.FireflyNodeFetchNotificationRequested.Publish(new(id, string.Empty, false));
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
