namespace Configurite.Cli.Commands;

/// <summary>
/// EN: Tiny argv parser. Positional arguments come first; long options use <c>--name</c> with
///     either <c>--name value</c> or <c>--name=value</c>. Boolean flags are valueless.
/// TR: Küçük argv ayrıştırıcı. Pozisyonel argümanlar önce gelir; uzun opsiyonlar <c>--ad</c>
///     biçimindedir (<c>--ad değer</c> veya <c>--ad=değer</c>). Boolean bayraklar değer almaz.
/// </summary>
internal sealed class Args
{
    public List<string> Positional { get; } = new();
    public Dictionary<string, List<string>> Options { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static Args Parse(string[] argv, ISet<string> knownFlags)
    {
        ArgumentNullException.ThrowIfNull(argv);
        ArgumentNullException.ThrowIfNull(knownFlags);

        var args = new Args();
        for (var i = 0; i < argv.Length; i++)
        {
            var token = argv[i];
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var name = token[2..];
                string? value = null;

                var eq = name.IndexOf('=', StringComparison.Ordinal);
                if (eq >= 0)
                {
                    value = name[(eq + 1)..];
                    name = name[..eq];
                }

                if (knownFlags.Contains(name) && value is null)
                {
                    args.Flags.Add(name);
                    continue;
                }

                if (value is null)
                {
                    if (i + 1 >= argv.Length)
                    {
                        throw new ArgumentException($"option --{name} requires a value");
                    }
                    value = argv[++i];
                }

                if (!args.Options.TryGetValue(name, out var list))
                {
                    list = new List<string>();
                    args.Options[name] = list;
                }
                list.Add(value);
            }
            else
            {
                args.Positional.Add(token);
            }
        }

        return args;
    }

    public string? Single(string name)
        => Options.TryGetValue(name, out var list) ? list.Count > 0 ? list[^1] : null : null;

    public IReadOnlyList<string> Multi(string name)
        => Options.TryGetValue(name, out var list) ? list : Array.Empty<string>();

    public bool HasFlag(string name) => Flags.Contains(name);

    public string RequirePositional(int index, string label)
    {
        if (index >= Positional.Count)
        {
            throw new ArgumentException($"missing required argument: {label}");
        }
        return Positional[index];
    }

    public string Require(string name)
    {
        var v = Single(name);
        if (string.IsNullOrEmpty(v))
        {
            throw new ArgumentException($"option --{name} is required");
        }
        return v;
    }
}
