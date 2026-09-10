using System.Globalization;

namespace ServiceLib.Handler;

/// <summary>
/// Implements the Worker's P256-HKDF-SHA256-A256GCM subscription envelope.
/// </summary>
public static class FireflyCryptoV2
{
    public const int Version = 2;
    public const string Algorithm = "P256-HKDF-SHA256-A256GCM";
    public const string HkdfInfo = "FireflyVPN-Subscription-V2";
    public const int ResponseTtlSeconds = 300;

    public static (string PublicKeySpki, string PrivateKeyPkcs8) CreateKeyPair()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return (
            Base64UrlEncode(key.ExportSubjectPublicKeyInfo()),
            Base64UrlEncode(key.ExportPkcs8PrivateKey()));
    }

    public static string CreateChallenge()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
    }

    public static string Decrypt(
        FireflyCryptoV2Envelope envelope,
        FireflyDeviceIdentity identity,
        string subscriptionId,
        string challenge,
        DateTimeOffset? now = null)
    {
        if (envelope.Version != Version || envelope.Algorithm != Algorithm)
        {
            throw new CryptographicException("The Worker returned an unsupported crypto envelope.");
        }
        if (envelope.DeviceId != identity.DeviceId
            || envelope.SubscriptionId != subscriptionId
            || envelope.RequestId != challenge)
        {
            throw new CryptographicException("The Worker crypto envelope does not match this request.");
        }
        if (envelope.IssuedAt < 0
            || envelope.ExpiresAt - envelope.IssuedAt != ResponseTtlSeconds)
        {
            throw new CryptographicException("The Worker crypto envelope has an invalid lifetime.");
        }

        var current = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        if (envelope.IssuedAt > current + ResponseTtlSeconds || envelope.ExpiresAt < current)
        {
            throw new CryptographicException("The Worker crypto envelope has expired or is not yet valid.");
        }

        var ephemeralKeyBytes = Base64UrlDecode(envelope.EphemeralPublicKey);
        var salt = Base64UrlDecode(envelope.Salt);
        var nonce = Base64UrlDecode(envelope.Nonce);
        var ciphertextAndTag = Base64UrlDecode(envelope.Ciphertext);
        if (salt.Length != 32 || nonce.Length != 12 || ciphertextAndTag.Length < 16)
        {
            throw new CryptographicException("The Worker crypto envelope contains invalid parameters.");
        }
        var privateKeyBytes = Base64UrlDecode(identity.PrivateKeyPkcs8);

        var sharedSecret = Array.Empty<byte>();
        var aesKey = new byte[32];
        var plaintext = new byte[ciphertextAndTag.Length - 16];
        try
        {
            using var localKey = ECDiffieHellman.Create();
            localKey.ImportPkcs8PrivateKey(privateKeyBytes, out var privateBytesRead);
            if (privateBytesRead != privateKeyBytes.Length)
            {
                throw new CryptographicException("The local Firefly private key is invalid.");
            }

            using var ephemeralKey = ECDiffieHellman.Create();
            ephemeralKey.ImportSubjectPublicKeyInfo(ephemeralKeyBytes, out var publicBytesRead);
            if (publicBytesRead != ephemeralKeyBytes.Length)
            {
                throw new CryptographicException("The Worker ephemeral key is invalid.");
            }

            sharedSecret = localKey.DeriveRawSecretAgreement(ephemeralKey.PublicKey);
            HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                sharedSecret,
                aesKey,
                salt,
                Encoding.UTF8.GetBytes(HkdfInfo));

            var aad = BuildAad(envelope, challenge);
            using var aes = new AesGcm(aesKey, 16);
            aes.Decrypt(
                nonce,
                ciphertextAndTag.AsSpan(0, plaintext.Length),
                ciphertextAndTag.AsSpan(plaintext.Length, 16),
                plaintext,
                aad);
            return new UTF8Encoding(false, true).GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyBytes);
            CryptographicOperations.ZeroMemory(sharedSecret);
            CryptographicOperations.ZeroMemory(aesKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static byte[] BuildAad(FireflyCryptoV2Envelope envelope, string challenge)
    {
        return Encoding.UTF8.GetBytes(string.Join('\n',
            Version.ToString(CultureInfo.InvariantCulture),
            Algorithm,
            envelope.DeviceId,
            envelope.SubscriptionId,
            challenge,
            envelope.IssuedAt.ToString(CultureInfo.InvariantCulture),
            envelope.ExpiresAt.ToString(CultureInfo.InvariantCulture)));
    }

    public static string Base64UrlEncode(ReadOnlySpan<byte> value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] Base64UrlDecode(string value)
    {
        if (value.IsNullOrEmpty()
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            || value.Length % 4 == 1)
        {
            throw new FormatException("Invalid Base64URL value.");
        }

        var standard = value.Replace('-', '+').Replace('_', '/');
        standard += new string('=', (4 - (standard.Length % 4)) % 4);
        var bytes = Convert.FromBase64String(standard);
        if (Base64UrlEncode(bytes) != value)
        {
            throw new FormatException("Non-canonical Base64URL value.");
        }
        return bytes;
    }
}

public sealed class FireflyCryptoV2Envelope
{
    public int Version { get; set; }
    public string Algorithm { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string EphemeralPublicKey { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public string Ciphertext { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public long IssuedAt { get; set; }
    public long ExpiresAt { get; set; }
}
