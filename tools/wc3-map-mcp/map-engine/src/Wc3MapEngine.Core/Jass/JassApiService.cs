using System.Text.Json.Nodes;

namespace Wc3MapEngine.Core.Jass;

/// <summary>
/// Small worker-facing facade over the repository and validator. The MCP
/// boundary can pass JsonObject payloads without taking a dependency on the
/// catalogue's storage format.
/// </summary>
public sealed class JassApiService
{
    public JassApiService(JassApiRepository repository)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Validator = new JassValidationService(repository);
    }

    public JassApiRepository Repository { get; }
    public JassValidationService Validator { get; }

    public JassApiSymbol? LookupSymbol(string name) => Repository.Lookup(name);
    public IReadOnlyList<JassApiSearchResult> SearchSymbols(string query, int limit = JassApiRepository.DefaultSearchLimit) => Repository.Search(query, limit);
    public JassValidationResult ValidateCall(string function, IReadOnlyList<JassCallArgument> arguments, JassValidationContext? context = null) => Validator.ValidateCall(function, arguments, context);
    public JassValidationResult ValidateSource(string source, JassValidationContext? context = null) => Validator.ValidateSource(source, context);

    public JsonObject Lookup(JsonObject payload)
        => Repository.LookupJson(payload["name"]?.GetValue<string>() ?? string.Empty);

    public JsonObject Search(JsonObject payload)
    {
        var query = payload["query"]?.GetValue<string>() ?? string.Empty;
        var limit = payload["limit"]?.GetValue<int>() ?? JassApiRepository.DefaultSearchLimit;
        return Repository.SearchJson(query, limit);
    }

    public JsonObject ValidateCall(JsonObject payload) => Validator.ValidateCallJson(payload);
    public JsonObject ValidateSource(JsonObject payload) => Validator.ValidateSourceJson(payload);
}
