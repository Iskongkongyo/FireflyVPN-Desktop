using System.Buffers.Binary;

namespace ServiceLib.Handler;

/// <summary>
/// Collects stable hardware identity material and hashes it before it leaves
/// the client. Raw hardware identifiers are never persisted or transmitted.
/// </summary>
public static class FireflyDeviceIdentityProvider
{
    private const uint RawSmbiosProvider = 0x52534D42; // "RSMB"
    private const uint TpmRsaEkHandle = 0x81010001;
    private const uint TpmEccEkHandle = 0x81010002;

    public static FireflyDeviceMaterial Collect()
    {
        if (Utils.IsWindows())
        {
            return CollectWindows();
        }

        if (Utils.IsMacOS())
        {
            return CollectMacOS();
        }

        throw new PlatformNotSupportedException(
            "Firefly device identity currently supports Windows and macOS desktops.");
    }

    internal static string HashIdentityMaterial(string identityMaterial)
    {
        if (identityMaterial.IsNullOrEmpty())
        {
            throw new CryptographicException("Device identity material is empty.");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityMaterial)))
            .ToLowerInvariant();
    }

    internal static string? ParseTpmReadPublicResponse(ReadOnlySpan<byte> response)
    {
        const int responseHeaderLength = 10;
        if (response.Length < responseHeaderLength + 2
            || BinaryPrimitives.ReadUInt32BigEndian(response[2..6]) != response.Length
            || BinaryPrimitives.ReadUInt32BigEndian(response[6..10]) != 0)
        {
            return null;
        }

        var publicAreaLength = BinaryPrimitives.ReadUInt16BigEndian(response[10..12]);
        if (publicAreaLength == 0 || responseHeaderLength + 2 + publicAreaLength > response.Length)
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(response.Slice(12, publicAreaLength)))
            .ToLowerInvariant();
    }

    internal static string BuildSmbiosIdentityMaterial(ReadOnlySpan<byte> rawSmbios)
    {
        if (rawSmbios.Length < 8)
        {
            throw new CryptographicException("The SMBIOS table is truncated.");
        }

        var tableLength = BinaryPrimitives.ReadUInt32LittleEndian(rawSmbios[4..8]);
        if (tableLength == 0 || tableLength > rawSmbios.Length - 8)
        {
            throw new CryptographicException("The SMBIOS table has an invalid length.");
        }

        var table = rawSmbios.Slice(8, checked((int)tableLength));
        var systemUuid = string.Empty;
        var biosSerial = string.Empty;
        var baseBoardSerial = string.Empty;

        var offset = 0;
        while (offset + 4 <= table.Length)
        {
            var type = table[offset];
            var structureLength = table[offset + 1];
            if (structureLength < 4 || offset + structureLength > table.Length)
            {
                break;
            }

            var stringsStart = offset + structureLength;
            var structureEnd = FindStructureEnd(table, stringsStart);
            if (structureEnd < 0)
            {
                break;
            }

            var structure = table.Slice(offset, structureLength);
            var strings = table.Slice(stringsStart, structureEnd - stringsStart);
            switch (type)
            {
                case 0 when structureLength > 7:
                    biosSerial = NormalizeHardwareValue(GetSmbiosString(strings, structure[7]));
                    break;
                case 1 when structureLength >= 24:
                    var uuid = structure.Slice(8, 16);
                    if (!IsAll(uuid, 0x00) && !IsAll(uuid, 0xFF))
                    {
                        // Raw bytes avoid SMBIOS-version byte-order ambiguity while
                        // remaining stable for the same physical firmware table.
                        systemUuid = Convert.ToHexString(uuid).ToLowerInvariant();
                    }
                    break;
                case 2 when structureLength > 7:
                    baseBoardSerial = NormalizeHardwareValue(GetSmbiosString(strings, structure[7]));
                    break;
            }

            if (type == 127)
            {
                break;
            }
            offset = structureEnd + 2;
        }

        if (systemUuid.IsNullOrEmpty() && biosSerial.IsNullOrEmpty() && baseBoardSerial.IsNullOrEmpty())
        {
            throw new CryptographicException("SMBIOS does not contain usable device identifiers.");
        }

        return string.Join('|', systemUuid, biosSerial, baseBoardSerial);
    }

    internal static string BuildMacIdentityMaterial(string ioregOutput)
    {
        var platformUuid = NormalizeHardwareValue(ReadIoregString(ioregOutput, "IOPlatformUUID"));
        var serialNumber = NormalizeHardwareValue(ReadIoregString(ioregOutput, "IOPlatformSerialNumber"));
        if (platformUuid.IsNullOrEmpty() || serialNumber.IsNullOrEmpty())
        {
            throw new CryptographicException(
                "IORegistry does not contain the required Mac platform identifiers.");
        }

        return platformUuid + "|" + serialNumber;
    }

    private static FireflyDeviceMaterial CollectWindows()
    {
        var ekPublicHash = TryReadTpmEkPublicHash();
        if (ekPublicHash.IsNotEmpty())
        {
            // The platform rule uses SHA256(EK public key) as the identity
            // material, followed by the common identity-material hash.
            return new FireflyDeviceMaterial(
                "windows", HashIdentityMaterial(ekPublicHash), "tpm-ek");
        }

        var size = GetSystemFirmwareTable(RawSmbiosProvider, 0, null, 0);
        if (size is 0 or > (16 * 1024 * 1024))
        {
            throw new CryptographicException("Unable to read the SMBIOS firmware table.");
        }

        var data = new byte[size];
        if (GetSystemFirmwareTable(RawSmbiosProvider, 0, data, size) != size)
        {
            throw new CryptographicException("Unable to read the complete SMBIOS firmware table.");
        }

        return new FireflyDeviceMaterial(
            "windows", HashIdentityMaterial(BuildSmbiosIdentityMaterial(data)), "smbios");
    }

    private static FireflyDeviceMaterial CollectMacOS()
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/usr/sbin/ioreg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        foreach (var argument in new[] { "-r", "-d", "1", "-c", "IOPlatformExpertDevice" })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new CryptographicException("Unable to read the macOS IORegistry platform identity.");
        }

        return new FireflyDeviceMaterial(
            "macos", HashIdentityMaterial(BuildMacIdentityMaterial(output)), "iokit-platform");
    }

    private static string? TryReadTpmEkPublicHash()
    {
        var parameters = new TbsContextParams2
        {
            Version = 2,
            Flags = 0x00000004, // includeTpm20
        };
        if (TbsiContextCreate(ref parameters, out var context) != 0)
        {
            return null;
        }

        try
        {
            var command = new byte[14];
            foreach (var handle in new[] { TpmRsaEkHandle, TpmEccEkHandle })
            {
                BinaryPrimitives.WriteUInt16BigEndian(command, 0x8001); // TPM_ST_NO_SESSIONS
                BinaryPrimitives.WriteUInt32BigEndian(command[2..], (uint)command.Length);
                BinaryPrimitives.WriteUInt32BigEndian(command[6..], 0x00000173); // TPM2_CC_ReadPublic
                BinaryPrimitives.WriteUInt32BigEndian(command[10..], handle);

                var response = new byte[4096];
                var responseLength = (uint)response.Length;
                if (TbsipSubmitCommand(
                        context,
                        0,
                        200,
                        command,
                        (uint)command.Length,
                        response,
                        ref responseLength) != 0
                    || responseLength > response.Length)
                {
                    continue;
                }

                var publicHash = ParseTpmReadPublicResponse(response.AsSpan(0, (int)responseLength));
                if (publicHash.IsNotEmpty())
                {
                    return publicHash;
                }
            }
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
        finally
        {
            TbsipContextClose(context);
        }

        return null;
    }

    private static int FindStructureEnd(ReadOnlySpan<byte> table, int stringsStart)
    {
        for (var index = stringsStart; index + 1 < table.Length; index++)
        {
            if (table[index] == 0 && table[index + 1] == 0)
            {
                return index;
            }
        }
        return -1;
    }

    private static string GetSmbiosString(ReadOnlySpan<byte> strings, byte oneBasedIndex)
    {
        if (oneBasedIndex == 0)
        {
            return string.Empty;
        }

        var currentIndex = 1;
        var start = 0;
        for (var index = 0; index <= strings.Length; index++)
        {
            if (index != strings.Length && strings[index] != 0)
            {
                continue;
            }

            if (currentIndex == oneBasedIndex)
            {
                return Encoding.UTF8.GetString(strings.Slice(start, index - start));
            }
            currentIndex++;
            start = index + 1;
        }
        return string.Empty;
    }

    private static string ReadIoregString(string output, string key)
    {
        var match = Regex.Match(
            output,
            $"\\\"{Regex.Escape(key)}\\\"\\s*=\\s*\\\"(?<value>[^\\\"]+)\\\"",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : string.Empty;
    }

    private static string NormalizeHardwareValue(string? value)
    {
        var normalized = Regex.Replace(value.TrimEx(), @"\s+", " ").ToUpperInvariant();
        if (normalized.IsNullOrEmpty()
            || normalized.All(character => character is '0' or 'F' or '-' or ' ')
            || normalized is "NONE" or "DEFAULT STRING" or "SYSTEM SERIAL NUMBER"
                or "TO BE FILLED BY O.E.M." or "NOT APPLICABLE" or "UNKNOWN")
        {
            return string.Empty;
        }
        return normalized;
    }

    private static bool IsAll(ReadOnlySpan<byte> value, byte expected)
    {
        foreach (var item in value)
        {
            if (item != expected)
            {
                return false;
            }
        }
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(
        uint firmwareTableProviderSignature,
        uint firmwareTableId,
        [Out] byte[]? firmwareTableBuffer,
        uint bufferSize);

    [DllImport("tbs.dll", EntryPoint = "Tbsi_Context_Create")]
    private static extern uint TbsiContextCreate(
        ref TbsContextParams2 contextParams,
        out IntPtr context);

    [DllImport("tbs.dll", EntryPoint = "Tbsip_Submit_Command")]
    private static extern uint TbsipSubmitCommand(
        IntPtr context,
        uint locality,
        uint priority,
        byte[] commandBuffer,
        uint commandBufferLength,
        [Out] byte[] resultBuffer,
        ref uint resultBufferLength);

    [DllImport("tbs.dll", EntryPoint = "Tbsip_Context_Close")]
    private static extern uint TbsipContextClose(IntPtr context);

    [StructLayout(LayoutKind.Sequential)]
    private struct TbsContextParams2
    {
        public uint Version;
        public uint Flags;
    }
}

public sealed record FireflyDeviceMaterial(
    string Platform,
    string IdentityHash,
    string IdentityLevel);
