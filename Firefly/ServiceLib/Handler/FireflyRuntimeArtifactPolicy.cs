namespace ServiceLib.Handler;

/// <summary>
/// Keeps generated core configurations out of the install directory and removes
/// them as soon as the core that needs them stops.
/// </summary>
public static class FireflyRuntimeArtifactPolicy
{
    private const string RuntimeDirectoryName = "firefly-accelerator";

    public static string GetRuntimeConfigDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            RuntimeDirectoryName,
            "binConfigs");
        Directory.CreateDirectory(directory);

        // macOS does not guarantee a private ACL when the user has an unusual
        // umask. Explicitly restrict this directory to its owner there.
        if (Utils.IsMacOS())
        {
            try
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch
            {
                // The directory is still under the user's Library/Application Support.
            }
        }
        return directory;
    }

    public static void CleanupGeneratedConfig(string configPath)
    {
        try
        {
            var directory = Path.GetFullPath(GetRuntimeConfigDirectory());
            var candidate = Path.GetFullPath(configPath);
            if (candidate.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && Path.GetFileName(candidate).StartsWith("config", StringComparison.OrdinalIgnoreCase)
                && candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(candidate);
            }
        }
        catch
        {
            // Cleanup must not interfere with stopping the proxy core.
        }
    }

    public static void CleanupAllGeneratedConfigs()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(GetRuntimeConfigDirectory(), "config*.json", SearchOption.TopDirectoryOnly))
            {
                CleanupGeneratedConfig(file);
            }
        }
        catch
        {
            // Best effort for files still held by a just-stopped core.
        }
    }
}
