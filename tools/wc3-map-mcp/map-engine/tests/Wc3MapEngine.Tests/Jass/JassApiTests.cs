using System.Text.Json.Nodes;
using Wc3MapEngine.Core.Jass;
using Wc3MapEngine.Core.Scripts;
using Xunit;

namespace Wc3MapEngine.Tests.Jass;

public sealed class JassApiTests
{
    private const string AddUnitToStockDeclaration =
        "native AddUnitToStock               takes unit whichUnit, integer unitId, integer currentStock, integer stockMax returns nothing";

    private static readonly JassApiRepository Repository = JassApiRepository.FromDefault();

    private static JassValidationService Validator => new(Repository);

    [Fact]
    public void DefaultRepositoryLoadsThePinnedGeneratedJassdocDataset()
    {
        Assert.Equal("1.0", Repository.SchemaVersion);
        Assert.Equal("https://github.com/lep/jassdoc", Repository.SourceRepository);
        Assert.Equal(JassApiRepository.CanonicalSourceCommit, Repository.SourceCommit);
        Assert.True(Repository.Count > 4_000);
        Assert.Equal("jass-api.json", Path.GetFileName(Repository.DataPath));
    }

    [Fact]
    public void LookupAddUnitToStockRetainsTheExactCanonicalSignature()
    {
        var symbol = Repository.Lookup("AddUnitToStock");

        Assert.NotNull(symbol);
        Assert.Equal("AddUnitToStock", symbol!.Name);
        Assert.Equal("native", symbol.Kind);
        Assert.Equal("common.j", symbol.Source);
        Assert.Equal(AddUnitToStockDeclaration, symbol.Declaration);
        Assert.Equal("nothing", symbol.ReturnType);
        Assert.Collection(
            symbol.Parameters,
            parameter =>
            {
                Assert.Equal("whichUnit", parameter.Name);
                Assert.Equal("unit", parameter.Type);
            },
            parameter =>
            {
                Assert.Equal("unitId", parameter.Name);
                Assert.Equal("integer", parameter.Type);
            },
            parameter =>
            {
                Assert.Equal("currentStock", parameter.Name);
                Assert.Equal("integer", parameter.Type);
            },
            parameter =>
            {
                Assert.Equal("stockMax", parameter.Name);
                Assert.Equal("integer", parameter.Type);
            });
    }

    [Fact]
    public void LookupBlizzardFunctionRetainsDocumentationAndAnnotations()
    {
        var symbol = Repository.Lookup("GetUnitStatePercent");

        Assert.NotNull(symbol);
        Assert.Equal("function", symbol!.Kind);
        Assert.Equal("Blizzard.j", symbol.Source);
        Assert.Equal(
            "function GetUnitStatePercent takes unit whichUnit, unitstate whichState, unitstate whichMaxState returns real",
            symbol.Declaration);
        Assert.Equal("real", symbol.ReturnType);
        Assert.Contains("Returns the current unit state in percent.", symbol.Documentation ?? string.Empty);

        var patch = Assert.Single(symbol.Annotations, annotation => annotation.Name == "patch");
        Assert.Equal("1.07", patch.Value);
    }

    [Fact]
    public void UnknownSetUnitStockLookupReturnsGenericStockSuggestions()
    {
        Assert.Null(Repository.Lookup("SetUnitStock"));

        var response = Repository.LookupJson("SetUnitStock");

        Assert.False(response["found"]!.GetValue<bool>());
        Assert.Equal("Unknown JASS symbol: SetUnitStock", response["error"]!.GetValue<string>());
        var suggestionNames = response["suggestions"]!
            .AsArray()
            .OfType<JsonObject>()
            .Select(item => item["name"]!.GetValue<string>())
            .ToArray();
        Assert.Contains("AddUnitToStock", suggestionNames);
        Assert.Contains("AddUnitToStockBJ", suggestionNames);
        Assert.Contains("RemoveUnitFromStock", suggestionNames);
    }

