namespace ServiceLib;

/// <summary>
/// Central registry for FireflyVPN product identity, public endpoints, and
/// user-facing external links. Keep branded links here rather than scattering
/// them across views and services.
/// </summary>
public static class FireflyBranding
{
    public const string ProductName = "流萤加速器";
    public static string ClientApiUrl { get; } = GetClientApiUrl();
    public const string WebsiteUrl = "https://vpn.202132.xyz";
    public const string CommunityUrl = "https://t.me/+N3h80bmqvVMwYzll";
    public const string SourceCodeUrl = "https://github.com/Iskongkongyo/FireflyVPN-Desktop";
    public const string UpstreamProjectName = "v2rayN";
    public const string UpstreamProjectUrl = "https://github.com/2dust/v2rayN";
    public const string LicenseName = "GNU GPL v3.0";
    public const string LicenseUrl = SourceCodeUrl + "/blob/main/LICENSE";

    private static string GetClientApiUrl()
    {
        var endpoint = typeof(FireflyBranding).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "FireflyClientApiUrl")
            ?.Value
            ?.Trim();

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps
               && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.AbsoluteUri.TrimEnd('/')
            : string.Empty;
    }

    public static ECoreType GetDefaultCoreType(EConfigType configType)
    {
        return Global.SingboxSupportConfigType.Contains(configType)
            ? ECoreType.sing_box
            : ECoreType.Xray;
    }
}
