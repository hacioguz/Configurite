using System.Globalization;
using System.Text.Json;

namespace Configurite.Migration;

/// <summary>
/// EN: Flattens a JSON document into the colon-separated key/value pairs used by the
///     .NET configuration system. Arrays are emitted as <c>section:0</c>, <c>section:1</c>, …
/// TR: Bir JSON belgesini, .NET yapılandırma sisteminin kullandığı iki nokta üst üste ile
///     ayrılmış anahtar/değer çiftlerine düzleştirir. Diziler <c>section:0</c>, <c>section:1</c>, … olarak yayılır.
/// </summary>
internal static class JsonFlattener
{
    /// <summary>
    /// EN: Flattens <paramref name="json"/> into a key/value sequence.
    /// TR: <paramref name="json"/> verisini bir anahtar/değer dizisine düzleştirir.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> Flatten(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);

        using var doc = JsonDocument.Parse(json);
        var output = new List<KeyValuePair<string, string>>();
        Walk(doc.RootElement, prefix: string.Empty, output);
        return output;
    }

    private static void Walk(JsonElement element, string prefix, ICollection<KeyValuePair<string, string>> output)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = prefix.Length == 0 ? prop.Name : prefix + ":" + prop.Name;
                    Walk(prop.Value, key, output);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var key = prefix.Length == 0
                        ? index.ToString(CultureInfo.InvariantCulture)
                        : prefix + ":" + index.ToString(CultureInfo.InvariantCulture);
                    Walk(item, key, output);
                    index++;
                }
                break;

            case JsonValueKind.String:
                output.Add(new KeyValuePair<string, string>(prefix, element.GetString() ?? string.Empty));
                break;

            case JsonValueKind.Number:
                output.Add(new KeyValuePair<string, string>(prefix, element.GetRawText()));
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                output.Add(new KeyValuePair<string, string>(prefix, element.GetBoolean() ? "true" : "false"));
                break;

            case JsonValueKind.Null:
                output.Add(new KeyValuePair<string, string>(prefix, string.Empty));
                break;

            case JsonValueKind.Undefined:
            default:
                // EN: Undefined elements are not emitted.
                // TR: Tanımsız elemanlar yayılmaz.
                break;
        }
    }
}
