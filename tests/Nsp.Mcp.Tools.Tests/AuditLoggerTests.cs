using Nsp.Audit;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public class AuditLoggerTests
{
    [Fact]
    public async Task AppendAndQuery_RoundTripsAnEntry()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "nsp-audit-test-" + Guid.NewGuid());
        var logger = new AuditLogger(tempDir);

        try
        {
            var entry = new AuditLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                ToolName = "nsp_write_cell",
                InputsJson = "{\"tableId\":\"fuel_ve_table\",\"row\":1,\"col\":1}",
                DryRun = false,
                Success = true,
                DriverUsed = "uia",
                ClampProvenance = "local-config",
                ClampMin = 0,
                ClampMax = 100,
                RequestedValueJson = "42.0",
                AppliedValueJson = "42.0",
                AfterValueJson = "42.0",
                WasClamped = false,
            };

            await logger.AppendAsync(entry);

            var results = await logger.QueryAsync(toolNameFilter: "nsp_write_cell");

            Assert.Single(results);
            Assert.Equal("fuel_ve_table", System.Text.Json.JsonDocument.Parse(results[0].InputsJson).RootElement.GetProperty("tableId").GetString());
            Assert.True(results[0].Success);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AppendAndQuery_FiltersByToolName()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "nsp-audit-test-" + Guid.NewGuid());
        var logger = new AuditLogger(tempDir);

        try
        {
            await logger.AppendAsync(MakeEntry("nsp_save_map"));
            await logger.AppendAsync(MakeEntry("nsp_write_cell"));

            var results = await logger.QueryAsync(toolNameFilter: "nsp_save_map");

            Assert.Single(results);
            Assert.Equal("nsp_save_map", results[0].ToolName);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static AuditLogEntry MakeEntry(string toolName) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        ToolName = toolName,
        InputsJson = "{}",
        DryRun = false,
        Success = true,
        DriverUsed = "uia",
    };
}
