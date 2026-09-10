using System.Diagnostics;

namespace ServiceLib.Handler;

public sealed class FireflyDeviceIdentity
{
    public string DeviceId { get; set; } = string.Empty;
    public string IdentityHash { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string PublicKeySpki { get; set; } = string.Empty;
    public string PrivateKeyPkcs8 { get; set; } = string.Empty;
    public string DeviceToken { get; set; } = string.Empty;
}

/// <summary>
/// Persists the anonymous device credential using the operating system's
/// user-scoped secret protection. The Worker token and ECDH private key are
/// never stored as plaintext in guiNConfig.json or SQLite.
/// </summary>
public static class FireflyDeviceIdentityStore
{
    private const string CredentialId = "firefly-device-v2";
    private const string MacKeychainService = "liuying-accelerator-device-identity";
    private const string MacKeychainAccount = "master-key-v1";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("liuying-accelerator/device/v2");

    public static async Task<FireflyDeviceIdentity?> LoadAsync()
    {
        var row = await SQLiteHelper.Instance.TableAsync<FireflyDeviceCredential>()
            .FirstOrDefaultAsync(item => item.Id == CredentialId);
        if (row?.Ciphertext.IsNullOrEmpty() != false)
        {
            return null;
        }

        var identity = JsonUtils.Deserialize<FireflyDeviceIdentity>(Decrypt(row.Ciphertext));
        if (identity is null
            || !Regex.IsMatch(identity.DeviceId, "^[a-f0-9]{64}$")
            || !Regex.IsMatch(identity.IdentityHash, "^[a-f0-9]{64}$")
            || identity.PublicKeySpki.IsNullOrEmpty()
            || identity.PrivateKeyPkcs8.IsNullOrEmpty()
            || (identity.DeviceToken.IsNotEmpty()
                && FireflyCryptoV2.Base64UrlDecode(identity.DeviceToken).Length != 32))
        {
            throw new CryptographicException("The Firefly device credential is invalid.");
        }
        return identity;
    }

    public static async Task SaveAsync(FireflyDeviceIdentity identity)
    {
        var payload = JsonUtils.Serialize(identity, false);
        if (payload.IsNullOrEmpty())
        {
            throw new CryptographicException("Unable to serialize the Firefly device credential.");
        }

        await SQLiteHelper.Instance.ReplaceAsync(new FireflyDeviceCredential
        {
            Id = CredentialId,
            Ciphertext = Encrypt(payload),
        });
    }

    public static async Task ResetAsync()
    {
        await SQLiteHelper.Instance.DeleteAsync(new FireflyDeviceCredential { Id = CredentialId });
    }

    private static string Encrypt(string plainText)
    {
        var input = Encoding.UTF8.GetBytes(plainText);
        try
        {
            if (Utils.IsWindows())
            {
                return "dpapi:" + Convert.ToBase64String(
                    ProtectedData.Protect(input, Entropy, DataProtectionScope.CurrentUser));
            }

            if (Utils.IsMacOS())
            {
                var nonce = RandomNumberGenerator.GetBytes(12);
                var cipher = new byte[input.Length];
                var tag = new byte[16];
                using var aes = new AesGcm(GetMacKey(), 16);
                aes.Encrypt(nonce, input, cipher, tag, Entropy);
                return "keychain:" + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
            }

            throw new PlatformNotSupportedException(
                "The Firefly device protocol currently supports Windows and macOS desktops.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private static string Decrypt(string ciphertext)
    {
        if (ciphertext.StartsWith("dpapi:", StringComparison.Ordinal) && Utils.IsWindows())
        {
            var plain = ProtectedData.Unprotect(
                Convert.FromBase64String(ciphertext[6..]), Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        if (ciphertext.StartsWith("keychain:", StringComparison.Ordinal) && Utils.IsMacOS())
        {
            var payload = Convert.FromBase64String(ciphertext[9..]);
            if (payload.Length < 28)
            {
                throw new CryptographicException("The Firefly device credential is truncated.");
            }
            var plain = new byte[payload.Length - 28];
            try
            {
                using var aes = new AesGcm(GetMacKey(), 16);
                aes.Decrypt(payload[..12], payload[12..28], payload[28..], plain, Entropy);
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
                CryptographicOperations.ZeroMemory(payload);
            }
        }

        throw new CryptographicException("The Firefly device credential cannot be opened on this device.");
    }

    private static byte[] GetMacKey()
    {
        var value = RunSecurity("find-generic-password", "-s", MacKeychainService, "-a", MacKeychainAccount, "-w");
        if (value.IsNullOrEmpty())
        {
            value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            // `security add-generic-password` normally has no stdout, so verify
            // the value by reading it back instead of inspecting command output.
            RunSecurity("add-generic-password", "-U", "-s", MacKeychainService,
                "-a", MacKeychainAccount, "-w", value);
            var persisted = RunSecurity("find-generic-password", "-s", MacKeychainService,
                "-a", MacKeychainAccount, "-w");
            if (persisted.IsNullOrEmpty())
            {
                throw new CryptographicException("Unable to persist the Firefly device key in Keychain.");
            }
            value = persisted;
        }
        return SHA256.HashData(Convert.FromBase64String(value));
    }

    private static string RunSecurity(params string[] args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/security",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }
        process.Start();
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : string.Empty;
    }
}
