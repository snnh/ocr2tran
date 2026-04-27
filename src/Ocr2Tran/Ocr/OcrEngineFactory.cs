using Ocr2Tran.App;
using Ocr2Tran.Plugins;

namespace Ocr2Tran.Ocr;

public static class OcrEngineFactory
{
    public static IOcrEngine Create(OcrSettings settings)
    {
        return Create(settings, plugins: null);
    }

    public static IOcrEngine Create(OcrSettings settings, PluginPipeline? plugins)
    {
        if (plugins?.FindOcrService(settings.Provider) is { } plugin)
        {
            return plugin;
        }

        return settings.Provider.Trim().ToLowerInvariant() switch
        {
            "onnx" or "onnxruntime" or "ort" => new OnnxOcrEngine(settings.Onnx),
            "paddlecli" or "paddle" or "ppocr" => new PaddleOcrCliEngine(settings.Paddle),
            _ => new NoopOcrEngine()
        };
    }
}
