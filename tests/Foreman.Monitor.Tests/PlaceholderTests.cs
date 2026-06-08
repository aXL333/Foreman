namespace Foreman.Monitor.Tests;

public sealed class PlaceholderTests
{
    [Fact]
    public void ProcessTreeTracker_StartEmpty()
    {
        var tracker = new ProcessTreeTracker();
        Assert.Empty(tracker.GetAll());
    }
}
