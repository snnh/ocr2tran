using Ocr2Tran.Core;

namespace Ocr2Tran.Ocr;

public sealed class NoopOcrEngine : IOcrEngine
{
    public Task<IReadOnlyList<TextRegion>> RecognizeAsync(CapturedScreen screen, CancellationToken cancellationToken)
    {
        IReadOnlyList<TextRegion> regions =
        [
            new TextRegion(new Rectangle(screen.Origin.X + 80, screen.Origin.Y + 80, 420, 42), "noop OCR: configure PaddleOCR to replace this text", Confidence: 1)
        ];
        return Task.FromResult(regions);
    }
}
