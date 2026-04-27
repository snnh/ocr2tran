using Ocr2Tran.App;

namespace Ocr2Tran.Ocr;

public static class OcrModelImportService
{
    private static readonly string[] ModelFileNames = ["inference.pdmodel", "model.pdmodel", "inference.json"];
    private static readonly string[] ParamFileNames = ["inference.pdiparams", "model.pdiparams"];

    public static OcrModelImportResult Import(string root, PaddleSettings settings)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        var modelDirs = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Prepend(root)
            .Where(IsPaddleModelDir)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var det = PickModel(modelDirs, "det", "detect", "detection");
        var rec = PickModel(modelDirs, "rec", "recognition");
        var cls = PickModel(modelDirs, "cls", "angle", "class");
        var dict = PickDictionary(root);
        var executable = PickExecutable(root);
        var warnings = new List<string>();

        if (det.Length == 0)
        {
            warnings.Add("未找到检测模型目录，通常目录名包含 det。");
        }

        if (rec.Length == 0)
        {
            warnings.Add("未找到识别模型目录，通常目录名包含 rec。");
        }

        if (dict.Length == 0)
        {
            warnings.Add("未找到识别字典文件，中文模型通常需要 ppocr_keys_v1.txt。");
        }

        if (executable.Length > 0)
        {
            settings.Executable = executable;
        }
        else if (settings.Executable.Equals("paddleocr.exe", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("未在导入目录中找到 paddleocr.exe，将继续使用 PATH 中的 paddleocr.exe。");
        }

        settings.UseImportedModels = true;
        settings.ModelRoot = root;
        settings.DetectionModelDir = det;
        settings.RecognitionModelDir = rec;
        settings.ClassificationModelDir = cls;
        settings.RecCharDictPath = dict;

        return new OcrModelImportResult(root, det, rec, cls, dict, warnings);
    }

    private static bool IsPaddleModelDir(string dir)
    {
        var hasModel = ModelFileNames.Any(name => File.Exists(Path.Combine(dir, name)));
        var hasParams = ParamFileNames.Any(name => File.Exists(Path.Combine(dir, name)));
        return hasModel && hasParams || File.Exists(Path.Combine(dir, "inference.json"));
    }

    private static string PickModel(IEnumerable<string> dirs, params string[] tokens)
    {
        return dirs
            .Select(dir => new
            {
                Dir = dir,
                Score = ScorePath(dir, tokens)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Dir.Length)
            .Select(item => item.Dir)
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

        if (normalized.Contains("infer", StringComparison.Ordinal))
        {
            score += 2;
        }

        if (normalized.Contains("pp-ocr", StringComparison.Ordinal) || normalized.Contains("ppocr", StringComparison.Ordinal))
        {
            score += 1;
        }

        return score;
    }

    private static string PickDictionary(string root)
    {
        return Directory.EnumerateFiles(root, "*.txt", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Score = ScoreDictionary(path)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path.Length)
            .Select(item => item.Path)
            .FirstOrDefault() ?? "";
    }

    private static string PickExecutable(string root)
    {
        return Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Score = ScoreExecutable(path)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path.Length)
            .Select(item => item.Path)
            .FirstOrDefault() ?? "";
    }

    private static int ScoreExecutable(string path)
    {
        var file = Path.GetFileName(path).ToLowerInvariant();
        var score = 0;
        if (file == "paddleocr.exe")
        {
            score += 20;
        }

        if (file.Contains("paddle", StringComparison.Ordinal))
        {
            score += 10;
        }

        if (file.Contains("ocr", StringComparison.Ordinal))
        {
            score += 10;
        }

        return score;
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