    [Fact]
    public void UnitStockConceptAndFuzzySearchFindTheCanonicalFunctions()
    {
        var conceptResults = Repository.Search("unit stock", JassApiRepository.MaximumSearchLimit);
        var fuzzyResults = Repository.Search("AddUnitToStok", JassApiRepository.MaximumSearchLimit);

        Assert.Contains(conceptResults, result => result.Symbol.Name == "AddUnitToStock" && result.Symbol.Source == "common.j");
        Assert.Contains(conceptResults, result => result.Symbol.Name == "RemoveUnitFromStock");
        Assert.Contains(fuzzyResults, result => result.Symbol.Name == "AddUnitToStock");
        Assert.Contains("name_fuzzy", fuzzyResults.Single(result => result.Symbol.Name == "AddUnitToStock").MatchedFields);
    }

    [Fact]
    public void ValidateCallAcceptsCorrectAddUnitToStockArityAndTypes()
    {
        var result = Validator.ValidateCall("AddUnitToStock", ValidAddUnitToStockArguments());

        Assert.True(result.IsValid, JassValidationFailure.Format(result));
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Equal("AddUnitToStock", result.ResolvedSymbol?.Name);
    }

    [Fact]
    public void ValidateCallRejectsWrongAddUnitToStockArity()
    {
        var result = Validator.ValidateCall("AddUnitToStock", ValidAddUnitToStockArguments().Take(3).ToArray());

        var issue = Assert.Single(result.Errors);
        Assert.False(result.IsValid);
        Assert.Equal("ARGUMENT_COUNT", issue.Code);
        Assert.Contains("expects 4 arguments", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCallRejectsAConfidentArgumentTypeMismatch()
    {
        var arguments = ValidAddUnitToStockArguments();
        arguments[0] = new JassCallArgument("\"not a unit\"", "string", confident: true);

        var result = Validator.ValidateCall("AddUnitToStock", arguments);

        var issue = Assert.Single(result.Errors);
        Assert.Equal("ARGUMENT_TYPE", issue.Code);
        Assert.Equal("whichUnit", issue.Parameter);
        Assert.Contains("confidently typed as 'string'", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCallRejectsUnknownFunctionAndProvidesGenericSuggestions()
    {
        var result = Validator.ValidateCall("SetUnitStock", Array.Empty<JassCallArgument>());

        var issue = Assert.Single(result.Errors);
        Assert.False(result.IsValid);
        Assert.Equal("UNKNOWN_FUNCTION", issue.Code);
        Assert.Equal("SetUnitStock", issue.Function);
        Assert.Contains("AddUnitToStock", issue.Suggestions);
        Assert.Contains("RemoveUnitFromStock", issue.Suggestions);
    }

    [Fact]
    public void ValidateCallResolvesAFunctionDeclaredInLocalContext()
    {
        const string localSource = """
            function LocalStockPing takes integer amount returns nothing
            endfunction
            """;

        var result = Validator.ValidateCall(
            "LocalStockPing",
            new[] { new JassCallArgument("1", "integer", confident: true) },
            new JassValidationContext { Source = localSource });

        Assert.True(result.IsValid, JassValidationFailure.Format(result));
        Assert.Empty(result.Errors);
        Assert.Equal("LocalStockPing", result.ResolvedSymbol?.Name);
        Assert.Equal("local", result.ResolvedSymbol?.Source);
    }

    [Fact]
    public void ValidateCallUsesContextSourceLocalDeclarationsForArgumentTypes()
    {
        const string contextSource = """
            function LocalContext takes nothing returns nothing
                local unit merchant
            endfunction
            """;
        var payload = new JsonObject
        {
            ["function"] = "AddUnitToStock",
            ["arguments"] = new JsonArray("merchant", "'hfoo'", 0, 1),
            ["context_source"] = contextSource
        };

        var response = Validator.ValidateCallJson(payload);

        Assert.True(response["valid"]!.GetValue<bool>(), response.ToJsonString());
        Assert.Equal(0, response["errors"]!.GetValue<int>());
        Assert.Equal("AddUnitToStock", response["function"]!.GetValue<string>());
    }

    [Fact]
    public void ValidateSourceAcceptsLocalDeclarationAndNestedCanonicalCalls()
    {
        const string source = """
            function LocalStockSource takes nothing returns nothing
                local unit merchant
                call AddUnitToStock(merchant, 'hfoo', 0, 1)
            endfunction

            function main takes nothing returns nothing
                call BJDebugMsg(I2S(GetPlayerId(Player(0))))
            endfunction
            """;

        var result = Validator.ValidateSource(source);

        Assert.True(result.IsValid, JassValidationFailure.Format(result));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSourceReportsNestedCallTypeErrors()
    {
        const string source = """
            function main takes nothing returns nothing
                call BJDebugMsg(I2S(GetPlayerId("not a player")))
            endfunction
            """;

        var result = Validator.ValidateSource(source);

        var issue = Assert.Single(result.Errors);
        Assert.Equal("ARGUMENT_TYPE", issue.Code);
        Assert.Equal("GetPlayerId", issue.Function);
        Assert.Equal("whichPlayer", issue.Parameter);
        Assert.Equal(2, issue.Line);
    }

    [Fact]
    public void ValidateSourceKeepsLocalsScopedToTheirDeclaringFunction()
    {
        const string source = """
            function DefinesMerchant takes nothing returns nothing
                local unit merchant
            endfunction

            function UsesOutOfScopeMerchant takes nothing returns nothing
                call AddUnitToStock(merchant, 'hfoo', 0, 1)
            endfunction
            """;

        var result = Validator.ValidateSource(source);

        Assert.Contains(result.Warnings, issue =>
            issue.Code == "TYPE_UNCERTAIN"
            && issue.Function == "AddUnitToStock"
            && issue.Parameter == "whichUnit");
    }

    [Fact]
    public void ValidateCallJsonLoadsStructuredContextSymbolsAndTypes()
    {
        var payload = new JsonObject
        {
            ["function"] = "LocalMerchantHelper",
            ["arguments"] = new JsonArray("merchant"),
            ["context"] = new JsonObject
            {
                ["variables"] = new JsonObject { ["merchant"] = "merchantunit" },
                ["types"] = new JsonObject { ["merchantunit"] = "unit" },
                ["symbols"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "LocalMerchantHelper",
                        ["kind"] = "function",
                        ["source"] = "context",
                        ["declaration"] = "function LocalMerchantHelper takes unit whichUnit returns nothing",
                        ["parameters"] = new JsonArray
                        {
                            new JsonObject { ["name"] = "whichUnit", ["type"] = "unit" }
                        },
                        ["return_type"] = "nothing"
                    }
                }
            }
        };

        var response = Validator.ValidateCallJson(payload);

        Assert.True(response["valid"]!.GetValue<bool>(), response.ToJsonString());
        Assert.Equal(0, response["errors"]!.GetValue<int>());
        Assert.Equal("LocalMerchantHelper", response["function"]!.GetValue<string>());
    }

    [Fact]
    public void ScriptOwnershipGateAcceptsValidLocalAndCanonicalApiSource()
    {
        const string source = """
            function HTWLocalMessage takes string value returns nothing
                call BJDebugMsg(value)
            endfunction

            function main takes nothing returns nothing
                call HTWLocalMessage("ok")
            endfunction

            function config takes nothing returns nothing
                call SetPlayers(1)
            endfunction
            """;

        ScriptOwnership.ValidateMcpOwnedJass("war3map.j", source);
    }

    [Fact]
    public void ScriptOwnershipGateRejectsSetUnitStockWithCanonicalSuggestions()
    {
        const string source = """
            function main takes nothing returns nothing
                call SetUnitStock(null, 'hfoo', 1)
            endfunction

            function config takes nothing returns nothing
                call SetPlayers(1)
            endfunction
            """;

        var exception = Assert.Throws<InvalidDataException>(() => ScriptOwnership.ValidateMcpOwnedJass("war3map.j", source));

        Assert.Contains("SetUnitStock", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AddUnitToStock", exception.Message, StringComparison.Ordinal);
    }

    private static JassCallArgument[] ValidAddUnitToStockArguments()
        => new[]
        {
            new JassCallArgument("merchant", "unit", confident: true),
            new JassCallArgument("'hfoo'", "integer", confident: true),
            new JassCallArgument("0", "integer", confident: true),
            new JassCallArgument("1", "integer", confident: true)
        };
}
