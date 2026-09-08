using System.Windows;

namespace CodexLanguageFix.Core;

internal readonly record struct ComposerControlBounds(Rect Bounds, bool IsButton, bool IsEditor);
internal sealed record ComposerToolbarAssociation(Rect SurfaceBounds, IReadOnlyList<Rect> OccupiedBounds, IReadOnlyList<Rect> RowButtons);

internal static class ComposerToolbarGeometry
{
    public static ComposerToolbarAssociation? TryAssociate(
        Rect editor, Rect document, IReadOnlyList<ComposerControlBounds> controls, double dpiScale)
    {
        if (!Valid(editor) || !Valid(document) || !double.IsFinite(dpiScale) || dpiScale <= 0 || !document.Contains(editor))
        {
            return null;
        }

        var buttons = controls.Where(control => control.IsButton && Valid(control.Bounds))
            .Select(control => control.Bounds)
            .Where(bounds => bounds.Left >= editor.Left - 12 * dpiScale
                && bounds.Right <= editor.Right + 12 * dpiScale
                && bounds.Top >= editor.Bottom - 2 * dpiScale
                && bounds.Top <= editor.Bottom + 24 * dpiScale
                && bounds.Height >= 18 * dpiScale && bounds.Height <= 64 * dpiScale
                && bounds.Width >= 18 * dpiScale)
            .Distinct().ToArray();
        var rows = new List<Rect[]>();
        foreach (var seed in buttons)
        {
            var center = seed.Top + seed.Height / 2;
            var row = buttons.Where(button => Math.Abs(button.Top + button.Height / 2 - center) <= 8 * dpiScale
                    && Math.Min(seed.Bottom, button.Bottom) - Math.Max(seed.Top, button.Top) >= Math.Min(seed.Height, button.Height) / 2)
                .OrderBy(button => button.Left).ToArray();
            if (row.Length < 2
                || !row.Any(left => left.Right <= editor.Left + editor.Width * 0.45
                    && row.Any(right => right.Left >= editor.Left + editor.Width * 0.55
                        && right.Left - left.Right >= 8 * dpiScale)))
            {
                continue;
            }

            if (!rows.Any(existing => existing.SequenceEqual(row)))
            {
                rows.Add(row);
            }
        }

        if (rows.Count == 0)
        {
            var inlineButtons = controls.Where(control => control.IsButton && Valid(control.Bounds))
                .Select(control => control.Bounds).Distinct()
                .Where(button => button.Height >= 18 * dpiScale && button.Height <= 64 * dpiScale
                    && button.Width >= 18 * dpiScale
                    && Math.Min(button.Bottom, editor.Bottom) - Math.Max(button.Top, editor.Top) >= Math.Min(button.Height, editor.Height) / 2)
                .ToArray();
            var pairs = (from left in inlineButtons
                         where left.Right <= editor.Left && editor.Left - left.Right <= 24 * dpiScale
                         from right in inlineButtons
                         where right.Left >= editor.Right && right.Left - editor.Right <= 24 * dpiScale
                             && Math.Abs(left.Top + left.Height / 2 - right.Top - right.Height / 2) <= 8 * dpiScale
                         select new[] { left, right }).ToArray();
            if (pairs.Length != 1) return null;
            var inlineSurface = editor;
            foreach (var button in pairs[0]) inlineSurface.Union(button);
            inlineSurface = new Rect(inlineSurface.Left - 36 * dpiScale, inlineSurface.Top - 4 * dpiScale,
                inlineSurface.Width + 40 * dpiScale, inlineSurface.Height + 8 * dpiScale);
            return Complete(inlineSurface, pairs[0]);
        }

        if (rows.Count != 1)
        {
            return null;
        }

        var surface = editor;
        foreach (var button in rows[0]) surface.Union(button);
        surface.Inflate(4 * dpiScale, 4 * dpiScale);
        return Complete(surface, rows[0]);

        ComposerToolbarAssociation? Complete(Rect candidate, IReadOnlyList<Rect> rowButtons)
        {
            candidate.Intersect(document);
            if (controls.Any(control => control.IsEditor && control.Bounds != editor
                && Valid(control.Bounds) && control.Bounds.IntersectsWith(candidate)))
            {
                return null;
            }

            var occupied = controls.Where(control => Valid(control.Bounds) && control.Bounds.IntersectsWith(candidate))
                .Select(control => control.Bounds).Append(editor).Distinct().ToArray();
            return new ComposerToolbarAssociation(candidate, occupied, rowButtons);
        }
    }

    private static bool Valid(Rect bounds) => !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0
        && double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top)
        && double.IsFinite(bounds.Right) && double.IsFinite(bounds.Bottom);
}
