using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Ocr2Tran.App;
using Ocr2Tran.Core;

namespace Ocr2Tran.Ocr;

public sealed class OnnxOcrEngine : IOcrEngine, IDisposable
{
    private static readonly float[] DetMean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] DetStd = [0.229f, 0.224f, 0.225f];
    private readonly OnnxOcrSettings _settings;
    private readonly InferenceSession _detSession;
    private readonly InferenceSession _recSession;
    private readonly string[] _characters;
    private readonly string _detInputName;
    private readonly string _recInputName;

    public OnnxOcrEngine(OnnxOcrSettings settings)
    {
        _settings = settings;
        if (!File.Exists(settings.DetectionModelPath))
        {
            throw new FileNotFoundException("ONNX detection model not found.", settings.DetectionModelPath);
        }

        if (!File.Exists(settings.RecognitionModelPath))
        {
            throw new FileNotFoundException("ONNX recognition model not found.", settings.RecognitionModelPath);
        }

        var sessionOptions = new SessionOptions
        {
            IntraOpNumThreads = Math.Max(1, settings.IntraOpNumThreads),
            InterOpNumThreads = 1
        };
        _detSession = new InferenceSession(settings.DetectionModelPath, sessionOptions);
        _recSession = new InferenceSession(settings.RecognitionModelPath, sessionOptions);
        _characters = LoadCharacters(settings, _recSession);
        _detInputName = _detSession.InputMetadata.Keys.First();
        _recInputName = _recSession.InputMetadata.Keys.First();
    }

    private static string[] LoadCharacters(OnnxOcrSettings settings, InferenceSession recSession)
    {
        string[] characters;
        if (!string.IsNullOrWhiteSpace(settings.RecCharDictPath))
        {
            if (!File.Exists(settings.RecCharDictPath))
            {
                throw new FileNotFoundException("OCR character dictionary not found.", settings.RecCharDictPath);
            }

            characters = File.ReadAllLines(settings.RecCharDictPath);
            return AddPaddleSpaceCharacterIfNeeded(characters, recSession);
        }

        foreach (var metadata in EnumerateCharacterMetadata(recSession))
        {
            characters = ParseCharacterMetadata(metadata).ToArray();
            if (characters.Length > 0)
            {
                return AddPaddleSpaceCharacterIfNeeded(characters, recSession);
            }
        }

        throw new InvalidOperationException("OCR character dictionary is not configured and no embedded character metadata was found in the ONNX recognition model.");
    }

    private static string[] AddPaddleSpaceCharacterIfNeeded(string[] characters, InferenceSession recSession)
    {
        if (characters.Any(ch => ch == " "))
        {
            return characters;
        }

        var outputClasses = ResolveRecognitionClassCount(recSession);
        if (outputClasses > 0 && characters.Length == outputClasses - 2)
        {
            return [.. characters, " "];
        }

        return characters;
    }

    private static int ResolveRecognitionClassCount(InferenceSession recSession)
    {
        var output = recSession.OutputMetadata.Values.FirstOrDefault();
        if (output is null)
        {
            return -1;
        }

        var dims = output.Dimensions;
        return dims.Count() > 0 ? dims[^1] : -1;
    }

    private static IEnumerable<string> EnumerateCharacterMetadata(InferenceSession recSession)
    {
        var preferredKeys = new[]
        {
            "character",
            "characters",
            "charset",
            "vocab",
            "vocabulary",
            "labels",
            "dict",
            "dictionary",
            "rec_char_dict",
            "rec_characters"
        };

        var metadata = recSession.ModelMetadata.CustomMetadataMap;
        foreach (var key in preferredKeys)
        {
            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }

        foreach (var item in metadata)
        {
            if (preferredKeys.Contains(item.Key, StringComparer.OrdinalIgnoreCase) ||
                !LooksLikeCharacterMetadataKey(item.Key) ||
                string.IsNullOrWhiteSpace(item.Value))
            {
                continue;
            }

            yield return item.Value;
        }
    }

    private static bool LooksLikeCharacterMetadataKey(string key)
    {
        return key.Contains("char", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("label", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("vocab", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("dict", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ParseCharacterMetadata(string value)
    {
        if (value.Length == 0)
        {
            yield break;
        }

        var jsonCharacters = ParseJsonCharacters(value);
        if (jsonCharacters.Length > 0)
        {
            foreach (var item in jsonCharacters)
            {
                yield return item;
            }

            yield break;
        }

        var trimmedStart = value.TrimStart();
        if (trimmedStart.StartsWith("[", StringComparison.Ordinal) || trimmedStart.StartsWith("{", StringComparison.Ordinal))
        {
            yield break;
        }

        var normalized = value.Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal);
        var separator = normalized.Contains("\n", StringComparison.Ordinal) ? '\n' :
            normalized.Contains(",", StringComparison.Ordinal) ? ',' : '\0';
        if (separator == '\0')
        {
            foreach (var rune in normalized.EnumerateRunes())
            {
                yield return rune.ToString();
            }

            yield break;
        }

        foreach (var item in normalized.Split(separator))
        {
            yield return item.EndsWith("\r", StringComparison.Ordinal) ? item[..^1] : item;
        }
    }

    private static string[] ParseJsonCharacters(string value)
    {
        var trimmedStart = value.TrimStart();
        if (!trimmedStart.StartsWith("[", StringComparison.Ordinal) && !trimmedStart.StartsWith("{", StringComparison.Ordinal))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "character", "characters", "charset", "vocab", "labels", "dict" })
                {
                    if (root.TryGetProperty(key, out var property))
                    {
                        root = property;
                        break;
                    }
                }
            }

            if (root.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return root.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                .Where(item => item is not null)
                .Select(item => item!)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public Task<IReadOnlyList<TextRegion>> RecognizeAsync(CapturedScreen screen, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var boxes = Detect(screen.Image, cancellationToken);
        var regions = new List<TextRegion>(boxes.Count);

        foreach (var box in boxes.OrderBy(b => b.Top).ThenBy(b => b.Left))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var crop = Crop(screen.Image, box);
            var recognized = RecognizeCrop(crop);
            if (string.IsNullOrWhiteSpace(recognized.Text))
            {
                continue;
            }

            regions.Add(new TextRegion(screen.ImageBoundsToScreen(box), recognized.Text, Confidence: recognized.Confidence));
        }

        return Task.FromResult<IReadOnlyList<TextRegion>>(regions);
    }

    private List<Rectangle> Detect(Bitmap image, CancellationToken cancellationToken)
    {
        var prepared = PrepareDetectionInput(image);
        var input = NamedOnnxValue.CreateFromTensor(_detInputName, prepared.Tensor);
        using var results = _detSession.Run([input]);
        cancellationToken.ThrowIfCancellationRequested();

        var output = results.First().AsTensor<float>();
        return ExtractBoxes(output, prepared.OriginalWidth, prepared.OriginalHeight, prepared.ScaleX, prepared.ScaleY);
    }

    private DetectionInput PrepareDetectionInput(Bitmap image)
    {
        var originalWidth = image.Width;
        var originalHeight = image.Height;
        var limit = Math.Max(64, _settings.DetLimitSideLen);
        var ratio = Math.Min(1f, limit / (float)Math.Max(originalWidth, originalHeight));
        var resizedWidth = RoundToMultiple(Math.Max(32, (int)Math.Round(originalWidth * ratio)), 32);
        var resizedHeight = RoundToMultiple(Math.Max(32, (int)Math.Round(originalHeight * ratio)), 32);
        var scaleX = originalWidth / (float)resizedWidth;
        var scaleY = originalHeight / (float)resizedHeight;

        using var resized = new Bitmap(resizedWidth, resizedHeight);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(image, 0, 0, resizedWidth, resizedHeight);
        }

        var tensor = new DenseTensor<float>([1, 3, resizedHeight, resizedWidth]);
        FillNormalizedTensor(resized, tensor, DetMean, DetStd);
        return new DetectionInput(tensor, originalWidth, originalHeight, scaleX, scaleY);
    }

    private List<Rectangle> ExtractBoxes(Tensor<float> output, int originalWidth, int originalHeight, float scaleX, float scaleY)
    {
        var dims = output.Dimensions.ToArray();
        var height = dims[^2];
        var width = dims[^1];
        var data = output.ToArray();
        var visited = new bool[width * height];
        var boxes = new List<Rectangle>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (visited[index] || data[index] < _settings.DetThreshold)
                {
                    continue;
                }

                var component = FloodFill(data, visited, width, height, x, y);
                if (component.Count == 0)
                {
                    continue;
                }

                var averageScore = component.Sum(p => data[p.Y * width + p.X]) / component.Count;
                if (averageScore < _settings.BoxThreshold)
                {
                    continue;
                }

                var left = component.Min(p => p.X);
                var right = component.Max(p => p.X);
                var top = component.Min(p => p.Y);
                var bottom = component.Max(p => p.Y);
                var rect = ScaleAndUnclip(left, top, right, bottom, originalWidth, originalHeight, scaleX, scaleY);
                if (rect.Width >= _settings.MinBoxSize && rect.Height >= _settings.MinBoxSize)
                {
                    boxes.Add(rect);
                }
            }
        }

        return MergeCloseBoxes(boxes);
    }

    private List<Point> FloodFill(float[] data, bool[] visited, int width, int height, int startX, int startY)
    {
        var queue = new Queue<Point>();
        var points = new List<Point>();
        queue.Enqueue(new Point(startX, startY));
        visited[startY * width + startX] = true;

        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            points.Add(point);

            foreach (var next in Neighbor4(point))
            {
                if (next.X < 0 || next.Y < 0 || next.X >= width || next.Y >= height)
                {
                    continue;
                }

                var index = next.Y * width + next.X;
                if (visited[index] || data[index] < _settings.DetThreshold)
                {
                    continue;
                }

                visited[index] = true;
                queue.Enqueue(next);
            }
        }

        return points;
    }

    private Rectangle ScaleAndUnclip(int left, int top, int right, int bottom, int originalWidth, int originalHeight, float scaleX, float scaleY)
    {
        var x = (int)Math.Floor(left * scaleX);
        var y = (int)Math.Floor(top * scaleY);
        var width = Math.Max(1, (int)Math.Ceiling((right - left + 1) * scaleX));
        var height = Math.Max(1, (int)Math.Ceiling((bottom - top + 1) * scaleY));
        var inflateX = (int)Math.Round(width * Math.Max(0, _settings.UnclipRatio - 1) / 2);
        var inflateY = (int)Math.Round(height * Math.Max(0, _settings.UnclipRatio - 1) / 2);
        var rect = Rectangle.Inflate(new Rectangle(x, y, width, height), inflateX, inflateY);
        rect.Intersect(new Rectangle(0, 0, originalWidth, originalHeight));
        return rect;
    }

    private static List<Rectangle> MergeCloseBoxes(IEnumerable<Rectangle> boxes)
    {
        var merged = new List<Rectangle>();
        foreach (var box in boxes.OrderBy(b => b.Top).ThenBy(b => b.Left))
        {
            var mergedIntoExisting = false;
            for (var i = 0; i < merged.Count; i++)
            {
                var expanded = Rectangle.Inflate(merged[i], 4, 4);
                if (!expanded.IntersectsWith(box))
                {
                    continue;
                }

                merged[i] = Rectangle.Union(merged[i], box);
                mergedIntoExisting = true;
                break;
            }

            if (!mergedIntoExisting)
            {
                merged.Add(box);
            }
        }

        return merged;
    }

    private RecognizedText RecognizeCrop(Bitmap crop)
    {
        var targetSize = GetRecognitionTargetSize(crop);
        var segments = SplitForRecognition(crop, targetSize);
        if (segments.Count == 0)
        {
            return RecognizeCrop(crop, targetSize);
        }

        return RecognizeSegments(crop, targetSize, segments);
    }

    private RecognizedText RecognizeSegments(Bitmap crop, Size targetSize, IReadOnlyList<Rectangle> segments)
    {
        var texts = new List<string>(segments.Count);
        var confidences = new List<double>(segments.Count);
        foreach (var segment in segments)
        {
            using var segmentBitmap = crop.Clone(segment, crop.PixelFormat);
            var recognized = RecognizeCrop(segmentBitmap, targetSize);
            if (string.IsNullOrWhiteSpace(recognized.Text))
            {
                continue;
            }

            texts.Add(recognized.Text);
            if (recognized.Confidence > 0)
            {
                confidences.Add(recognized.Confidence);
            }
        }

        return new RecognizedText(JoinRecognizedSegments(texts), confidences.Count == 0 ? 0 : confidences.Average());
    }

    private Size GetRecognitionTargetSize(Bitmap crop)
    {
        var inputShape = _recSession.InputMetadata[_recInputName].Dimensions;
        var shape = inputShape.ToArray();
        var targetHeight = shape.Length >= 3 && shape[^2] > 0 ? shape[^2] : _settings.RecImageHeight;
        targetHeight = Math.Max(16, targetHeight);
        var targetWidth = shape.Length >= 4 && shape[^1] > 0
            ? shape[^1]
            : DynamicRecognitionWidth(crop, targetHeight);
        targetWidth = Math.Max(32, targetWidth);

        return new Size(targetWidth, targetHeight);
    }

    private int DynamicRecognitionWidth(Bitmap crop, int targetHeight)
    {
        if (crop.Height <= 0)
        {
            return Math.Max(32, _settings.RecImageWidth);
        }

        var aspectWidth = (int)Math.Ceiling(crop.Width * (targetHeight / (float)crop.Height));
        var maxWidth = Math.Max(_settings.RecImageWidth, 2048);
        return RoundToMultiple(Math.Min(maxWidth, Math.Max(_settings.RecImageWidth, aspectWidth)), 8);
    }

    private RecognizedText RecognizeCrop(Bitmap crop, Size targetSize)
    {
        var targetHeight = targetSize.Height;
        var targetWidth = targetSize.Width;
        using var resized = ResizeForRecognition(crop, targetWidth, targetHeight);
        var tensor = new DenseTensor<float>([1, 3, targetHeight, targetWidth]);
        FillNormalizedTensor(resized, tensor, [0.5f, 0.5f, 0.5f], [0.5f, 0.5f, 0.5f]);

        var input = NamedOnnxValue.CreateFromTensor(_recInputName, tensor);
        using var results = _recSession.Run([input]);
        var output = results.First().AsTensor<float>();
        return DecodeCtc(output);
    }

    private static Bitmap ResizeForRecognition(Bitmap crop, int targetWidth, int targetHeight)
    {
        var resized = new Bitmap(targetWidth, targetHeight);
        using var graphics = Graphics.FromImage(resized);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        var ratio = Math.Min(targetWidth / (float)crop.Width, targetHeight / (float)crop.Height);
        var width = Math.Max(1, (int)Math.Round(crop.Width * ratio));
        var height = Math.Max(1, (int)Math.Round(crop.Height * ratio));
        graphics.DrawImage(crop, 0, 0, width, height);
        return resized;
    }

    private RecognizedText DecodeCtc(Tensor<float> output)
    {
        var dims = output.Dimensions.ToArray();
        if (dims.Length < 3)
        {
            return new RecognizedText("", 0);
        }

        var timeSteps = dims[^2];
        var classes = dims[^1];
        var data = output.ToArray();
        var chars = new List<string>();
        var confidences = new List<double>();
        var previous = -1;

        for (var t = 0; t < timeSteps; t++)
        {
            var offset = t * classes;
            var bestIndex = 0;
            var bestValue = data[offset];
            for (var c = 1; c < classes; c++)
            {
                var value = data[offset + c];
                if (value > bestValue)
                {
                    bestValue = value;
                    bestIndex = c;
                }
            }

            if (bestIndex != previous)
            {
                var charIndex = MapCtcClassToCharacterIndex(bestIndex, classes);
                if (charIndex.HasValue)
                {
                    chars.Add(_characters[charIndex.Value]);
                    confidences.Add(ConfidenceFromRow(data, offset, classes, bestValue));
                }
            }

            previous = bestIndex;
        }

        return new RecognizedText(string.Concat(chars), confidences.Count == 0 ? 0 : confidences.Average());
    }

    private static List<Rectangle> SplitForRecognition(Bitmap crop, Size targetSize)
    {
        if (crop.Width <= 0 || crop.Height <= 0)
        {
            return [];
        }

        var maxSegmentWidth = Math.Max(
            targetSize.Width,
            (int)Math.Floor(crop.Height * (targetSize.Width / (float)Math.Max(1, targetSize.Height)) * 0.92f));
        if (crop.Width <= maxSegmentWidth)
        {
            return [];
        }

        var blankColumns = FindBlankColumns(crop);
        var segments = new List<Rectangle>();
        var start = 0;
        while (start < crop.Width)
        {
            var remaining = crop.Width - start;
            if (remaining <= maxSegmentWidth)
            {
                segments.Add(TrimSegment(crop, start, crop.Width - 1));
                break;
            }

            var targetEnd = Math.Min(crop.Width - 1, start + maxSegmentWidth);
            var minEnd = Math.Min(crop.Width - 1, start + Math.Max(maxSegmentWidth / 2, targetSize.Width / 2));
            var split = FindBestSplit(blankColumns, minEnd, targetEnd);
            if (split <= start)
            {
                split = targetEnd;
            }

            segments.Add(TrimSegment(crop, start, split));
            start = Math.Min(crop.Width - 1, split + 1);
        }

        return segments.Where(segment => segment.Width > 1 && segment.Height > 1).ToList();
    }

    private static Rectangle TrimSegment(Bitmap crop, int left, int right)
    {
        left = Math.Max(0, Math.Min(crop.Width - 1, left));
        right = Math.Max(left, Math.Min(crop.Width - 1, right));
        var padding = Math.Max(2, crop.Height / 12);
        return new Rectangle(
            Math.Max(0, left - padding),
            0,
            Math.Min(crop.Width - Math.Max(0, left - padding), right - left + 1 + padding * 2),
            crop.Height);
    }

    private static bool[] FindBlankColumns(Bitmap crop)
    {
        var background = EstimateBorderLuminance(crop);
        var blankColumns = new bool[crop.Width];
        var maxForegroundPixels = Math.Max(1, crop.Height / 40);
        for (var x = 0; x < crop.Width; x++)
        {
            var foregroundPixels = 0;
            for (var y = 0; y < crop.Height; y++)
            {
                var color = crop.GetPixel(x, y);
                var luminance = color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
                if (Math.Abs(luminance - background) >= 28)
                {
                    foregroundPixels++;
                    if (foregroundPixels > maxForegroundPixels)
                    {
                        break;
                    }
                }
            }

            blankColumns[x] = foregroundPixels <= maxForegroundPixels;
        }

        return blankColumns;
    }

    private static int FindBestSplit(bool[] blankColumns, int minEnd, int targetEnd)
    {
        var bestCenter = -1;
        var bestDistance = int.MaxValue;
        var runStart = -1;
        var runLength = 0;
        for (var x = minEnd; x <= targetEnd; x++)
        {
            if (blankColumns[x])
            {
                if (runStart < 0)
                {
                    runStart = x;
                }

                runLength++;
                continue;
            }

            ChooseRun();
            runStart = -1;
            runLength = 0;
        }

        ChooseRun();
        return bestCenter;

        void ChooseRun()
        {
            if (runLength < 2 || runStart < 0)
            {
                return;
            }

            var center = runStart + runLength / 2;
            var distance = Math.Abs(targetEnd - center);
            if (distance < bestDistance)
            {
                bestCenter = center;
                bestDistance = distance;
            }
        }
    }

    private static double EstimateBorderLuminance(Bitmap bitmap)
    {
        var sum = 0d;
        var count = 0;
        for (var x = 0; x < bitmap.Width; x++)
        {
            Add(bitmap.GetPixel(x, 0));
            Add(bitmap.GetPixel(x, bitmap.Height - 1));
        }

        for (var y = 1; y < bitmap.Height - 1; y++)
        {
            Add(bitmap.GetPixel(0, y));
            Add(bitmap.GetPixel(bitmap.Width - 1, y));
        }

        return count == 0 ? 255 : sum / count;

        void Add(Color color)
        {
            sum += color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
            count++;
        }
    }

    private static string JoinRecognizedSegments(IReadOnlyList<string> texts)
    {
        if (texts.Count == 0)
        {
            return "";
        }

        var joined = texts[0].Trim();
        for (var i = 1; i < texts.Count; i++)
        {
            var next = texts[i].Trim();
            if (next.Length == 0)
            {
                continue;
            }

            joined += NeedsSpace(joined, next) ? " " + next : next;
        }

        return joined;
    }

    private static bool NeedsSpace(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        return IsAsciiWordCharacter(left[^1]) && IsAsciiWordCharacter(right[0]);
    }

    private int? MapCtcClassToCharacterIndex(int classIndex, int classes)
    {
        var blankIndex = ResolveBlankIndex(classes);
        if (classIndex == blankIndex)
        {
            return null;
        }

        if (_characters.Length == classes - 1)
        {
            var shiftedIndex = blankIndex == 0 ? classIndex - 1 : classIndex;
            return shiftedIndex >= 0 && shiftedIndex < _characters.Length ? shiftedIndex : null;
        }

        if (_characters.Length >= classes)
        {
            if (classIndex >= 0 && classIndex < _characters.Length && !IsBlankToken(_characters[classIndex]))
            {
                return classIndex;
            }

            return null;
        }

        var fallbackIndex = classIndex - 1;
        return fallbackIndex >= 0 && fallbackIndex < _characters.Length ? fallbackIndex : null;
    }

    private int ResolveBlankIndex(int classes)
    {
        if (_characters.Length >= classes && _characters.Length > 0)
        {
            if (IsBlankToken(_characters[0]))
            {
                return 0;
            }

            var lastClassIndex = Math.Max(0, classes - 1);
            if (lastClassIndex < _characters.Length && IsBlankToken(_characters[lastClassIndex]))
            {
                return lastClassIndex;
            }
        }

        return 0;
    }

    private static bool IsBlankToken(string value)
    {
        var token = value.Trim();
        return value.Length == 0 ||
               token.Equals("blank", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("<blank>", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("[blank]", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("ctc_blank", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("<ctc_blank>", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("sos/eos", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("<sos/eos>", StringComparison.OrdinalIgnoreCase);
    }

    private static double ConfidenceFromRow(float[] data, int offset, int classes, float bestValue)
    {
        var sum = 0d;
        var min = float.PositiveInfinity;
        var max = float.NegativeInfinity;
        for (var i = 0; i < classes; i++)
        {
            var value = data[offset + i];
            sum += value;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        if (min >= 0 && max <= 1 && sum >= 0.9 && sum <= 1.1)
        {
            return bestValue;
        }

        var expSum = 0d;
        for (var i = 0; i < classes; i++)
        {
            expSum += Math.Exp(data[offset + i] - bestValue);
        }

        return expSum <= 0 ? 0 : 1 / expSum;
    }

    private static Bitmap Crop(Bitmap image, Rectangle box)
    {
        var bounds = new Rectangle(0, 0, image.Width, image.Height);
        box.Intersect(bounds);
        return image.Clone(box, image.PixelFormat);
    }

    private static void FillNormalizedTensor(Bitmap bitmap, DenseTensor<float> tensor, float[] mean, float[] std)
    {
        var bitsPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat);
        var bytesPerPixel = bitsPerPixel / 8;
        if (bitsPerPixel is not 24 and not 32 || bytesPerPixel < 3)
        {
            FillNormalizedTensorSlow(bitmap, tensor, mean, std);
            return;
        }

        BitmapData? data = null;
        var useSlowPath = false;
        try
        {
            data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
            var span = tensor.Buffer.Span;
            var width = bitmap.Width;
            var height = bitmap.Height;
            var plane = width * height;
            var rowBytes = width * bytesPerPixel;
            var stride = data.Stride;
            var row = new byte[rowBytes];

            for (var y = 0; y < height; y++)
            {
                var offset = stride >= 0
                    ? y * stride
                    : (height - 1 - y) * -stride;
                Marshal.Copy(IntPtr.Add(data.Scan0, offset), row, 0, rowBytes);

                var rowOffset = y * width;
                for (var x = 0; x < width; x++)
                {
                    var source = x * bytesPerPixel;
                    var target = rowOffset + x;
                    var b = row[source] / 255f;
                    var g = row[source + 1] / 255f;
                    var r = row[source + 2] / 255f;
                    span[target] = (r - mean[0]) / std[0];
                    span[plane + target] = (g - mean[1]) / std[1];
                    span[plane * 2 + target] = (b - mean[2]) / std[2];
                }
            }
        }
        catch
        {
            useSlowPath = true;
        }
        finally
        {
            if (data is not null)
            {
                bitmap.UnlockBits(data);
            }
        }

        if (useSlowPath)
        {
            FillNormalizedTensorSlow(bitmap, tensor, mean, std);
        }
    }

    private static void FillNormalizedTensorSlow(Bitmap bitmap, DenseTensor<float> tensor, float[] mean, float[] std)
    {
        var span = tensor.Buffer.Span;
        var width = bitmap.Width;
        var height = bitmap.Height;
        var plane = width * height;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var target = y * width + x;
                span[target] = (color.R / 255f - mean[0]) / std[0];
                span[plane + target] = (color.G / 255f - mean[1]) / std[1];
                span[plane * 2 + target] = (color.B / 255f - mean[2]) / std[2];
            }
        }
    }

    private static int RoundToMultiple(int value, int multiple)
    {
        return Math.Max(multiple, value / multiple * multiple);
    }

    private static bool IsAsciiWordCharacter(char ch)
    {
        return ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
    }

    private static IEnumerable<Point> Neighbor4(Point point)
    {
        yield return new Point(point.X + 1, point.Y);
        yield return new Point(point.X - 1, point.Y);
        yield return new Point(point.X, point.Y + 1);
        yield return new Point(point.X, point.Y - 1);
    }

    public void Dispose()
    {
        _detSession.Dispose();
        _recSession.Dispose();
    }

    private sealed record DetectionInput(DenseTensor<float> Tensor, int OriginalWidth, int OriginalHeight, float ScaleX, float ScaleY);
    private sealed record RecognizedText(string Text, double Confidence);
}
