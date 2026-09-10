namespace ServiceLib.Models.Entities;

[Serializable]
public class SubItem : INotifyPropertyChanged
{
    [field: NonSerialized]
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isUnread;

    [PrimaryKey]
    public string Id { get; set; }

    public string Remarks { get; set; }

    public string Url { get; set; }

    public string MoreUrl { get; set; }

    public bool Enabled { get; set; } = true;

    public string UserAgent { get; set; } = string.Empty;

    public int Sort { get; set; }

    public string? Filter { get; set; }

    public int AutoUpdateInterval { get; set; }

    public long UpdateTime { get; set; }

    public string? ConvertTarget { get; set; }

    public string? PrevProfile { get; set; }

    public string? NextProfile { get; set; }

    public int? PreSocksPort { get; set; }

    public string? Memo { get; set; }

    public ECoreType? CustomCoreType { get; set; }

    [Ignore]
    public bool IsUnread
    {
        get => _isUnread;
        set
        {
            if (_isUnread == value)
            {
                return;
            }

            _isUnread = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUnread)));
        }
    }
}
