using System.Text.RegularExpressions;

namespace Nsp.Automation.Core;

public sealed record NativeIoAssignment(string Port, string? Wire);

/// <summary>Parse captions only inside an independently verified, exact connection row.</summary>
public static class NativeIoCaptions
{
    public static NativeIoAssignment? Parse(IEnumerable<string> captions)
    {
        var labels = captions.ToArray();
        var resources = labels.Where(t => Regex.IsMatch(t,
            @"^(?:(?:25A-)?HCO[1-4]|(?:DPO|INJ|IGN|HBO|AVI|SPI)[1-9][0-9]?|.+ \(GenCond[1-9][0-9]* Out\))$", RegexOptions.IgnoreCase)).Distinct().ToArray();
        var wires = labels.Where(t => Regex.IsMatch(t, @",\s*[ACE][1-9][0-9]?$", RegexOptions.IgnoreCase)).Distinct().ToArray();
        if (resources.Length > 1 || wires.Length > 1) throw new InvalidOperationException("Connection row contains ambiguous native resource/pin captions.");
        if (resources.Length == 1) return new(resources[0], wires.SingleOrDefault());
        if (wires.Length == 0 && labels.Contains("Not Assigned")) return new("Not Assigned", null);
        return null; // Unknown caption is not proof of an unassigned port; try exact native IDs or refuse.
    }
}
