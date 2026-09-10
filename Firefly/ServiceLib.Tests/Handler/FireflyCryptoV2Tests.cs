using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Handler;

public class FireflyCryptoV2Tests
{
    [Fact]
    public void Decrypt_ShouldOpenAValidWorkerEnvelope()
    {
        const string subscriptionId = "main";
        const string plaintext = "vless://uuid@example.com:443?security=reality#Firefly\nss://example";
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var identity = CreateIdentity();
        var challenge = FireflyCryptoV2.CreateChallenge();
        var envelope = Encrypt(identity, subscriptionId, challenge, plaintext, now.ToUnixTimeSeconds());

        var result = FireflyCryptoV2.Decrypt(envelope, identity, subscriptionId, challenge, now);

        result.Should().Be(plaintext);
    }

    [Fact]
    public void Decrypt_ShouldRejectAnEnvelopeForAnotherChallenge()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var identity = CreateIdentity();
        var challenge = FireflyCryptoV2.CreateChallenge();
        var envelope = Encrypt(identity, "main", challenge, "vless://test", now.ToUnixTimeSeconds());

        var action = () => FireflyCryptoV2.Decrypt(
            envelope, identity, "main", FireflyCryptoV2.CreateChallenge(), now);

        action.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Base64UrlDecode_ShouldRejectPaddingAndNonCanonicalValues()
    {
        var action = () => FireflyCryptoV2.Base64UrlDecode("AQ==");

        action.Should().Throw<FormatException>();
    }

    private static FireflyDeviceIdentity CreateIdentity()
    {
        var (PublicKeySpki, PrivateKeyPkcs8) = FireflyCryptoV2.CreateKeyPair();
        return new FireflyDeviceIdentity
        {
            DeviceId = new string('a', 64),
            IdentityHash = new string('b', 64),
            PublicKeySpki = PublicKeySpki,
            PrivateKeyPkcs8 = PrivateKeyPkcs8,
            DeviceToken = FireflyCryptoV2.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)),
        };
    }

    private static FireflyCryptoV2Envelope Encrypt(
        FireflyDeviceIdentity identity,
        string subscriptionId,
        string challenge,
        string plaintext,
        long issuedAt)
    {
        using var clientPublic = ECDiffieHellman.Create();
        clientPublic.ImportSubjectPublicKeyInfo(
            FireflyCryptoV2.Base64UrlDecode(identity.PublicKeySpki), out _);
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var sharedSecret = ephemeral.DeriveRawSecretAgreement(clientPublic.PublicKey);
        var salt = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, key, salt,
            Encoding.UTF8.GetBytes(FireflyCryptoV2.HkdfInfo));

        var envelope = new FireflyCryptoV2Envelope
        {
            Version = FireflyCryptoV2.Version,
            Algorithm = FireflyCryptoV2.Algorithm,
            DeviceId = identity.DeviceId,
            SubscriptionId = subscriptionId,
            EphemeralPublicKey = FireflyCryptoV2.Base64UrlEncode(ephemeral.ExportSubjectPublicKeyInfo()),
            Salt = FireflyCryptoV2.Base64UrlEncode(salt),
            Nonce = FireflyCryptoV2.Base64UrlEncode(nonce),
            RequestId = challenge,
            IssuedAt = issuedAt,
            ExpiresAt = issuedAt + FireflyCryptoV2.ResponseTtlSeconds,
        };
        var input = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[input.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, input, ciphertext, tag, FireflyCryptoV2.BuildAad(envelope, challenge));
        envelope.Ciphertext = FireflyCryptoV2.Base64UrlEncode([.. ciphertext, .. tag]);
        return envelope;
    }
}
