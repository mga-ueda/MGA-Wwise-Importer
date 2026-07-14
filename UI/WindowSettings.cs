using System.Globalization;

namespace MgaWwiseImporter.UI;

internal sealed class WindowSettings
{
    private const string Section = "Window";

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public static WindowSettings? Load()
    {
        try
        {
            var values = IniFile.ReadSection(Section);
            if (!TryGetInt(values, "X", out var x)
                || !TryGetInt(values, "Y", out var y)
                || !TryGetInt(values, "Width", out var width)
                || !TryGetInt(values, "Height", out var height))
            {
                return null;
            }

            return new WindowSettings
            {
                X = x,
                Y = y,
                Width = width,
                Height = height,
            };
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        IniFile.WriteSection(Section, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X"] = X.ToString(CultureInfo.InvariantCulture),
            ["Y"] = Y.ToString(CultureInfo.InvariantCulture),
            ["Width"] = Width.ToString(CultureInfo.InvariantCulture),
            ["Height"] = Height.ToString(CultureInfo.InvariantCulture),
        });
    }

    public static WindowSettings FromForm(Form form)
    {
        var bounds = form.WindowState == FormWindowState.Normal
            ? form.Bounds
            : form.RestoreBounds;

        return new WindowSettings
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
        };
    }

    public bool TryApply(Form form)
    {
        if (Width < form.MinimumSize.Width || Height < form.MinimumSize.Height)
        {
            return false;
        }

        var bounds = new Rectangle(X, Y, Width, Height);
        if (!IsVisibleOnAnyScreen(bounds))
        {
            return false;
        }

        form.StartPosition = FormStartPosition.Manual;
        form.WindowState = FormWindowState.Normal;
        form.Bounds = bounds;
        return true;
    }

    private static bool IsVisibleOnAnyScreen(Rectangle bounds)
    {
        const int margin = 40;
        var visibleArea = new Rectangle(
            bounds.X + margin,
            bounds.Y + margin,
            Math.Max(1, bounds.Width - margin * 2),
            Math.Max(1, bounds.Height - margin * 2));

        return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(visibleArea));
    }

    private static bool TryGetInt(Dictionary<string, string> values, string key, out int value)
    {
        value = 0;
        return values.TryGetValue(key, out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
