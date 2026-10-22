using System.Drawing;

namespace Nsp.Automation.Fallback;

/// <summary>
/// Coarse red-configuration-marker detection by color heuristic over a captured window
/// bitmap — the build logs describe a "red ECR configuration marker" appearing on invalid
/// states, and a cleared marker after a valid change. Exact marker regions/colors should be
/// refined once Phase 0/1 testing shows where NSP actually draws them; this starts as a
/// whole-window scan, which is deliberately conservative (prefers false positives to misses).
/// </summary>
public static class ErrorMarkerDetector
{
    public static bool HasRedMarker(Bitmap windowCapture, int minMatchingPixels = 20)
    {
        var matchingPixels = 0;

        for (var y = 0; y < windowCapture.Height; y += 2)
        {
            for (var x = 0; x < windowCapture.Width; x += 2)
            {
                var pixel = windowCapture.GetPixel(x, y);
                if (pixel.R > 180 && pixel.G < 90 && pixel.B < 90)
                {
                    matchingPixels++;
                    if (matchingPixels >= minMatchingPixels)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
