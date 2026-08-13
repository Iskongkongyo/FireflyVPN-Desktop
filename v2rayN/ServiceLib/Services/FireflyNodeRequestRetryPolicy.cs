namespace ServiceLib.Services;

/// <summary>
/// Retries Firefly node-related network requests without surfacing endpoint or
/// subscription details in notifications.
/// </summary>
public static class FireflyNodeRequestRetryPolicy
{
    public const int MaxRetries = 3;

    public static async Task<T?> ExecuteAsync<T>(Func<Task<T?>> request) where T : class
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
            catch (Exception ex)
            {
                Logging.SaveLog(nameof(FireflyNodeRequestRetryPolicy), ex);
            }

            if (retryCount == MaxRetries)
            {
                NoticeManager.Instance.SendMessageAndEnqueue($"节点信息获取失败，已重试 {MaxRetries} 次，请检查网络后重试。");
                return null;
            }

            var nextRetry = retryCount + 1;
            NoticeManager.Instance.SendMessageAndEnqueue($"节点信息获取超时或失败，正在重试（{nextRetry}/{MaxRetries}）......");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return null;
    }
}
