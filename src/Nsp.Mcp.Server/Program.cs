using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Nsp.Audit;
using Nsp.Automation.Core;
using Nsp.Automation.Fallback;
using Nsp.Automation.Uia;
using Nsp.Mcp.Tools;
using Nsp.Mcp.Server;
using Nsp.TuningTables;

var builder = WebApplication.CreateBuilder(args);
var operations = new AutomationOperations();
builder.Services.AddSingleton(operations);
var isWorker = builder.Configuration.GetValue<bool>("Nsp:Worker");
if (isWorker)
{
    PhysicalDpiScope.InitializeProcess();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    AutomationServices.Add(builder);
    var mcp = builder.Services.AddMcpServer().WithHttpTransport()
        .WithRequestFilters(filters => {
            filters.AddCallToolFilter(OperationFilter.Create(operations));
            // Development-only integration fault injection; no public fault tool.
            if (builder.Environment.IsEnvironment("RecoveryTest"))
                filters.AddCallToolFilter(next => async (context, token) => {
                    if (context.Params?.Name == builder.Configuration["Nsp:RecoveryTestHangTool"])
                        Thread.Sleep(Timeout.Infinite);
                    if (context.Params?.Name == "nsp_probe_recovery_state" && builder.Configuration.GetValue<bool>("Nsp:RecoveryTestFrozenProbe"))
                        return new CallToolResult { Content = [new TextContentBlock { Text = "{\"success\":false,\"dialogs\":[],\"customerActionRequired\":true,\"message\":\"Simulated unresponsive NSP probe; no NSP process was suspended.\"}" }] };
                    return await next(context, token);
                });
        })
        .WithTools<AppCommandTools>().WithTools<TableTools>().WithTools<UiaProbeTools>()
        .WithTools<SettingsTools>().WithTools<SettingsDialogTools>().WithTools<AxisTools>().WithTools<OperationTools>().WithTools<WorkerHealthTools>();
    var app = builder.Build();
    app.Use(async (context, next) => {
        if (context.Request.Headers["X-Nsp-Worker-Generation"] != builder.Configuration["Nsp:WorkerToken"])
        { context.Response.StatusCode = 409; return; } // stale client cannot cross a worker generation
        await next(context);
    });
    app.MapMcp();
    await app.StartAsync();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    var readyFile = builder.Configuration["Nsp:WorkerReadyFile"]!;
    await File.WriteAllTextAsync(readyFile + ".tmp", address);
    File.Move(readyFile + ".tmp", readyFile, true);
    await app.WaitForShutdownAsync();
}
else
{
    var bind = builder.Configuration["Nsp:Server:BindAddress"] ?? "0.0.0.0";
    var port = builder.Configuration.GetValue("Nsp:Server:Port", 6274);
    builder.WebHost.UseUrls($"http://{bind}:{port}");
    var supervisor = new WorkerSupervisor(builder.Configuration, operations);
    builder.Services.AddSingleton(supervisor);
    builder.Services.AddSingleton(new AuditLogger(builder.Configuration["Nsp:AuditLogDirectory"] ?? "logs"));
    builder.Services.AddSingleton(new TableCatalog(KnownTables.All));
    var proxies = new List<McpServerTool>();
    foreach (var type in new[] { typeof(AppCommandTools), typeof(TableTools), typeof(UiaProbeTools), typeof(SettingsTools), typeof(SettingsDialogTools), typeof(AxisTools), typeof(WorkerHealthTools) })
        foreach (var method in type.GetMethods().Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            var metadata = McpServerTool.Create(method, _ => throw new InvalidOperationException("UIA tools execute only in the isolated worker."));
            if (metadata.ProtocolTool.Name is not ("nsp_list_tables" or "nsp_get_audit_log")) proxies.Add(new WorkerProxyTool(metadata, supervisor));
        }
    builder.Services.AddMcpServer().WithHttpTransport()
        .WithRequestFilters(filters => filters.AddCallToolFilter(OperationFilter.Create(operations)))
        .WithTools(proxies).WithTools<SupervisorTools>();
    try { await supervisor.StartAsync(); }
    catch (Exception ex) { supervisor.RecordStartupFailure(ex.Message); Console.Error.WriteLine("Worker startup failed; recovery tools remain available: " + ex.Message); }
    var app = builder.Build(); app.MapMcp();
    app.Lifetime.ApplicationStopping.Register(() => supervisor.DisposeAsync().AsTask().GetAwaiter().GetResult());
    await app.RunAsync();
}
