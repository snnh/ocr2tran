namespace Ocr2Tran.Translation;

public interface ITranslator
{
    string Name { get; }

    Task<string> TranslateAsync(string text, CancellationToken cancellationToken);
}
