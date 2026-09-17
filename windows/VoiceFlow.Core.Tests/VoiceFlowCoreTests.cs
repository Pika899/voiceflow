using VoiceFlow.Core;

namespace VoiceFlow.Core.Tests;

public class VoiceFlowCoreTests
{
    [Fact]
    public void Version_IsNotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(VoiceFlowCore.Version));
    }
}
