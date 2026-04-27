using System.Security.Cryptography;
using System.Text;

namespace Ocr2Tran.Core;

public sealed class TranslationCache
{
    private readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public bool TryGet(string provider, string source, string target, string text, out string translation)
    {
        lock (_gate)
        {
            return _items.TryGetValue(MakeKey(provider, source, target, text), out translation!);
        }
    }

    public void Set(string provider, string source, string target, string text, string translation)
    {
        lock (_gate)
        {
            _items[MakeKey(provider, source, target, text)] = translation;
        }
    }

    private static string MakeKey(string provider, string source, string target, string text)
    {
        var payload = $"{provider}\n{source}\n{target}\n{text}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
