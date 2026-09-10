namespace ServiceLib.Services;

/// <summary>
/// Batches the proxy byte deltas produced by the core statistics services and
/// reports them with an idempotent session id. Failures never interrupt VPN
/// traffic and retain the same batch id for the next in-process retry.
/// </summary>
public sealed class FireflyUsageReporter
{
    private const long MaxBytesPerReport = 10L * 1024 * 1024 * 1024 * 1024;
    private static readonly Lazy<FireflyUsageReporter> InstanceFactory = new(() => new());
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private long _accumulatedUpload;
    private long _accumulatedDownload;
    private PendingUsage? _pending;
    private DateTimeOffset _nextAutomaticFlush = DateTimeOffset.UtcNow.AddMinutes(5);

    public static FireflyUsageReporter Instance => InstanceFactory.Value;

    public void Add(Config config, long uploadBytes, long downloadBytes)
    {
        if (uploadBytes <= 0 && downloadBytes <= 0)
        {
            return;
        }

        var shouldFlush = false;
        lock (_lock)
        {
            _accumulatedUpload = SaturatingAdd(_accumulatedUpload, Math.Max(uploadBytes, 0));
            _accumulatedDownload = SaturatingAdd(_accumulatedDownload, Math.Max(downloadBytes, 0));
            if (DateTimeOffset.UtcNow >= _nextAutomaticFlush)
            {
                _nextAutomaticFlush = DateTimeOffset.UtcNow.AddMinutes(5);
                shouldFlush = true;
            }
        }

        if (shouldFlush)
        {
            _ = Task.Run(() => FlushAsync(config));
        }
    }

    public async Task FlushAsync(Config config)
    {
        await _flushLock.WaitAsync();
        try
        {
            while (true)
            {
                PendingUsage? batch;
                lock (_lock)
                {
                    _pending ??= TakeNextBatch();
                    batch = _pending;
                }

                if (batch is null)
                {
                    return;
                }

                try
                {
                    await new FireflyWorkerService().ReportUsageAsync(
                        config, batch.SessionId, batch.UploadBytes, batch.DownloadBytes);
                    lock (_lock)
                    {
                        if (ReferenceEquals(_pending, batch))
                        {
                            _pending = null;
                        }
                    }
                }
                catch (Exception exception)
                {
                    Logging.SaveLog(nameof(FireflyUsageReporter), exception);
                    return;
                }
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private PendingUsage? TakeNextBatch()
    {
        if (_accumulatedUpload == 0 && _accumulatedDownload == 0)
        {
            return null;
        }

        var upload = Math.Min(_accumulatedUpload, MaxBytesPerReport);
        var download = Math.Min(_accumulatedDownload, MaxBytesPerReport);
        _accumulatedUpload -= upload;
        _accumulatedDownload -= download;
        return new PendingUsage($"desktop-{Guid.NewGuid():N}", upload, download);
    }

    private static long SaturatingAdd(long left, long right)
    {
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }

    private sealed record PendingUsage(string SessionId, long UploadBytes, long DownloadBytes);
}
