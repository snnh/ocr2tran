using System.Net;
using System.Text.RegularExpressions;

namespace Ocr2Tran.Translation;

public static class HttpResponseErrors
{
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var details = await ReadDetailsAsync(response, cancellationToken).ConfigureAwait(false);
        var message = $"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).";
        if (!string.IsNullOrWhiteSpace(details))
        {
            message += $" {details}";
        }

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static async Task<string> ReadDetailsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var summary = Summarize(content);
            return summary.Length == 0 ? "" : $"Response body: {summary}";
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
        {
            return "";
        }
    }

    private static string Summarize(string value)
    {
        var singleLine = Regex.Replace(value, @"\s+", " ").Trim();
        return singleLine.Length <= 500 ? singleLine : singleLine[..500] + "...";
    }
}
