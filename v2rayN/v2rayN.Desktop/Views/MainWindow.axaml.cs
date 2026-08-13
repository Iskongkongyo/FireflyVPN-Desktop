using Avalonia.Controls.Notifications;
using Avalonia.VisualTree;
using DialogHostAvalonia;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Manager;

namespace v2rayN.Desktop.Views;

public partial class MainWindow : WindowBase<MainWindowViewModel>
{
    private enum NewUserGuideTarget
    {
        SubscriptionGroups,
        Nodes,
        SystemProxy,
        Routing,
        EnableTun,
        RealDelay,
        MixedTest,
    }

    private sealed record NewUserGuideStep(string Title, string Content, NewUserGuideTarget Target);

    private static readonly NewUserGuideStep[] _newUserGuideSteps =
    [
        new("先看订阅分组", "顶部写着“所有”的这一行，是节点所在的订阅分组。点不同分组，就能查看它们各自的节点。", NewUserGuideTarget.SubscriptionGroups),
        new("这里是节点列表", "下面的表格就是节点区域。地址、端口、协议和延迟等信息都在这里看。", NewUserGuideTarget.Nodes),
        new("选择要用的节点", "已为你选中第一条节点。请在该节点上右键，然后选择“设为活动”（就是选中节点），即可开始使用或切换它。", NewUserGuideTarget.Nodes),
        new("系统代理怎么选", "这里决定系统流量怎么走：清除系统代理=关闭代理；自动配置=让系统走当前节点；不改变=不碰系统设置；PAC=按规则分流。", NewUserGuideTarget.SystemProxy),
        new("选择分流规则", "常用 V4-绕过大陆：国内直连、国外走代理；V4-黑名单：只让需要代理的网站走节点；V4-全局：所有流量都走节点。", NewUserGuideTarget.Routing),
        new("需要全局接管时", "打开“启用 TUN”后，更多不支持系统代理的软件也能走节点。首次开启可能需要管理员权限。", NewUserGuideTarget.EnableTun),
        new("快速测真实延迟", "点闪电图标，可以一键测试所有节点的真实连接延迟，方便先挑响应快的节点。", NewUserGuideTarget.RealDelay),
        new("测延迟和速度", "点油表图标，可以一键多线程测试延迟和速度。测试会消耗一点流量，建议按需使用。", NewUserGuideTarget.MixedTest),
    ];

    private static Config _config;
    private readonly SingleReplaceableDisposable _layoutBindingsDisposable = new();
    private readonly WindowNotificationManager? _manager;
    private readonly Dictionary<Guid, Notification> _nodeFetchNotifications = [];
    private CheckUpdateView? _checkUpdateView;
    private BackupAndRestoreView? _backupAndRestoreView;
    private AboutView? _aboutView;
    private bool _blCloseByUser = false;
    private bool _newUserGuideRunning;
    private int _newUserGuideStepIndex;
    private int _newUserGuideDisplayedStepIndex = -1;
    private ComboBox? _newUserGuideOpenedComboBox;
    private ProfilesView? _newUserGuideProfilesView;

