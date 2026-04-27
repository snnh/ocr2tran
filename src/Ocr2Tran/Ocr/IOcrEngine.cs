using Ocr2Tran.Core;

namespace Ocr2Tran.Ocr;

public interface IOcrEngine
{
    Task<IReadOnlyList<TextRegion>> RecognizeAsync(CapturedScreen screen, CancellationToken cancellationToken);
}
