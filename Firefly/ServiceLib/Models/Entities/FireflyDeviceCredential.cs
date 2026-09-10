namespace ServiceLib.Models.Entities;

/// <summary>
/// The encrypted local credential used for the anonymous Firefly device.
/// Device identifiers, tokens, and private keys all remain inside Ciphertext.
/// </summary>
public class FireflyDeviceCredential
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string Ciphertext { get; set; } = string.Empty;
}
