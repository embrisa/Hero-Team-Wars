using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Wc3MapEngine.Core.Jass;

public interface IJassApiRepository
{
    JassApiSymbol? Lookup(string name);
    IReadOnlyList<JassApiSearchResult> Search(string query, int limit = JassApiRepository.DefaultSearchLimit);
    IReadOnlyList<JassApiSearchResult> Suggestions(string name, int limit = 6);
}

/// <summary>
/// Read-only metadata for one parameter in the canonical jassdoc catalogue.
/// </summary>
public sealed class JassApiParameter
{
    public JassApiParameter(string name, string type, string? documentation = null)
    {
        Name = name;
        Type = type;
        Documentation = documentation;
    }

    public string Name { get; }
    public string Type { get; }
    public string? Documentation { get; }

    public JsonObject ToJson()
    {
        var result = new JsonObject
        {
            ["name"] = Name,
            ["type"] = Type
        };
        if (!string.IsNullOrWhiteSpace(Documentation)) result["documentation"] = Documentation;
        return result;
    }
}

/// <summary>
/// A jassdoc annotation. Values are intentionally strings because jassdoc
/// annotations may be flags, prose, or structured-looking text.
/// </summary>
public sealed class JassApiAnnotation
{
    public JassApiAnnotation(string name, string? value = null)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public string? Value { get; }

    public JsonObject ToJson()
    {
        var result = new JsonObject { ["name"] = Name };
        if (Value is not null) result["value"] = Value;
        return result;
    }
}

/// <summary>
/// A native, Blizzard.j function, type, or other symbol from jassdoc.
/// </summary>
public sealed class JassApiSymbol
{
    public JassApiSymbol(
        string name,
        string kind,
        string source,
        string declaration,
        IReadOnlyList<JassApiParameter>? parameters = null,
        string? returnType = null,
        string? documentation = null,
        IReadOnlyList<JassApiAnnotation>? annotations = null,
        int? sourceLine = null,
        string? extends = null)
    {
        Name = name;
        Kind = kind;
        Source = source;
        Declaration = declaration;
        Parameters = parameters ?? Array.Empty<JassApiParameter>();
        ReturnType = returnType;
        Documentation = documentation;
        Annotations = annotations ?? Array.Empty<JassApiAnnotation>();
        SourceLine = sourceLine;
        Extends = extends;
    }

    public string Name { get; }
    public string Kind { get; }
    public string Source { get; }
    public string Declaration { get; }
    public IReadOnlyList<JassApiParameter> Parameters { get; }
    public string? ReturnType { get; }
    public string? Documentation { get; }
    public IReadOnlyList<JassApiAnnotation> Annotations { get; }
    public int? SourceLine { get; }
    public string? Extends { get; }

    // Compatibility aliases keep callers independent of the JSON spelling.
    public string? Return_type => ReturnType;
    public int? Source_line => SourceLine;

    public JsonObject ToJson()
    {
        var result = new JsonObject
        {
            ["name"] = Name,
            ["kind"] = Kind,
            ["source"] = Source,
            ["declaration"] = Declaration,
            ["parameters"] = new JsonArray(Parameters.Select(parameter => (JsonNode?)parameter.ToJson()).ToArray())
        };
        if (ReturnType is not null) result["return_type"] = ReturnType;
        if (!string.IsNullOrWhiteSpace(Documentation)) result["documentation"] = Documentation;
        result["annotations"] = new JsonArray(Annotations.Select(annotation => (JsonNode?)annotation.ToJson()).ToArray());
        if (SourceLine is not null) result["source_line"] = SourceLine.Value;
        if (Extends is not null) result["extends"] = Extends;
        return result;
    }
}

public sealed class JassApiSearchResult
{
    public JassApiSearchResult(JassApiSymbol symbol, double score, IReadOnlyList<string>? matchedFields = null)
    {
        Symbol = symbol;
        Score = score;
        MatchedFields = matchedFields ?? Array.Empty<string>();
    }

    public JassApiSymbol Symbol { get; }
    public double Score { get; }
    public IReadOnlyList<string> MatchedFields { get; }

    public JsonObject ToJson()
    {
        var result = Symbol.ToJson();
        result["score"] = Math.Round(Score, 6);
        result["matched_fields"] = new JsonArray(MatchedFields.Select(field => (JsonNode?)field).ToArray());
        return result;
    }
}

