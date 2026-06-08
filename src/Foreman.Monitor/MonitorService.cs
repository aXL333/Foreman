using Foreman.Core.Behavior;
using Foreman.Core.Events;
using Foreman.Core.Settings;
using Foreman.Monitor.Wmi;

namespace Foreman.Monitor;

/// <summary>
/// Top-level composition root for all monitoring components.
/// Call Start() once at app startup, Dispose() on exit.
/// </summary>
public sealed class MonitorService : IDisposable
{
    private readonly WmiProcessWatcher _watcher;
    private readonly IoPoller _poller;
    private bool _started;

    public ProcessTreeTracker Tree     { get; }
    public BehaviorTracker    Behavior { get; }

    public MonitorService(ForemanSettings settings, EventBus bus)
    {
        Tree = new ProcessTreeTracker();
        var hangDetector = new HangDetector(bus, settings, Tree);
        _watcher  = new WmiProcessWatcher(Tree, bus, settings);
        _poller   = new IoPoller(Tree, hangDetector, settings);
        Behavior  = new BehaviorTracker(
            settings, bus,
            pid => Tree.GetByPid(pid),
            pid => Tree.FindHarnessTypeAncestor(pid));
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _watcher.Start();
        _poller.Start();
    }

    public void Dispose()
    {
        _poller.Dispose();
        _watcher.Dispose();
    }
}
