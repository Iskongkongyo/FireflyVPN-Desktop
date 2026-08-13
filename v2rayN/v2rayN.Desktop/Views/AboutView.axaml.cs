using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();

        txtVersion.Text = Utils.GetVersion();
        btnWebsite.Content = FireflyBranding.WebsiteUrl;
        btnSourceCode.Content = FireflyBranding.SourceCodeUrl;
        btnUpstream.Content = $"{FireflyBranding.UpstreamProjectName} · {FireflyBranding.UpstreamProjectUrl}";
        btnLicense.Content = $"{FireflyBranding.LicenseName} · {FireflyBranding.LicenseUrl}";

        btnWebsite.Click += (_, _) => OpenUrl(FireflyBranding.WebsiteUrl);
        btnSourceCode.Click += (_, _) => OpenUrl(FireflyBranding.SourceCodeUrl);
        btnUpstream.Click += (_, _) => OpenUrl(FireflyBranding.UpstreamProjectUrl);
        btnLicense.Click += (_, _) => OpenUrl(FireflyBranding.LicenseUrl);
        btnClose.Click += (_, _) => DialogHost.Close(null);
    }

    private static void OpenUrl(string url)
    {
        ProcUtils.ProcessStart(url);
    }
}
