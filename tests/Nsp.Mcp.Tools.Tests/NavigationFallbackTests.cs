using Nsp.Automation.Core;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public sealed class NavigationFallbackTests
{
    [Fact] public void PluralQueryIsTriedBeforeSingular()
    {
        Assert.Equal(new[] { "Gear Ratios", "Gear Ratio", "Gear Ra", "Gear" }, NavigationQueries.Build("Gear Ratios", "Transmission - Gear Ratios"));
        Assert.Equal(new[] { "Gear Ratios", "Gear Ratio", "Gear Ra", "Gear" }, NavigationQueries.Build("Gear Ratios", null));
    }
    [Theory]
    [InlineData("Engine Configuration")]
    [InlineData("  Engine   Configuration  ")]
    public void FailedFullPhraseHasPartialAndFirstWordFallbacks(string query)
    {
        var queries = NavigationQueries.Build(query, "Engine Configuration");
        Assert.Equal(query.Trim(), queries[0]);
        Assert.Equal(new[] { "Engine Co", "Engine" }, queries.Skip(1));
    }
    [Fact] public void DiscoveryWithoutDestinationAlsoGetsShorterQueries()
    {
        Assert.Equal(new[] { "engine configuration", "engine co", "engine" }, NavigationQueries.Build("engine configuration", null));
    }
    [Fact] public void ShortFinalWordAndSingleWordDoNotInventDuplicatePrefixes()
    {
        Assert.Equal(new[] { "Engine Co", "Engine" }, NavigationQueries.Build("Engine Co", null));
        Assert.Equal(new[] { "Engine" }, NavigationQueries.Build("Engine", null));
    }
    [Fact] public void ShortOrDoubleSQueriesAreNotTruncated()
    {
        Assert.Equal(new[] { "ABS", "Fuel Pressure", "Boost" }, NavigationQueries.WithSingularFallbacks(["ABS", "Fuel Pressure", "Boost"]));
        Assert.Equal(new[] { "Bypass" }, NavigationQueries.WithSingularFallbacks(["Bypass"]));
    }
    [Fact] public void SingularFallbackIsDeduplicatedAndOriginalQueriesStayFirst()
    {
        Assert.Equal(new[] { "Gear Ratios", "Gear Ratio" }, NavigationQueries.WithSingularFallbacks(["Gear Ratios", "Gear Ratio", "gear ratios"]));
    }
}
