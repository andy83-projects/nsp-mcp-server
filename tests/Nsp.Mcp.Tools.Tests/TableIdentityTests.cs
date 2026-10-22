using Nsp.TuningTables;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public sealed class TableIdentityTests
{
    [Theory]
    [InlineData("Fuel Tuning - Base\r\rMinimum: 0.0, Maximum: 200.0", true)]
    [InlineData("Fuel Tuning - Base - Stage 2\r\rMinimum: 0.0, Maximum: 200.0", false)]
    [InlineData("Ignition Tuning - Base\r\rMinimum: -60.0, Maximum: 60.0", false)]
    [InlineData("Other table\rFuel Tuning - Base", false)]
    public void ChecksTheWholeIdentityLine(string help, bool expected) =>
        Assert.Equal(expected, KnownTables.BaseFuel.MatchesHelpDocument(help));
}
