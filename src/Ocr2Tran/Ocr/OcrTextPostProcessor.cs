using System.Globalization;
using System.Text;
using Ocr2Tran.App;
using Ocr2Tran.Core;

namespace Ocr2Tran.Ocr;

public sealed class OcrTextPostProcessor
{
    private readonly OcrPostProcessingSettings _settings;

    public OcrTextPostProcessor(OcrPostProcessingSettings settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<TextRegion> Process(IReadOnlyList<TextRegion> regions)
    {
        if (!_settings.Enabled)
        {
            return regions;
        }

        var processed = new List<TextRegion>(regions.Count);
        foreach (var region in regions)
        {
            var text = Clean(region.Text);
            if (ShouldDrop(region, text))
            {
                continue;
            }

            processed.Add(region with { Text = text });
        }

        if (_settings.DropOverlappingDuplicates)
        {
            processed = DropOverlappingDuplicates(processed);
        }

        if (_settings.DropShortIsolatedText)
        {
            processed = DropShortIsolatedText(processed);
        }

        if (_settings.MergeNearbyTextRegions)
        {
            processed = MergeNearbyTextRegions(processed);
        }

        return LimitRegions(processed);
    }

    private string Clean(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;
        foreach (var ch in value)
        {
            if (_settings.CharactersToRemove.Contains(ch, StringComparison.Ordinal))
            {
                continue;
            }

            if (_settings.RemoveControlCharacters && char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t')
            {
                continue;
            }

            if (_settings.NormalizeWhitespace && char.IsWhiteSpace(ch))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(ch);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private bool ShouldDrop(TextRegion region, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (value.Length < Math.Max(1, _settings.MinTextLength))
        {
            return true;
        }

        if (region.Confidence > 0 && region.Confidence < Clamp(_settings.MinConfidence, 0, 1))
        {
            return true;
        }

        if (region.Bounds.Width < Math.Max(1, _settings.MinRegionWidth) ||
            region.Bounds.Height < Math.Max(1, _settings.MinRegionHeight) ||
            region.Bounds.Width * region.Bounds.Height < Math.Max(1, _settings.MinRegionArea))
        {
            return true;
        }

        if (!_settings.DropPunctuationOnly)
        {
            return false;
        }

        return CountMeaningfulCharacters(value) < Math.Max(1, _settings.MinMeaningfulCharacters);
    }

    private List<TextRegion> DropOverlappingDuplicates(IEnumerable<TextRegion> regions)
    {
        var kept = new List<TextRegion>();
        foreach (var region in regions
                     .OrderByDescending(region => region.Confidence)
                     .ThenByDescending(region => region.Bounds.Width * region.Bounds.Height))
        {
            if (kept.Any(existing => IsDuplicate(existing, region)))
            {
                continue;
            }

            kept.Add(region);
        }

        return kept
            .OrderBy(region => region.Bounds.Top)
            .ThenBy(region => region.Bounds.Left)
            .ToList();
    }

    private bool IsDuplicate(TextRegion a, TextRegion b)
    {
        if (!NormalizeForComparison(a.Text).Equals(NormalizeForComparison(b.Text), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var intersection = Rectangle.Intersect(a.Bounds, b.Bounds);
        if (intersection.IsEmpty)
        {
            return false;
        }

        var smallerArea = Math.Max(1, Math.Min(Area(a.Bounds), Area(b.Bounds)));
        return Area(intersection) / (double)smallerArea >= Clamp(_settings.DuplicateOverlapRatio, 0.1, 1);
    }

    private List<TextRegion> DropShortIsolatedText(IReadOnlyList<TextRegion> regions)
    {
        var maxShortLength = Math.Max(1, _settings.ShortTextMaxLength);
        var filtered = new List<TextRegion>(regions.Count);
        foreach (var region in regions)
        {
            if (CountMeaningfulCharacters(region.Text) > maxShortLength || HasNearbyPeer(region, regions))
            {
                filtered.Add(region);
            }
        }

        return filtered;
    }

    private bool HasNearbyPeer(TextRegion region, IReadOnlyList<TextRegion> regions)
    {
        foreach (var other in regions)
        {
            if (ReferenceEquals(region, other))
            {
                continue;
            }

            if (!IsSameLine(region.Bounds, other.Bounds))
            {
                continue;
            }

            var gap = HorizontalGap(region.Bounds, other.Bounds);
            if (gap <= Math.Max(4, _settings.SameLineMaxHorizontalGapPx))
            {
                return true;
            }
        }

        return false;
    }

    private List<TextRegion> MergeNearbyTextRegions(IReadOnlyList<TextRegion> regions)
    {
        var lines = new List<List<TextRegion>>();
        foreach (var region in regions.OrderBy(region => CenterY(region.Bounds)).ThenBy(region => region.Bounds.Left))
        {
            var line = lines.FirstOrDefault(candidate => candidate.Any(existing => IsSameLine(existing.Bounds, region.Bounds)));
            if (line is null)
            {
                lines.Add([region]);
            }
            else
            {
                line.Add(region);
            }
        }

        var merged = new List<TextRegion>();
        foreach (var line in lines)
        {
            TextRegion? current = null;
            foreach (var region in line.OrderBy(region => region.Bounds.Left))
            {
                if (current is null)
                {
                    current = region;
                    continue;
                }

                if (HorizontalGap(current.Bounds, region.Bounds) <= Math.Max(4, _settings.SameLineMaxHorizontalGapPx))
                {
                    current = Merge(current, region);
                }
                else
                {
                    merged.Add(current);
                    current = region;
                }
            }

            if (current is not null)
            {
                merged.Add(current);
            }
        }

        return merged
            .OrderBy(region => region.Bounds.Top)
            .ThenBy(region => region.Bounds.Left)
            .ToList();
    }

    private IReadOnlyList<TextRegion> LimitRegions(IReadOnlyList<TextRegion> regions)
    {
        var maxRegions = Math.Max(0, _settings.MaxRegions);
        if (maxRegions == 0 || regions.Count <= maxRegions)
        {
            return regions;
        }

        return regions
            .OrderByDescending(region => region.Confidence)
            .ThenByDescending(region => CountMeaningfulCharacters(region.Text))
            .Take(maxRegions)
            .OrderBy(region => region.Bounds.Top)
            .ThenBy(region => region.Bounds.Left)
            .ToArray();
    }

    private TextRegion Merge(TextRegion left, TextRegion right)
    {
        var separator = NeedsSpace(left.Text, right.Text) ? " " : "";
        var confidence = left.Confidence > 0 && right.Confidence > 0
            ? (left.Confidence + right.Confidence) / 2
            : Math.Max(left.Confidence, right.Confidence);
        return new TextRegion(
            Rectangle.Union(left.Bounds, right.Bounds),
            left.Text + separator + right.Text,
            Confidence: confidence);
    }

    private static bool NeedsSpace(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        return IsAsciiWordCharacter(left[^1]) && IsAsciiWordCharacter(right[0]);
    }

    private bool IsSameLine(Rectangle a, Rectangle b)
    {
        var tolerance = Math.Max(1, _settings.SameLineVerticalTolerancePx);
        var centerDelta = Math.Abs(CenterY(a) - CenterY(b));
        var sharedHeight = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return centerDelta <= tolerance || sharedHeight >= Math.Min(a.Height, b.Height) * 0.55;
    }

    private static int HorizontalGap(Rectangle a, Rectangle b)
    {
        if (a.Right < b.Left)
        {
            return b.Left - a.Right;
        }

        if (b.Right < a.Left)
        {
            return a.Left - b.Right;
        }

        return 0;
    }

    private static int CenterY(Rectangle rectangle)
    {
        return rectangle.Top + rectangle.Height / 2;
    }

    private static int Area(Rectangle rectangle)
    {
        return Math.Max(0, rectangle.Width) * Math.Max(0, rectangle.Height);
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static string NormalizeForComparison(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray());
    }

    private static int CountMeaningfulCharacters(string value)
    {
        var count = 0;
        foreach (var ch in value)
        {
            if (IsMeaningful(ch))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsMeaningful(char ch)
    {
        if (char.IsLetterOrDigit(ch))
        {
            return true;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(ch);
        return category is UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber;
    }

    private static bool IsAsciiWordCharacter(char ch)
    {
        return ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
    }
}
