using System.Text.Json;

namespace Ocr2Tran.Translation;

public static class TemplateRenderer
{
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var rendered = template;
        foreach (var item in values)
        {
            rendered = rendered.Replace("{" + item.Key + "Raw}", item.Value, StringComparison.Ordinal);
            rendered = rendered.Replace("{" + item.Key + "Url}", Uri.EscapeDataString(item.Value), StringComparison.Ordinal);
            rendered = rendered.Replace("{" + item.Key + "}", JsonEscape(item.Value), StringComparison.Ordinal);
        }

        return rendered;
    }

    private static string JsonEscape(string value) => JsonSerializer.Serialize(value)[1..^1];
}
