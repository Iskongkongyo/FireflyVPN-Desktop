using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Handler;

public class FireflySubscriptionCodecTests
{
    private const string PrivateBase64Alphabet =
        "iWm124UFyO7KG9NBPhCj5kS8IYbHRvtselQcTA3ugVEo6and-Lprq_DJMw0ZzfxX";

    private const string StandardBase64Alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    [Theory]
    [InlineData("203.0.113.42")]
    [InlineData("2001:db8::1a")]
    [InlineData("[2001:db8::1a]")]
    public void Decode_ShouldReverseWorkerEncoding(string clientIp)
    {
        const string subscription = "vless://uuid@example.com:443?security=reality#Firefly\nss://YWVzLTI1Ni1nY206cGFzc3dvcmQ=@example.com:8388#备用";

        var encoded = EncodeAsWorker(subscription, clientIp);
        var decoded = FireflySubscriptionCodec.Decode(encoded, clientIp);

        decoded.Should().Be(subscription);
    }

    [Fact]
    public void Decode_ShouldRejectAnInvalidClientIp()
    {
        var action = () => FireflySubscriptionCodec.Decode("abcd", "not-an-ip");

        action.Should().Throw<FormatException>();
    }

    private static string EncodeAsWorker(string value, string clientIp)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        var shift = GetShift(clientIp);
        return new string(encoded.Select(character =>
        {
            if (character == '=') return character;
            var index = StandardBase64Alphabet.IndexOf(character);
            return PrivateBase64Alphabet[(index + shift) % 64];
        }).ToArray());
    }

    private static int GetShift(string clientIp)
    {
        var address = clientIp.Trim('[', ']');
        if (IPAddress.TryParse(address, out var ipAddress))
        {
            var bytes = ipAddress.MapToIPv4().GetAddressBytes();
            if (ipAddress.AddressFamily == AddressFamily.InterNetwork || ipAddress.IsIPv4MappedToIPv6)
            {
                return bytes[^1] % 64;
            }
        }

        return Convert.ToInt32(address[(address.LastIndexOf(':') + 1)..], 16) % 64;
    }
}
