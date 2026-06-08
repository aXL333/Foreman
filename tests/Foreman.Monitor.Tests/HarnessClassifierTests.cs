using Foreman.Core.Models;

namespace Foreman.Monitor.Tests;

public sealed class HarnessClassifierTests
{
    [Theory]
    [InlineData("T3 Code.exe", "", "t3-code")]
    [InlineData("t3code.exe", "", "t3-code")]
    [InlineData("node.exe", "node W:\\src\\t3code\\apps\\desktop\\main.js", "t3-code")]
    [InlineData("opencode.exe", "", "opencode")]
    [InlineData("node.exe", "node C:\\Users\\user\\AppData\\Roaming\\npm\\node_modules\\opencode-ai\\bin\\opencode", "opencode")]
    public void Classify_DetectsNewBuiltInHarnesses(string processName, string commandLine, string expectedHarnessId)
    {
        var record = new ProcessRecord
        {
            Pid = 940_001,
            Name = processName,
            CommandLine = commandLine,
            StartTime = DateTimeOffset.UtcNow,
        };

        HarnessClassifier.Classify(record);

        Assert.True(record.IsHarness);
        Assert.Equal(expectedHarnessId, record.HarnessType);
    }

    [Fact]
    public void Classify_StillWritesHarnessType_WhenHarnessIsDisabled()
    {
        var record = new ProcessRecord
        {
            Pid = 940_101,
            Name = "opencode.exe",
            StartTime = DateTimeOffset.UtcNow,
        };

        HarnessClassifier.Classify(record, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "opencode" });

        Assert.False(record.IsHarness);
        Assert.Equal("opencode", record.HarnessType);
    }

    [Fact]
    public void Classify_DoesNotTreatPlainT3CodeRepoNodeProcessAsHarness()
    {
        var record = new ProcessRecord
        {
            Pid = 940_201,
            Name = "node.exe",
            CommandLine = "node W:\\src\\t3code\\scripts\\build.js",
            StartTime = DateTimeOffset.UtcNow,
        };

        HarnessClassifier.Classify(record);

        Assert.False(record.IsHarness);
        Assert.Null(record.HarnessType);
    }
}
