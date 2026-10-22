using System.Drawing;

namespace Nsp.Automation.Fallback;

/// <summary>
/// Confirms a field/row is actually in the visually-selected state before typing or
/// committing — directly encodes two build-log lessons: "double-click field, verify
/// highlighted value before typing" (single coordinate clicks can land one field behind),
/// and connection-selector rows that "highlight cyan on selection".
/// </summary>
public static class FocusVerifier
{
    /// <summary>
    /// Samples the average color of a client-rect region and reports how "cyan-selected"
    /// it looks. This is a coarse heuristic pending real calibration against NSP's actual
    /// selection color — refine the threshold once Phase 0/1 testing shows real values.
    /// </summary>
    public static bool LooksSelected(Bitmap windowCapture, Rectangle regionInClientCoords, double cyanThreshold = 0.35)
    {
        long redTotal = 0, greenTotal = 0, blueTotal = 0;
        var sampledPixelCount = 0;

        for (var y = regionInClientCoords.Top; y < regionInClientCoords.Bottom; y++)
        {
            for (var x = regionInClientCoords.Left; x < regionInClientCoords.Right; x++)
            {
                if (x < 0 || y < 0 || x >= windowCapture.Width || y >= windowCapture.Height)
                {
                    continue;
                }

                var pixel = windowCapture.GetPixel(x, y);
                redTotal += pixel.R;
                greenTotal += pixel.G;
                blueTotal += pixel.B;
                sampledPixelCount++;
            }
        }

        if (sampledPixelCount == 0)
        {
            return false;
        }

        var averageRed = redTotal / (double)sampledPixelCount;
        var averageGreen = greenTotal / (double)sampledPixelCount;
        var averageBlue = blueTotal / (double)sampledPixelCount;

        // Cyan: low red, high green, high blue.
        var cyanScore = (averageGreen + averageBlue) / 2.0 - averageRed;
        return cyanScore / 255.0 > cyanThreshold;
    }
}
