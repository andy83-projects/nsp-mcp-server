using System.Drawing;
using System.Globalization;
using Nsp.Automation.Core;
using Nsp.TuningTables;

namespace Nsp.Automation.Fallback;

/// <summary>
/// Coordinate+keyboard cell read/write for tuning tables the Phase 0 probe found to be
/// UIA-inaddressable — the expected common case for custom-rendered 2D/3D map widgets,
/// which are frequently drawn as a single canvas for performance rather than as individual
/// accessible grid cells.
///
/// Every write follows the full lesson set from the build logs: double-click the computed
/// pixel to focus -> verify the cell visibly highlighted (never type blind) -> Ctrl+A /
/// Backspace / type / Tab (never clipboard) -> re-screenshot -> OCR-read the cell back ->
/// compare against the intended value before reporting success.
/// </summary>
public sealed class TuningGridFallbackDriver(
    Func<TableDefinition, IntPtr> resolveWindowHandle,
    Func<TableDefinition, PixelAnchorLocator> resolveAnchorLocator,
    IOcrTextReader ocrTextReader) : ICellDriver
{
    private const int CellRegionHalfWidth = 30;
    private const int CellRegionHalfHeight = 10;

    public bool CanHandle(TableDefinition table) => table.DriverKind == TableDriverKind.Fallback;

    public Task<AutomationResult> ReadCellAsync(TableDefinition table, int row, int col, CancellationToken cancellationToken = default)
    {
        var windowHandle = resolveWindowHandle(table);
        var cellPixel = resolveAnchorLocator(table).LocateCell(row, col);

        using var capture = ScreenCapture.CaptureWindow(windowHandle);
        var region = ClampToBitmap(capture, cellPixel);
        using var cropped = capture.Clone(region, capture.PixelFormat);

        var text = ocrTextReader.ReadText(cropped);
        var markers = ErrorMarkerDetector.HasRedMarker(capture)
            ? new[] { "red-marker-detected" }
            : Array.Empty<string>();

        return Task.FromResult(AutomationResult.Succeeded("fallback", text, markers));
    }

    public async Task<AutomationResult> WriteCellAsync(TableDefinition table, int row, int col, double appliedValue, CancellationToken cancellationToken = default)
    {
        var windowHandle = resolveWindowHandle(table);
        var cellPixel = resolveAnchorLocator(table).LocateCell(row, col);

        // Double-click to focus (lesson: single coordinate clicks can land one field behind).
        NativeClick.DoubleClick(windowHandle, cellPixel);

        using (var preTypeCapture = ScreenCapture.CaptureWindow(windowHandle))
        {
            var region = ClampToBitmap(preTypeCapture, cellPixel);
            if (!FocusVerifier.LooksSelected(preTypeCapture, region))
            {
                return AutomationResult.Failed(
                    "fallback",
                    $"Cell ({row},{col}) in '{table.TableId}' did not visibly highlight after double-click — refusing to type blind.");
            }
        }

        InputSynthesizer.ReplaceFieldValueAndCommit(appliedValue.ToString(CultureInfo.InvariantCulture));

        // Verification-first: re-screenshot and re-read rather than trusting the keystrokes alone.
        return await ReadCellAsync(table, row, col, cancellationToken);
    }

    public Task<AutomationResult> ReadTableSnapshotAsync(TableDefinition table, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(
            "Bulk snapshot reading needs real axis dimensions from GetAxesAsync first — wire this up in Phase 3.");

    public Task<(double[] RowBreakpoints, double[] ColumnBreakpoints)> GetAxesAsync(TableDefinition table, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(
            "Axis breakpoints must be read from NSP's own axis/header cells (OCR) or a one-time calibration step — implement in Phase 3.");

    private static Rectangle ClampToBitmap(Bitmap bitmap, Point center)
    {
        var left = Math.Max(0, center.X - CellRegionHalfWidth);
        var top = Math.Max(0, center.Y - CellRegionHalfHeight);
        var right = Math.Min(bitmap.Width, center.X + CellRegionHalfWidth);
        var bottom = Math.Min(bitmap.Height, center.Y + CellRegionHalfHeight);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}
