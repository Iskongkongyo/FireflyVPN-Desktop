namespace ServiceLib.Services;

/// <summary>
/// Verifies that the active local proxy can reach the public Internet and
/// confirms transient failures before connection recovery is attempted.
/// </summary>
public sealed class ConnectionRecoveryService
{
    public const string ProbeUrl = "https://www.google.com/generate_204";
    public const int RetryCount = 2;
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan RecoveryCooldown = TimeSpan.FromMinutes(5);

    private readonly Func<CancellationToken, Task<bool>> _probe;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public ConnectionRecoveryService()
    {
        _probe = ProbeThroughLocalProxyAsync;
        _delay = Task.Delay;
    }

    internal ConnectionRecoveryService(
        Func<CancellationToken, Task<bool>> probe,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _probe = probe;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>
    /// Returns true only after the initial probe and both retries fail.
    /// Any successful HTTP 204 response cancels recovery for this cycle.
    /// </summary>
    public async Task<bool> IsFailureConfirmedAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt <= RetryCount; attempt++)
        {
            if (await _probe(cancellationToken))
            {
                return false;
            }

            if (attempt < RetryCount)
            {
                await _delay(RetryDelay, cancellationToken);
            }
        }

        return true;
    }

    internal static ProfileItem? FindNodeByEndpoint(
        IEnumerable<ProfileItem>? candidates,
        ProfileItem original)
    {
        if (candidates is null || original.Address.IsNullOrEmpty() || original.Port <= 0)
        {
            return null;
        }

        return candidates.FirstOrDefault(candidate =>
            candidate.Port == original.Port
            && candidate.Address.TrimEx().Equals(
                original.Address.TrimEx(),
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> ProbeThroughLocalProxyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var localPort = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"socks5://{Global.Loopback}:{localPort}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(3),
            };
            var certificateChainPolicy = CertPemManager.Instance.BuildCertificateChainPolicy();
            if (certificateChainPolicy is not null)
            {
                handler.SslOptions.CertificateChainPolicy = certificateChainPolicy;
                handler.SslOptions.RemoteCertificateValidationCallback = null;
            }

            using (handler)
            using (var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan })
            using (var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl))
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true,
                };
                timeout.CancelAfter(ProbeTimeout);
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                return response.StatusCode == HttpStatusCode.NoContent;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(ConnectionRecoveryService), ex);
            return false;
        }
    }
}
