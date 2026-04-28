using Ocr2Tran.App;
using Ocr2Tran.Ocr;

namespace Ocr2Tran.Windows;

public sealed class ControlPanelForm : Form
{
    private readonly ConfigStore _configStore;
    private readonly OcrTranslationCoordinator _coordinator;
    private readonly Func<AppSettings, Task> _applySettings;
    private readonly Label _status = new();
    private readonly Button _autoOcr = new();
    private readonly Button _autoTranslate = new();
    private readonly Button _autoRegionTranslate = new();

    public ControlPanelForm(ConfigStore configStore, OcrTranslationCoordinator coordinator, Func<AppSettings, Task> applySettings)
    {
        _configStore = configStore;
        _coordinator = coordinator;
        _applySettings = applySettings;
        Text = "ocr2tran";
        AppIcon.ApplyTo(this);
        Width = 460;
        Height = 590;
        MinimumSize = new Size(420, 560);
        StartPosition = FormStartPosition.CenterScreen;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(16),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        panel.Controls.Add(_status, 0, 0);
        panel.SetColumnSpan(_status, 2);

        var singleOcr = MakeButton("单次 OCR");
        singleOcr.Click += async (_, _) => await _coordinator.RunOcrOnceAsync().ConfigureAwait(false);
        panel.Controls.Add(singleOcr, 0, 1);

        var singleTranslate = MakeButton("单次 OCR + 翻译");
        singleTranslate.Click += async (_, _) => await _coordinator.RunOcrTranslateOnceAsync().ConfigureAwait(false);
        panel.Controls.Add(singleTranslate, 1, 1);

        var regionOcr = MakeButton("框选 OCR");
        regionOcr.Click += async (_, _) => await _coordinator.RunRegionOcrOnceAsync().ConfigureAwait(false);
        panel.Controls.Add(regionOcr, 0, 2);

        var regionTranslate = MakeButton("框选 OCR + 翻译");
        regionTranslate.Click += async (_, _) => await _coordinator.RunRegionOcrTranslateOnceAsync().ConfigureAwait(false);
        panel.Controls.Add(regionTranslate, 1, 2);

        _autoOcr.Text = "启动自动 OCR";
        StyleButton(_autoOcr);
        _autoOcr.Click += (_, _) => _coordinator.ToggleAutoOcr();
        panel.Controls.Add(_autoOcr, 0, 3);

        _autoTranslate.Text = "启动自动翻译";
        StyleButton(_autoTranslate);
        _autoTranslate.Click += (_, _) => _coordinator.ToggleAutoTranslate();
        panel.Controls.Add(_autoTranslate, 1, 3);

        _autoRegionTranslate.Text = "启动框选自动翻译";
        StyleButton(_autoRegionTranslate);
        _autoRegionTranslate.Click += async (_, _) => await _coordinator.ToggleAutoRegionTranslateAsync().ConfigureAwait(false);
        panel.Controls.Add(_autoRegionTranslate, 0, 4);
        panel.SetColumnSpan(_autoRegionTranslate, 2);

        var settings = MakeButton("配置");
        settings.Click += async (_, _) => await ShowSettingsAsync();
        panel.Controls.Add(settings, 0, 5);
        panel.SetColumnSpan(settings, 2);

        var importModel = MakeButton("导入 PPOCR 模型");
        importModel.Click += async (_, _) => await ImportPaddleOcrModelAsync();
        panel.Controls.Add(importModel, 0, 6);

        var importOnnx = MakeButton("导入 ONNX 模型");
        importOnnx.Click += async (_, _) => await ImportOnnxOcrModelAsync();
        panel.Controls.Add(importOnnx, 1, 6);

        var testOcr = MakeButton("测试 OCR");
        testOcr.Click += async (_, _) => await _coordinator.RunOcrOnceAsync().ConfigureAwait(false);
        panel.Controls.Add(testOcr, 0, 7);

        var close = MakeButton("隐藏到托盘");
        close.Click += (_, _) => Hide();
        panel.Controls.Add(close, 1, 7);

        var homepage = MakeButton("项目主页");
        homepage.Click += (_, _) => ProjectHomepage.Open(this);
        panel.Controls.Add(homepage, 0, 8);
        panel.SetColumnSpan(homepage, 2);

        var exit = MakeButton("退出");
        exit.Click += (_, _) => Application.Exit();
        panel.Controls.Add(exit, 0, 9);
        panel.SetColumnSpan(exit, 2);

        Controls.Add(panel);
        _coordinator.StateChanged += (_, _) => PostUpdateState();
        UpdateState();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateState();
    }

    private Button MakeButton(string text)
    {
        var button = new Button { Text = text };
        StyleButton(button);
        return button;
    }

    private static void StyleButton(Button button)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(4);
    }

    private void UpdateState()
    {
        _status.Text = _coordinator.Status;
        _autoOcr.Text = _coordinator.AutoOcrEnabled ? "暂停自动 OCR" : "启动自动 OCR";
        _autoTranslate.Text = _coordinator.AutoTranslateEnabled ? "暂停自动翻译" : "启动自动翻译";
        _autoRegionTranslate.Text = _coordinator.AutoRegionTranslateEnabled ? "暂停框选自动翻译" : "启动框选自动翻译";
    }

    private async Task ShowSettingsAsync()
    {
        using var form = new ConfigEditorForm(_configStore.Settings);
        if (form.ShowDialog(this) != DialogResult.OK || form.EditedSettings is null)
        {
            return;
        }

        await _applySettings(form.EditedSettings);
        UpdateState();
    }

    private async Task ImportPaddleOcrModelAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 PaddleOCR 模型目录。可以选择包含 det/rec/cls 子目录和字典文件的总目录。",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var result = OcrModelImportService.Import(dialog.SelectedPath, _configStore.Settings.Ocr.Paddle);
            _configStore.MarkDirty();
            _configStore.SaveIfDirty();
            await _coordinator.ReloadOcrEngineAsync();

            var message = result.HasRequiredModels
                ? "OCR 模型已导入。"
                : "已保存模型目录，但缺少必要模型。";
            if (result.Warnings.Count > 0)
            {
                message += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, result.Warnings);
            }

            MessageBox.Show(this, message, "ocr2tran", MessageBoxButtons.OK, result.HasRequiredModels ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入 OCR 模型失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ImportOnnxOcrModelAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 ONNX OCR 模型目录。可以选择包含 det.onnx、rec.onnx 的总目录；字典已集成到 rec.onnx 时不需要单独字典文件。",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var result = OnnxOcrModelImportService.Import(dialog.SelectedPath, _configStore.Settings.Ocr);
            _configStore.MarkDirty();
            _configStore.SaveIfDirty();
            await _coordinator.ReloadOcrEngineAsync();

            var message = result.HasRequiredModels
                ? "ONNX OCR 模型已导入，已切换到内置 ONNX Runtime 后端。"
                : "已保存 ONNX 模型目录，但缺少必要模型。";
            if (result.Warnings.Count > 0)
            {
                message += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, result.Warnings);
            }

            MessageBox.Show(this, message, "ocr2tran", MessageBoxButtons.OK, result.HasRequiredModels ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入 ONNX 模型失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PostUpdateState()
    {
        if (IsDisposed)
        {
            return;
        }

        if (IsHandleCreated)
        {
            BeginInvoke(UpdateState);
        }
    }
}
