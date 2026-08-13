using System.Globalization;

namespace ServiceLib.Handler;

/// <summary>
/// Decodes subscriptions managed by the FireflyVPN Worker.
/// The Worker uses a private Base64 alphabet and rotates it using the
/// client's IP address, returned in the X-Firefly-Client-IP response header.
/// </summary>
public static class FireflySubscriptionCodec
{
    public const string ClientIpHeaderName = "X-Firefly-Client-IP";

    private const string PrivateBase64Alphabet =
        "iWm124UFyO7KG9NBPhCj5kS8IYbHRvtselQcTA3ugVEo6and-Lprq_DJMw0ZzfxX";

    private const string StandardBase64Alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    public static string Decode(string value, string clientIp)
    {
        if (value.IsNullOrEmpty())
        {
            return string.Empty;
        }

        var shift = GetShiftFromClientIp(clientIp);
        var source = value.Trim().ReplaceLineBreaks(string.Empty);
        var base64 = new StringBuilder(source.Length);

        foreach (var character in source)
        {
            if (character == '=')
            {
                base64.Append(character);
                continue;
            }

            var shiftedIndex = PrivateBase64Alphabet.IndexOf(character);
            if (shiftedIndex < 0)
            {
                throw new FormatException("Invalid Firefly managed subscription encoding.");
            }

            var originalIndex = (shiftedIndex - shift + 64) % 64;
            base64.Append(StandardBase64Alphabet[originalIndex]);
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64.ToString()));
        }
        catch (FormatException exception)
        {
            throw new FormatException("Invalid Firefly managed subscription payload.", exception);
        }
    }

    private static int GetShiftFromClientIp(string value)
    {
        var address = value.Split(',', 2)[0].Trim();
        if (address.StartsWith('[') && address.Contains(']'))
        {
            address = address[1..address.IndexOf(']')];
        }

        var ipv4Tail = GetIpv4Tail(address);
        if (ipv4Tail is not null)
        {
            return ipv4Tail.Value % 64;
        }

        if (address.Contains(':'))
        {
            var lastGroup = address[(address.LastIndexOf(':') + 1)..];
            if (lastGroup.Length is > 0 and <= 4
                && int.TryParse(lastGroup, NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var value16))
            {
                return value16 % 64;
            }
        }

        throw new FormatException("Invalid X-Firefly-Client-IP response header.");
    }

    private static int? GetIpv4Tail(string address)
    {
        if (address.Contains(':') && address.Contains('.'))
        {
            address = address[(address.LastIndexOf(':') + 1)..];
        }

        var parts = address.Split('.');
        if (parts.Length != 4)
        {
            return null;
        }

        foreach (var part in parts)
        {
            if (part.Length is < 1 or > 3 || !part.All(char.IsAsciiDigit)
                || !int.TryParse(part, CultureInfo.InvariantCulture, out var octet)
                || octet is < 0 or > 255)
            {
                return null;
            }
        }

        return int.Parse(parts[3], CultureInfo.InvariantCulture);
    }
}
