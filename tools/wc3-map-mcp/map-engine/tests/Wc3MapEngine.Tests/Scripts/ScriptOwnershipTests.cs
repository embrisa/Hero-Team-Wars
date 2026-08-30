using Wc3MapEngine.Core.Scripts;
using Wc3MapEngine.Core;
using Xunit;

namespace Wc3MapEngine.Tests.Scripts;

public sealed class ScriptOwnershipTests
{
    [Fact]
    public void DuplicateTriggerStringIdsAreRejected()
    {
        var exception = Assert.Throws<InvalidDataException>(() => ScriptOwnership.ParseTriggerStrings(System.Text.Encoding.UTF8.GetBytes("STRING 1 {A}\nSTRING 1 {B}")));
        Assert.Contains("Duplicate", exception.Message);
    }

    [Fact]
    public void JassMainEntryPointIsDetectedForMcpOwnedSource()
    {
        Assert.True(ScriptOwnership.HasEntryPoint("war3map.j", System.Text.Encoding.UTF8.GetBytes("function main takes nothing returns nothing\nendfunction")));
        Assert.False(ScriptOwnership.HasEntryPoint("war3map.j", System.Text.Encoding.UTF8.GetBytes("function init takes nothing returns nothing\nendfunction")));
        Assert.Equal("mcp_owned_jass", ScriptOwnership.Strategy);
    }

    [Fact]
    public void ValidJassSourcePassesTheStaticParser()
    {
        ScriptOwnership.ValidateMcpOwnedJass("war3map.j", "function main takes nothing returns nothing\n    call BJDebugMsg(\"ok\")\nendfunction\n");
    }

    [Fact]
    public void InvalidJassSourceIsRejectedBeforeBuild()
    {
        var exception = Assert.Throws<InvalidDataException>(() => ScriptOwnership.ValidateMcpOwnedJass("war3map.j", "function main takes nothing returns nothing\n    call BJDebugMsg(\"missing endfunction\")\n"));
        Assert.Contains("parse", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentHeroTeamWarsMapScriptPassesTheStaticParser()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            var candidate = Path.Combine(current.FullName, "map", "HeroTeamWars_M0_2Arena.w3m");
            if (!File.Exists(candidate))
            {
                current = current.Parent;
                continue;
            }

            var member = MapArchive.Read(candidate).Find("war3map.j");
            Assert.NotNull(member);
            ScriptOwnership.ValidateMcpOwnedJass("war3map.j", System.Text.Encoding.UTF8.GetString(member!.Bytes));
            return;
        }

        throw new FileNotFoundException("The local Hero Team Wars source fixture was not found.");
    }
}
