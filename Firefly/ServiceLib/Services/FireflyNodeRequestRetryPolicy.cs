namespace ServiceLib.Services;

/// <summary>
/// Retries Firefly node-related network requests without surfacing endpoint or
/// subscription details in notifications.
/// </summary>
public static class FireflyNodeRequestRetryPolicy
{
    public const int MaxRetries = 3;
    public static readonly TimeSpan FinalFailureNotificationDuration = TimeSpan.FromSeconds(12);

    public static async Task<T?> ExecuteAsync<T>(
        Func<Task<T?>> request,
        string? groupName = null,
        bool notifyFinalFailure = true) where T : class
    {
        for (var retryCount = 0; retryCount <= MaxRetries; retryCount++)
        {
            try
            {
                var result = await request();
                if (result is not null)
                {
                    return result;
                }
            }
            catch (FireflyApiException ex) when (
                ex.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
            {
                // A deterministic client/auth/access decision cannot be repaired
                // by retrying and must not be presented as a network timeout.
                Logging.SaveLog(nameof(FireflyNodeRequestRetryPolicy), ex);
                throw;
            }
            catch (Exception ex)
            {
                Logging.SaveLog(nameof(FireflyNodeRequestRetryPolicy), ex);
            }

            if (retryCount == MaxRetries)
            {
                if (notifyFinalFailure)
                {
                    NotifyFinalFailure([groupName]);
                }
                return null;
            }

            var nextRetry = retryCount + 1;
            NoticeManager.Instance.SendMessageAndEnqueue(
                FormatRetryMessage(groupName, nextRetry));
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return null;
    }

    internal static string FormatFinalFailureMessage(IEnumerable<string?> groupNames)
    {
        var names = groupNames
            .Select(name => name.TrimEx())
            .Where(name => name.IsNotEmpty())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var groupPrefix = names.Count > 0
            ? $"{string.Join('、', names)}分组"
            : string.Empty;
        return $"{groupPrefix}节点信息获取失败，已重试 {MaxRetries} 次，请检查网络后重试。";
    }

    internal static void NotifyFinalFailure(IEnumerable<string?> groupNames)
    {
        NoticeManager.Instance.SendMessageAndEnqueue(
            FormatFinalFailureMessage(groupNames),
            FinalFailureNotificationDuration);
    }

    private static string FormatRetryMessage(string? groupName, int retry)
    {
        var name = groupName.TrimEx();
        var groupPrefix = name.IsNotEmpty() ? $"{name}分组" : string.Empty;
        return $"{groupPrefix}节点信息获取超时或失败，正在重试（{retry}/{MaxRetries}）......";
    }
}
