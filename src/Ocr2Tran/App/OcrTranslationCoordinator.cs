using System.Security.Cryptography;
using System.Text;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Ocr2Tran.Core;
using Ocr2Tran.Ocr;
using Ocr2Tran.Plugins;
using Ocr2Tran.Runtime;
using Ocr2Tran.Translation;
using Ocr2Tran.Windows;

namespace Ocr2Tran.App;

public sealed class OcrTranslationCoordinator : IDisposable
{
    private AppSettings _settings;
    private readonly OverlayForm _overlay;
    private readonly ScreenCaptureService _screenCapture = new();
    private IOcrEngine _ocrEngine;
    private OcrTextPostProcessor _postProcessor;
    private OcrImagePreprocessor _imagePreprocessor;
    private ITranslator _translator;
    private PluginPipeline _plugins;
    private readonly TranslationCache _cache = new();
    private PerformanceGuard _performanceGuard;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly System.Windows.Forms.Timer _autoTimer = new();
    private CancellationTokenSource? _runCts;
    private string? _lastScreenshotHash;
    private Rectangle? _lastCaptureBounds;
    private Rectangle? _autoRegionTranslateBounds;
    private string? _lastOcrFingerprint;
    private IReadOnlyList<TextRegion> _lastRegions = Array.Empty<TextRegion>();

    public OcrTranslationCoordinator(AppSettings settings, OverlayForm overlay)
    {
        _settings = settings;
        _overlay = overlay;
        _plugins = PluginPipeline.Load(settings);
        _ocrEngine = CreateOcrEngineOrNoop(settings, _plugins, "Ready", "OCR disabled", out var initialStatus);
        _postProcessor = new OcrTextPostProcessor(settings.Ocr.PostProcessing);
        _imagePreprocessor = new OcrImagePreprocessor(settings.Ocr.ImagePreprocessing);
        _translator = TranslatorFactory.Create(settings.Translation, _plugins);
        _performanceGuard = new PerformanceGuard(settings.Performance);
        _autoTimer.Interval = Math.Max(250, settings.Mode.AutoIntervalMs);
        _autoTimer.Tick += async (_, _) => await RunCurrentLoopAsync().ConfigureAwait(false);
        Status = AddPluginLoadStatus(initialStatus, _plugins);
    }

    public bool AutoOcrEnabled { get; private set; }
    public bool AutoTranslateEnabled { get; private set; }
    public bool AutoRegionTranslateEnabled { get; private set; }
    public string Status { get; private set; } = "Ready";

    public event EventHandler? StateChanged;

    public void Start()
    {
        _performanceGuard.Apply();
    }

    public async Task ReloadOcrEngineAsync()
    {
        StopLoops();
        await _runGate.WaitAsync();
        try
        {
            DisposeOcrEngine();
            _ocrEngine = CreateOcrEngineOrNoop(_settings, _plugins, "OCR model loaded", "OCR model load failed", out var status);
            ClearRunCache();
            SetStatus(AddPluginLoadStatus(status, _plugins));
        }
        finally
        {
            _runGate.Release();
        }
    }

    public async Task ApplySettingsAsync(AppSettings settings)
    {
        StopLoops();

        await _runGate.WaitAsync();
        try
        {
            DisposeOcrEngine();
            DisposeTranslator();
            DisposePlugins();
            _settings = settings;
            _plugins = PluginPipeline.Load(settings);
            _ocrEngine = CreateOcrEngineOrNoop(settings, _plugins, "Configuration applied", "Configuration applied, OCR disabled", out var status);
            _postProcessor = new OcrTextPostProcessor(settings.Ocr.PostProcessing);
            _imagePreprocessor = new OcrImagePreprocessor(settings.Ocr.ImagePreprocessing);
            _translator = TranslatorFactory.Create(settings.Translation, _plugins);
            _performanceGuard = new PerformanceGuard(settings.Performance);
            _autoTimer.Interval = Math.Max(250, settings.Mode.AutoIntervalMs);
            ApplyOverlaySettings(settings.Overlay);
            _performanceGuard.Apply();
            ClearRunCache();
            SetStatus(AddPluginLoadStatus(status, _plugins));
        }
        finally
        {
            _runGate.Release();
        }
    }

