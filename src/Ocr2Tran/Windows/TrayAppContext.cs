using Ocr2Tran.App;

namespace Ocr2Tran.Windows;

public sealed class TrayAppContext : ApplicationContext
{
    private readonly ConfigStore _configStore;
    private readonly NotifyIcon _notifyIcon;
    private readonly OverlayForm _overlay;
    private readonly OcrTranslationCoordinator _coordinator;
    private readonly ControlPanelForm _controlPanel;
    private readonly GlobalHotkeyManager _hotkeys = new();

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
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());

        var icon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Text = "ocr2tran",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowControlPanel();
        return icon;
    }

    private void RegisterHotkeys(HotkeySettings hotkeys)
    {
        TryRegister(HotkeyAction.SingleOcr, hotkeys.SingleOcr);
        TryRegister(HotkeyAction.SingleOcrTranslate, hotkeys.SingleOcrTranslate);
        TryRegister(HotkeyAction.RegionOcr, hotkeys.RegionOcr);
        TryRegister(HotkeyAction.RegionOcrTranslate, hotkeys.RegionOcrTranslate);
        TryRegister(HotkeyAction.ToggleAutoOcr, hotkeys.ToggleAutoOcr);
        TryRegister(HotkeyAction.ToggleAutoTranslate, hotkeys.ToggleAutoTranslate);
        TryRegister(HotkeyAction.ToggleAutoRegionTranslate, hotkeys.ToggleAutoRegionTranslate);
        TryRegister(HotkeyAction.Exit, hotkeys.Exit);
    }

    private void TryRegister(HotkeyAction action, string value)
    {
        try
        {
            _hotkeys.Register(action, value);
        }
        catch (Exception)
        {
            // 热键可能被其他程序占用，跳过该热键继续运行
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
        using var form = new ConfigEditorForm(_configStore.Settings);
        var owner = _controlPanel.Visible ? _controlPanel : null;
        if (form.ShowDialog(owner) != DialogResult.OK || form.EditedSettings is null)
        {
            return;
        }

        await ApplySettingsAsync(form.EditedSettings);
    }

    private async Task ApplySettingsAsync(AppSettings settings)
    {
        _configStore.ReplaceSettings(settings);
        _configStore.SaveIfDirty();
        await _coordinator.ApplySettingsAsync(settings);
        _hotkeys.Clear();
        RegisterHotkeys(settings.Hotkeys);
        PostUpdateTrayText();
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

    protected override void ExitThreadCore()
    {
        _configStore.SaveIfDirty();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _hotkeys.Dispose();
        _coordinator.Dispose();
        _controlPanel.Dispose();
        _overlay.Dispose();
        base.ExitThreadCore();
    }
}
