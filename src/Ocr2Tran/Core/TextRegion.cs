namespace Ocr2Tran.Core;

public sealed record TextRegion(
    Rectangle Bounds,
    string Text,
    string? Translation = null,
    double Confidence = 0);
