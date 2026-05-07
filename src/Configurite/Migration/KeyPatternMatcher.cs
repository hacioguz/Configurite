using System.Text.RegularExpressions;

namespace Configurite.Migration;

/// <summary>
/// EN: Matches configuration keys against glob-style patterns where <c>*</c> means
///     "zero or more characters" (segment-agnostic). Comparison is case-insensitive.
/// TR: Yapılandırma anahtarlarını <c>*</c> "sıfır veya daha fazla karakter" anlamına gelen
///     glob-stili desenlerle karşılaştırır (segment'e duyarsız). Büyük/küçük harf duyarsızdır.
/// </summary>
internal sealed class KeyPatternMatcher
{
    private readonly Regex[] _patterns;

    public KeyPatternMatcher(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        _patterns = patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(ToRegex)
            .ToArray();
    }

    /// <summary>
    /// EN: Returns <see langword="true"/> when <paramref name="key"/> matches any pattern.
    /// TR: <paramref name="key"/> herhangi bir desenle eşleşirse <see langword="true"/> döner.
    /// </summary>
    public bool IsMatch(string key)
    {
        if (_patterns.Length == 0 || string.IsNullOrEmpty(key))
        {
            return false;
        }

        foreach (var pattern in _patterns)
        {
            if (pattern.IsMatch(key))
            {
                return true;
            }
        }

        return false;
    }

    private static Regex ToRegex(string glob)
    {
        // EN: Escape regex metacharacters, then turn the literal '*' back into '.*'.
        // TR: Regex meta karakterlerini kaçır, sonra harfi harfine '*'ı yeniden '.*' yap.
        var escaped = Regex.Escape(glob).Replace("\\*", ".*", StringComparison.Ordinal);
        return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
