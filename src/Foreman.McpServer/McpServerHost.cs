#pragma warning disable MCPEXP002 // RunSessionHandler is experimental but stable enough for our use
// Alias avoids Foreman.McpServer namespace shadowing ModelContextProtocol.Server.McpServer
using McpServerType = global::ModelContextProtocol.Server.McpServer;
using Foreman.Core.Events;
using Foreman.Core.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;

namespace Foreman.McpServer;

/// <summary>
/// Hosts an embedded Kestrel MCP HTTP+SSE server.
/// Call StartAsync() from App startup, DisposeAsync() on shutdown.
/// </summary>
public sealed class McpServerHost : IAsyncDisposable
{
    private readonly ForemanSettings _settings;
    private readonly EventBus _bus;
    private WebApplication? _app;

    public ForemanState State { get; } = new();
    public SseSessionManager Sessions { get; } = new();

    public McpServerHost(ForemanSettings settings, EventBus bus)
    {
        _settings = settings;
        _bus = bus;
        State.McpPort = settings.McpPort;
        State.LlmTriage = settings.LlmTriage;
        State.GetMcpSessionCount = () => Sessions.Count;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        // inject shared state into the static tool type
        ForemanMcpTools.SetState(State);

        // subscribe state to the event bus
        _bus.Subscribe(State);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(opts =>
        {
            opts.ListenLocalhost(_settings.McpPort);
        });

        builder.Logging.SetMinimumLevel(LogLevel.Warning); // suppress Kestrel noise from tray

        builder.Services
            .AddSingleton(Sessions)
            .AddSingleton<AlertDispatcher>()
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                // Register/unregister each SSE session so AlertDispatcher can push
                // notifications/message proactively without the client polling.
                options.RunSessionHandler = async (Microsoft.AspNetCore.Http.HttpContext httpCtx, McpServerType server, CancellationToken sessionCt) =>
                {
                    var id = Sessions.Register(server);
                    try
                    {
                        await server.RunAsync(sessionCt).ConfigureAwait(false);
                    }
                    finally
                    {
                        Sessions.Unregister(id);
                    }
                };
            })
            .WithToolsFromAssembly(typeof(ForemanMcpTools).Assembly);

        _app = builder.Build();

        // Register the alert dispatcher as an event sink
        var dispatcher = _app.Services.GetRequiredService<AlertDispatcher>();
        _bus.Subscribe(dispatcher);

        _app.MapMcp("/mcp");
        _app.MapGet("/health", () => new { status = "ok", port = _settings.McpPort, sessions = Sessions.Count });

        await _app.StartAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync().ConfigureAwait(false);
    }
}
