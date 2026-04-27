namespace Ocr2Tran.Translation;

public sealed class NoopTranslator : ITranslator
{
    public string Name => "noop";

    public Task<string> TranslateAsync(string text, CancellationToken cancellationToken)
    {
        return Task.FromResult(text);
    }
}
