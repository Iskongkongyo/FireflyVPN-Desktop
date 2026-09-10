namespace ServiceLib.ViewModels;

public partial class SubSettingViewModel : MyReactiveObject
{
    public Interaction<string, bool> ShowYesNoInteraction { get; } = new();
    public Interaction<string, RxVoid> ShareSubInteraction { get; } = new();

    public BulkObservableCollection<SubItem> SubItems { get; } = [];

    [Reactive]
    public partial SubItem SelectedSource { get; set; }

    public IList<SubItem> SelectedSources { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> SubAddCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubDeleteCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubEditCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubShareCmd { get; }
    public bool IsModified { get; set; }

    public SubSettingViewModel()
    {
        _config = AppManager.Instance.Config;

        var canEditRemove = this.WhenAnyValue(
           x => x.SelectedSource,
           selectedSource => selectedSource != null && !selectedSource.Id.IsNullOrEmpty());

        SubAddCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await EditSubAsync(true);
        });
        SubDeleteCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await DeleteSubAsync();
        }, canEditRemove);
        SubEditCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await EditSubAsync(false);
        }, canEditRemove);
        SubShareCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ShareSubAsync();
        }, canEditRemove);

        _ = Init();
    }

    private async Task Init()
    {
        SelectedSource = new();

        await RefreshSubItems();
    }

    public async Task RefreshSubItems()
    {
        SubItems.Clear();
        SubItems.AddRange(await AppManager.Instance.SubItems());
    }

    public async Task EditSubAsync(bool blNew)
    {
        SubItem item;
        if (blNew)
        {
            item = new();
        }
        else
        {
            item = await AppManager.Instance.GetSubItem(SelectedSource?.Id);
            if (item is null)
            {
                return;
            }
            if (FireflyManagedSubscriptionPolicy.IsManagedSubscription(item))
            {
                NoticeManager.Instance.Enqueue(FireflyManagedSubscriptionPolicy.ProtectedMessage);
                return;
            }
        }
        var subEditViewModel = new SubEditViewModel(item);
        if (await AppManager.Instance.WindowDialog.ShowDialogAsync(subEditViewModel) == true)
        {
            await RefreshSubItems();
            IsModified = true;
        }
    }

    private async Task DeleteSubAsync()
    {
        var selectedItems = SelectedSources ?? [SelectedSource];
        if (selectedItems.Any(FireflyManagedSubscriptionPolicy.IsManagedSubscription))
        {
            NoticeManager.Instance.Enqueue(FireflyManagedSubscriptionPolicy.ProtectedMessage);
            return;
        }
        if (await ShowYesNoInteraction.Handle(ResUI.RemoveServer) == false)
        {
            return;
        }

        foreach (var it in selectedItems)
        {
            await ConfigHandler.DeleteSubItem(_config, it.Id);
        }
        await RefreshSubItems();
        NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
        IsModified = true;
    }

    private async Task ShareSubAsync()
    {
        var item = await AppManager.Instance.GetSubItem(SelectedSource?.Id);
        if (item is null)
        {
            return;
        }
        if (FireflyManagedSubscriptionPolicy.IsManagedSubscription(item))
        {
            NoticeManager.Instance.Enqueue(FireflyManagedSubscriptionPolicy.ProtectedMessage);
            return;
        }

        await ShareSubInteraction.Handle(item.Url);
    }
}
