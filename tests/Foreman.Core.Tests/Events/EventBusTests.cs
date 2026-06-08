using Foreman.Core.Events;
using Foreman.Core.Models;

namespace Foreman.Core.Tests.Events;

public sealed class EventBusTests
{
    [Fact]
    public void Unsubscribe_HandlerStopsReceivingEvents()
    {
        var count = 0;
        void Handler(ForemanEvent _) => count++;

        EventBus.Instance.Subscribe(Handler);
        EventBus.Instance.Publish(new InfoEvent(DateTimeOffset.UtcNow, "test", "before"));
        EventBus.Instance.Unsubscribe(Handler);
        EventBus.Instance.Publish(new InfoEvent(DateTimeOffset.UtcNow, "test", "after"));

        Assert.Equal(1, count);
    }
}
