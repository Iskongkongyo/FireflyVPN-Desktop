namespace ServiceLib.Models.Entities;

/// <summary>
/// Encrypted-at-rest payload for a Worker-managed profile. The ProfileItem row
/// retains only the minimum metadata required to list and group the node.
/// </summary>
public class FireflyManagedProfileSecret
{
    [PrimaryKey]
    public string IndexId { get; set; } = string.Empty;

    public string Ciphertext { get; set; } = string.Empty;
}
