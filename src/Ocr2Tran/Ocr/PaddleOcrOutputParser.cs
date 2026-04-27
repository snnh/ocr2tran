using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ocr2Tran.Core;

namespace Ocr2Tran.Ocr;

public static partial class PaddleOcrOutputParser
{
    public static IReadOnlyList<TextRegion> Parse(string output, Point origin, float scaleX = 1, float scaleY = 1)
    {
        var trimmed = output.Trim();
        if (trimmed.Length == 0)
        {
            return Array.Empty<TextRegion>();
        }

        if (TryParseJson(trimmed, origin, scaleX, scaleY, out var jsonRegions))
        {
            return jsonRegions;
        }

        var paddleRegions = ParsePaddleTextLines(trimmed, origin, scaleX, scaleY);
        if (paddleRegions.Count > 0)
        {
            return paddleRegions;
        }

        var structuredTextRegions = ParseStructuredTextOutput(trimmed, origin);
        if (structuredTextRegions.Count > 0)
        {
            return structuredTextRegions;
        }

        return ParsePlainLines(trimmed, origin);
    }

    private static bool TryParseJson(string output, Point origin, float scaleX, float scaleY, out IReadOnlyList<TextRegion> regions)
    {
        regions = Array.Empty<TextRegion>();
        foreach (var candidate in EnumerateJsonCandidates(output))
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                if (TryParseStructuredResult(document.RootElement, origin, scaleX, scaleY, out var structuredRegions))
                {
                    regions = structuredRegions;
                    return true;
                }

                var array = FindResultArray(document.RootElement);
                if (array is null)
                {
                    continue;
                }

                var parsed = array.Value.EnumerateArray()
                    .Select((item, index) => ParseJsonItem(item, index, origin, scaleX, scaleY))
                    .Where(region => !string.IsNullOrWhiteSpace(region.Text))
                    .ToArray();

                if (parsed.Length > 0)
                {
                    regions = parsed;
                    return true;
                }
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateJsonCandidates(string output)
    {
        yield return output;

        var firstArray = output.IndexOf('[');
        var lastArray = output.LastIndexOf(']');
        if (firstArray >= 0 && lastArray > firstArray)
        {
            yield return output[firstArray..(lastArray + 1)];
        }

        var firstObject = output.IndexOf('{');
        var lastObject = output.LastIndexOf('}');
        if (firstObject >= 0 && lastObject > firstObject)
        {
            yield return output[firstObject..(lastObject + 1)];
        }
    }

    private static JsonElement? FindResultArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in new[] { "results", "result", "data", "ocr_results", "items" })
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                return value;
            }
        }

        return null;
    }

    private static TextRegion ParseJsonItem(JsonElement item, int index, Point origin, float scaleX, float scaleY)
    {
        if (item.ValueKind == JsonValueKind.Array)
        {
            return ParseJsonArrayItem(item, index, origin, scaleX, scaleY);
        }

        var text = ReadString(item, "text")
            ?? ReadString(item, "transcription")
            ?? ReadString(item, "label")
            ?? ReadString(item, "value")
            ?? "";
        var confidence = ReadDouble(item, "confidence") ?? ReadDouble(item, "score") ?? ReadDouble(item, "prob") ?? 0;
        var bounds = ReadBounds(item, origin, scaleX, scaleY) ?? FallbackBounds(text, index, origin);
        return new TextRegion(bounds, text, Confidence: confidence);
    }

    private static TextRegion ParseJsonArrayItem(JsonElement item, int index, Point origin, float scaleX, float scaleY)
    {
        var text = "";
        var confidence = 0d;
        Rectangle? bounds = null;

        foreach (var child in item.EnumerateArray())
        {
            if (bounds is null && child.ValueKind == JsonValueKind.Array)
            {
                var numbers = child.EnumerateArray().SelectMany(ReadNumbers).ToArray();
                bounds = BoundsFromNumbers(numbers, origin, scaleX, scaleY);
            }

            if (child.ValueKind == JsonValueKind.String)
            {
                text = child.GetString() ?? text;
            }
            else if (child.ValueKind == JsonValueKind.Number && child.TryGetDouble(out var score))
            {
                confidence = score;
            }
            else if (child.ValueKind == JsonValueKind.Array)
            {
                var leaf = child.EnumerateArray().ToArray();
                if (leaf.Length > 0 && leaf[0].ValueKind == JsonValueKind.String)
                {
                    text = leaf[0].GetString() ?? text;
                    if (leaf.Length > 1 && leaf[1].TryGetDouble(out var nestedScore))
                    {
                        confidence = nestedScore;
                    }
                }
            }
        }

        return new TextRegion(bounds ?? FallbackBounds(text, index, origin), text, Confidence: confidence);
    }

    private static List<TextRegion> ParsePaddleTextLines(string output, Point origin, float scaleX, float scaleY)
    {
        var regions = new List<TextRegion>();
        foreach (var line in output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var textMatch = PaddleTextRegex().Match(line);
            if (!textMatch.Success)
            {
                continue;
            }

            var text = UnescapePythonString(textMatch.Groups["text"].Value);
            var score = ParseDouble(textMatch.Groups["score"].Value) ?? 0;
            var numbers = NumberRegex().Matches(line)
                .Select(match => ParseDouble(match.Value))
                .Where(number => number.HasValue)
                .Select(number => number!.Value)
                .ToArray();

            var bounds = BoundsFromNumbers(numbers, origin, scaleX, scaleY) ?? FallbackBounds(text, regions.Count, origin);
            regions.Add(new TextRegion(bounds, text, Confidence: score));
        }

        return regions;
    }

    private static IReadOnlyList<TextRegion> ParseStructuredTextOutput(string output, Point origin)
    {
        var textsPayload = ExtractBracketPayload(output, "rec_texts");
        if (textsPayload is null)
        {
            return Array.Empty<TextRegion>();
        }

        var texts = ParseQuotedStringList(textsPayload);
        if (texts.Count == 0)
        {
            return Array.Empty<TextRegion>();
        }

        var scoresPayload = ExtractBracketPayload(output, "rec_scores");
        double[] scores = scoresPayload is null
            ? []
            : NumberRegex().Matches(scoresPayload)
                .Select(match => ParseDouble(match.Value))
                .Where(number => number.HasValue)
                .Select(number => number!.Value)
                .ToArray();

        return texts
            .Select((text, index) => new TextRegion(
                FallbackBounds(text, index, origin),
                text,
                Confidence: index < scores.Length ? scores[index] : 0))
            .Where(region => !string.IsNullOrWhiteSpace(region.Text))
            .ToArray();
    }

    private static IReadOnlyList<TextRegion> ParsePlainLines(string output, Point origin)
    {
        return output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !IsLogOnlyLine(line))
            .Select((line, index) => new TextRegion(FallbackBounds(line.Trim(), index, origin), line.Trim(), Confidence: 0))
            .Where(region => !string.IsNullOrWhiteSpace(region.Text))
            .ToArray();
    }

    private static bool IsLogOnlyLine(string line)
    {
        return line.Contains(" ppocr ", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(" DEBUG: ", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(" INFO: ", StringComparison.OrdinalIgnoreCase) && !line.Contains("('", StringComparison.Ordinal);
    }

    private static string? ReadString(JsonElement item, string name)
    {
        return item.ValueKind == JsonValueKind.Object &&
               item.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryParseStructuredResult(JsonElement root, Point origin, float scaleX, float scaleY, out IReadOnlyList<TextRegion> regions)
    {
        var collected = new List<TextRegion>();
        CollectStructuredResults(root, origin, scaleX, scaleY, collected);
        regions = collected;
        return collected.Count > 0;
    }

    private static void CollectStructuredResults(JsonElement element, Point origin, float scaleX, float scaleY, List<TextRegion> regions)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryReadStringArray(element, out var texts))
            {
                var scores = ReadDoubleArray(element);
                var boxes = ReadBoundsArray(element, origin, scaleX, scaleY);
                for (var i = 0; i < texts.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(texts[i]))
                    {
                        continue;
                    }

                    regions.Add(new TextRegion(
                        i < boxes.Length ? boxes[i] : FallbackBounds(texts[i], regions.Count, origin),
                        texts[i],
                        Confidence: i < scores.Length ? scores[i] : 0));
                }

                return;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    CollectStructuredResults(property.Value, origin, scaleX, scaleY, regions);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    CollectStructuredResults(item, origin, scaleX, scaleY, regions);
                }
            }
        }
    }

    private static bool TryReadStringArray(JsonElement item, out string[] values)
    {
        foreach (var name in new[] { "rec_texts", "texts", "text_lines", "transcriptions" })
        {
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                values = value.EnumerateArray()
                    .Select(element => element.ValueKind == JsonValueKind.String ? element.GetString() : null)
                    .Where(text => text is not null)
                    .Select(text => text!)
                    .ToArray();
                return values.Length > 0;
            }
        }

        values = [];
        return false;
    }

    private static double[] ReadDoubleArray(JsonElement item)
    {
        foreach (var name in new[] { "rec_scores", "scores", "confidences", "probs" })
        {
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Select(element => element.TryGetDouble(out var number) ? number : (double?)null)
                    .Where(number => number.HasValue)
                    .Select(number => number!.Value)
                    .ToArray();
            }
        }

        return [];
    }

    private static Rectangle[] ReadBoundsArray(JsonElement item, Point origin, float scaleX, float scaleY)
    {
        foreach (var name in new[] { "dt_polys", "dt_boxes", "boxes", "rec_polys", "points" })
        {
            if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return value.EnumerateArray()
                .Select(element => BoundsFromNumbers(element.EnumerateArray().SelectMany(ReadNumbers).ToArray(), origin, scaleX, scaleY))
                .Where(bounds => bounds is not null)
                .Select(bounds => bounds!.Value)
                .ToArray();
        }

        return [];
    }

    private static double? ReadDouble(JsonElement item, string name)
    {
        return item.ValueKind == JsonValueKind.Object &&
               item.TryGetProperty(name, out var value) &&
               value.TryGetDouble(out var number)
            ? number
            : null;
    }

    private static Rectangle? ReadBounds(JsonElement item, Point origin, float scaleX, float scaleY)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in new[] { "box", "bbox", "points", "dt_box", "rect" })
        {
            if (!item.TryGetProperty(name, out var box) || box.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var nums = box.EnumerateArray().SelectMany(ReadNumbers).ToArray();
            var bounds = BoundsFromNumbers(nums, origin, scaleX, scaleY, name.Equals("rect", StringComparison.OrdinalIgnoreCase));
            if (bounds is not null)
            {
                return bounds;
            }
        }

        return null;
    }

    private static Rectangle? BoundsFromNumbers(IReadOnlyList<double> nums, Point origin, float scaleX, float scaleY, bool fourNumbersAreSize = false)
    {
        if (nums.Count < 4)
        {
            return null;
        }

        if (nums.Count == 4)
        {
            var x = nums[0];
            var y = nums[1];
            var width = nums[2];
            var height = nums[3];
            if (!fourNumbersAreSize && nums[2] > nums[0] && nums[3] > nums[1])
            {
                width = nums[2] - nums[0];
                height = nums[3] - nums[1];
            }

            return new Rectangle(
                origin.X + ScaleCoordinate(x, scaleX),
                origin.Y + ScaleCoordinate(y, scaleY),
                Math.Max(1, ScaleCoordinate(width, scaleX)),
                Math.Max(1, ScaleCoordinate(height, scaleY)));
        }

        var xs = nums.Where((_, i) => i % 2 == 0).ToArray();
        var ys = nums.Where((_, i) => i % 2 == 1).ToArray();
        if (xs.Length == 0 || ys.Length == 0)
        {
            return null;
        }

        var left = (int)Math.Floor(xs.Min());
        var top = (int)Math.Floor(ys.Min());
        var right = (int)Math.Ceiling(xs.Max());
        var bottom = (int)Math.Ceiling(ys.Max());
        return new Rectangle(
            origin.X + ScaleCoordinate(left, scaleX),
            origin.Y + ScaleCoordinate(top, scaleY),
            Math.Max(1, ScaleCoordinate(right - left, scaleX)),
            Math.Max(1, ScaleCoordinate(bottom - top, scaleY)));
    }

    private static int ScaleCoordinate(double value, float scale)
    {
        return (int)Math.Round(value / Math.Max(scale, 0.0001f));
    }

    private static Rectangle FallbackBounds(string text, int index, Point origin)
    {
        return new Rectangle(origin.X + 40, origin.Y + 40 + index * 28, Math.Max(240, text.Length * 12), 26);
    }

    private static IEnumerable<double> ReadNumbers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            yield return number;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray())
            {
                foreach (var childNumber in ReadNumbers(child))
                {
                    yield return childNumber;
                }
            }
        }
    }

    private static double? ParseDouble(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static string UnescapePythonString(string value)
    {
        return value
            .Replace("\\'", "'", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static string? ExtractBracketPayload(string output, string key)
    {
        var keyIndex = output.IndexOf(key, StringComparison.Ordinal);
        if (keyIndex < 0)
        {
            return null;
        }

        var start = output.IndexOf('[', keyIndex);
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var quote = '\0';
        var escaped = false;
        for (var i = start; i < output.Length; i++)
        {
            var ch = output[i];
            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }

            if (ch == '[')
            {
                depth++;
            }
            else if (ch == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return output[(start + 1)..i];
                }
            }
        }

        return null;
    }

    private static List<string> ParseQuotedStringList(string payload)
    {
        var strings = new List<string>();
        for (var i = 0; i < payload.Length; i++)
        {
            if (payload[i] is not ('\'' or '"'))
            {
                continue;
            }

            var quote = payload[i];
            var builder = new System.Text.StringBuilder();
            var escaped = false;
            for (i++; i < payload.Length; i++)
            {
                var ch = payload[i];
                if (escaped)
                {
                    builder.Append(ch switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => ch
                    });
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == quote)
                {
                    strings.Add(builder.ToString());
                    break;
                }

                builder.Append(ch);
            }
        }

        return strings;
    }

    [GeneratedRegex(@"\(\s*['""](?<text>(?:\\.|[^'""])*)['""]\s*,\s*(?<score>[0-9]+(?:\.[0-9]+)?)\s*\)")]
    private static partial Regex PaddleTextRegex();

    [GeneratedRegex(@"-?[0-9]+(?:\.[0-9]+)?")]
    private static partial Regex NumberRegex();
}
