using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests;

public class FireflyBrandingTests
{
    [Fact]
    public void BrandingLinks_ShouldPointToFireflyOwnedDestinations()
    {
        FireflyBranding.ProductName.Should().Be("流萤加速器");
        FireflyBranding.WebsiteUrl.Should().Be("https://vpn.202132.xyz");
        FireflyBranding.SourceCodeUrl.Should().Be("https://github.com/Iskongkongyo/FireflyVPN-Desktop");
        FireflyBranding.ClientApiUrl.Should().Be(Global.FireflyClientApiUrl);
    }

    [Fact]
    public void DefaultCore_ShouldPreferSingBoxWhereItIsSupported()
    {
        FireflyBranding.GetDefaultCoreType(EConfigType.VLESS).Should().Be(ECoreType.sing_box);
        FireflyBranding.GetDefaultCoreType(EConfigType.TUIC).Should().Be(ECoreType.sing_box);
        FireflyBranding.GetDefaultCoreType(EConfigType.PolicyGroup).Should().Be(ECoreType.Xray);
    }
}
