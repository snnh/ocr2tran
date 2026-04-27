using Ocr2Tran.App;
using Ocr2Tran.Plugins;

namespace Ocr2Tran.Translation;

public static class TranslatorFactory
{
    public static ITranslator Create(TranslationSettings settings)
    {
        return Create(settings, plugins: null);
    }

    public static ITranslator Create(TranslationSettings settings, PluginPipeline? plugins)
    {
        if (plugins?.FindTranslationService(settings.Provider) is { } plugin)
        {
            return plugin;
        }

        return settings.Provider.Trim().ToLowerInvariant() switch
        {
            "baidu" => new BaiduTranslator(settings),
            "google" => new GoogleTranslator(settings),
            "http" or "ai" => new HttpTranslator(settings),
            _ => new NoopTranslator()
        };
    }
}
