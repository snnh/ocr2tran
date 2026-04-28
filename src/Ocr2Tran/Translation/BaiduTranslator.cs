using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ocr2Tran.App;

namespace Ocr2Tran.Translation;

public sealed class BaiduTranslator : ITranslator, IDisposable
{
    private readonly TranslationSettings _settings;
    private readonly HttpClient _client = new();
    private readonly RateLimiter _rateLimiter;

    public BaiduTranslator(TranslationSettings settings)
    {
        _settings = settings;
        _rateLimiter = new RateLimiter(settings.Rps);
    }

    public string Name => "baidu";

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.Baidu.AppId) || string.IsNullOrWhiteSpace(_settings.Baidu.Secret))
        {
            return text;
        }

        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        var salt = Random.Shared.Next(100000, 999999).ToString();
        var sign = Md5(_settings.Baidu.AppId + text + salt + _settings.Baidu.Secret);
        var url = $"{_settings.Baidu.Endpoint}?q={Uri.EscapeDataString(text)}&from={Uri.EscapeDataString(_settings.SourceLanguage)}&to={Uri.EscapeDataString(_settings.TargetLanguage)}&appid={Uri.EscapeDataString(_settings.Baidu.AppId)}&salt={salt}&sign={sign}";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(1000, _settings.TimeoutMs));
        using var response = await _client.GetAsync(url, timeout.Token).ConfigureAwait(false);
        await HttpResponseErrors.EnsureSuccessAsync(response, timeout.Token).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("trans_result", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return text;
        }

        return string.Join(Environment.NewLine, results.EnumerateArray()
            .Select(item => item.TryGetProperty("dst", out var dst) ? dst.GetString() : null)
            .Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string Md5(string value)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose()
    {
        _client.Dispose();
        _rateLimiter.Dispose();
    }
}
