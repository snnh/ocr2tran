using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ocr2Tran.App;

namespace Ocr2Tran.Windows;

public sealed class ConfigEditorForm : Form
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly TreeView _navigation = new();
    private readonly Label _sectionTitle = new();
    private readonly PropertyGrid _propertyGrid = new();

    public ConfigEditorForm(AppSettings settings)
    {
        EditedSettings = Clone(settings);

        Text = "配置";
        AppIcon.ApplyTo(this);
        Width = 900;
        Height = 720;
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;

        _navigation.Dock = DockStyle.Fill;
        _navigation.HideSelection = false;
        _navigation.FullRowSelect = true;
        _navigation.ShowLines = false;
        _navigation.AfterSelect += (_, e) => SelectSection(e.Node);
        BuildNavigation();

        _sectionTitle.Dock = DockStyle.Fill;
        _sectionTitle.Font = new Font(Font, FontStyle.Bold);
        _sectionTitle.TextAlign = ContentAlignment.MiddleLeft;
        _sectionTitle.Padding = new Padding(4, 0, 0, 0);

        _propertyGrid.Dock = DockStyle.Fill;
        _propertyGrid.PropertySort = PropertySort.Alphabetical;
        _propertyGrid.HelpVisible = true;
        _propertyGrid.ToolbarVisible = false;

        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightPanel.Controls.Add(_sectionTitle, 0, 0);
        rightPanel.Controls.Add(_propertyGrid, 0, 1);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 190,
            Panel1MinSize = 160,
            Panel2MinSize = 360
        };
        split.Panel1.Controls.Add(_navigation);
        split.Panel2.Controls.Add(rightPanel);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 54,
            Padding = new Padding(8)
        };

        var save = MakeButton("保存并应用");
        save.Click += (_, _) => SaveAndClose();
        var cancel = MakeButton("取消");
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        Controls.Add(split);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;

        _navigation.ExpandAll();
        _navigation.SelectedNode = _navigation.Nodes.Count > 0 ? _navigation.Nodes[0] : null;
    }

    public AppSettings? EditedSettings { get; private set; }

    private void BuildNavigation()
    {
        if (EditedSettings is null)
        {
            return;
        }

        _navigation.Nodes.Clear();
        _navigation.Nodes.Add(CreateSection("运行模式", EditedSettings.Mode));
        _navigation.Nodes.Add(CreateSection("快捷键", EditedSettings.Hotkeys));

        var ocr = CreateSection("OCR", new OcrGeneralSection(EditedSettings.Ocr));
        ocr.Nodes.Add(CreateSection("图像预处理", EditedSettings.Ocr.ImagePreprocessing));
        ocr.Nodes.Add(CreateSection("文本后处理", EditedSettings.Ocr.PostProcessing));
        ocr.Nodes.Add(CreateSection("ONNX OCR", EditedSettings.Ocr.Onnx));
        ocr.Nodes.Add(CreateSection("PaddleOCR", EditedSettings.Ocr.Paddle));
        _navigation.Nodes.Add(ocr);

        var translation = CreateSection("翻译", new TranslationGeneralSection(EditedSettings.Translation));
        translation.Nodes.Add(CreateSection("百度翻译", EditedSettings.Translation.Baidu));
        translation.Nodes.Add(CreateSection("Google 翻译", EditedSettings.Translation.Google));
        translation.Nodes.Add(CreateSection("HTTP/AI 翻译", EditedSettings.Translation.Http));
        _navigation.Nodes.Add(translation);

        _navigation.Nodes.Add(CreateSection("覆盖层", EditedSettings.Overlay));
        _navigation.Nodes.Add(CreateSection("性能", EditedSettings.Performance));
        _navigation.Nodes.Add(CreateSection("插件", EditedSettings.Plugins));
    }

    private static TreeNode CreateSection(string text, object settings)
    {
        return new TreeNode(text) { Tag = settings };
    }

    private void SelectSection(TreeNode? node)
    {
        if (node is null)
        {
            return;
        }

        _sectionTitle.Text = node.Text;
        _propertyGrid.SelectedObject = node.Tag;
    }

    private void SaveAndClose()
    {
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Button MakeButton(string text)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(96, 32),
            Margin = new Padding(6)
        };
    }

    private static AppSettings Clone(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    private sealed class OcrGeneralSection
    {
        private readonly OcrSettings _settings;

        public OcrGeneralSection(OcrSettings settings)
        {
            _settings = settings;
        }

        [DisplayName("OCR 后端")]
        [Description("可选 onnx、paddleCli 或 noop。")]
        public string Provider
        {
            get => _settings.Provider;
            set => _settings.Provider = value;
        }

        [DisplayName("截图未变化时跳过")]
        [Description("启用后，相同截图会复用上一轮 OCR 结果。")]
        public bool SkipIfScreenshotUnchanged
        {
            get => _settings.SkipIfScreenshotUnchanged;
            set => _settings.SkipIfScreenshotUnchanged = value;
        }
    }

    private sealed class TranslationGeneralSection
    {
        private readonly TranslationSettings _settings;

        public TranslationGeneralSection(TranslationSettings settings)
        {
            _settings = settings;
        }

        [DisplayName("翻译后端")]
        [Description("可选 noop、baidu、google、http 或 ai。")]
        public string Provider
        {
            get => _settings.Provider;
            set => _settings.Provider = value;
        }

        [DisplayName("源语言")]
        [Description("源语言代码，auto 表示自动检测。")]
        public string SourceLanguage
        {
            get => _settings.SourceLanguage;
            set => _settings.SourceLanguage = value;
        }

        [DisplayName("目标语言")]
        [Description("目标语言代码，例如 zh、en、ja。")]
        public string TargetLanguage
        {
            get => _settings.TargetLanguage;
            set => _settings.TargetLanguage = value;
        }

        [DisplayName("每秒请求数")]
        [Description("翻译请求速率限制；0 表示不限制。")]
        public double Rps
        {
            get => _settings.Rps;
            set => _settings.Rps = value;
        }

        [DisplayName("超时毫秒")]
        [Description("单次翻译请求超时时间。")]
        public int TimeoutMs
        {
            get => _settings.TimeoutMs;
            set => _settings.TimeoutMs = value;
        }
    }
}
