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

    private readonly PropertyGrid _propertyGrid = new();

    public ConfigEditorForm(AppSettings settings)
    {
        EditedSettings = Clone(settings);

        Text = "配置";
        Width = 760;
        Height = 680;
        MinimumSize = new Size(620, 520);
        StartPosition = FormStartPosition.CenterParent;

        _propertyGrid.Dock = DockStyle.Fill;
        _propertyGrid.SelectedObject = EditedSettings;
        _propertyGrid.PropertySort = PropertySort.CategorizedAlphabetical;
        _propertyGrid.HelpVisible = true;
        _propertyGrid.ToolbarVisible = true;

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
        Controls.Add(_propertyGrid);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
    }

    public AppSettings? EditedSettings { get; private set; }

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
}
