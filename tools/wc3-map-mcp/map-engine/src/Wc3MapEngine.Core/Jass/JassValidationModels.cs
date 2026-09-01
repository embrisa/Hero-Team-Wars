using System.Text.Json.Nodes;

namespace Wc3MapEngine.Core.Jass;

public enum JassValidationSeverity
{
    Error,
    Warning
}

public sealed class JassCallArgument
{
    public JassCallArgument(string? expression = null, string? type = null, bool? confident = null)
    {
        Expression = expression ?? string.Empty;
        Type = type;
        Confident = confident ?? type is not null;
    }

    public string Expression { get; }
    public string? Type { get; }
    public bool Confident { get; }

    public static JassCallArgument FromJson(JsonNode? value)
    {
        if (value is JsonObject objectValue)
        {
            var expression = objectValue["expression"]?.GetValue<string>()
                ?? objectValue["value"]?.ToJsonString()
                ?? string.Empty;
            var type = objectValue["type"]?.GetValue<string>();
            bool? confident = null;
            if (objectValue["confident"] is JsonValue confidenceValue && confidenceValue.TryGetValue<bool>(out var confidence)) confident = confidence;
            return new JassCallArgument(expression, type, confident);
        }

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var text)) return new JassCallArgument(text);
            if (jsonValue.TryGetValue<bool>(out var boolean)) return new JassCallArgument(boolean ? "true" : "false", "boolean", true);
            if (jsonValue.TryGetValue<int>(out var integer)) return new JassCallArgument(integer.ToString(System.Globalization.CultureInfo.InvariantCulture), "integer", true);
            if (jsonValue.TryGetValue<double>(out var real)) return new JassCallArgument(real.ToString(System.Globalization.CultureInfo.InvariantCulture), "real", true);
        }

        return new JassCallArgument(value?.ToJsonString() ?? string.Empty);
    }

    public JsonObject ToJson() => new()
    {
        ["expression"] = Expression,
        ["type"] = Type,
        ["confident"] = Confident
    };
}

public sealed class JassValidationContext
{
    public string? Source { get; init; }
    public string? ExistingSource { get; init; }
    public IReadOnlyList<JassApiSymbol> Symbols { get; init; } = Array.Empty<JassApiSymbol>();
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> Types { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public string? CombinedSource => string.Join("\n", new[] { ExistingSource, Source }.Where(text => !string.IsNullOrWhiteSpace(text)));

    public static JassValidationContext FromJson(JsonObject? value)
    {
        if (value is null) return new JassValidationContext();
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        if (value["variables"] is JsonObject variableObject)
        {
            foreach (var item in variableObject)
            {
                var type = item.Value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : null;
                if (!string.IsNullOrWhiteSpace(type)) variables[item.Key] = type;
            }
        }

        var types = ReadStringMap(value["types"] as JsonObject);
        var symbols = new List<JassApiSymbol>();
        if (value["symbols"] is JsonArray symbolArray)
        {
            foreach (var node in symbolArray.OfType<JsonObject>()) symbols.Add(ReadSymbol(node));
        }

        return new JassValidationContext
        {
            Source = value["source"]?.GetValue<string>() ?? value["context_source"]?.GetValue<string>(),
            ExistingSource = value["existing_source"]?.GetValue<string>(),
            Symbols = symbols,
            Variables = variables,
            Types = types
        };
    }

    private static Dictionary<string, string> ReadStringMap(JsonObject? value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in value ?? new JsonObject())
        {
            if (item.Value is JsonValue scalar && scalar.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                result[item.Key] = text;
            }
        }
        return result;
    }

    private static JassApiSymbol ReadSymbol(JsonObject value)
    {
        var name = value["name"]?.GetValue<string>() ?? throw new InvalidDataException("A validation context symbol requires a name.");
        var kind = value["kind"]?.GetValue<string>() ?? "function";
        var source = value["source"]?.GetValue<string>() ?? "context";
        var declaration = value["declaration"]?.GetValue<string>() ?? name;
        var parameters = (value["parameters"] as JsonArray)?.OfType<JsonObject>().Select(parameter => new JassApiParameter(
            parameter["name"]?.GetValue<string>() ?? throw new InvalidDataException($"Validation context symbol '{name}' has a parameter without a name."),
            parameter["type"]?.GetValue<string>() ?? throw new InvalidDataException($"Validation context symbol '{name}' has a parameter without a type."),
            parameter["documentation"]?.GetValue<string>())).ToArray() ?? Array.Empty<JassApiParameter>();
        return new JassApiSymbol(
            name,
            kind,
            source,
            declaration,
            parameters,
            value["return_type"]?.GetValue<string>() ?? value["returnType"]?.GetValue<string>(),
            value["documentation"]?.GetValue<string>(),
            extends: value["extends"]?.GetValue<string>());
    }
}