    private void ApplyOverlaySettings(OverlaySettings settings)
    {
        if (_overlay.IsDisposed)
        {
            return;
        }

        if (_overlay.InvokeRequired)
        {
            _overlay.Invoke(() => _overlay.ApplySettings(settings));
        }
        else
        {
            _overlay.ApplySettings(settings);
        }
    }

    private void DisposeOcrEngine()
    {
        if (_plugins.Owns(_ocrEngine))
        {
            return;
        }

        if (_ocrEngine is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void DisposeTranslator()
    {
        if (_plugins.Owns(_translator))
        {
            return;
        }

        if (_translator is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void DisposePlugins()
    {
        _plugins.Dispose();
    }

    private static IOcrEngine CreateOcrEngineOrNoop(AppSettings settings, PluginPipeline plugins, string successStatus, string failureStatusPrefix, out string status)
    {
        try
        {
            status = successStatus;
            return OcrEngineFactory.Create(settings.Ocr, plugins);
        }
        catch (Exception ex)
        {
            status = $"{failureStatusPrefix}: {ex.Message}";
            return new NoopOcrEngine();
        }
    }

    private static string AddPluginLoadStatus(string status, PluginPipeline plugins)
    {
        if (plugins.LoadErrors.Count == 0)
        {
            return status;
        }

        return $"{status}; plugin issue: {plugins.LoadErrors[0]}";
    }

    private void ClearRunCache()
    {
        _lastScreenshotHash = null;
        _lastCaptureBounds = null;
        _lastOcrFingerprint = null;
        _lastRegions = Array.Empty<TextRegion>();
    }

    public async Task RunOcrOnceAsync()
    {
        await RunOnceAsync(translate: false).ConfigureAwait(false);
    }

    public async Task RunOcrTranslateOnceAsync()
    {
        await RunOnceAsync(translate: true).ConfigureAwait(false);
    }

    public async Task RunRegionOcrOnceAsync()
    {
        await RunRegionOnceAsync(translate: false).ConfigureAwait(false);
    }

    public async Task RunRegionOcrTranslateOnceAsync()
    {
        await RunRegionOnceAsync(translate: true).ConfigureAwait(false);
    }

    public void ToggleAutoOcr()
    {
        AutoOcrEnabled = !AutoOcrEnabled;
        if (AutoOcrEnabled)
        {
            AutoTranslateEnabled = false;
            AutoRegionTranslateEnabled = false;
            _autoRegionTranslateBounds = null;
        }

        SyncTimer();
        SetStatus(AutoOcrEnabled ? "Auto OCR running" : "Auto OCR paused");
    }

    public void ToggleAutoTranslate()
    {
        AutoTranslateEnabled = !AutoTranslateEnabled;
        if (AutoTranslateEnabled)
        {
            AutoOcrEnabled = false;
            AutoRegionTranslateEnabled = false;
            _autoRegionTranslateBounds = null;
        }

        SyncTimer();
        SetStatus(AutoTranslateEnabled ? "Auto translation running" : "Auto translation paused");
    }

    public async Task ToggleAutoRegionTranslateAsync()
    {
        if (AutoRegionTranslateEnabled)
        {
            StopLoops();
            return;
        }

        StopLoops();
        SetStatus("Select auto translation region...");
        ClearOverlayForSelection();
        var selectedBounds = SelectRegion();
        if (selectedBounds is null)
        {
            SetStatus("Region auto translation cancelled");
            return;
        }

        _autoRegionTranslateBounds = selectedBounds.Value;
        AutoRegionTranslateEnabled = true;
        SyncTimer();
        SetStatus("Auto region translation running");
        await RunOnceAsync(translate: true, selectedBounds.Value).ConfigureAwait(false);
    }

    public void StopLoops()
    {
        AutoOcrEnabled = false;
        AutoTranslateEnabled = false;
        AutoRegionTranslateEnabled = false;
        _autoRegionTranslateBounds = null;
        _autoTimer.Stop();
        _runCts?.Cancel();
        SetStatus("Paused");
    }

    private void SyncTimer()
    {
        if (AutoOcrEnabled || AutoTranslateEnabled || AutoRegionTranslateEnabled)
        {
            _autoTimer.Start();
        }
        else
        {
            _autoTimer.Stop();
        }
    }

    private Task RunCurrentLoopAsync()
    {
        if (AutoRegionTranslateEnabled)
        {
            if (_autoRegionTranslateBounds is { } bounds)
            {
                return RunOnceAsync(translate: true, bounds);
            }

            StopLoops();
            return Task.CompletedTask;
        }

        return RunOnceAsync(translate: AutoTranslateEnabled);
    }

    private async Task RunRegionOnceAsync(bool translate)
    {
        StopLoops();
        SetStatus("Select OCR region...");
        ClearOverlayForSelection();
        var selectedBounds = SelectRegion();
        if (selectedBounds is null)
        {
            SetStatus("Region selection cancelled");
            return;
        }

        await RunOnceAsync(translate, selectedBounds.Value).ConfigureAwait(false);
    }

    private async Task RunOnceAsync(bool translate, Rectangle? captureBounds = null)
    {
        if (!await _runGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        var cancellationToken = _runCts.Token;

        try
        {
            SetStatus(translate ? "OCR + translating..." : "OCR...");
            using var screen = CaptureScreen(captureBounds);
            var hash = HashBitmap(screen.Image);
            if (_settings.Ocr.SkipIfScreenshotUnchanged &&
                hash == _lastScreenshotHash &&
                screen.Bounds == _lastCaptureBounds &&
                _lastRegions.Count > 0)
            {
                if (translate && !_lastRegions.Any(region => !string.IsNullOrWhiteSpace(region.Translation)))
                {
                    var translated = await TranslateRegionsAsync(_lastRegions, cancellationToken).ConfigureAwait(false);
                    _lastRegions = translated;
                    _lastOcrFingerprint = Fingerprint(translated);
                    Render(translated);
                    SetStatus($"{translated.Count} cached OCR region(s), translated");
                    return;
                }

                SetStatus("Skipped unchanged screenshot");
                Render(_lastRegions);
                return;
            }

            var preprocessedScreen = _imagePreprocessor.Process(screen);
            var ocrScreen = await _plugins.ProcessImageAsync(preprocessedScreen, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<TextRegion> regions;
            try
            {
                regions = await _ocrEngine.RecognizeAsync(ocrScreen, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (!ReferenceEquals(ocrScreen, screen))
                {
                    ocrScreen.Dispose();
                }
            }

            regions = _postProcessor.Process(regions);
            regions = await _plugins.ProcessOcrTextAsync(regions, cancellationToken).ConfigureAwait(false);
            _lastScreenshotHash = hash;
            _lastCaptureBounds = screen.Bounds;

            if (translate)
            {
                regions = await TranslateRegionsAsync(regions, cancellationToken).ConfigureAwait(false);
            }

            _lastRegions = regions;
            _lastOcrFingerprint = Fingerprint(regions);
            Render(regions);
            SetStatus($"{regions.Count} region(s)");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            Render([new TextRegion(new Rectangle(SystemInformation.VirtualScreen.Left + 80, SystemInformation.VirtualScreen.Top + 80, 760, 52), ex.Message, Confidence: 0)]);
        }
        finally
        {
            _performanceGuard.TrimIfOverSoftLimit();
            _runGate.Release();
        }
    }

    private CapturedScreen CaptureScreen(Rectangle? captureBounds)
    {
        var restoreOverlay = HideOverlayForCapture();
        try
        {
            return captureBounds is { } bounds
                ? _screenCapture.Capture(bounds)
                : _screenCapture.CaptureVirtualScreen();
        }
        finally
        {
            RestoreOverlayAfterCapture(restoreOverlay);
        }
    }

    private bool HideOverlayForCapture()
    {
        if (_overlay.IsDisposed)
        {
            return false;
        }

        var wasVisible = false;
        void Hide()
        {
            wasVisible = _overlay.Visible;
            if (wasVisible)
            {
                _overlay.Hide();
                _overlay.Update();
            }
        }

        if (_overlay.InvokeRequired)
        {
            _overlay.Invoke(Hide);
        }
        else
        {
            Hide();
        }

        return wasVisible;
    }

    private void RestoreOverlayAfterCapture(bool restore)
    {
        if (!restore || _overlay.IsDisposed)
        {
            return;
        }

        void Show()
        {
            if (!_overlay.Visible)
            {
                _overlay.Show();
            }
        }

        if (_overlay.InvokeRequired)
        {
            _overlay.Invoke(Show);
        }
        else
        {
            Show();
        }
    }

    private Rectangle? SelectRegion()
    {
        if (_overlay.InvokeRequired)
        {
            Rectangle? selected = null;
            _overlay.Invoke(new Action(() => selected = RegionSelectionForm.SelectRegion()));
            return selected;
        }

        return RegionSelectionForm.SelectRegion();
    }

    private void ClearOverlayForSelection()
    {
        if (_overlay.IsDisposed)
        {
            return;
        }

        if (_overlay.InvokeRequired)
        {
            _overlay.BeginInvoke(_overlay.ClearRegions);
        }
        else
        {
            _overlay.ClearRegions();
        }
    }

    private async Task<IReadOnlyList<TextRegion>> TranslateRegionsAsync(IReadOnlyList<TextRegion> regions, CancellationToken cancellationToken)
    {
        var fingerprint = Fingerprint(regions);
        if (_settings.Performance.SkipTranslationWhenOcrUnchanged && fingerprint == _lastOcrFingerprint && _lastRegions.Any(r => r.Translation is not null))
        {
            return _lastRegions;
        }

        var translated = new List<TextRegion>(regions.Count);
        foreach (var region in regions)
        {
            var text = region.Text.Trim();
            if (text.Length == 0)
            {
                translated.Add(region);
                continue;
            }

            if (_settings.Performance.EnableTranslationCache &&
                _cache.TryGet(_translator.Name, _settings.Translation.SourceLanguage, _settings.Translation.TargetLanguage, text, out var cached))
            {
                translated.Add(region with { Translation = cached });
                continue;
            }

            var translation = await _translator.TranslateAsync(text, cancellationToken).ConfigureAwait(false);
            if (_settings.Performance.EnableTranslationCache)
            {
                _cache.Set(_translator.Name, _settings.Translation.SourceLanguage, _settings.Translation.TargetLanguage, text, translation);
            }

            translated.Add(region with { Translation = translation });
        }

        return translated;
    }

    private void Render(IReadOnlyList<TextRegion> regions)
    {
        if (_overlay.IsDisposed)
        {
            return;
        }

        if (_overlay.InvokeRequired)
        {
            _overlay.BeginInvoke(() => _overlay.SetRegions(regions));
        }
        else
        {
            _overlay.SetRegions(regions);
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string HashBitmap(Bitmap bitmap)
    {
        var bitsPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat);
        if (bitsPerPixel <= 0 || bitsPerPixel % 8 != 0)
        {
            return HashBitmapAsPng(bitmap);
        }

        BitmapData? data = null;
        try
        {
            var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, bitmap.PixelFormat);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            Span<byte> header = stackalloc byte[16];
            BitConverter.TryWriteBytes(header[..4], bitmap.Width);
            BitConverter.TryWriteBytes(header[4..8], bitmap.Height);
            BitConverter.TryWriteBytes(header[8..12], bitsPerPixel);
            BitConverter.TryWriteBytes(header[12..16], (int)bitmap.PixelFormat);
            hash.AppendData(header);

            var rowBytes = bitmap.Width * bitsPerPixel / 8;
            var stride = data.Stride;
            var buffer = new byte[rowBytes];
            for (var y = 0; y < bitmap.Height; y++)
            {
                var offset = stride >= 0
                    ? y * stride
                    : (bitmap.Height - 1 - y) * -stride;
                Marshal.Copy(IntPtr.Add(data.Scan0, offset), buffer, 0, rowBytes);
                hash.AppendData(buffer);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch
        {
        }
        finally
        {
            if (data is not null)
            {
                bitmap.UnlockBits(data);
            }
        }

        return HashBitmapAsPng(bitmap);
    }

    private static string HashBitmapAsPng(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
    }

    private static string Fingerprint(IEnumerable<TextRegion> regions)
    {
        var builder = new StringBuilder();
        foreach (var region in regions)
        {
            builder.Append(region.Bounds).Append('|').Append(region.Text).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public void Dispose()
    {
        _autoTimer.Dispose();
        _runCts?.Cancel();
        _runCts?.Dispose();
        DisposeOcrEngine();
        DisposeTranslator();
        DisposePlugins();

        _runGate.Dispose();
    }
}
