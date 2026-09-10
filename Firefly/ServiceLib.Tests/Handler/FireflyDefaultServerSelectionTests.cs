using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Handler;

public class FireflyDefaultServerSelectionTests
{
    [Fact]
    public void SelectDefaultServerIndex_ShouldUseHydratedManagedNode()
    {
        const string indexId = "managed-node";
        var visibleProfiles = new List<ProfileItemModel>
        {
            new()
            {
                IndexId = indexId,
                Address = FireflyManagedSubscriptionPolicy.ProtectedAddress,
                Port = 0,
            },
        };
        var hydratedProfiles = new List<ProfileItem>
        {
            new()
            {
                IndexId = indexId,
                Address = "node.example.com",
                Port = 443,
                ConfigType = EConfigType.VLESS,
            },
        };

        var selected = ConfigHandler.SelectDefaultServerIndex(visibleProfiles, hydratedProfiles);

        selected.Should().Be(indexId);
    }

    [Fact]
    public void SelectDefaultServerIndex_ShouldPreferDirectlyUsableVisibleNode()
    {
        var visibleProfiles = new List<ProfileItemModel>
        {
            new() { IndexId = "normal-node", Address = "normal.example.com", Port = 443 },
            new() { IndexId = "managed-node", Port = 0 },
        };
        var hydratedProfiles = new List<ProfileItem>
        {
            new() { IndexId = "managed-node", Address = "managed.example.com", Port = 443 },
        };

        var selected = ConfigHandler.SelectDefaultServerIndex(visibleProfiles, hydratedProfiles);

        selected.Should().Be("normal-node");
    }
}
