namespace Ocr2Tran.Ocr;

public sealed record OcrModelImportResult(
    string Root,
    string DetectionModelDir,
    string RecognitionModelDir,
    string ClassificationModelDir,
    string RecCharDictPath,
    IReadOnlyList<string> Warnings)
{
    public bool HasRequiredModels => !string.IsNullOrWhiteSpace(DetectionModelDir) && !string.IsNullOrWhiteSpace(RecognitionModelDir);
}
