using System.Text;
using System.Text.Json;
using Ocr2Tran.App;

namespace Ocr2Tran.Translation;

public sealed class GoogleTranslator : ITranslator, IDisposable
{
    private readonly TranslationSettings _settings;
    private readonly HttpClient _client = new();
    private readonly RateLimiter _rateLimiter;

    public GoogleTranslator(TranslationSettings settings)
    {
        _settings = settings;
        _rateLimiter = new RateLimiter(settings.Rps);
    }

    public string Name => "google";

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.Google.ApiKey) && string.IsNullOrWhiteSpace(_settings.Google.BearerToken))
        {
            return text;
        }

        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(1000, _settings.TimeoutMs));

        var url = string.IsNullOrWhiteSpace(_settings.Google.ApiKey)
            ? _settings.Google.Endpoint
            : $"{_settings.Google.Endpoint}?key={Uri.EscapeDataString(_settings.Google.ApiKey)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrWhiteSpace(_settings.Google.BearerToken))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.Google.BearerToken);
        }

        var body = JsonSerializer.Serialize(new
        {
            q = text,
            source = _settings.SourceLanguage == "auto" ? null : _settings.SourceLanguage,
            target = _settings.TargetLanguage,
            format = "text"
        });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        return JsonPathReader.ReadString(document.RootElement, "data.translations.0.translatedText") ?? text;
    }

    public void Dispose()
    {
        _client.Dispose();
        _rateLimiter.Dispose();
    }
}
