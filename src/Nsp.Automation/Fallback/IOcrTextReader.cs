using System.Drawing;

namespace Nsp.Automation.Fallback;

/// <summary>
/// Reads the displayed text of a small screen region — needed to verify a tuning-grid
/// cell's value after a fallback write, since custom-drawn grids typically don't expose
/// ValuePattern.
///
/// INTENTIONALLY STUBBED: which OCR approach to use (Windows.Media.Ocr via a WinRT-projected
/// TFM, vs. a bundled OCR NuGet package) should be decided from the Phase 0/1 findings — if
/// the grid turns out to support UIA ValuePattern after all, this is never even needed. This
/// must be implemented for real before Phase 4 (table writes) ships; building it out further
/// now, before the real app's grid has been inspected, would just be guessing.
/// </summary>
public interface IOcrTextReader
{
    string ReadText(Bitmap regionBitmap);
}

public sealed class NotImplementedOcrTextReader : IOcrTextReader
{
    public string ReadText(Bitmap regionBitmap) =>
        throw new NotImplementedException(
            "OCR text reading is not implemented yet. Pick Windows.Media.Ocr (requires a " +
            "net*-windows10.0.xxxxx.0 TFM for WinRT projections) or a bundled OCR NuGet package " +
            "based on what the Phase 0 probe finds about the tuning grid, before Phase 4.");
}
