using Nsp.Automation.Core;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public sealed class FunctionRowSelectorTests
{
    private static FunctionRowEntry Checkbox() => new("", false, true);
    private static FunctionRowEntry Label(string name) => new(name, true, false);

    [Theory]
    [InlineData("Oil Temperature Sensor", 2)]
    [InlineData("Differential Oil Temperature Sensor", 0)]
    [InlineData("Transmission Temperature Sensor", 4)]
    public void OverlappingNamesSelectTheirOwnCheckbox(string requested, int expected)
    {
        FunctionRowEntry[] siblings = [Checkbox(), Label("Differential Oil Temperature Sensor"), Checkbox(), Label("Oil Temperature Sensor"),
            Checkbox(), Label("Transmission Temperature Sensor")];
        Assert.Equal(expected, FunctionRowSelector.CheckboxIndex(siblings, requested));
    }

    [Fact]
    public void DoesNotUseSubstringAsAnExactFunction()
    {
        FunctionRowEntry[] siblings = [Checkbox(), Label("Differential Oil Temperature Sensor")];
        Assert.Throws<InvalidOperationException>(() => FunctionRowSelector.CheckboxIndex(siblings, "Oil Temperature Sensor"));
    }

    [Fact]
    public void DuplicateExactLabelsAreRefused()
    {
        FunctionRowEntry[] siblings = [Checkbox(), Label("Oil Temperature Sensor"), Checkbox(), Label("Oil Temperature Sensor")];
        Assert.Throws<InvalidOperationException>(() => FunctionRowSelector.CheckboxIndex(siblings, "Oil Temperature Sensor"));
    }

    [Fact]
    public void DoesNotBorrowAnotherRowsCheckboxWhenPairIsMissing()
    {
        FunctionRowEntry[] siblings = [Checkbox(), Label("Differential Oil Temperature Sensor"), Label("Oil Temperature Sensor")];
        Assert.Throws<InvalidOperationException>(() => FunctionRowSelector.CheckboxIndex(siblings, "Oil Temperature Sensor"));
    }

    [Fact]
    public void DoesNotTreatOtherNamedControlsAsFunctionLabels()
    {
        FunctionRowEntry[] siblings = [new("Oil Temperature Sensor", false, false), Checkbox(), Label("Oil Temperature Sensor")];
        Assert.Equal(1, FunctionRowSelector.CheckboxIndex(siblings, "Oil Temperature Sensor"));
    }
}
