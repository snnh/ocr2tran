using Ocr2Tran.App;
using Ocr2Tran.Core;
using Ocr2Tran.Ocr;
using Ocr2Tran.Translation;

namespace Ocr2Tran.Plugins;

public sealed record PluginContext(
    AppSettings Settings,
    string BaseDirectory,
    string PluginDirectory);

public interface IOcr2TranPlugin
{
    string Name => GetType().FullName ?? GetType().Name;
    int Order => 0;
}

public interface IImageProcessingPlugin : IOcr2TranPlugin
{
    ValueTask<CapturedScreen> ProcessAsync(CapturedScreen screen, PluginContext context, CancellationToken cancellationToken);
}

public interface ITextProcessingPlugin : IOcr2TranPlugin
{
    ValueTask<IReadOnlyList<TextRegion>> ProcessAsync(IReadOnlyList<TextRegion> regions, PluginContext context, CancellationToken cancellationToken);
}

public interface IOcrServicePlugin : IOcr2TranPlugin, IOcrEngine
{
}

public interface ITranslationServicePlugin : IOcr2TranPlugin, ITranslator
{
}
