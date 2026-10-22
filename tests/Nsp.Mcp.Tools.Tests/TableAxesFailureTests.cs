using System.Text.Json;
using Nsp.Automation.Core;
using Nsp.Mcp.Tools;
using Nsp.TuningTables;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public class TableAxesFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableAxesReturnStructuredRefusalAndRangeWritesNeverStart(bool range)
    {
        var catalog = new TableCatalog([KnownTables.BoostTargetPressure]);
        var driver = new UnavailableAxesDriver();
        var tools = new TableTools(new NspAppController(null!, [driver], new CapabilityRegistry(catalog), catalog), null!, null!);
        var result = range
            ? await tools.WriteTableRange("boost_target_pressure", [new(0, 0, 10)], true, default)
            : await tools.GetTableAxes("boost_target_pressure", default);
        var json = JsonSerializer.SerializeToElement(result);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("axes_unavailable", json.GetProperty("errorCode").GetString());
        Assert.False(json.GetProperty("valuesWritten").GetBoolean());
        Assert.Contains("Axis setup dialog", json.GetProperty("errorMessage").GetString());
        Assert.Equal(1, driver.Reads);
    }

    private sealed class UnavailableAxesDriver : ICellDriver
    {
        public int Reads;
        public bool CanHandle(TableDefinition table) => true;
        public Task<(double[] RowBreakpoints, double[] ColumnBreakpoints)> GetAxesAsync(TableDefinition table, CancellationToken cancellationToken = default)
        { Reads++; throw new InvalidOperationException("Axis setup dialog did not appear."); }
        public Task<AutomationResult> ReadCellAsync(TableDefinition table, int row, int col, CancellationToken cancellationToken = default) => throw new Exception("Unexpected cell read");
        public Task<AutomationResult> WriteCellAsync(TableDefinition table, int row, int col, double appliedValue, CancellationToken cancellationToken = default) => throw new Exception("Unexpected write");
        public Task<AutomationResult> ReadTableSnapshotAsync(TableDefinition table, CancellationToken cancellationToken = default) => throw new Exception("Unexpected snapshot");
    }
}
