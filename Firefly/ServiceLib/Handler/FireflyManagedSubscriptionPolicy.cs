namespace ServiceLib.Handler;

/// <summary>
/// Applies the desktop-side handling rules for subscriptions provisioned by the
/// Firefly Worker. This is a UI and accidental-disclosure boundary, not a DRM
/// boundary: a local VPN client must still materialize an outbound at runtime.
/// </summary>
public static class FireflyManagedSubscriptionPolicy
{
    public const string ManagedSubscriptionMemoPrefix = "firefly-worker:";
    public static string ProtectedMessage => ResUI.TbFireflyManagedSubscriptionProtected;
    public static string ProtectedNodeRemarks => ResUI.TbFireflyProtectedNode;
    public static string ProtectedAddress => ResUI.TbFireflyProtectedAddress;

    public static bool IsManagedSubscription(SubItem? item)
    {
        return item?.Memo?.StartsWith(ManagedSubscriptionMemoPrefix, StringComparison.Ordinal) == true;
    }

    public static string GetSourceId(SubItem? item)
    {
        return IsManagedSubscription(item)
            ? item!.Memo![ManagedSubscriptionMemoPrefix.Length..]
            : string.Empty;
    }

    public static async Task<HashSet<string>> GetManagedSubscriptionIdsAsync()
    {
        var subscriptions = await AppManager.Instance.SubItems() ?? [];
        return subscriptions
            .Where(IsManagedSubscription)
            .Select(item => item.Id)
            .Where(id => id.IsNotEmpty())
            .ToHashSet(StringComparer.Ordinal);
    }

    public static async Task<bool> IsManagedSubscriptionIdAsync(string? subId)
    {
        if (subId.IsNullOrEmpty())
        {
            return false;
        }

        return IsManagedSubscription(await AppManager.Instance.GetSubItem(subId));
    }

    public static bool IsManagedProfile(ProfileItem? item, ISet<string> managedSubscriptionIds)
    {
        return item != null && item.Subid.IsNotEmpty() && managedSubscriptionIds.Contains(item.Subid);
    }

    /// <summary>
    /// Detects a managed node both directly and through PolicyGroup/ProxyChain
    /// child references, so an indirect group cannot be used as an export bypass.
    /// </summary>
    public static async Task<bool> ContainsManagedProfileAsync(ProfileItem? item)
    {
        if (item is null)
        {
            return false;
        }

        var managedSubscriptionIds = await GetManagedSubscriptionIdsAsync();
        return await ContainsManagedProfileAsync(item, managedSubscriptionIds, new HashSet<string>());
    }

    public static async Task<bool> ContainsManagedProfilesAsync(IEnumerable<ProfileItem>? items)
    {
        if (items is null)
        {
            return false;
        }

        var managedSubscriptionIds = await GetManagedSubscriptionIdsAsync();
        var visited = new HashSet<string>();
        foreach (var item in items)
        {
            if (await ContainsManagedProfileAsync(item, managedSubscriptionIds, visited))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<string> GetSafeSummaryAsync(ProfileItem item)
    {
        return await ContainsManagedProfileAsync(item)
            // Keep the subscription-provided alias (which commonly carries the
            // country/region) while never showing the endpoint in status or
            // startup notifications.
            ? $"[{item.ConfigType}] {item.Remarks}"
            : item.GetSummary();
    }

    public static void MaskProfileForDisplay(ProfileItemModel profile, ISet<string> managedSubscriptionIds)
    {
        if (!IsManagedProfile(new ProfileItem { Subid = profile.Subid }, managedSubscriptionIds))
        {
            return;
        }

        profile.IsFireflyManaged = true;
        profile.Address = ProtectedAddress;
        profile.Port = 0;
        profile.Network = string.Empty;
        profile.StreamSecurity = string.Empty;
        profile.SubRemarks = Global.AppName;
    }

    private static async Task<bool> ContainsManagedProfileAsync(ProfileItem item, ISet<string> managedSubscriptionIds, ISet<string> visited)
    {
        if (IsManagedProfile(item, managedSubscriptionIds))
        {
            return true;
        }

        if (!item.ConfigType.IsGroupType() || !visited.Add(item.IndexId))
        {
            return false;
        }

        var children = await GroupProfileManager.GetChildProfileItems(item);
        return await ContainsManagedProfilesAsync(children.Items, managedSubscriptionIds, visited);
    }

    private static async Task<bool> ContainsManagedProfilesAsync(IEnumerable<ProfileItem> items, ISet<string> managedSubscriptionIds, ISet<string> visited)
    {
        foreach (var item in items)
        {
            if (await ContainsManagedProfileAsync(item, managedSubscriptionIds, visited))
            {
                return true;
            }
        }

        return false;
    }
}
