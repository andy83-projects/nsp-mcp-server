using Nsp.Audit;
using Nsp.Automation.Core;
using Nsp.Automation.Fallback;
using Nsp.Automation.Uia;
using Nsp.Automation.WindowManager;
using Nsp.TuningTables;
namespace Nsp.Mcp.Server;
internal static class AutomationServices
{
 public static void Add(WebApplicationBuilder builder)
 {
  var processNameHint = builder.Configuration["Nsp:ProcessNameHint"] ?? "NSP";
  var expectedOpenFileName = builder.Configuration["Nsp:ExpectedOpenFileName"];
  var auditLogDirectory = builder.Configuration["Nsp:AuditLogDirectory"] ?? "logs";
  var clampRangesPath = builder.Configuration["Nsp:ClampRangesPath"] ?? "clamp-ranges.json";
// --- Window management ------------------------------------------------------------------
builder.Services.AddSingleton(new NspWindowLocator(processNameHint));
builder.Services.AddSingleton<WindowHealthMonitor>();

// --- UIA primitives + app driver ---------------------------------------------------------
builder.Services.AddSingleton<MenuNavigator>();
builder.Services.AddSingleton<FunctionManagerDriver>();
builder.Services.AddSingleton<FormFieldDriver>();
builder.Services.AddSingleton(new NspAppDriverOptions { ExpectedOpenFileName = expectedOpenFileName });
builder.Services.AddSingleton<IAppDriver, NspUiaAppDriver>();
builder.Services.AddSingleton<UiaCapabilityProbe>();

// --- Tuning tables ------------------------------------------------------------------------
// Register only tables whose identity, axes, values and write path have been verified live.
builder.Services.AddSingleton(new TableCatalog(KnownTables.All));
builder.Services.AddSingleton<NspTableNavigator>();
builder.Services.AddSingleton<CapabilityRegistry>();

builder.Services.AddSingleton(sp => ClampRangeConfig.LoadAsync(clampRangesPath).GetAwaiter().GetResult());
builder.Services.AddSingleton<ClampRangeResolver>(sp =>
    new ClampRangeResolver(sp.GetRequiredService<ClampRangeConfig>(), sp.GetRequiredService<NspTableNavigator>()));

// --- Cell drivers ---------------------------------------------------------------------------
// The dwell table uses UIA grid addressing plus NSP's native inline keyboard editor.
// The fallback resolver remains unconfigured; no table routes there without calibration.
builder.Services.AddSingleton<TuningGridUiaDriver>();
builder.Services.AddSingleton<ICellDriver>(sp => sp.GetRequiredService<TuningGridUiaDriver>());
builder.Services.AddSingleton<ICellDriver>(_ => new TuningGridFallbackDriver(
    resolveWindowHandle: _ => throw new NotImplementedException(
        "No real table's window handle is wired up yet — do this once Phase 3 defines a real fallback table."),
    resolveAnchorLocator: _ => throw new NotImplementedException(
        "No real table's pixel anchors are calibrated yet — do this once Phase 3 defines a real fallback table."),
    ocrTextReader: new NotImplementedOcrTextReader()));

builder.Services.AddSingleton<NspAppController>();

// --- Audit log ------------------------------------------------------------------------------
builder.Services.AddSingleton(new AuditLogger(auditLogDirectory));


 }
}
