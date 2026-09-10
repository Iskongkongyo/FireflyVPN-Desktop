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
    public void DefaultCore_ShouldPreferXrayUnlessTheProtocolRequiresSingBox()
    {
        FireflyBranding.GetDefaultCoreType(EConfigType.VMess).Should().Be(ECoreType.Xray);
        FireflyBranding.GetDefaultCoreType(EConfigType.Custom).Should().Be(ECoreType.Xray);
        FireflyBranding.GetDefaultCoreType(EConfigType.VLESS).Should().Be(ECoreType.Xray);
        FireflyBranding.GetDefaultCoreType(EConfigType.Shadowsocks).Should().Be(ECoreType.Xray);
        FireflyBranding.GetDefaultCoreType(EConfigType.SOCKS).Should().Be(ECoreType.Xray);
        FireflyBranding.GetDefaultCoreType(EConfigType.Trojan).Should().Be(ECoreType.Xray);
        FireflyBranding.GetDefaultCoreType(EConfigType.Hysteria2).Should().Be(ECoreType.Xray);
        FireflyBranding.GetDefaultCoreType(EConfigType.WireGuard).Should().Be(ECoreType.Xray);
        FireflyBranding.GetDefaultCoreType(EConfigType.HTTP).Should().Be(ECoreType.Xray);
        FireflyBranding.GetDefaultCoreType(EConfigType.TUIC).Should().Be(ECoreType.sing_box);
        FireflyBranding.GetDefaultCoreType(EConfigType.Anytls).Should().Be(ECoreType.sing_box);
        FireflyBranding.GetDefaultCoreType(EConfigType.Naive).Should().Be(ECoreType.sing_box);
    }
}
