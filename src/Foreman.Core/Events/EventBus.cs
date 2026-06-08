using Foreman.Core.Models;
using System.Collections.Concurrent;

namespace Foreman.Core.Events;

public interface IEventSink
{
    void OnEvent(ForemanEvent evt);
}

/// <summary>
/// Thread-safe in-process pub/sub. Subscribers receive events on the publisher's thread.
/// UI subscribers must marshal to the UI thread themselves.
/// Also maintains a rolling history of recent events so late subscribers (e.g. LogWindow
/// opened after startup) can hydrate their views.
/// </summary>
public sealed class EventBus
{
    private readonly ConcurrentBag<IEventSink> _sinks = new();
    private readonly ConcurrentBag<Action<ForemanEvent>> _handlers = new();
    private readonly ConcurrentQueue<ForemanEvent> _history = new();
    private const int MaxHistory = 1000;

    public static EventBus Instance { get; } = new();

    private EventBus() { }

    public void Subscribe(IEventSink sink) => _sinks.Add(sink);
    public void Subscribe(Action<ForemanEvent> handler) => _handlers.Add(handler);

    /// <summary>Returns a snapshot of all events since startup, oldest first (capped at 1000).</summary>
    public IReadOnlyList<ForemanEvent> GetHistory() => _history.ToArray();

    public void Publish(ForemanEvent evt)
    {
        // buffer for late subscribers (log window opened after events already fired)
        _history.Enqueue(evt);
        while (_history.Count > MaxHistory)
            _history.TryDequeue(out _);

        foreach (var sink in _sinks)
        {
            try { sink.OnEvent(evt); }
            catch { /* never let a bad sink kill the bus */ }
        }

        foreach (var handler in _handlers)
        {
            try { handler(evt); }
            catch { }
        }
    }
}
