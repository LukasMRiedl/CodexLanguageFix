using System.Windows;

namespace CodexLanguageFix.Core;

internal static class OverlayPlacement
{
    internal static Rect? Find(Rect composer, Rect editor, IReadOnlyList<Rect> occupied,
        Rect screen, double scale, Rect? preferredControl = null)
    {
        if (composer.IsEmpty || editor.IsEmpty || screen.IsEmpty || !double.IsFinite(scale) || scale <= 0)
            return null;

        var size = 28 * scale;
        var gap = 4 * scale;
        var available = Rect.Intersect(composer, screen);
        if (available.IsEmpty || available.Width < size || available.Height < size)
            return null;

        var obstacles = occupied.Where(r => !r.IsEmpty).Append(editor).ToArray();
        var xs = new List<double>();
        var ys = new List<double>();
        if (preferredControl is { IsEmpty: false } control)
        {
            xs.Add(control.Left - gap - size);
            ys.Add(control.Top + (control.Height - size) / 2);
        }
        ys.Add(editor.Bottom + gap);
        ys.Add(available.Bottom - size - gap);
        ys.Add(available.Top + gap);
        xs.Add(available.Right - size - gap);
        xs.Add(available.Left + gap);
        foreach (var obstacle in obstacles)
        {
            xs.Add(obstacle.Left - size - gap);
            xs.Add(obstacle.Right + gap);
            ys.Add(obstacle.Bottom + gap);
            ys.Add(obstacle.Top - size - gap);
        }
        foreach (var y in ys.Distinct())
        foreach (var x in xs.Distinct())
        {
            var candidate = new Rect(x, y, size, size);
            if (available.Contains(candidate) && !obstacles.Any(r => r.IntersectsWith(candidate)))
                return candidate;
        }
        return null;
    }
}
