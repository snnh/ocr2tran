using Ocr2Tran.App;

namespace Ocr2Tran.Ocr;

public static class OnnxOcrModelImportService
{
    public static OcrModelImportResult Import(string root, OcrSettings settings)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        var models = Directory.EnumerateFiles(root, "*.onnx", SearchOption.AllDirectories).ToArray();
        var det = PickModel(models, "det", "detect", "detection");
        var rec = PickModel(models, "rec", "recognition");
        var cls = PickModel(models, "cls", "angle", "class", "textline");
        var dict = PickDictionary(root);
        var warnings = new List<string>();

        if (det.Length == 0)
        {
            warnings.Add("未找到 ONNX 检测模型，文件名或目录名建议包含 det。");
        }

        if (rec.Length == 0)
        {
            warnings.Add("未找到 ONNX 识别模型，文件名或目录名建议包含 rec。");
        }

        settings.Provider = "onnx";
        settings.Onnx.ModelRoot = root;
        settings.Onnx.DetectionModelPath = det;
        settings.Onnx.RecognitionModelPath = rec;
        settings.Onnx.ClassificationModelPath = cls;
        settings.Onnx.RecCharDictPath = dict;

        return new OcrModelImportResult(root, Path.GetDirectoryName(det) ?? "", Path.GetDirectoryName(rec) ?? "", Path.GetDirectoryName(cls) ?? "", dict, warnings);
    }

    private static string PickModel(IEnumerable<string> paths, params string[] tokens)
    {
        return paths
            .Select(path => new { Path = path, Score = ScorePath(path, tokens) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path.Length)
            .Select(item => item.Path)
            .FirstOrDefault() ?? "";
    }

    private static int ScorePath(string path, IReadOnlyCollection<string> tokens)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        var score = 0;
        foreach (var token in tokens)
        {
            if (normalized.Contains(token, StringComparison.Ordinal))
            {
                score += 10;
            }
        }

        if (normalized.Contains("pp-ocr", StringComparison.Ordinal) || normalized.Contains("ppocr", StringComparison.Ordinal))
        {
            score += 2;
        }

        if (normalized.Contains("infer", StringComparison.Ordinal))
        {
            score += 1;
        }

        return score;
    }

    private static string PickDictionary(string root)
    {
        return Directory.EnumerateFiles(root, "*.txt", SearchOption.AllDirectories)
            .Select(path => new { Path = path, Score = ScoreDictionary(path) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path.Length)
            .Select(item => item.Path)
            .FirstOrDefault() ?? "";
    }

    private static int ScoreDictionary(string path)
    {
        var file = Path.GetFileName(path).ToLowerInvariant();
        var full = path.Replace('\\', '/').ToLowerInvariant();
        var score = 0;
        if (file.Contains("keys", StringComparison.Ordinal))
        {
            score += 10;
        }

        if (file.Contains("dict", StringComparison.Ordinal))
        {
            score += 8;
        }

        if (full.Contains("ppocr", StringComparison.Ordinal))
        {
            score += 4;
        }

        if (file is "readme.txt" or "license.txt")
        {
            score -= 100;
        }

        return score;
    }
}
