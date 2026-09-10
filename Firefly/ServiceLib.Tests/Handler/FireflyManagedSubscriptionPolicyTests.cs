using Xunit;

namespace ServiceLib.Tests.Handler;

public class FireflyManagedSubscriptionPolicyTests
{
    [Fact]
    public void IsManagedSubscription_RecognizesOnlyFireflyWorkerMarker()
    {
        Assert.True(FireflyManagedSubscriptionPolicy.IsManagedSubscription(new SubItem
        {
            Memo = FireflyManagedSubscriptionPolicy.ManagedSubscriptionMemoPrefix + "primary"
        }));
        Assert.False(FireflyManagedSubscriptionPolicy.IsManagedSubscription(new SubItem
        {
            Memo = "user-note:primary"
        }));
    }

    [Fact]
    public void MaskProfileForDisplay_KeepsAliasAndRemovesConnectionFieldsForManagedSubscription()
    {
        var profile = new ProfileItemModel
        {
            Remarks = "Tokyo 01",
            Address = "node.example.com",
            Port = 443,
            Network = "ws",
            StreamSecurity = "tls",
            Subid = "firefly-subscription",
            SubRemarks = "Private source",
            IpInfo = "198.51.100.10"
        };

        FireflyManagedSubscriptionPolicy.MaskProfileForDisplay(profile, new HashSet<string> { "firefly-subscription" });

        Assert.True(profile.IsFireflyManaged);
        Assert.Equal("Tokyo 01", profile.Remarks);
        Assert.Equal(FireflyManagedSubscriptionPolicy.ProtectedAddress, profile.Address);
        Assert.Equal(0, profile.Port);
        Assert.Equal(string.Empty, profile.Network);
        Assert.Equal(string.Empty, profile.StreamSecurity);
        Assert.Equal("198.51.100.10", profile.IpInfo);
    }

    [Fact]
    public void MaskProfileForDisplay_LeavesOrdinarySubscriptionUntouched()
    {
        var profile = new ProfileItemModel
        {
            Remarks = "Personal node",
            Address = "personal.example.com",
            Port = 8443,
            Subid = "personal-subscription"
        };

        FireflyManagedSubscriptionPolicy.MaskProfileForDisplay(profile, new HashSet<string> { "firefly-subscription" });

        Assert.False(profile.IsFireflyManaged);
        Assert.Equal("Personal node", profile.Remarks);
        Assert.Equal("personal.example.com", profile.Address);
        Assert.Equal(8443, profile.Port);
    }
}
