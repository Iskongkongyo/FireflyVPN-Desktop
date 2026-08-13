using System.Diagnostics;
using System.Security.Cryptography;

namespace ServiceLib.Handler;

/// <summary>
/// Protects the credentials of Firefly Worker-managed nodes at rest. This does
/// not attempt to hide the active core configuration while a VPN is running.
/// </summary>
public static class FireflyManagedProfileStorage
{
    private const string MacKeychainService = "liuying-accelerator-managed-profiles";
    private const string MacKeychainAccount = "master-key-v1";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("liuying-accelerator/profile/v1");

    public static async Task<ProfileItem> PrepareForStorageAsync(ProfileItem profile)
    {
        if (!await FireflyManagedSubscriptionPolicy.IsManagedSubscriptionIdAsync(profile.Subid))
        {
            await SQLiteHelper.Instance.DeleteAsync(new FireflyManagedProfileSecret { IndexId = profile.IndexId });
            return profile;
        }

        var payload = JsonUtils.Serialize(profile, false);
        var secret = new FireflyManagedProfileSecret
        {
            IndexId = profile.IndexId,
            Ciphertext = Encrypt(payload)
        };
        await SQLiteHelper.Instance.ReplaceAsync(secret);
        return CreateMetadataOnlyCopy(profile);
    }

    public static async Task<ProfileItem> HydrateAsync(ProfileItem profile)
    {
        var secret = await SQLiteHelper.Instance.TableAsync<FireflyManagedProfileSecret>()
            .FirstOrDefaultAsync(item => item.IndexId == profile.IndexId);
        if (secret?.Ciphertext.IsNullOrEmpty() != false)
        {
            return profile;
        }

        var hydrated = JsonUtils.Deserialize<ProfileItem>(Decrypt(secret.Ciphertext));
        if (hydrated is null)
        {
            throw new CryptographicException("Unable to decrypt a managed profile.");
        }

        // Keep ownership and alias metadata authoritative from the normal row.
        hydrated.IndexId = profile.IndexId;
        hydrated.Subid = profile.Subid;
        hydrated.IsSub = profile.IsSub;
        hydrated.Remarks = profile.Remarks;
        hydrated.CoreType = profile.CoreType;
        hydrated.ConfigType = profile.ConfigType;
        hydrated.ConfigVersion = profile.ConfigVersion;
        hydrated.PreSocksPort = profile.PreSocksPort;
        hydrated.DisplayLog = profile.DisplayLog;
        return hydrated;
    }

    public static async Task<List<ProfileItem>> HydrateAsync(IEnumerable<ProfileItem> profiles)
    {
        var result = new List<ProfileItem>();
        foreach (var profile in profiles)
        {
            result.Add(await HydrateAsync(profile));
        }
        return result;
    }

    /// <summary>
    /// Converts profiles written by earlier desktop builds after the secret
    /// table is available. It is idempotent: already protected rows have an
    /// empty address and are skipped.
    /// </summary>
    public static async Task ProtectExistingProfilesAsync()
    {
        var profiles = await SQLiteHelper.Instance.TableAsync<ProfileItem>().ToListAsync();
        foreach (var profile in profiles)
        {
            if (profile.Address.IsNullOrEmpty()
                || !await FireflyManagedSubscriptionPolicy.IsManagedSubscriptionIdAsync(profile.Subid))
            {
                continue;
            }
            await SQLiteHelper.Instance.UpdateAsync(profile);
        }
    }

    private static ProfileItem CreateMetadataOnlyCopy(ProfileItem profile)
    {
        var stored = JsonUtils.DeepCopy(profile);
        stored.Address = string.Empty;
        stored.Port = 0;
        stored.Password = string.Empty;
        stored.Username = string.Empty;
        stored.Network = string.Empty;
        stored.StreamSecurity = string.Empty;
        stored.AllowInsecure = string.Empty;
        stored.Sni = string.Empty;
        stored.Alpn = string.Empty;
        stored.Fingerprint = string.Empty;
        stored.PublicKey = string.Empty;
        stored.ShortId = string.Empty;
        stored.SpiderX = string.Empty;
        stored.Mldsa65Verify = string.Empty;
        stored.Cert = string.Empty;
        stored.CertSha = string.Empty;
        stored.EchConfigList = string.Empty;
        stored.VerifyPeerCertByName = string.Empty;
        stored.Finalmask = string.Empty;
        stored.ProtoExtra = string.Empty;
        stored.TransportExtra = string.Empty;
#pragma warning disable CS0618 // These legacy fields can still contain endpoint credentials in old databases.
        stored.HeaderType = string.Empty;
        stored.RequestHost = string.Empty;
        stored.Path = string.Empty;
        stored.Extra = string.Empty;
        stored.Ports = string.Empty;
        stored.Flow = string.Empty;
        stored.Id = string.Empty;
        stored.Security = string.Empty;
#pragma warning restore CS0618
        return stored;
    }

    private static string Encrypt(string plainText)
    {
        var input = Encoding.UTF8.GetBytes(plainText);
        if (Utils.IsWindows())
        {
            return "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(input, Entropy, DataProtectionScope.CurrentUser));
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

        throw new PlatformNotSupportedException("Managed profile encryption is supported on Windows and macOS only.");
    }

    private static string Decrypt(string ciphertext)
    {
        if (ciphertext.StartsWith("dpapi:", StringComparison.Ordinal) && Utils.IsWindows())
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ciphertext[6..]), Entropy, DataProtectionScope.CurrentUser));
        }

        if (ciphertext.StartsWith("keychain:", StringComparison.Ordinal) && Utils.IsMacOS())
        {
            var payload = Convert.FromBase64String(ciphertext[9..]);
            var plain = new byte[payload.Length - 28];
            using var aes = new AesGcm(GetMacKey(), 16);
            aes.Decrypt(payload[..12], payload[12..28], payload[28..], plain, Entropy);
            return Encoding.UTF8.GetString(plain);
        }

        throw new CryptographicException("Managed profile ciphertext cannot be opened on this device.");
    }

    private static byte[] GetMacKey()
    {
        var value = RunSecurity("find-generic-password", "-s", MacKeychainService, "-a", MacKeychainAccount, "-w");
        if (value.IsNullOrEmpty())
        {
            value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            RunSecurity("add-generic-password", "-U", "-s", MacKeychainService, "-a", MacKeychainAccount, "-w", value);
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
