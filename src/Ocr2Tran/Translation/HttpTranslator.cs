using System.Text;
using System.Text.Json;
using Ocr2Tran.App;

namespace Ocr2Tran.Translation;

public sealed class HttpTranslator : ITranslator, IDisposable
{
    private readonly TranslationSettings _settings;
    private readonly HttpClient _client = new();
    private readonly RateLimiter _rateLimiter;

    public HttpTranslator(TranslationSettings settings)
    {
        _settings = settings;
        _rateLimiter = new RateLimiter(settings.Rps);
    }

    public string Name => _settings.Provider;

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(1000, _settings.TimeoutMs));

        using var request = BuildRequest(text);
        var streamMode = _settings.Http.StreamMode.Trim().ToLowerInvariant();
        var completion = streamMode is "sse" or "ndjson"
            ? HttpCompletionOption.ResponseHeadersRead
            : HttpCompletionOption.ResponseContentRead;

        using var response = await _client.SendAsync(request, completion, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (streamMode == "sse")
        {
            return await ReadSseAsync(response, timeout.Token).ConfigureAwait(false);
        }

        if (streamMode == "ndjson")
        {
            return await ReadNdjsonAsync(response, timeout.Token).ConfigureAwait(false);
        }

        var content = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        return ReadResponseText(content);
    }

    private HttpRequestMessage BuildRequest(string text)
    {
        var method = new HttpMethod(string.IsNullOrWhiteSpace(_settings.Http.Method) ? "POST" : _settings.Http.Method);
        var request = new HttpRequestMessage(method, _settings.Http.Endpoint);
        var headers = ReadHeaders();

        if (method != HttpMethod.Get)
        {
            var body = TemplateRenderer.Render(_settings.Http.BodyTemplate, new Dictionary<string, string>
            {
                ["text"] = text,
                ["prompt"] = _settings.Http.Prompt,
                ["source"] = _settings.SourceLanguage,
                ["target"] = _settings.TargetLanguage,
                ["apiKey"] = _settings.Http.ApiKey
            });
            request.Content = new StringContent(body, Encoding.UTF8, ResolveBodyContentType(headers));
        }

        ApplyHeaders(request, headers);
        ApplyStreamHeaders(request, headers);
        ApplyApiKey(request, headers);
        return request;
    }

    private Dictionary<string, string> ReadHeaders()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(_settings.Http.HeadersJson))
        {
            return headers;
        }

        using var document = JsonDocument.Parse(_settings.Http.HeadersJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return headers;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            headers[property.Name] = property.Value.GetString() ?? property.Value.ToString();
        }

        return headers;
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (name, value) in headers)
        {
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }
    }

    private void ApplyStreamHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        if (_settings.Http.StreamMode.Trim().Equals("sse", StringComparison.OrdinalIgnoreCase) &&
            !headers.ContainsKey("Accept") &&
            !request.Headers.Contains("Accept"))
        {
            request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        }
    }

    private string ResolveBodyContentType(IReadOnlyDictionary<string, string> headers)
    {
        if (headers.TryGetValue("Content-Type", out var contentType) && !string.IsNullOrWhiteSpace(contentType))
        {
            return contentType;
        }

        return string.IsNullOrWhiteSpace(_settings.Http.BodyContentType)
            ? "application/json"
            : _settings.Http.BodyContentType;
    }

    private void ApplyApiKey(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        if (string.IsNullOrWhiteSpace(_settings.Http.ApiKey) ||
            headers.ContainsKey("Authorization") ||
            request.Headers.Contains("Authorization"))
        {
            return;
        }

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.Http.ApiKey);
    }

    private async Task<string> ReadSseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var eventBuilder = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (AppendSseEvent(builder, eventBuilder))
                {
                    break;
                }

                continue;
            }

            if (line.StartsWith(":", StringComparison.Ordinal))
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (eventBuilder.Length > 0)
            {
                eventBuilder.Append('\n');
            }

            eventBuilder.Append(line[5..].TrimStart());
        }

        if (eventBuilder.Length > 0)
        {
            AppendSseEvent(builder, eventBuilder);
        }

        return builder.ToString();
    }

    private async Task<string> ReadNdjsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                var payload = line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    ? line[5..].TrimStart()
                    : line;
                if (payload.Trim() == "[DONE]")
                {
                    break;
                }

                AppendDelta(builder, payload);
            }
        }

        return builder.ToString();
    }

    private bool AppendSseEvent(StringBuilder builder, StringBuilder eventBuilder)
    {
        var payload = eventBuilder.ToString().Trim();
        eventBuilder.Clear();
        if (payload.Length == 0)
        {
            return false;
        }

        if (payload == "[DONE]")
        {
            return true;
        }

        AppendDelta(builder, payload);
        return false;
    }

    private void AppendDelta(StringBuilder builder, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var delta = ReadStreamText(document.RootElement);
            if (!string.IsNullOrEmpty(delta))
            {
                builder.Append(delta);
            }
        }
        catch (JsonException)
        {
            if (!json.StartsWith("{", StringComparison.Ordinal) &&
                !json.StartsWith("[", StringComparison.Ordinal) &&
                json != "[DONE]")
            {
                builder.Append(json);
            }
        }
    }

    private string ReadResponseText(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return JsonPathReader.ReadString(document.RootElement, _settings.Http.ResponseFieldPath) ??
                   ReadCommonText(document.RootElement) ??
                   content;
        }
        catch (JsonException)
        {
            return content;
        }
    }

    private string? ReadStreamText(JsonElement root)
    {
        return JsonPathReader.ReadString(root, _settings.Http.StreamDeltaFieldPath) ??
               JsonPathReader.ReadString(root, _settings.Http.ResponseFieldPath) ??
               ReadCommonText(root);
    }

    private static string? ReadCommonText(JsonElement root)
    {
        return JsonPathReader.ReadString(root, "response") ??
               JsonPathReader.ReadString(root, "text") ??
               JsonPathReader.ReadString(root, "content");
    }

    public void Dispose()
    {
        _client.Dispose();
        _rateLimiter.Dispose();
    }
}
