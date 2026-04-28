using Ocr2Tran.App;

namespace Ocr2Tran.Windows;

public sealed class TrayAppContext : ApplicationContext
{
    private const int ConfigReloadDelayMs = 600;
    private const int MaxConfigReloadAttempts = 3;
    private readonly ConfigStore _configStore;
    private readonly NotifyIcon _notifyIcon;
    private readonly OverlayForm _overlay;
    private readonly OcrTranslationCoordinator _coordinator;
    private readonly ControlPanelForm _controlPanel;
    private readonly GlobalHotkeyManager _hotkeys = new();
    private readonly System.Windows.Forms.Timer _configReloadTimer = new();
    private readonly FileSystemWatcher? _configWatcher;
    private DateTimeOffset _ignoreConfigEventsUntil = DateTimeOffset.MinValue;
    private bool _reloadingConfig;
    private bool _settingsDialogOpen;
    private bool _pendingConfigReload;
    private int _configReloadAttempts;

    public TrayAppContext(ConfigStore configStore)
    {
        _configStore = configStore;
        _overlay = new OverlayForm(configStore.Settings.Overlay);
        _coordinator = new OcrTranslationCoordinator(configStore.Settings, _overlay);
        _controlPanel = new ControlPanelForm(configStore, _coordinator, ApplySettingsAsync);
        _ = _controlPanel.Handle;
        _ = _overlay.Handle;
        _notifyIcon = BuildNotifyIcon();

        RegisterHotkeys(configStore.Settings.Hotkeys);
        _hotkeys.HotkeyPressed += async (_, action) => await HandleHotkeyAsync(action).ConfigureAwait(false);
        _coordinator.StateChanged += (_, _) => PostUpdateTrayText();
        _configReloadTimer.Interval = ConfigReloadDelayMs;
        _configReloadTimer.Tick += async (_, _) => await ReloadConfigFromDiskAsync();
        _configWatcher = BuildConfigWatcher(configStore.Path);
        _coordinator.Start();

        if (configStore.Settings.Mode.ShowControlPanelOnStart || !configStore.Settings.Mode.StartMinimizedToTray)
        {
            ShowControlPanel();
        }
    }

