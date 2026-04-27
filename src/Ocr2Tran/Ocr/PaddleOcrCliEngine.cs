using System.Diagnostics;
using System.Text;
using Ocr2Tran.App;
using Ocr2Tran.Core;

namespace Ocr2Tran.Ocr;

public sealed class PaddleOcrCliEngine : IOcrEngine
{
    private readonly PaddleSettings _settings;

    public PaddleOcrCliEngine(PaddleSettings settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<TextRegion>> RecognizeAsync(CapturedScreen screen, CancellationToken cancellationToken)
    {
        var imagePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ocr2tran-{Guid.NewGuid():N}.png");
        screen.Image.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Math.Max(1000, _settings.TimeoutMs));

            var psi = new ProcessStartInfo
            {
                FileName = _settings.Executable,
                Arguments = BuildArguments(imagePath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Unable to start PaddleOCR process.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"PaddleOCR exited with code {process.ExitCode}: {stderr}");
            }

            return PaddleOcrOutputParser.Parse(stdout, screen.Origin, screen.ScaleX, screen.ScaleY);
        }
        finally
        {
            TryDelete(imagePath);
        }
    }

    private string BuildArguments(string imagePath)
    {
        var modelArgs = BuildModelArguments();
        var arguments = _settings.ArgumentsTemplate
            .Replace("{image}", imagePath, StringComparison.Ordinal)
            .Replace("{lang}", _settings.Language, StringComparison.Ordinal)
            .Replace("{useAngleCls}", _settings.UseAngleCls ? "true" : "false", StringComparison.Ordinal)
            .Replace("{modelArgs}", modelArgs, StringComparison.Ordinal);

        if (!arguments.Contains("--det_model_dir", StringComparison.Ordinal) &&
            !arguments.Contains("--rec_model_dir", StringComparison.Ordinal) &&
            modelArgs.Length > 0)
        {
            arguments = $"{arguments} {modelArgs}";
        }

        return arguments.Trim();
    }

    private string BuildModelArguments()
    {
        if (!_settings.UseImportedModels)
        {
            return "";
        }

        var args = new List<string>();
        AddPathArg(args, "--det_model_dir", _settings.DetectionModelDir);
        AddPathArg(args, "--rec_model_dir", _settings.RecognitionModelDir);
        AddPathArg(args, "--cls_model_dir", _settings.ClassificationModelDir);
        AddPathArg(args, "--rec_char_dict_path", _settings.RecCharDictPath);

        return string.Join(' ', args);
    }

    private static void AddPathArg(List<string> args, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            args.Add($"{name} {Quote(value)}");
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
