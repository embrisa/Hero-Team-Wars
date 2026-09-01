using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using War3Net.CodeAnalysis.Jass;

namespace Wc3MapEngine.Core.Jass;

/// <summary>
/// Conservative JASS validator. It validates facts that can be established
/// from the canonical API and source declarations, and emits warnings for
/// expressions whose types cannot be inferred with confidence.
/// </summary>
public sealed class JassValidationService
{
    private static readonly Regex FunctionDeclaration = new(
        @"(?im)^\s*(?:(?:constant|private|public|stub|once)\s+)*(?<kind>function|native)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s+takes\s+(?<parameters>.*?)\s+returns\s+(?<returnType>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EndFunction = new(@"(?im)^\s*endfunction\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeDeclaration = new(@"(?im)^\s*type\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s+extends\s+(?<extends>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GlobalBlock = new(@"(?ims)^\s*globals\b(?<body>.*?)^\s*endglobals\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GlobalDeclaration = new(@"(?im)^\s*(?:constant\s+)?(?<type>[A-Za-z_][A-Za-z0-9_]*)(?:\s+array)?\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LocalDeclaration = new(@"(?im)^\s*local\s+(?<type>[A-Za-z_][A-Za-z0-9_]*)(?:\s+array)?\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ParameterDeclaration = new(@"^(?<type>[A-Za-z_][A-Za-z0-9_]*)(?:\s+array)?\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Word = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly JassApiRepository repository;

    public JassValidationService(JassApiRepository repository)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public JassApiRepository Repository => repository;

    public JassValidationResult ValidateCall(string function, IReadOnlyList<string> arguments, JassValidationContext? context = null)
        => ValidateCall(function, arguments.Select(argument => new JassCallArgument(argument)).ToArray(), context);

    public JassValidationResult ValidateCall(string function, IReadOnlyList<JassCallArgument> arguments, JassValidationContext? context = null)
    {
        var result = new JassValidationResult("call");
        context ??= new JassValidationContext();
        var localModel = BuildContextModel(context);
        ValidateCallCore(function ?? string.Empty, arguments ?? Array.Empty<JassCallArgument>(), localModel, result, null, null);
        return result;
    }

    public JassValidationResult ValidateCall(JsonObject payload)
    {
        var function = payload["function"]?.GetValue<string>() ?? payload["name"]?.GetValue<string>() ?? string.Empty;
        var arguments = payload["arguments"] is JsonArray array
            ? array.Select(JassCallArgument.FromJson).ToArray()
            : Array.Empty<JassCallArgument>();
        var context = JassValidationContext.FromJson(payload["context"] as JsonObject);
        var contextSource = payload["context_source"] as JsonValue ?? payload["local_source"] as JsonValue;
        if (context.Source is null && contextSource is not null)
        {
            context = new JassValidationContext
            {
                Source = contextSource.GetValue<string>(),
                ExistingSource = context.ExistingSource,
                Symbols = context.Symbols,
                Variables = context.Variables,
                Types = context.Types
            };
        }
        return ValidateCall(function, arguments, context);
    }

    public JsonObject ValidateCallJson(JsonObject payload) => ValidateCall(payload).ToJson();

    public JassValidationResult ValidateSource(string source, JassValidationContext? context = null)
    {
        source ??= string.Empty;
        context ??= new JassValidationContext();
        var result = new JassValidationResult("source", source);

        // War3Net gives us a syntax/lexing check without requiring us to
        // duplicate all of JASS's punctuation rules in the semantic scanner.
        try
        {
            _ = JassSyntaxFactory.ParseCompilationUnit(source);
        }
        catch (Exception exception)
        {
            result.Error("SYNTAX_ERROR", $"JASS source could not be parsed: {exception.Message}");
        }

        // Context source contains declarations already present in the map. It
        // is parsed separately so offsets from that text cannot accidentally
        // claim calls in the newly supplied source block.
        var contextSource = context.ExistingSource ?? context.Source;
        var contextModel = ParseSource(contextSource ?? string.Empty, result: null);
        var sourceModel = ParseSource(source, result);
        var model = Merge(contextModel, sourceModel, result);
        foreach (var type in repository.Symbols.Where(symbol => symbol.Kind == "type" && symbol.Extends is not null))
        {
            model.Types.TryAdd(type.Name, type.Extends!);
        }

        foreach (var call in ScanCalls(source))
        {
            if (model.IsDeclarationPosition(call.Offset)) continue;
            var owner = model.FunctionContaining(call.Offset);
            ValidateCallCore(call.Name, call.Arguments.Select(argument => new JassCallArgument(argument)).ToArray(), model, result, call, owner);
        }

        return result;
    }

    public JassValidationResult ValidateSource(JsonObject payload)
    {
        var source = payload["source"]?.GetValue<string>() ?? payload["jass"]?.GetValue<string>() ?? string.Empty;
        var context = JassValidationContext.FromJson(payload["context"] as JsonObject);
        var directContext = payload["context_source"]?.GetValue<string>();
        if (directContext is not null) context = new JassValidationContext
        {
            Source = context.Source,
            ExistingSource = string.Join("\n", new[] { context.ExistingSource, directContext }.Where(value => !string.IsNullOrWhiteSpace(value))),
            Symbols = context.Symbols,
            Variables = context.Variables,
            Types = context.Types
        };
        return ValidateSource(source, context);
    }

    public JsonObject ValidateSourceJson(JsonObject payload) => ValidateSource(payload).ToJson();

    public string FormatFailure(JassValidationResult result) => JassValidationFailure.Format(result);
    public static string FormatValidationFailure(JassValidationResult result) => JassValidationFailure.Format(result);

    private void ValidateCallCore(string function, IReadOnlyList<JassCallArgument> arguments, SourceModel model, JassValidationResult result, CallSite? call, SourceFunction? owner)
    {
        var line = call?.Line;
        var column = call?.Column;
        var symbol = model.Functions.TryGetValue(function, out var localFunction)
            ? localFunction.Symbol
            : repository.Lookup(function);

        if (symbol is null)
        {
            if (model.Variables.TryGetValue(function, out var variableType) && string.Equals(variableType, "code", StringComparison.Ordinal))
            {
                result.Warning("DYNAMIC_CALL_UNCERTAIN", $"Call through code variable '{function}' cannot be resolved statically.", line, column, function);
                return;
            }

            var suggestions = repository.Suggestions(function, 6).Select(item => item.Symbol.Name).ToArray();
            result.Error("UNKNOWN_FUNCTION", $"Unknown JASS function '{function}'. No local declaration or canonical Warcraft III JASS API symbol matches this exact case-sensitive name.", line, column, function, suggestions: suggestions);
            return;
        }

        result.ResolvedSymbol = symbol;

        var expected = symbol.Parameters.Count;
        if (expected != arguments.Count)
        {
            result.Error("ARGUMENT_COUNT", $"Function '{function}' expects {expected} argument{(expected == 1 ? string.Empty : "s")}, but {arguments.Count} {(arguments.Count == 1 ? "was" : "were")} supplied.", line, column, function);
        }

        var count = Math.Min(expected, arguments.Count);
        for (var index = 0; index < count; index++)
        {
            var parameter = symbol.Parameters[index];
            var argument = arguments[index];
            var inferred = InferType(argument, model, owner);
            if (inferred.Type is null)
            {
                result.Warning("TYPE_UNCERTAIN", $"Could not confidently infer the type of argument {index + 1} ('{argument.Expression}') for parameter '{parameter.Name}' ({parameter.Type}).", line, column, function, parameter.Name);
                continue;
            }

            if (inferred.Confident && !IsCompatible(inferred.Type, parameter.Type, model.Types))
            {
                result.Error("ARGUMENT_TYPE", $"Argument {index + 1} for '{function}' parameter '{parameter.Name}' expects '{parameter.Type}', but the expression is confidently typed as '{inferred.Type}'.", line, column, function, parameter.Name);
            }
            else if (!inferred.Confident)
            {
                result.Warning("TYPE_UNCERTAIN", $"Argument {index + 1} for '{function}' parameter '{parameter.Name}' may not be type-compatible with '{parameter.Type}'.", line, column, function, parameter.Name);
            }
        }

        foreach (var annotation in symbol.Annotations.Where(IsImportantAnnotation))
        {
            var detail = string.IsNullOrWhiteSpace(annotation.Value) ? annotation.Name : $"{annotation.Name}: {annotation.Value}";
            result.Warning("API_ANNOTATION", $"JASS API annotation for '{function}': {detail}.", line, column, function);
        }
    }

    private SourceModel BuildContextModel(JassValidationContext context)
    {
        var model = ParseSource(context.CombinedSource ?? string.Empty, result: null);
        foreach (var type in repository.Symbols.Where(symbol => symbol.Kind == "type" && symbol.Extends is not null))
        {
            model.Types.TryAdd(type.Name, type.Extends!);
        }
        foreach (var symbol in context.Symbols)
        {
            model.Functions.TryAdd(symbol.Name, new SourceFunction(symbol, -1, -1, new Dictionary<string, string>(StringComparer.Ordinal)));
        }
        foreach (var variable in context.Variables) model.Variables[variable.Key] = variable.Value;
        foreach (var type in context.Types) model.Types[type.Key] = type.Value;
        return model;
    }

    private SourceModel ParseSource(string source, JassValidationResult? result)
    {
        var model = new SourceModel();
        if (string.IsNullOrWhiteSpace(source)) return model;
        var masked = MaskCommentsAndStrings(source);

        foreach (Match type in TypeDeclaration.Matches(masked)) model.Types[type.Groups["name"].Value] = type.Groups["extends"].Value;
        foreach (Match block in GlobalBlock.Matches(masked))
        {
            foreach (Match declaration in GlobalDeclaration.Matches(block.Groups["body"].Value))
            {
                var name = declaration.Groups["name"].Value;
                model.Variables[name] = declaration.Groups["type"].Value;
            }
        }

        foreach (Match declaration in FunctionDeclaration.Matches(masked))
        {
            var name = declaration.Groups["name"].Value;
            var parameters = ParseParameters(declaration.Groups["parameters"].Value);
            var returnType = declaration.Groups["returnType"].Value;
            var kind = declaration.Groups["kind"].Value.Equals("native", StringComparison.OrdinalIgnoreCase) ? "native" : "function";
            var symbol = new JassApiSymbol(name, kind, "local", declaration.Value.Trim(), parameters.Select(item => new JassApiParameter(item.Key, item.Value)).ToArray(), returnType);
            var bodyStart = declaration.Index + declaration.Length;
            var endMatch = EndFunction.Match(masked, bodyStart);
            var bodyEnd = endMatch.Success ? endMatch.Index + endMatch.Length : masked.Length;
            var function = new SourceFunction(symbol, declaration.Index, bodyEnd, parameters);
            if (!model.Functions.TryAdd(name, function))
            {
                result?.Error("DUPLICATE_FUNCTION", $"JASS source declares function '{name}' more than once.", LineOf(source, declaration.Index));
            }

            foreach (Match local in LocalDeclaration.Matches(masked.Substring(bodyStart, Math.Max(0, bodyEnd - bodyStart))))
            {
                function.Variables[local.Groups["name"].Value] = local.Groups["type"].Value;
            }
        }
        return model;
    }

    private static SourceModel Merge(SourceModel context, SourceModel source, JassValidationResult result)
    {
        foreach (var type in context.Types) source.Types.TryAdd(type.Key, type.Value);
        foreach (var variable in context.Variables) source.Variables.TryAdd(variable.Key, variable.Value);
        foreach (var function in context.Functions)
        {
            // Context offsets belong to a different source string. Keep its
            // declaration and variables, but never let those offsets suppress
            // or misattribute calls in the source currently being validated.
            if (!source.Functions.TryAdd(function.Key, new SourceFunction(function.Value.Symbol, -1, -1, function.Value.Variables)))
            {
                result.Error("DUPLICATE_FUNCTION", $"JASS source redeclares context function '{function.Key}'.");
            }
        }
        return source;
    }

    private ExpressionType InferType(JassCallArgument argument, SourceModel model, SourceFunction? owner)
    {
        if (!string.IsNullOrWhiteSpace(argument.Type)) return new ExpressionType(argument.Type, argument.Confident);
        var expression = argument.Expression.Trim();
        if (expression.Length == 0 || string.Equals(expression, "null", StringComparison.OrdinalIgnoreCase)) return new ExpressionType(null, false);
        if (Regex.IsMatch(expression, "^(?:true|false)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return new ExpressionType("boolean", true);
        if (Regex.IsMatch(expression, "^[+-]?[0-9]+$", RegexOptions.CultureInvariant)) return new ExpressionType("integer", true);
        if (Regex.IsMatch(expression, "^[+-]?(?:[0-9]+\\.[0-9]*|\\.[0-9]+)(?:[eE][+-]?[0-9]+)?$", RegexOptions.CultureInvariant)) return new ExpressionType("real", true);
        if (expression.StartsWith("\"", StringComparison.Ordinal) && expression.EndsWith("\"", StringComparison.Ordinal)) return new ExpressionType("string", true);
        if (Regex.IsMatch(expression, "^'[A-Za-z0-9]{4}'$", RegexOptions.CultureInvariant)) return new ExpressionType("integer", true);
        if (Regex.IsMatch(expression, "^(?:not|!?)\\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) && expression.Contains(' ')) return new ExpressionType("boolean", false);
        if (Regex.IsMatch(expression, "^(?:function)\\s+[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return new ExpressionType("code", true);

        var variableMatch = Regex.Match(expression, "^(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\\s*\\[.*\\])?$", RegexOptions.CultureInvariant);
        if (variableMatch.Success)
        {
            var name = variableMatch.Groups["name"].Value;
            if (owner?.Variables.TryGetValue(name, out var localType) == true) return new ExpressionType(localType, true);
            if (model.Variables.TryGetValue(name, out var globalType)) return new ExpressionType(globalType, true);
            var apiGlobal = repository.Lookup(name);
            if (apiGlobal?.Kind == "global" && apiGlobal.ReturnType is { Length: > 0 } apiGlobalType)
            {
                return new ExpressionType(apiGlobalType, true);
            }
        }

        var callMatch = Regex.Match(expression, "^(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*\\(", RegexOptions.CultureInvariant);
        if (callMatch.Success)
        {
            var function = callMatch.Groups["name"].Value;
            var symbol = model.Functions.TryGetValue(function, out var local) ? local.Symbol : repository.Lookup(function);
            if (symbol?.ReturnType is { Length: > 0 } returnType && !string.Equals(returnType, "nothing", StringComparison.OrdinalIgnoreCase)) return new ExpressionType(returnType, true);
        }

        if (expression.Contains("==", StringComparison.Ordinal) || expression.Contains("!=", StringComparison.Ordinal) || expression.Contains("<", StringComparison.Ordinal) || expression.Contains(">", StringComparison.Ordinal)) return new ExpressionType("boolean", false);
        if (expression.Contains('+') || expression.Contains('-') || expression.Contains('*') || expression.Contains('/')) return new ExpressionType(expression.Contains('.', StringComparison.Ordinal) ? "real" : "integer", false);
        return new ExpressionType(null, false);
    }

    private static bool IsCompatible(string actual, string expected, IReadOnlyDictionary<string, string> types)
    {
        actual = actual.Trim();
        expected = expected.Trim();
        if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;
        if (expected.Equals("real", StringComparison.OrdinalIgnoreCase) && actual.Equals("integer", StringComparison.OrdinalIgnoreCase)) return true;
        if (expected.Equals("handle", StringComparison.OrdinalIgnoreCase) && IsHandle(actual)) return true;
        var current = actual;
        var guard = 0;
        while (guard++ < 32 && types.TryGetValue(current, out var parent))
        {
            if (parent.Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;
            current = parent;
        }
        return false;
    }

    private static bool IsHandle(string type)
        => !new[] { "nothing", "boolean", "integer", "real", "string", "code" }.Contains(type, StringComparer.OrdinalIgnoreCase);

    private static bool IsImportantAnnotation(JassApiAnnotation annotation)
    {
        var name = annotation.Name.ToLowerInvariant();
        return name.Contains("async", StringComparison.Ordinal) || name.Contains("desync", StringComparison.Ordinal) || name.Contains("bug", StringComparison.Ordinal) || name.Contains("warn", StringComparison.Ordinal) || name.Contains("deprecated", StringComparison.Ordinal) || name.Contains("unsafe", StringComparison.Ordinal);
    }

    private static Dictionary<string, string> ParseParameters(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (text.Trim().Equals("nothing", StringComparison.OrdinalIgnoreCase)) return result;
        foreach (var segment in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = ParameterDeclaration.Match(segment.Trim());
            if (match.Success) result[match.Groups["name"].Value] = match.Groups["type"].Value;
        }
        return result;
    }

    private static IReadOnlyList<CallSite> ScanCalls(string source)
    {
        var calls = new List<CallSite>();
        var index = 0;
        while (index < source.Length)
        {
            if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                index = source.IndexOf('\n', index + 2);
                if (index < 0) break;
                continue;
            }
            if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                var endComment = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = endComment < 0 ? source.Length : endComment + 2;
                continue;
            }
            if (source[index] == '"')
            {
                index = SkipString(source, index);
                continue;
            }
            if (!IsIdentifierStart(source[index])) { index++; continue; }
            var nameStart = index;
            index++;
            while (index < source.Length && IsIdentifierPart(source[index])) index++;
            var name = source[nameStart..index];
            var afterName = index;
            while (afterName < source.Length && char.IsWhiteSpace(source[afterName])) afterName++;
            if (afterName >= source.Length || source[afterName] != '(' || IsNonCallWord(name)) { index = afterName; continue; }
            if (nameStart > 0 && IsIdentifierPart(source[nameStart - 1])) continue;
            var close = FindMatchingParen(source, afterName);
            if (close < 0)
            {
                calls.Add(new CallSite(name, Array.Empty<string>(), nameStart, LineOf(source, nameStart), ColumnOf(source, nameStart)));
                break;
            }
            var argumentText = source.Substring(afterName + 1, close - afterName - 1);
            calls.Add(new CallSite(name, SplitArguments(argumentText), nameStart, LineOf(source, nameStart), ColumnOf(source, nameStart)));
            // Scan inside the arguments too so nested function calls receive
            // their own existence, arity, and type validation.
            index = afterName + 1;
        }
        return calls;
    }

    private static bool IsNonCallWord(string name)
        => new[] { "if", "elseif", "loop", "exitwhen", "not", "function", "takes", "returns", "native", "call", "local", "type", "globals", "endglobals" }.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static List<string> SplitArguments(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        var quote = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"' && (index == 0 || text[index - 1] != '\\')) quote = !quote;
            if (quote) continue;
            if (character == '(' || character == '[') depth++;
            else if (character == ')' || character == ']') depth--;
            else if (character == ',' && depth == 0)
            {
                result.Add(text[start..index].Trim());
                start = index + 1;
            }
        }
        result.Add(text[start..].Trim());
        return result;
    }

    private static int FindMatchingParen(string source, int open)
    {
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '"') { index = SkipString(source, index) - 1; continue; }
            if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '/') { index = source.IndexOf('\n', index + 2); if (index < 0) return -1; continue; }
            if (source[index] == '(') depth++;
            else if (source[index] == ')' && --depth == 0) return index;
        }
        return -1;
    }

    private static int SkipString(string source, int index)
    {
        index++;
        while (index < source.Length)
        {
            if (source[index] == '\\') { index += 2; continue; }
            if (source[index] == '"') return index + 1;
            index++;
        }
        return source.Length;
    }

    private static string MaskCommentsAndStrings(string source)
    {
        var chars = source.ToCharArray();
        var index = 0;
        while (index < chars.Length)
        {
            if (chars[index] == '/' && index + 1 < chars.Length && chars[index + 1] == '/')
            {
                while (index < chars.Length && chars[index] != '\n') chars[index++] = ' ';
                continue;
            }
            if (chars[index] == '/' && index + 1 < chars.Length && chars[index + 1] == '*')
            {
                chars[index++] = ' '; chars[index++] = ' ';
                while (index + 1 < chars.Length && !(chars[index] == '*' && chars[index + 1] == '/')) { if (chars[index] != '\n') chars[index] = ' '; index++; }
                if (index + 1 < chars.Length) { chars[index++] = ' '; chars[index++] = ' '; }
                continue;
            }
            if (chars[index] == '"')
            {
                chars[index++] = ' ';
                while (index < chars.Length)
                {
                    if (chars[index] == '\\') { chars[index++] = ' '; if (index < chars.Length && chars[index] != '\n') chars[index++] = ' '; continue; }
                    if (chars[index] == '"') { chars[index++] = ' '; break; }
                    if (chars[index] != '\n') chars[index] = ' ';
                    index++;
                }
                continue;
            }
            index++;
        }
        return new string(chars);
    }

    private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';
    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';
    private static int LineOf(string source, int offset) => source.AsSpan(0, Math.Clamp(offset, 0, source.Length)).Count('\n') + 1;
    private static int ColumnOf(string source, int offset)
    {
        offset = Math.Clamp(offset, 0, source.Length);
        var lineStart = source.LastIndexOf('\n', Math.Max(0, offset - 1));
        return offset - lineStart;
    }

    private sealed class SourceModel
    {
        public Dictionary<string, SourceFunction> Functions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Types { get; } = new(StringComparer.Ordinal);

        public SourceFunction? FunctionContaining(int offset)
            => Functions.Values.Where(function => function.Start >= 0 && offset >= function.Start && offset <= function.End).OrderByDescending(function => function.Start).FirstOrDefault();
        public bool IsDeclarationPosition(int offset) => Functions.Values.Any(function => offset >= function.Start && offset < function.Start + function.Symbol.Declaration.Length);
    }

    private sealed class SourceFunction
    {
        public SourceFunction(JassApiSymbol symbol, int start, int end, IReadOnlyDictionary<string, string> variables)
        {
            Symbol = symbol;
            Start = start;
            End = end;
            Variables = new Dictionary<string, string>(variables, StringComparer.Ordinal);
        }
        public JassApiSymbol Symbol { get; }
        public int Start { get; }
        public int End { get; }
        public Dictionary<string, string> Variables { get; }
    }

    private sealed record ExpressionType(string? Type, bool Confident);
    private sealed record CallSite(string Name, IReadOnlyList<string> Arguments, int Offset, int Line, int Column);
}
