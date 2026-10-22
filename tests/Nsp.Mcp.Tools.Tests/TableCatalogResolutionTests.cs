using System.Text.Json;
using Nsp.Automation.Core;
using Nsp.Mcp.Tools;
using Nsp.TuningTables;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public class TableCatalogResolutionTests
{
    [Fact] public void EveryRegisteredHelpIdentityAndDisplayNameResolvesToItsCanonicalDefinition()
    {
        var catalog = new TableCatalog(KnownTables.All);
        Assert.Equal(16, catalog.ListTables().Count);
        foreach (var table in KnownTables.All)
        {
            Assert.Same(table, catalog.TryGet(table.TableId));
            Assert.Same(table, catalog.TryGet("  " + table.HelpIdentity!.ToUpperInvariant() + "  "));
            Assert.Same(table, catalog.TryGet(table.DisplayName));
        }
    }
    [Fact] public void PartialAndAmbiguousAliasesAreRefused()
    {
        var catalog = new TableCatalog([KnownTables.BaseFuel, KnownTables.BaseIgnition with { DisplayName = KnownTables.BaseFuel.DisplayName }]);
        Assert.Null(catalog.TryGet("Fuel Tuning"));
        Assert.Null(catalog.TryGet(KnownTables.BaseFuel.DisplayName));
        Assert.NotNull(catalog.TryGet("base_fuel"));
        Assert.Null(catalog.TryGet(" "));
    }
    [Fact] public async Task AllTablePrimitivesRefuseUnknownIdentityBeforeAnyDriverOrClampAccess()
    {
        var catalog = new TableCatalog(KnownTables.All);
        // Deliberately no app, drivers, clamp resolver or audit. A missing-table
        // refusal must happen before touching any of those dependencies.
        var tools = new TableTools(new NspAppController(null!, [], new CapabilityRegistry(catalog), catalog), null!, null!);
        var results = new[] {
            await tools.GetTableAxes("missing", default),
            await tools.ReadCell("missing", 0, 0, default),
            await tools.ReadTableSnapshot("missing", default),
            await tools.WriteCell("missing", 0, 0, 350, true, default),
            await tools.WriteTableRange("missing", [new CellWrite(0, 0, 350)], true, default),
        };
        foreach (var result in results)
        {
            var json = JsonSerializer.SerializeToElement(result);
            Assert.False(json.GetProperty("success").GetBoolean());
            Assert.Equal("unknown_table", json.GetProperty("errorCode").GetString());
            Assert.False(json.GetProperty("actionAttempted").GetBoolean());
            Assert.Equal(16, json.GetProperty("availableTables").GetArrayLength());
        }
    }
}