/// <summary>
/// The in-memory, deterministic read-only index over one generated jassdoc
/// JSON file. The repository never contacts GitHub or reparses data per call.
/// </summary>
public sealed class JassApiRepository : IJassApiRepository
{
    public const string ExpectedSchemaVersion = "1.0";
    public const string CanonicalSourceCommit = "deddec452ec16ea355ca0aa47046b88d416dbc65";
    public const int DefaultSearchLimit = 12;
    public const int MaximumSearchLimit = 64;

    private static readonly ConcurrentDictionary<string, Lazy<JassApiRepository>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<JassApiRepository> DefaultRepository = new(LoadDefault, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Regex CamelCaseBoundary = new("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WordPattern = new("[A-Za-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyDictionary<string, JassApiSymbol> symbols;
    private readonly IReadOnlyDictionary<string, string> normalizedTokenText;
    private readonly IReadOnlyDictionary<string, int> tokenDocumentFrequency;

    public JassApiRepository(string dataPath)
    {
        if (string.IsNullOrWhiteSpace(dataPath)) throw new ArgumentException("A jass-api.json path is required.", nameof(dataPath));

        DataPath = Path.GetFullPath(dataPath);
        if (!File.Exists(DataPath)) throw new FileNotFoundException($"Canonical JASS API data was not found: {DataPath}", DataPath);

        using var stream = File.OpenRead(DataPath);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Canonical JASS API data must be a JSON object.");

        SchemaVersion = ReadString(root, "schema_version") ?? string.Empty;
        SourceRepository = ReadString(root, "source_repository") ?? string.Empty;
        SourceCommit = ReadString(root, "source_commit") ?? string.Empty;
        if (!string.Equals(SchemaVersion, ExpectedSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Canonical JASS API schema '{SchemaVersion}' is unsupported; expected '{ExpectedSchemaVersion}'.");
        }
        if (!string.Equals(SourceCommit, CanonicalSourceCommit, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Canonical JASS API commit '{SourceCommit}' does not match the project pin '{CanonicalSourceCommit}'.");
        }
        var entries = root.TryGetProperty("symbols", out var symbolArray) && symbolArray.ValueKind == JsonValueKind.Array
            ? symbolArray.EnumerateArray().ToArray()
            : throw new InvalidDataException("Canonical JASS API data must contain a symbols array.");

        var index = new Dictionary<string, JassApiSymbol>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Every JASS API symbol must be an object.");
            var symbol = ParseSymbol(entry);
            if (string.IsNullOrWhiteSpace(symbol.Name)) throw new InvalidDataException("Every JASS API symbol must have a name.");
            if (!index.TryAdd(symbol.Name, symbol)) throw new InvalidDataException($"Canonical JASS API data contains duplicate symbol '{symbol.Name}'.");
        }

        symbols = index;
        normalizedTokenText = index.Values.ToDictionary(symbol => symbol.Name, BuildSearchText, StringComparer.Ordinal);
        tokenDocumentFrequency = index.Values
            .SelectMany(symbol => Tokenize(symbol.Name).Distinct(StringComparer.Ordinal))
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    public string DataPath { get; }
    public string SchemaVersion { get; }
    public string SourceRepository { get; }
    public string SourceCommit { get; }
    public int Count => symbols.Count;
    public IReadOnlyCollection<JassApiSymbol> Symbols => symbols.Values.ToArray();

    /// <summary>Returns the exact case-sensitive symbol, or null if absent.</summary>
    public JassApiSymbol? Lookup(string name)
        => name is not null && symbols.TryGetValue(name, out var symbol) ? symbol : null;

    public JassApiSymbol? LookupSymbol(string name) => Lookup(name);
    public JassApiSymbol? GetFunctionSignature(string name) => Lookup(name);

    /// <summary>
    /// Ranks generic name, token, parameter, documentation, annotation, and
    /// source matches. Ties are broken by exact ordinal symbol name.
    /// </summary>
    public IReadOnlyList<JassApiSearchResult> Search(string query, int limit = DefaultSearchLimit)
    {
        limit = Math.Clamp(limit, 1, MaximumSearchLimit);
        query ??= string.Empty;
        var queryTokens = Tokenize(query);
        var normalizedQuery = Normalize(query);

        return symbols.Values
            .Select(symbol => Score(symbol, normalizedQuery, queryTokens))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Symbol.Name, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public IReadOnlyList<JassApiSearchResult> SearchSymbols(string query, int limit = DefaultSearchLimit) => Search(query, limit);
    public IReadOnlyList<JassApiSearchResult> Suggestions(string name, int limit = 6) => Search(name, MaximumSearchLimit)
        .Where(result => result.Symbol.Kind is "native" or "function")
        .Take(Math.Clamp(limit, 1, MaximumSearchLimit))
        .ToArray();

    public JsonObject LookupJson(string name)
    {
        var symbol = Lookup(name);
        var result = new JsonObject
        {
            ["found"] = symbol is not null,
            ["name"] = name
        };
        if (symbol is not null)
        {
            result["symbol"] = symbol.ToJson();
            return result;
        }

        result["error"] = $"Unknown JASS symbol: {name}";
        result["suggestions"] = new JsonArray(Suggestions(name).Select(item => (JsonNode?)item.ToJson()).ToArray());
        return result;
    }

    public JsonObject SearchJson(string query, int limit = DefaultSearchLimit)
    {
        var results = Search(query, limit);
        return new JsonObject
        {
            ["query"] = query,
            ["results"] = new JsonArray(results.Select(item => (JsonNode?)item.ToJson()).ToArray()),
            ["count"] = results.Count
        };
    }

    public JsonObject ToJson(bool includeSymbols = false)
    {
        var result = new JsonObject
        {
            ["schema_version"] = SchemaVersion,
            ["source_repository"] = SourceRepository,
            ["source_commit"] = SourceCommit,
            ["data_path"] = DataPath,
            ["symbol_count"] = Count
        };
        if (includeSymbols) result["symbols"] = new JsonArray(Symbols.OrderBy(symbol => symbol.Name, StringComparer.Ordinal).Select(symbol => (JsonNode?)symbol.ToJson()).ToArray());
        return result;
    }

    public static JassApiRepository Load(string dataPath)
    {
        var fullPath = Path.GetFullPath(dataPath);
        return Cache.GetOrAdd(fullPath, path => new Lazy<JassApiRepository>(() => new JassApiRepository(path), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public static JassApiRepository FromFile(string dataPath) => Load(dataPath);
    public static JassApiRepository FromDefault() => Default;
    public static JassApiRepository Default => DefaultRepository.Value;

    private static JassApiRepository LoadDefault() => Load(ResolveDefaultDataPath());

    /// <summary>
    /// Locates both copied runtime data and a repository checkout. This is
    /// deliberately bounded and deterministic; no remote fallback exists.
    /// </summary>
    public static string ResolveDefaultDataPath()
    {
        var candidates = new List<string>();
        var configured = Environment.GetEnvironmentVariable("WC3_JASS_API_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);

        foreach (var root in Roots())
        {
            candidates.Add(Path.Combine(root, "data", "jassdoc", "jass-api.json"));
            candidates.Add(Path.Combine(root, "jassdoc", "jass-api.json"));
            candidates.Add(Path.Combine(root, "jass-api.json"));
            candidates.Add(Path.Combine(root, "map-engine", "data", "jassdoc", "jass-api.json"));
            candidates.Add(Path.Combine(root, "tools", "wc3-map-mcp", "map-engine", "data", "jassdoc", "jass-api.json"));
        }

        foreach (var candidate in candidates.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(
            "Canonical JASS API data was not found. Expected a copied data/jassdoc/jass-api.json or repository map-engine/data/jassdoc/jass-api.json. Set WC3_JASS_API_PATH to an explicit local dataset path.");
    }

    public static bool TryLoadDefault(out JassApiRepository? repository, out string? error)
    {
        try
        {
            repository = Default;
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            repository = null;
            error = exception.Message;
            return false;
        }
    }

    private static IEnumerable<string> Roots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var starts = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory(), Path.GetDirectoryName(typeof(JassApiRepository).Assembly.Location) };
        foreach (var start in starts.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var current = new DirectoryInfo(Path.GetFullPath(start!));
            for (var depth = 0; depth < 10 && current is not null; depth++, current = current.Parent)
            {
                if (seen.Add(current.FullName)) yield return current.FullName;
            }
        }
    }

    private static JassApiSymbol ParseSymbol(JsonElement entry)
    {
        var parameters = new List<JassApiParameter>();
        if (entry.TryGetProperty("parameters", out var parameterArray) && parameterArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var parameter in parameterArray.EnumerateArray())
            {
                var parameterName = ReadString(parameter, "name") ?? string.Empty;
                var parameterType = ReadString(parameter, "type") ?? string.Empty;
                parameters.Add(new JassApiParameter(parameterName, parameterType, ReadString(parameter, "documentation")));
            }
        }

        var annotations = new List<JassApiAnnotation>();
        if (entry.TryGetProperty("annotations", out var annotationArray) && annotationArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var annotation in annotationArray.EnumerateArray())
            {
                if (annotation.ValueKind == JsonValueKind.String)
                {
                    annotations.Add(new JassApiAnnotation(annotation.GetString() ?? string.Empty));
                }
                else if (annotation.ValueKind == JsonValueKind.Object)
                {
                    annotations.Add(new JassApiAnnotation(ReadString(annotation, "name") ?? string.Empty, ReadString(annotation, "value")));
                }
            }
        }

        return new JassApiSymbol(
            ReadString(entry, "name") ?? string.Empty,
            ReadString(entry, "kind") ?? string.Empty,
            ReadString(entry, "source") ?? string.Empty,
            ReadString(entry, "declaration") ?? string.Empty,
            parameters,
            ReadString(entry, "return_type") ?? ReadString(entry, "returnType"),
            ReadString(entry, "documentation"),
            annotations,
            ReadInt(entry, "source_line") ?? ReadInt(entry, "sourceLine"),
            ReadString(entry, "extends"));
    }

    private static string? ReadString(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var child) || child.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        return child.ValueKind == JsonValueKind.String ? child.GetString() : child.ToString();
    }

    private static int? ReadInt(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var child) || child.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (child.ValueKind == JsonValueKind.Number && child.TryGetInt32(out var number)) return number;
        return int.TryParse(child.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : null;
    }

    private static string BuildSearchText(JassApiSymbol symbol)
    {
        var builder = new StringBuilder();
        builder.Append(symbol.Name).Append(' ').Append(string.Join(' ', Tokenize(symbol.Name)));
        builder.Append(' ').Append(symbol.Kind).Append(' ').Append(symbol.Source).Append(' ').Append(symbol.Declaration).Append(' ').Append(symbol.ReturnType);
        builder.Append(' ').Append(symbol.Documentation);
        foreach (var parameter in symbol.Parameters) builder.Append(' ').Append(parameter.Name).Append(' ').Append(parameter.Type).Append(' ').Append(parameter.Documentation);
        foreach (var annotation in symbol.Annotations) builder.Append(' ').Append(annotation.Name).Append(' ').Append(annotation.Value);
        return Normalize(builder.ToString());
    }

    private JassApiSearchResult Score(JassApiSymbol symbol, string normalizedQuery, IReadOnlyList<string> queryTokens)
    {
        if (queryTokens.Count == 0) return new JassApiSearchResult(symbol, 0, Array.Empty<string>());

        var name = Normalize(symbol.Name);
        var text = normalizedTokenText[symbol.Name];
        var nameTokens = Tokenize(symbol.Name);
        var matchedFields = new HashSet<string>(StringComparer.Ordinal);
        var score = 0d;

        if (name.Equals(normalizedQuery, StringComparison.Ordinal)) { score += 1000; matchedFields.Add("name_exact"); }
        else if (name.StartsWith(normalizedQuery, StringComparison.Ordinal)) { score += 180; matchedFields.Add("name_prefix"); }
        else if (name.Contains(normalizedQuery, StringComparison.Ordinal)) { score += 100; matchedFields.Add("name"); }

        var tokenMatches = 0;
        foreach (var token in queryTokens)
        {
            if (nameTokens.Contains(token, StringComparer.Ordinal))
            {
                tokenDocumentFrequency.TryGetValue(token, out var frequency);
                var rarity = Math.Log((symbols.Count + 1d) / (frequency + 1d));
                score += 45 + 30 * rarity;
                tokenMatches++;
                matchedFields.Add("name_token");
            }
            else if (text.Contains(token, StringComparison.Ordinal)) { score += 25; tokenMatches++; matchedFields.Add("metadata"); }
            else if (nameTokens.Any(candidate => candidate.StartsWith(token, StringComparison.Ordinal))) { score += 35; tokenMatches++; matchedFields.Add("name_prefix_token"); }
            else
            {
                var nearest = nameTokens.Select(candidate => EditDistance(token, candidate)).DefaultIfEmpty(int.MaxValue).Min();
                if (nearest <= Math.Max(1, token.Length / 3)) { score += 12; matchedFields.Add("name_fuzzy"); }
            }
        }

        if (tokenMatches == queryTokens.Count) score += 45;
        score += Math.Max(0, 20 - EditDistance(normalizedQuery, name));
        return new JassApiSearchResult(symbol, score, matchedFields.OrderBy(field => field, StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<string> Tokenize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
        var separated = CamelCaseBoundary.Replace(value.Replace('_', ' '), " ");
        return WordPattern.Matches(separated)
            .Select(match => match.Value.ToLowerInvariant())
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string Normalize(string value) => string.Join(' ', Tokenize(value));

    private static int EditDistance(string left, string right)
    {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }
}
