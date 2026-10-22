namespace Nsp.Automation.Core;

public sealed record FunctionRowEntry(string Name, bool IsText, bool IsCheckBox);

/// <summary>NSP function labels follow their own unnamed checkbox, even in a shared parent.</summary>
public static class FunctionRowSelector
{
    public static int CheckboxIndex(IReadOnlyList<FunctionRowEntry> children, string functionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        var labels = Enumerable.Range(0, children.Count)
            .Where(i => children[i].IsText && string.Equals(children[i].Name, functionName, StringComparison.Ordinal)).ToArray();
        if (labels.Length != 1)
            throw new InvalidOperationException($"Expected one exact function label '{functionName}', found {labels.Length}; no checkbox selected.");
        var index = labels[0] - 1;
        if (index < 0 || !children[index].IsCheckBox)
            throw new InvalidOperationException($"Exact function label '{functionName}' has no immediately preceding checkbox; no checkbox selected.");
        return index;
    }
}
