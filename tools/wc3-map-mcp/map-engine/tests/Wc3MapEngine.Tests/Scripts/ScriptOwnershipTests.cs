using Wc3MapEngine.Core.Scripts;
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
    public void JassMainEntryPointIsDetectedWithoutEnablingMutation()
    {
        Assert.True(ScriptOwnership.HasEntryPoint("war3map.j", System.Text.Encoding.UTF8.GetBytes("function main takes nothing returns nothing\nendfunction")));
        Assert.False(ScriptOwnership.HasEntryPoint("war3map.j", System.Text.Encoding.UTF8.GetBytes("function init takes nothing returns nothing\nendfunction")));
        Assert.Equal("editor_owned_gui_custom_text", ScriptOwnership.Strategy);
    }
}