    private NotifyIcon BuildNotifyIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("控制面板", null, (_, _) => ShowControlPanel());
        menu.Items.Add("配置", null, async (_, _) => await ShowSettingsAsync());
        menu.Items.Add("单次 OCR", null, async (_, _) => await _coordinator.RunOcrOnceAsync().ConfigureAwait(false));
        menu.Items.Add("单次 OCR + 翻译", null, async (_, _) => await _coordinator.RunOcrTranslateOnceAsync().ConfigureAwait(false));
        menu.Items.Add("框选 OCR", null, async (_, _) => await _coordinator.RunRegionOcrOnceAsync().ConfigureAwait(false));
        menu.Items.Add("框选 OCR + 翻译", null, async (_, _) => await _coordinator.RunRegionOcrTranslateOnceAsync().ConfigureAwait(false));
        menu.Items.Add("启动/暂停自动 OCR", null, (_, _) => _coordinator.ToggleAutoOcr());
        menu.Items.Add("启动/暂停自动翻译", null, (_, _) => _coordinator.ToggleAutoTranslate());
        menu.Items.Add("启动/暂停框选自动翻译", null, async (_, _) => await _coordinator.ToggleAutoRegionTranslateAsync().ConfigureAwait(false));
        menu.Items.Add("清空覆盖层", null, (_, _) => _overlay.ClearRegions());
        menu.Items.Add("项目主页", null, (_, _) => ProjectHomepage.Open());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());

        var icon = new NotifyIcon
        {
            Icon = AppIcon.Create(),
            Text = "ocr2tran",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowControlPanel();
        return icon;
    }

    private FileSystemWatcher? BuildConfigWatcher(string path)
    {
        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        watcher.Changed += (_, _) => ScheduleConfigReload();
        watcher.Created += (_, _) => ScheduleConfigReload();
        watcher.Renamed += (_, _) => ScheduleConfigReload();
        return watcher;
    }

    private void ScheduleConfigReload()
    {
        if (DateTimeOffset.UtcNow < _ignoreConfigEventsUntil)
        {
            return;
        }

        if (_settingsDialogOpen)
        {
            _pendingConfigReload = true;
            return;
        }

        if (_controlPanel.IsDisposed || !_controlPanel.IsHandleCreated)
        {
            return;
        }

        _controlPanel.BeginInvoke(() =>
        {
            _configReloadTimer.Stop();
            _configReloadTimer.Start();
        });
    }

    private void RegisterHotkeys(HotkeySettings hotkeys)
    {
        var failures = new List<string>();
        TryRegister(HotkeyAction.SingleOcr, hotkeys.SingleOcr, "单次 OCR", failures);
        TryRegister(HotkeyAction.SingleOcrTranslate, hotkeys.SingleOcrTranslate, "单次 OCR + 翻译", failures);
        TryRegister(HotkeyAction.RegionOcr, hotkeys.RegionOcr, "框选 OCR", failures);
        TryRegister(HotkeyAction.RegionOcrTranslate, hotkeys.RegionOcrTranslate, "框选 OCR + 翻译", failures);
        TryRegister(HotkeyAction.ToggleAutoOcr, hotkeys.ToggleAutoOcr, "启动/暂停自动 OCR", failures);
        TryRegister(HotkeyAction.ToggleAutoTranslate, hotkeys.ToggleAutoTranslate, "启动/暂停自动翻译", failures);
        TryRegister(HotkeyAction.ToggleAutoRegionTranslate, hotkeys.ToggleAutoRegionTranslate, "启动/暂停框选自动翻译", failures);
        TryRegister(HotkeyAction.ClearOverlay, hotkeys.ClearOverlay, "清空覆盖层", failures);
        TryRegister(HotkeyAction.Exit, hotkeys.Exit, "退出", failures);

        if (failures.Count > 0)
        {
            ShowBalloon("快捷键注册失败", string.Join(Environment.NewLine, failures.Take(3)));
        }
    }

    private void TryRegister(HotkeyAction action, string value, string displayName, List<string> failures)
    {
        try
        {
            _hotkeys.Register(action, value);
        }
        catch (Exception ex)
        {
            failures.Add($"{displayName}: {value} ({ex.Message})");
        }
    }

    private async Task HandleHotkeyAsync(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.SingleOcr:
                await _coordinator.RunOcrOnceAsync().ConfigureAwait(false);
                break;
            case HotkeyAction.SingleOcrTranslate:
                await _coordinator.RunOcrTranslateOnceAsync().ConfigureAwait(false);
                break;
            case HotkeyAction.RegionOcr:
                await _coordinator.RunRegionOcrOnceAsync().ConfigureAwait(false);
                break;
            case HotkeyAction.RegionOcrTranslate:
                await _coordinator.RunRegionOcrTranslateOnceAsync().ConfigureAwait(false);
                break;
            case HotkeyAction.ToggleAutoOcr:
                _coordinator.ToggleAutoOcr();
                break;
            case HotkeyAction.ToggleAutoTranslate:
                _coordinator.ToggleAutoTranslate();
                break;
            case HotkeyAction.ToggleAutoRegionTranslate:
                await _coordinator.ToggleAutoRegionTranslateAsync().ConfigureAwait(false);
                break;
            case HotkeyAction.ClearOverlay:
                _overlay.ClearRegions();
                break;
            case HotkeyAction.Exit:
                ExitThread();
                break;
        }
    }

    private void ShowControlPanel()
    {
        if (_controlPanel.Visible)
        {
            _controlPanel.Activate();
        }
        else
        {
            _controlPanel.Show();
        }
    }

    public async Task ShowSettingsAsync()
    {
        var openedAt = _configStore.LastWriteTimeUtc;
        _settingsDialogOpen = true;
        _pendingConfigReload = false;
        try
        {
            using var form = new ConfigEditorForm(_configStore.Settings);
            var owner = _controlPanel.Visible ? _controlPanel : null;
            if (form.ShowDialog(owner) != DialogResult.OK || form.EditedSettings is null)
            {
                return;
            }

            if (_configStore.HasChangedOnDiskSince(openedAt) &&
                MessageBox.Show(owner,
                    "配置文件已被外部修改。继续保存会覆盖磁盘上的新配置，是否继续？",
                    "配置已变化",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                ScheduleConfigReload();
                return;
            }

            await ApplySettingsAsync(form.EditedSettings);
        }
        finally
        {
            _settingsDialogOpen = false;
            if (_pendingConfigReload)
            {
                ScheduleConfigReload();
            }
        }
    }

    private async Task ApplySettingsAsync(AppSettings settings)
    {
        _configStore.ReplaceSettings(settings);
        _ignoreConfigEventsUntil = DateTimeOffset.UtcNow.AddSeconds(2);
        _configStore.SaveIfDirty();
        await _coordinator.ApplySettingsAsync(settings);
        _hotkeys.Clear();
        RegisterHotkeys(settings.Hotkeys);
        PostUpdateTrayText();
    }

    private async Task ReloadConfigFromDiskAsync()
    {
        _configReloadTimer.Stop();
        if (_reloadingConfig)
        {
            return;
        }

        _reloadingConfig = true;
        try
        {
            var settings = _configStore.Reload();
            await _coordinator.ApplySettingsAsync(settings);
            _hotkeys.Clear();
            RegisterHotkeys(settings.Hotkeys);
            _configReloadAttempts = 0;
            PostUpdateTrayText();
        }
        catch (Exception ex)
        {
            _configReloadAttempts++;
            if (_configReloadAttempts < MaxConfigReloadAttempts)
            {
                _configReloadTimer.Start();
                return;
            }

            _configReloadAttempts = 0;
            ShowBalloon("配置热加载失败", ex.Message);
        }
        finally
        {
            _reloadingConfig = false;
        }
    }

    private void UpdateTrayText()
    {
        var text = $"ocr2tran - {_coordinator.Status}";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void PostUpdateTrayText()
    {
        if (_controlPanel.IsDisposed || !_controlPanel.IsHandleCreated)
        {
            return;
        }

        _controlPanel.BeginInvoke(UpdateTrayText);
    }

    private void ShowBalloon(string title, string text)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text.Length > 240 ? text[..240] : text;
        _notifyIcon.ShowBalloonTip(5000);
    }

    protected override void ExitThreadCore()
    {
        _configStore.SaveIfDirty();
        _configWatcher?.Dispose();
        _configReloadTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _hotkeys.Dispose();
        _coordinator.Dispose();
        _controlPanel.Dispose();
        _overlay.Dispose();
        AppIcon.Dispose();
        base.ExitThreadCore();
    }
}