public sealed class JassValidationIssue
{
    public JassValidationIssue(
        JassValidationSeverity severity,
        string code,
        string message,
        int? line = null,
        int? column = null,
        string? function = null,
        string? parameter = null,
        IReadOnlyList<string>? suggestions = null)
    {
        Severity = severity;
        Code = code;
        Message = message;
        Line = line;
        Column = column;
        Function = function;
        Parameter = parameter;
        Suggestions = suggestions ?? Array.Empty<string>();
    }

    public JassValidationSeverity Severity { get; }
    public string Code { get; }
    public string Message { get; }
    public int? Line { get; }
    public int? Column { get; }
    public string? Function { get; }
    public string? Parameter { get; }
    public IReadOnlyList<string> Suggestions { get; }

    public JsonObject ToJson()
    {
        var result = new JsonObject
        {
            ["severity"] = Severity == JassValidationSeverity.Error ? "error" : "warning",
            ["code"] = Code,
            ["message"] = Message
        };
        if (Line is not null) result["line"] = Line.Value;
        if (Column is not null) result["column"] = Column.Value;
        if (Function is not null) result["function"] = Function;
        if (Parameter is not null) result["parameter"] = Parameter;
        if (Suggestions.Count > 0) result["suggestions"] = new JsonArray(Suggestions.Select(value => (JsonNode?)value).ToArray());
        return result;
    }
}

public sealed class JassValidationResult
{
    private readonly List<JassValidationIssue> issues = new();

    public JassValidationResult(string operation, string? source = null)
    {
        Operation = operation;
        Source = source;
    }

    public string Operation { get; }
    public string? Source { get; }
    public IReadOnlyList<JassValidationIssue> Issues => issues;
    public IReadOnlyList<JassValidationIssue> Errors => issues.Where(issue => issue.Severity == JassValidationSeverity.Error).ToArray();
    public IReadOnlyList<JassValidationIssue> Warnings => issues.Where(issue => issue.Severity == JassValidationSeverity.Warning).ToArray();
    public bool IsValid => issues.All(issue => issue.Severity != JassValidationSeverity.Error);
    public bool Valid => IsValid;
    public JassApiSymbol? ResolvedSymbol { get; internal set; }

    public void Add(JassValidationIssue issue)
    {
        if (issue.Code == "API_ANNOTATION" && issues.Any(existing => existing.Code == issue.Code && existing.Message == issue.Message)) return;
        issues.Add(issue);
    }
    public void Error(string code, string message, int? line = null, int? column = null, string? function = null, string? parameter = null, IReadOnlyList<string>? suggestions = null)
        => Add(new JassValidationIssue(JassValidationSeverity.Error, code, message, line, column, function, parameter, suggestions));
    public void Warning(string code, string message, int? line = null, int? column = null, string? function = null, string? parameter = null, IReadOnlyList<string>? suggestions = null)
        => Add(new JassValidationIssue(JassValidationSeverity.Warning, code, message, line, column, function, parameter, suggestions));

    public JsonObject ToJson()
    {
        var errors = Errors;
        var warnings = Warnings;
        var result = new JsonObject
        {
            ["valid"] = IsValid,
            ["operation"] = Operation,
            ["errors"] = errors.Count,
            ["warnings"] = warnings.Count,
            ["issues"] = new JsonArray(issues.Select(issue => (JsonNode?)issue.ToJson()).ToArray())
        };
        if (ResolvedSymbol is not null)
        {
            result["function"] = ResolvedSymbol.Name;
            result["return_type"] = ResolvedSymbol.ReturnType;
            result["declaration"] = ResolvedSymbol.Declaration;
        }
        return result;
    }

    public override string ToString() => JassValidationFailure.Format(this);
}

public static class JassValidationFailure
{
    public static string Format(JassValidationResult result)
    {
        if (result.IsValid) return "JASS validation passed.";
        var builder = new System.Text.StringBuilder("JASS validation failed.");
        foreach (var issue in result.Issues.Where(issue => issue.Severity == JassValidationSeverity.Error))
        {
            builder.AppendLine();
            builder.Append("ERROR");
            if (issue.Line is not null) builder.Append(" line ").Append(issue.Line.Value);
            builder.Append(": ").Append(issue.Message);
            if (issue.Suggestions.Count > 0)
            {
                builder.AppendLine();
                builder.Append("Possible intended functions: ").Append(string.Join(", ", issue.Suggestions));
            }
        }
        return builder.ToString();
    }

    public static JsonObject ToJson(JassValidationResult result) => result.ToJson();
}
