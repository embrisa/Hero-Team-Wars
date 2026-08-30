using System.Text;
using Wc3MapEngine.Core;
using Xunit;

namespace Wc3MapEngine.Tests;

public sealed class HashingTests
{
    [Fact]
    public void Sha256MatchesKnownVector()
    {
        var value = Hashing.Sha256(Encoding.UTF8.GetBytes("abc"));
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", value);
    }
}
