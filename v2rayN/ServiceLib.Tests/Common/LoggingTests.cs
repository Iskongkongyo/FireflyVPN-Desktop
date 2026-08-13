using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Common;

public class LoggingTests
{
    [Fact]
    public void RedactSensitiveData_ShouldRemoveNodeCredentialsAndEndpoints()
    {
        const string input = "vless://11111111-2222-4333-8444-555555555555@node.example.com:443?token=abc password=secret uuid: raw-uuid";

        var output = Logging.RedactSensitiveData(input);

        output.Should().NotContain("11111111-2222-4333-8444-555555555555");
        output.Should().NotContain("node.example.com");
        output.Should().NotContain("secret");
        output.Should().Contain("[REDACTED-URL]");
        output.Should().Contain("[REDACTED]");
    }
}