    public MainWindow()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this)) { MaxItems = 3, Position = NotificationPosition.TopRight };

        KeyDown += MainWindow_KeyDown;
        menuSettingsSetUWP.Click += MenuSettingsSetUWP_Click;
        menuAbout.Click += MenuAbout_Click;
        menuCheckUpdate.Click += MenuCheckUpdate_Click;
        menuNewUserGuide.Click += (_, _) => StartNewUserGuide();
        btnNewUpdate.Click += MenuCheckUpdate_Click;
        menuBackupAndRestore.Click += MenuBackupAndRestore_Click;
        menuClose.Click += MenuClose_Click;
        btnNewUserGuidePrevious.Click += (_, _) => MoveNewUserGuide(-1);
        btnNewUserGuideNext.Click += (_, _) => MoveNewUserGuide(1);
        btnNewUserGuideSkip.Click += async (_, _) => await FinishNewUserGuideAsync();
        btnNewUserGuidePrevious.PointerEntered += (_, _) => CloseNewUserGuideExamples();
        btnNewUserGuideNext.PointerEntered += (_, _) => CloseNewUserGuideExamples();
        btnNewUserGuideSkip.PointerEntered += (_, _) => CloseNewUserGuideExamples();
        SizeChanged += (_, _) => UpdateNewUserGuideStep();

        conTheme.Content ??= new ThemeSettingView();

        this.WhenActivated(disposables =>
        {
            //servers
            this.BindCommand(ViewModel, vm => vm.AddVmessServerCmd, v => v.menuAddVmessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddVlessServerCmd, v => v.menuAddVlessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddShadowsocksServerCmd, v => v.menuAddShadowsocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSocksServerCmd, v => v.menuAddSocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHttpServerCmd, v => v.menuAddHttpServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTrojanServerCmd, v => v.menuAddTrojanServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHysteria2ServerCmd, v => v.menuAddHysteria2Server).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTuicServerCmd, v => v.menuAddTuicServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddWireguardServerCmd, v => v.menuAddWireguardServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddAnytlsServerCmd, v => v.menuAddAnytlsServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddNaiveServerCmd, v => v.menuAddNaiveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomServerCmd, v => v.menuAddCustomServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomOutboundServerCmd, v => v.menuAddCustomOutboundServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddPolicyGroupServerCmd, v => v.menuAddPolicyGroupServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddProxyChainServerCmd, v => v.menuAddProxyChainServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaClipboardCmd, v => v.menuAddServerViaClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaScanCmd, v => v.menuAddServerViaScan).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaImageCmd, v => v.menuAddServerViaImage).DisposeWith(disposables);

            //sub
            this.BindCommand(ViewModel, vm => vm.SubSettingCmd, v => v.menuSubSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.menuSubUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.menuSubUpdateViaProxy).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateCmd, v => v.menuSubGroupUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateViaProxyCmd, v => v.menuSubGroupUpdateViaProxy).DisposeWith(disposables);

            //setting
            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.menuOptionSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingSettingCmd, v => v.menuRoutingSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DNSSettingCmd, v => v.menuDNSSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.FullConfigTemplateCmd, v => v.menuFullConfigTemplate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GlobalHotkeySettingCmd, v => v.menuGlobalHotkeySetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RebootAsAdminCmd, v => v.menuRebootAsAdmin).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ClearServerStatisticsCmd, v => v.menuClearServerStatistics).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenTheFileLocationCmd, v => v.menuOpenTheFileLocation).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetDefaultCmd, v => v.menuRegionalPresetsDefault).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetRussiaCmd, v => v.menuRegionalPresetsRussia).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetIranCmd, v => v.menuRegionalPresetsIran).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ReloadCmd, v => v.menuReload).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlReloadEnabled, v => v.menuReload.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlNewUpdate, v => v.btnNewUpdate.IsVisible).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.StatusBarViewModel, v => v.contentStatusBarView.Content).DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.InitialNodeFetchCompleted)
                .Where(completed => completed)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => QueueNewUserGuideStart())
                .DisposeWith(disposables);

            _layoutBindingsDisposable.DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.MainGirdOrientation)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(UpdateLayout)
                .DisposeWith(disposables);

            ViewModel.ReadTextFromClipboardInteraction.RegisterHandler(async interaction =>
            {
                var result = await AvaUtils.GetClipboardData(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.ScanScreenInteraction.RegisterHandler(async interaction =>
            {
                ShowHideWindow(false);
                await Task.Delay(200);
                var result = QRCodeAvaloniaUtils.CaptureScreen();
                ShowHideWindow(true);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.BrowseImageFileInteraction.RegisterHandler(async interaction =>
            {
                var result = await UI.OpenFileDialog(null);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.ShowHideWindowInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            AppEvents.SendSnackMsgRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(async content => await DelegateSnackMsg(content))
              .DisposeWith(disposables);

            AppEvents.FireflyNodeFetchNotificationRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(DelegateFireflyNodeFetchNotification)
              .DisposeWith(disposables);

            AppEvents.FireflyUpdateNotificationRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(DelegateFireflyUpdateNotification)
              .DisposeWith(disposables);

            AppEvents.AppExitRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(_ => StorageUI())
              .DisposeWith(disposables);

            AppEvents.ShutdownRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(Shutdown)
              .DisposeWith(disposables);
        });

        if (Utils.IsWindows())
        {
            Title = $"{Utils.GetVersion()} - {(Utils.IsAdministrator() ? ResUI.RunAsAdmin : ResUI.NotRunAsAdmin)}";

            if (!Design.IsDesignMode)
            {
                ThreadPool.RegisterWaitForSingleObject(Program.ProgramStarted, OnProgramStarted, null, -1, false);
                HotkeyManager.Instance.Init(_config, OnHotkeyHandler);
            }
        }
        else
        {
            Title = $"{Utils.GetVersion()}";
            menuAddServerViaScan.IsVisible = false;
        }

        if (_config.UiItem.AutoHideStartup && Utils.IsWindows())
        {
            WindowState = WindowState.Minimized;
        }

        AddHelpMenuItem();
    }

    #region Event

    private void OnProgramStarted(object state, bool timeout)
    {
        Dispatcher.UIThread.Post(() =>
                ShowHideWindow(true),
            DispatcherPriority.Default);
    }

    private async Task DelegateSnackMsg(string content)
    {
        _manager?.Show(new Avalonia.Controls.Notifications.Notification(null, content, NotificationType.Information));
        await Task.CompletedTask;
    }

    private void DelegateFireflyNodeFetchNotification(FireflyNodeFetchNotification notification)
    {
        if (notification.IsActive)
        {
            if (_nodeFetchNotifications.ContainsKey(notification.Id))
            {
                return;
            }

            var persistentNotification = new Notification(
                null,
                notification.Content,
                NotificationType.Information,
                TimeSpan.Zero,
                null,
                () => _nodeFetchNotifications.Remove(notification.Id));
            _nodeFetchNotifications[notification.Id] = persistentNotification;
            _manager?.Show(persistentNotification);
            return;
        }

        if (_nodeFetchNotifications.Remove(notification.Id, out var activeNotification))
        {
            _manager?.Close(activeNotification);
        }
    }

    private void DelegateFireflyUpdateNotification(FireflyUpdateNotification notification)
    {
        var content = new StackPanel { Width = 340, Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            Text = $"发现流萤加速器新版本 {notification.Version}",
            TextWrapping = TextWrapping.Wrap,
        });
        if (notification.Changelog.IsNotEmpty())
        {
            content.Children.Add(new TextBlock
            {
                Text = notification.Changelog,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var actions = new StackPanel
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
        };
        var closeButton = new Button { Content = "关闭" };
        closeButton.Click += (_, _) => _manager?.Close(content);
        var downloadButton = new Button { Content = "前往下载" };
        downloadButton.Click += (_, _) =>
        {
            ProcUtils.ProcessStart(notification.DownloadUrl);
            _manager?.Close(content);
        };
        actions.Children.Add(closeButton);
        actions.Children.Add(downloadButton);
        content.Children.Add(actions);

        _manager?.Show(content, NotificationType.Information, TimeSpan.Zero, null, null);
    }

    private void OnHotkeyHandler(EGlobalHotkey e)
    {
        switch (e)
        {
            case EGlobalHotkey.ShowForm:
                Dispatcher.UIThread.Post(() => ShowHideWindow(null));
                break;

            case EGlobalHotkey.SystemProxyClear:
            case EGlobalHotkey.SystemProxySet:
            case EGlobalHotkey.SystemProxyUnchanged:
            case EGlobalHotkey.SystemProxyPac:
                AppEvents.SysProxyChangeRequested.Publish((ESysProxyType)((int)e - 1));
                break;
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_blCloseByUser)
        {
            return;
        }

        Logging.SaveLog("OnClosing -> " + e.CloseReason.ToString());

        switch (e.CloseReason)
        {
            case WindowCloseReason.OwnerWindowClosing or WindowCloseReason.WindowClosing:
                e.Cancel = true;
                ShowHideWindow(false);
                break;

            case WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown:
                await AppManager.Instance.AppExitAsync(false);
                break;
        }

        base.OnClosing(e);
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            switch (e.Key)
            {
                case Key.V:
                    await AddServerViaClipboardAsync();
                    break;

                case Key.S:
                    await ScanScreenTaskAsync();
                    break;
            }
        }
        else
        {
            if (e.Key == Key.F5)
            {
                ViewModel?.Reload();
            }
        }
    }

    private void MenuSettingsSetUWP_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.GetBinPath("EnableLoopback.exe"));
    }

    public async Task AddServerViaClipboardAsync()
    {
        var clipboardData = await AvaUtils.GetClipboardData(this);
        if (clipboardData.IsNotEmpty() && ViewModel != null)
        {
            await ViewModel.AddServerViaClipboardAsync(clipboardData);
        }
    }

    public async Task ScanScreenTaskAsync()
    {
        ShowHideWindow(false);

        await Task.Delay(200);

        var bytes = QRCodeAvaloniaUtils.CaptureScreen();
        if (bytes != null && ViewModel != null)
        {
            await ViewModel.ScanScreenResult(bytes);
        }

        ShowHideWindow(true);
    }

    private void MenuCheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        _checkUpdateView ??= new CheckUpdateView();
        _checkUpdateView.ViewModel = ViewModel?.CheckUpdateViewModel;
        DialogHost.Show(_checkUpdateView);

        AppEvents.HasUpdateNotified.Publish(string.Empty);
    }

    private void MenuBackupAndRestore_Click(object? sender, RoutedEventArgs e)
    {
        _backupAndRestoreView ??= new BackupAndRestoreView();
        _backupAndRestoreView.ViewModel = ViewModel?.BackupAndRestoreViewModel;
        DialogHost.Show(_backupAndRestoreView);
    }

    private async void MenuAbout_Click(object? sender, RoutedEventArgs e)
    {
        _aboutView ??= new AboutView();
        var originalOverlay = mainDialogHost.OverlayBackground;
        var originalBackground = mainDialogHost.Background;
        try
        {
            mainDialogHost.OverlayBackground = Brushes.Transparent;
            mainDialogHost.Background = Brushes.Transparent;
            await DialogHost.Show(_aboutView);
        }
        finally
        {
            mainDialogHost.OverlayBackground = originalOverlay;
            mainDialogHost.Background = originalBackground;
        }
    }

    private async void MenuClose_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (await UI.ShowYesNo(ResUI.menuExitTips) != ButtonResult.Yes)
            {
                return;
            }

            _blCloseByUser = true;
            StorageUI();

            await AppManager.Instance.AppExitAsync(true);
        }
        catch
        {
            // Ignore
        }
    }

    private void Shutdown(bool obj)
    {
        if (obj is bool b && _blCloseByUser == false)
        {
            _blCloseByUser = b;
        }
        StorageUI();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            HotkeyManager.Instance.Dispose();
            desktop.Shutdown();
        }
    }

    #endregion Event

    #region UI

    public void ShowHideWindow(bool? blShow)
    {
        var bl = blShow ??
                    (Utils.IsLinux() || Utils.IsMacOS()
                    ? (!AppManager.Instance.ShowInTaskbar ^ (WindowState == WindowState.Minimized))
                    : !AppManager.Instance.ShowInTaskbar);
        if (bl)
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Focus();
        }
        else
        {
            if (Utils.IsLinux() && _config.UiItem.Hide2TrayWhenClose == false)
            {
                WindowState = WindowState.Minimized;
                return;
            }

            foreach (var ownedWindow in OwnedWindows)
            {
                ownedWindow.Close();
            }
            Hide();
        }

        AppManager.Instance.ShowInTaskbar = bl;
    }

    protected override void OnLoaded(object? sender, RoutedEventArgs e)
    {
        base.OnLoaded(sender, e);
        if (_config.UiItem.AutoHideStartup)
        {
            ShowHideWindow(false);
        }
        RestoreUI();

    }

    private void RestoreUI()
    {
        if (_config.UiItem.MainGirdHeight1 > 0 && _config.UiItem.MainGirdHeight2 > 0)
        {
            if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
            {
                gridMain.ColumnDefinitions[0].Width = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain.ColumnDefinitions[2].Width = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
            else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
            {
                gridMain1.RowDefinitions[0].Height = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain1.RowDefinitions[2].Height = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
        }
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);

        if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain.ColumnDefinitions[0].ActualWidth, gridMain.ColumnDefinitions[2].ActualWidth);
        }
        else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain1.RowDefinitions[0].ActualHeight, gridMain1.RowDefinitions[2].ActualHeight);
        }
    }

    private void UpdateLayout(EGirdOrientation orientation)
    {
        var currentLayoutDisposables = new MultipleDisposable();
        _layoutBindingsDisposable.Create(currentLayoutDisposables);

        gridMain.IsVisible = orientation == EGirdOrientation.Horizontal;
        gridMain1.IsVisible = orientation == EGirdOrientation.Vertical;
        gridMain2.IsVisible = orientation == EGirdOrientation.Tab;

        switch (orientation)
        {
            case EGirdOrientation.Horizontal:
                this.OneWayBind(ViewModel, vm => vm.ProfilesViewModel, v => v.tabProfiles.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.MsgViewModel, v => v.tabMsgView.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashProxiesViewModel, v => v.tabClashProxies.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashConnectionsViewModel, v => v.tabClashConnections.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabMsgView.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections.IsVisible).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Vertical:
                this.OneWayBind(ViewModel, vm => vm.ProfilesViewModel, v => v.tabProfiles1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.MsgViewModel, v => v.tabMsgView1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashProxiesViewModel, v => v.tabClashProxies1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashConnectionsViewModel, v => v.tabClashConnections1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabMsgView1.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies1.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections1.IsVisible).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain1.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Tab:
            default:
                this.OneWayBind(ViewModel, vm => vm.ProfilesViewModel, v => v.tabProfiles2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.MsgViewModel, v => v.tabMsgView2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashProxiesViewModel, v => v.tabClashProxies2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashConnectionsViewModel, v => v.tabClashConnections2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies2.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections2.IsVisible).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain2.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;
        }

        RestoreUI();
        QueueNewUserGuideStepUpdate();
    }

    private void QueueNewUserGuideStart(int attempts = 0)
    {
        DispatcherTimer.RunOnce(() =>
        {
            if (_config.UiItem.NewUserGuideCompleted || _newUserGuideRunning)
            {
                return;
            }

            if (GetNewUserGuideTarget(_newUserGuideSteps[0].Target) is null && attempts < 10)
            {
                QueueNewUserGuideStart(attempts + 1);
                return;
            }

            StartNewUserGuide();
        }, TimeSpan.FromMilliseconds(250));
    }

    private void StartNewUserGuide()
    {
        if (_newUserGuideRunning)
        {
            return;
        }

        _newUserGuideRunning = true;
        _newUserGuideStepIndex = 0;
        _newUserGuideDisplayedStepIndex = -1;
        newUserGuideOverlay.IsVisible = true;
        UpdateNewUserGuideStep();
        QueueNewUserGuideStepUpdate();
    }

    private void MoveNewUserGuide(int offset)
    {
        CloseNewUserGuideExamples();
        var nextIndex = _newUserGuideStepIndex + offset;
        if (nextIndex >= _newUserGuideSteps.Length)
        {
            _ = FinishNewUserGuideAsync();
            return;
        }

        _newUserGuideStepIndex = Math.Max(0, nextIndex);
        UpdateNewUserGuideStep();
        QueueNewUserGuideStepUpdate();
    }

    private async Task FinishNewUserGuideAsync()
    {
        _config.UiItem.NewUserGuideCompleted = true;
        _newUserGuideRunning = false;
        CloseNewUserGuideExamples();
        newUserGuideOverlay.IsVisible = false;
        newUserGuideHighlight.IsVisible = false;
        await ConfigHandler.SaveConfig(_config);
        await DelegateSnackMsg("点击帮助里的“新手引导”可以再次学习。");
    }

    private void QueueNewUserGuideStepUpdate()
    {
        if (_newUserGuideRunning)
        {
            DispatcherTimer.RunOnce(UpdateNewUserGuideStep, TimeSpan.FromMilliseconds(50));
        }
    }

    private void UpdateNewUserGuideStep()
    {
        if (!_newUserGuideRunning)
        {
            return;
        }

        var step = _newUserGuideSteps[_newUserGuideStepIndex];
        var isNewStep = _newUserGuideDisplayedStepIndex != _newUserGuideStepIndex;
        txtNewUserGuideTitle.Text = step.Title;
        txtNewUserGuideContent.Text = step.Content;
        txtNewUserGuideProgress.Text = $"{_newUserGuideStepIndex + 1} / {_newUserGuideSteps.Length}";
        btnNewUserGuidePrevious.IsEnabled = _newUserGuideStepIndex > 0;
        btnNewUserGuideNext.Content = _newUserGuideStepIndex == _newUserGuideSteps.Length - 1 ? "完成" : "下一步";

        var target = GetNewUserGuideTarget(step.Target);
        UpdateNewUserGuideDropdown(target, isNewStep);

        if (isNewStep)
        {
            _newUserGuideDisplayedStepIndex = _newUserGuideStepIndex;
            ShowNewUserGuideStepExample(step);
        }

        var transform = target?.TransformToVisual(newUserGuideOverlay);
        var origin = transform?.Transform(new Point());
        if (origin is null || target is null || target.Bounds.Width <= 0 || target.Bounds.Height <= 0)
        {
            newUserGuideHighlight.IsVisible = false;
            return;
        }

        const double padding = 7;
        var width = target.Bounds.Width + padding * 2;
        var height = target.Bounds.Height + padding * 2;
        Canvas.SetLeft(newUserGuideHighlight, Math.Max(0, origin.Value.X - padding));
        Canvas.SetTop(newUserGuideHighlight, Math.Max(0, origin.Value.Y - padding));
        newUserGuideHighlight.Width = width;
        newUserGuideHighlight.Height = height;
        newUserGuideHighlight.IsVisible = true;
    }

    private Control? GetNewUserGuideTarget(NewUserGuideTarget target)
    {
        // A ContentControl can briefly retain an old view while the selected
        // layout is changing. Resolve the currently visible view from the
        // visual tree so the highlight always follows the on-screen control.
        var profilesView = this.GetVisualDescendants().OfType<ProfilesView>()
            .FirstOrDefault(view => view.IsEffectivelyVisible);
        var statusBarView = this.GetVisualDescendants().OfType<StatusBarView>()
            .FirstOrDefault(view => view.IsEffectivelyVisible);

        return target switch
        {
            NewUserGuideTarget.SubscriptionGroups => profilesView?.SubscriptionGroupsTutorialTarget,
            NewUserGuideTarget.Nodes => profilesView?.NodesTutorialTarget,
            NewUserGuideTarget.SystemProxy => statusBarView?.SystemProxyTutorialTarget,
            NewUserGuideTarget.Routing => statusBarView?.RoutingTutorialTarget,
            NewUserGuideTarget.EnableTun => statusBarView?.EnableTunTutorialTarget,
            NewUserGuideTarget.RealDelay => profilesView?.RealDelayTutorialTarget,
            NewUserGuideTarget.MixedTest => profilesView?.MixedTestTutorialTarget,
            _ => null,
        };
    }

    private void ShowNewUserGuideStepExample(NewUserGuideStep step)
    {
        if (step.Target != NewUserGuideTarget.Nodes || _newUserGuideStepIndex != 2)
        {
            return;
        }

        _newUserGuideProfilesView = this.GetVisualDescendants().OfType<ProfilesView>()
            .FirstOrDefault(view => view.IsEffectivelyVisible);
        _newUserGuideProfilesView?.SelectFirstNodeForTutorial();
        DispatcherTimer.RunOnce(() =>
        {
            if (_newUserGuideRunning && _newUserGuideStepIndex == 2)
            {
                ShowNewUserGuideNodeContextExample();
            }
        }, TimeSpan.FromMilliseconds(50));
    }

    private void ShowNewUserGuideNodeContextExample()
    {
        var nodeTarget = _newUserGuideProfilesView?.GetFirstNodeTutorialTarget();
        var transform = nodeTarget?.TransformToVisual(newUserGuideOverlay);
        var origin = transform?.Transform(new Point());
        if (origin is null || nodeTarget is null || nodeTarget.Bounds.Height <= 0)
        {
            return;
        }

        Canvas.SetLeft(newUserGuideNodeContextExample, Math.Max(0, origin.Value.X + 12));
        Canvas.SetTop(newUserGuideNodeContextExample, Math.Max(0, origin.Value.Y + nodeTarget.Bounds.Height + 2));
        newUserGuideNodeContextExample.IsVisible = true;
    }

    private void UpdateNewUserGuideDropdown(Control? target, bool openForNewStep)
    {
        var comboBox = target as ComboBox;
        if (_newUserGuideOpenedComboBox is not null && !ReferenceEquals(_newUserGuideOpenedComboBox, comboBox))
        {
            _newUserGuideOpenedComboBox.IsDropDownOpen = false;
            _newUserGuideOpenedComboBox = null;
        }

        if (comboBox is null)
        {
            return;
        }

        if (ShouldShowPersistentNewUserGuideDropdown())
        {
            ShowPersistentNewUserGuideDropdown(comboBox);
            return;
        }

        _newUserGuideOpenedComboBox = comboBox;
        if (!openForNewStep)
        {
            return;
        }

        var stepIndex = _newUserGuideStepIndex;
        Dispatcher.UIThread.Post(() =>
        {
            if (_newUserGuideRunning
                && _newUserGuideStepIndex == stepIndex
                && ReferenceEquals(_newUserGuideOpenedComboBox, comboBox))
            {
                comboBox.IsDropDownOpen = true;
            }
        }, DispatcherPriority.Background);
        DispatcherTimer.RunOnce(() =>
        {
            if (_newUserGuideRunning
                && _newUserGuideStepIndex == stepIndex
                && ReferenceEquals(_newUserGuideOpenedComboBox, comboBox))
            {
                comboBox.IsDropDownOpen = false;
            }
        }, TimeSpan.FromMilliseconds(900));
    }

    private bool ShouldShowPersistentNewUserGuideDropdown() => _newUserGuideStepIndex is 3 or 4;

    private void ShowPersistentNewUserGuideDropdown(ComboBox comboBox)
    {
        var transform = comboBox.TransformToVisual(newUserGuideOverlay);
        var origin = transform?.Transform(new Point());
        if (origin is null || comboBox.Bounds.Width <= 0 || comboBox.Bounds.Height <= 0)
        {
            return;
        }

        var items = comboBox.Items.Cast<object>()
            .Select(item => item switch
            {
                ComboBoxItem comboBoxItem => comboBoxItem.Content?.ToString(),
                RoutingItem routingItem => routingItem.Remarks,
                _ => item?.ToString(),
            })
            .Where(text => text.IsNotEmpty());
        txtNewUserGuideDropdownExample.Text = string.Join(Environment.NewLine, items);
        newUserGuideDropdownExample.Width = comboBox.Bounds.Width;
        Canvas.SetLeft(newUserGuideDropdownExample, Math.Max(0, origin.Value.X));
        Canvas.SetTop(newUserGuideDropdownExample, Math.Max(0, origin.Value.Y - 118));
        newUserGuideDropdownExample.IsVisible = true;
    }

    private void CloseNewUserGuideDropdown()
    {
        if (_newUserGuideOpenedComboBox is not null)
        {
            _newUserGuideOpenedComboBox.IsDropDownOpen = false;
            _newUserGuideOpenedComboBox = null;
        }
    }

    private void CloseNewUserGuideExamples()
    {
        CloseNewUserGuideDropdown();
        newUserGuideNodeContextExample.IsVisible = false;
        newUserGuideDropdownExample.IsVisible = false;
        _newUserGuideProfilesView = null;
    }

    private void AddHelpMenuItem()
    {
        AddHelpMenuItem(FireflyBranding.ProductName, FireflyBranding.WebsiteUrl);
        AddHelpMenuItem(ResUI.menuFireflyCommunity, FireflyBranding.CommunityUrl);
    }

    private void AddHelpMenuItem(string header, string url)
    {
        var item = new MenuItem()
        {
            Tag = url,
            Header = header
        };
        item.Click += MenuItem_Click;
        menuHelp.Items.Add(item);
    }

    private void MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            ProcUtils.ProcessStart(item.Tag?.ToString());
        }
    }

    #endregion UI
}
