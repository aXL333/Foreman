// ForemanMcpTools._state and EventBus.Instance are process-global singletons. Tests that set
// state or capture published events must not run concurrently with each other, so disable
// xUnit's cross-class parallelization for this assembly.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
