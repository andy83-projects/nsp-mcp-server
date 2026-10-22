using System.Drawing;

namespace Nsp.Automation.Fallback;

/// <summary>
/// Maps a tuning table's (row, col) cell to an approximate client-rect pixel location by
/// interpolating from two calibrated anchor cells, rather than hardcoding pixel offsets
/// that would break the moment the window is resized or NSP's layout shifts slightly.
/// Calibration anchors are captured once per table (e.g. a one-time manual step reading two
/// known cells' screen positions) and persisted alongside the table definition.
/// </summary>
public sealed class PixelAnchorLocator(Point originAnchor, double pixelsPerColumn, double pixelsPerRow)
{
    public static PixelAnchorLocator FromTwoAnchors(
        (int Row, int Col, Point Pixel) anchorA,
        (int Row, int Col, Point Pixel) anchorB)
    {
        var columnDelta = anchorB.Col - anchorA.Col;
        var rowDelta = anchorB.Row - anchorA.Row;

        var pixelsPerColumn = columnDelta == 0 ? 0 : (anchorB.Pixel.X - anchorA.Pixel.X) / (double)columnDelta;
        var pixelsPerRow = rowDelta == 0 ? 0 : (anchorB.Pixel.Y - anchorA.Pixel.Y) / (double)rowDelta;

        var origin = new Point(
            anchorA.Pixel.X - (int)Math.Round(anchorA.Col * pixelsPerColumn),
            anchorA.Pixel.Y - (int)Math.Round(anchorA.Row * pixelsPerRow));

        return new PixelAnchorLocator(origin, pixelsPerColumn, pixelsPerRow);
    }

    public Point LocateCell(int row, int col) => new(
        originAnchor.X + (int)Math.Round(col * pixelsPerColumn),
        originAnchor.Y + (int)Math.Round(row * pixelsPerRow));
}
