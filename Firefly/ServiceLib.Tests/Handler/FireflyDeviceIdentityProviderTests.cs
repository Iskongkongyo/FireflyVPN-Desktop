using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Handler;

public class FireflyDeviceIdentityProviderTests
{
    [Fact]
    public void Collect_ShouldProduceAWindowsHardwareIdentity()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var result = FireflyDeviceIdentityProvider.Collect();

        result.Platform.Should().Be("windows");
        result.IdentityHash.Should().MatchRegex("^[a-f0-9]{64}$");
        result.IdentityLevel.Should().BeOneOf("tpm-ek", "smbios");
    }

    [Fact]
    public void HashIdentityMaterial_ShouldReturnCanonicalSha256()
    {
        var result = FireflyDeviceIdentityProvider.HashIdentityMaterial("hardware-identity");

        result.Should().Be("9a713d3f59ceb90caf57a70ea706ee133b7ae9af303bb59c5bb2dac2f2d1e59f");
    }

    [Fact]
    public void ParseTpmReadPublicResponse_ShouldHashThePublicArea()
    {
        var response = Convert.FromHexString(
            "80010000001100000000" + // response header
            "0005" +                 // TPM2B_PUBLIC size
            "0102030405");

        var result = FireflyDeviceIdentityProvider.ParseTpmReadPublicResponse(response);

        result.Should().Be("74f81fe167d99b4cb41d6d0ccda82278caee9f3e2f25d5e5a3936ff3dcec60d0");
    }

    [Fact]
    public void BuildSmbiosIdentityMaterial_ShouldUseUuidAndSerials()
    {
        var table = new List<byte>();
        AddStructure(table, 0, [0, 0, 0, 1], "BIOS-123");
        AddStructure(table, 1,
            [0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8,
                9, 10, 11, 12, 13, 14, 15, 16]);
        AddStructure(table, 2, [0, 0, 0, 1], "BOARD-456");
        AddStructure(table, 127, []);
        var raw = new byte[8 + table.Count];
        raw[1] = 3;
        raw[2] = 6;
        BitConverter.GetBytes(table.Count).CopyTo(raw, 4);
        table.CopyTo(raw, 8);

        var result = FireflyDeviceIdentityProvider.BuildSmbiosIdentityMaterial(raw);

        result.Should().Be("0102030405060708090a0b0c0d0e0f10|BIOS-123|BOARD-456");
    }

    [Fact]
    public void BuildMacIdentityMaterial_ShouldNormalizeIoregValues()
    {
        const string output = """
            | |   "IOPlatformUUID" = "12345678-abcd-ef01-2345-6789abcdef01"
            | |   "IOPlatformSerialNumber" = "c02example"
            """;

        var result = FireflyDeviceIdentityProvider.BuildMacIdentityMaterial(output);

        result.Should().Be("12345678-ABCD-EF01-2345-6789ABCDEF01|C02EXAMPLE");
    }

    private static void AddStructure(
        List<byte> table,
        byte type,
        IReadOnlyCollection<byte> formattedTail,
        params string[] strings)
    {
        table.Add(type);
        table.Add(checked((byte)(4 + formattedTail.Count)));
        table.Add(0);
        table.Add(0);
        table.AddRange(formattedTail);
        foreach (var value in strings)
        {
            table.AddRange(Encoding.UTF8.GetBytes(value));
            table.Add(0);
        }
        table.Add(0);
        if (strings.Length == 0)
        {
            table.Add(0);
        }
    }
}
