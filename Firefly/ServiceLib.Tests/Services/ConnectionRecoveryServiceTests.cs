using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Services;

public class ConnectionRecoveryServiceTests
{
    [Fact]
    public void Defaults_ShouldUseTheRequestedProbeAndCadence()
    {
        ConnectionRecoveryService.ProbeUrl.Should().Be("https://www.google.com/generate_204");
        ConnectionRecoveryService.ProbeInterval.Should().Be(TimeSpan.FromSeconds(6));
        ConnectionRecoveryService.ProbeTimeout.Should().Be(TimeSpan.FromSeconds(4));
        ConnectionRecoveryService.RetryCount.Should().Be(2);
        new GUIItem().EnableConnectionRecovery.Should().BeTrue();
    }

    [Fact]
    public async Task FailureConfirmation_ShouldStopAsSoonAsAProbeSucceeds()
    {
        var calls = 0;
        var service = new ConnectionRecoveryService(
            _ => Task.FromResult(++calls == 3),
            (_, _) => Task.CompletedTask);

        var confirmed = await service.IsFailureConfirmedAsync(TestContext.Current.CancellationToken);

        confirmed.Should().BeFalse();
        calls.Should().Be(3);
    }

    [Fact]
    public async Task FailureConfirmation_ShouldUseAnInitialAttemptAndTwoRetries()
    {
        var calls = 0;
        var delays = 0;
        var service = new ConnectionRecoveryService(
            _ =>
            {
                calls++;
                return Task.FromResult(false);
            },
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });

        var confirmed = await service.IsFailureConfirmedAsync(TestContext.Current.CancellationToken);

        confirmed.Should().BeTrue();
        calls.Should().Be(3);
        delays.Should().Be(2);
    }

    [Fact]
    public void EndpointMatching_ShouldUseOnlyAddressAndPort()
    {
        var original = new ProfileItem
        {
            Address = "Node.Example.com",
            Port = 443,
            Remarks = "旧名称",
        };
        var expected = new ProfileItem
        {
            Address = "node.example.com",
            Port = 443,
            Remarks = "新名称",
        };
        var candidates = new[]
        {
            new ProfileItem { Address = "node.example.com", Port = 8443 },
            expected,
        };

        ConnectionRecoveryService.FindNodeByEndpoint(candidates, original)
            .Should().BeSameAs(expected);
    }
}
