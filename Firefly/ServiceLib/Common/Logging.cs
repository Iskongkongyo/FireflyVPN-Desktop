using NLog;
using NLog.Config;
using NLog.Targets;
using System.Text.RegularExpressions;

namespace ServiceLib.Common;

public class Logging
{
    private static readonly Regex UrlPattern = new(@"\b[a-z][a-z0-9+.-]*://[^\s\""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UuidPattern = new(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DomainPattern = new(@"\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CredentialPattern = new(@"(?<key>\b(?:password|passwd|uuid|token|secret|authorization|private[_-]?key)\b\s*[:=]\s*)(?<value>\""(?:\\.|[^\""\\])*\""|[^,\s}\]]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Logger _logger1 = LogManager.GetLogger("Log1");
    private static readonly Logger _logger2 = LogManager.GetLogger("Log2");

    public static void Setup()
    {
        LoggingConfiguration config = new();
        FileTarget fileTarget = new();
        config.AddTarget("file", fileTarget);
        fileTarget.Layout = "${longdate}-${level:uppercase=true} ${message}";
        fileTarget.FileName = Utils.GetLogPath("${shortdate}.txt");
        config.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, fileTarget));
        LogManager.Configuration = config;
        LogManager.SuspendLogging();
    }

    public static void LoggingEnabled(bool enable)
    {
        if (enable)
        {
            LogManager.ResumeLogging();
        }
        else
        {
            LogManager.SuspendLogging();
        }
    }

    public static void SaveLog(string strContent)
    {
        if (!LogManager.IsLoggingEnabled())
        {
            return;
        }

        _logger1.Info(RedactSensitiveData(strContent));
    }

    public static void SaveLog(string strTitle, Exception ex)
    {
        if (!LogManager.IsLoggingEnabled())
        {
            return;
        }

        _logger2.Debug(RedactSensitiveData($"{strTitle},{ex.Message}"));
        _logger2.Debug(RedactSensitiveData(ex.StackTrace));
        if (ex?.InnerException != null)
        {
            _logger2.Error(RedactSensitiveData(ex.InnerException.ToString()));
        }
    }

    /// <summary>Removes reusable endpoint credentials before a message is persisted.</summary>
    public static string RedactSensitiveData(string? value)
    {
        if (value.IsNullOrEmpty())
        {
            return value ?? string.Empty;
        }

        var redacted = CredentialPattern.Replace(value, match => match.Groups["key"].Value + "[REDACTED]");
        redacted = UrlPattern.Replace(redacted, "[REDACTED-URL]");
        redacted = UuidPattern.Replace(redacted, "[REDACTED-UUID]");
        return DomainPattern.Replace(redacted, "[REDACTED-DOMAIN]");
    }
}
