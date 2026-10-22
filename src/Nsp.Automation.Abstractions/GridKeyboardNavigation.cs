namespace Nsp.Automation.Core;

/// <summary>Bounded native arrow navigation from an independently hit-tested grid cell.</summary>
public static class GridKeyboardNavigation
{
    public static string Keys(int anchorRow, int anchorColumn, int row, int column, int rows, int columns)
    {
        if (rows < 1 || columns < 1 || (long)rows * columns > 10000 ||
            anchorRow < 0 || anchorRow >= rows || anchorColumn < 0 || anchorColumn >= columns ||
            row < 0 || row >= rows || column < 0 || column >= columns)
            throw new ArgumentException("Grid navigation addresses must be within the bounded live grid.");
        var vertical = row - anchorRow;
        var horizontal = column - anchorColumn;
        if (Math.Abs(vertical) + Math.Abs(horizontal) > 512)
            throw new InvalidOperationException("Grid selection exceeds the 512-arrow navigation bound; no value input sent.");
        static string Move(string negative, string positive, int delta) => delta == 0 ? "" :
            "{" + (delta < 0 ? negative : positive) + " " + Math.Abs(delta) + "}";
        return Move("UP", "DOWN", vertical) + Move("LEFT", "RIGHT", horizontal);
    }
}
