namespace CodexTimezoneGuard.Core;

public static class WindowPlacement
{
    public static (int Width, int Height) Fit(int desiredWidth, int desiredHeight, int availableWidth, int availableHeight) =>
        (Math.Clamp(desiredWidth, 1, Math.Max(1, availableWidth)), Math.Clamp(desiredHeight, 1, Math.Max(1, availableHeight)));
}
