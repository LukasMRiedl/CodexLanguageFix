using System.Windows;

namespace CodexLanguageFix.Core;

internal static class OverlayPlacement
{
    internal static Rect? Find(Rect composer, Rect editor, IReadOnlyList<Rect> occupied,
        Rect screen, double scale, Rect? preferredControl = null, Rect? placementBounds = null)
    {
        if (!IsValid(composer) || !IsValid(editor) || !IsValid(screen)
            || !double.IsFinite(scale) || scale <= 0
            || (placementBounds is { } placement && !IsValid(placement))
            || (preferredControl is { } preferred && !preferred.IsEmpty && !IsValid(preferred))
            || occupied is null)
        {
            return null;
        }

        var obstacles = new List<Rect>();
        foreach (var bounds in occupied)
        {
            if (bounds.IsEmpty)
            {
                continue;
            }

            if (!IsValid(bounds))
            {
                return null;
            }

            obstacles.Add(bounds);
        }
        obstacles.Add(editor);

        var size = 28 * scale;
        var gap = 4 * scale;
        if (!double.IsFinite(size) || !double.IsFinite(gap) || size <= 0)
        {
            return null;
        }

        var toolbarBounds = Intersect(composer, screen);
        if (placementBounds is { } verifiedBounds)
        {
            toolbarBounds = Intersect(toolbarBounds, verifiedBounds);
        }

        if (IsValid(toolbarBounds))
        {
            var hasPreferredControl = preferredControl is { IsEmpty: false };
            var preferredX = hasPreferredControl
                ? preferredControl!.Value.Left - size - gap
                : toolbarBounds.Right - size - gap;
            var preferredY = hasPreferredControl
                ? preferredControl!.Value.Top + (preferredControl.Value.Height - size) / 2
                : editor.Bottom + gap;

            // Honor the preferred control (or the editor footer without one) before searching
            // other rows. On that row, inspect obstacle edges by distance from the anchor.
            var rowPlacement = FindOnFixedY(toolbarBounds, obstacles, size, gap,
                preferredX, preferredY);
            if (rowPlacement is not null)
            {
                return rowPlacement;
            }

            // Keep the established toolbar area available when its preferred row is blocked.
            var xPositions = CandidatePositions(toolbarBounds, obstacles, size, gap,
                horizontal: true, preferred: preferredX);
            var yPositions = CandidatePositions(toolbarBounds, obstacles, size, gap,
                horizontal: false, preferred: preferredY);
            foreach (var y in yPositions)
            foreach (var x in xPositions)
            {
                if (TryCandidate(x, y, size, out var candidate)
                    && toolbarBounds.Contains(candidate)
                    && IsClear(candidate, obstacles, gap))
                {
                    return candidate;
                }
            }
        }

        // Sidecar placement requires verified application/window bounds. These may include safe
        // space beside the composer, but candidates must also remain inside the monitor work area.
        if (placementBounds is not { } sidecarBounds)
        {
            return null;
        }

        var sidecarArea = Intersect(sidecarBounds, screen);
        if (!IsValid(sidecarArea))
        {
            return null;
        }

        var right = FindOnFixedX(sidecarArea, obstacles, size, gap,
            editor.Right + gap, editor.Top, editor.Bottom, editor.Bottom - size);
        if (right is not null)
        {
            return right;
        }

        var left = FindOnFixedX(sidecarArea, obstacles, size, gap,
            editor.Left - size - gap, editor.Top, editor.Bottom, editor.Bottom - size);
        if (left is not null)
        {
            return left;
        }

        var below = FindOnFixedY(sidecarArea, obstacles, size, gap,
            editor.Bottom + gap, editor.Left, editor.Right, editor.Right - size);
        if (below is not null)
        {
            return below;
        }

        return FindOnFixedY(sidecarArea, obstacles, size, gap,
            editor.Top - size - gap, editor.Left, editor.Right, editor.Right - size);
    }

    private static Rect? FindOnFixedX(Rect area, IReadOnlyList<Rect> obstacles,
        double size, double gap, double x, double fieldTop, double fieldBottom, double preferredY)
    {
        if (!double.IsFinite(x)
            || !TryGetFieldAxisRange(area.Top, area.Bottom, fieldTop, fieldBottom,
                size, preferredY, out var minimumY, out var maximumY, out var preferred))
        {
            return null;
        }

        foreach (var y in CandidatePositions(minimumY, maximumY, obstacles, size, gap,
                     horizontal: false, preferred: preferred))
        {
            if (TryCandidate(x, y, size, out var candidate)
                && area.Contains(candidate)
                && IsClear(candidate, obstacles, gap))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Rect? FindOnFixedY(Rect area, IReadOnlyList<Rect> obstacles,
        double size, double gap, double preferredX, double y)
    {
        if (!double.IsFinite(y))
        {
            return null;
        }

        foreach (var x in CandidatePositions(area, obstacles, size, gap,
                     horizontal: true, preferred: preferredX))
        {
            if (TryCandidate(x, y, size, out var candidate)
                && area.Contains(candidate)
                && IsClear(candidate, obstacles, gap))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Rect? FindOnFixedY(Rect area, IReadOnlyList<Rect> obstacles,
        double size, double gap, double y, double fieldLeft, double fieldRight, double preferredX)
    {
        if (!double.IsFinite(y)
            || !TryGetFieldAxisRange(area.Left, area.Right, fieldLeft, fieldRight,
                size, preferredX, out var minimumX, out var maximumX, out var preferred))
        {
            return null;
        }

        foreach (var x in CandidatePositions(minimumX, maximumX, obstacles, size, gap,
                     horizontal: true, preferred: preferred))
        {
            if (TryCandidate(x, y, size, out var candidate)
                && area.Contains(candidate)
                && IsClear(candidate, obstacles, gap))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyList<double> CandidatePositions(Rect area, IReadOnlyList<Rect> obstacles,
        double size, double gap, bool horizontal, double preferred)
    {
        var minimum = horizontal ? area.Left : area.Top;
        var maximum = horizontal ? area.Right - size : area.Bottom - size;
        return CandidatePositions(minimum, maximum, obstacles, size, gap, horizontal, preferred);
    }

    private static IReadOnlyList<double> CandidatePositions(double minimum, double maximum,
        IReadOnlyList<Rect> obstacles, double size, double gap, bool horizontal, double preferred)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum < minimum)
        {
            return [];
        }

        var positions = new HashSet<double>();
        void Add(double value)
        {
            if (double.IsFinite(value) && value >= minimum && value <= maximum)
            {
                positions.Add(value);
            }
        }

        void AddClamped(double value)
        {
            if (double.IsFinite(value))
            {
                Add(Math.Clamp(value, minimum, maximum));
            }
        }

        AddClamped(preferred);
        Add(minimum);
        Add(maximum);
        AddClamped(minimum + gap);
        AddClamped(maximum - gap);

        foreach (var obstacle in obstacles)
        {
            if (horizontal)
            {
                Add(obstacle.Left - size - gap);
                Add(obstacle.Right + gap);
            }
            else
            {
                Add(obstacle.Top - size - gap);
                Add(obstacle.Bottom + gap);
            }
        }

        if (double.IsFinite(preferred))
        {
            return positions.OrderBy(position => Math.Abs(position - preferred))
                .ThenBy(position => position).ToArray();
        }

        return positions.Order().ToArray();
    }

    private static bool TryGetFieldAxisRange(double areaStart, double areaEnd,
        double fieldStart, double fieldEnd, double size, double preferred,
        out double minimum, out double maximum, out double aligned)
    {
        minimum = maximum = aligned = 0;
        if (!double.IsFinite(areaStart) || !double.IsFinite(areaEnd)
            || !double.IsFinite(fieldStart) || !double.IsFinite(fieldEnd)
            || !double.IsFinite(size) || !double.IsFinite(preferred)
            || areaEnd - areaStart < size)
        {
            return false;
        }

        var fieldLength = fieldEnd - fieldStart;
        if (fieldLength >= size)
        {
            minimum = Math.Max(areaStart, fieldStart);
            maximum = Math.Min(areaEnd - size, fieldEnd - size);
            aligned = preferred;
            return maximum >= minimum;
        }

        // If the field is shorter than the button, center it on that field and do not search
        // elsewhere along the window edge.
        aligned = fieldStart + (fieldLength - size) / 2;
        if (!double.IsFinite(aligned) || aligned < areaStart || aligned > areaEnd - size)
        {
            return false;
        }

        minimum = maximum = aligned;
        return true;
    }

    private static bool IsClear(Rect candidate, IReadOnlyList<Rect> obstacles, double gap) =>
        !obstacles.Any(obstacle => HasPositiveIntersectionWithInflated(candidate, obstacle, gap));

    private static bool HasPositiveIntersectionWithInflated(Rect candidate, Rect obstacle, double gap)
    {
        var left = obstacle.Left - gap;
        var top = obstacle.Top - gap;
        var right = obstacle.Right + gap;
        var bottom = obstacle.Bottom + gap;
        if (!double.IsFinite(left) || !double.IsFinite(top)
            || !double.IsFinite(right) || !double.IsFinite(bottom))
        {
            return true;
        }

        return candidate.Left < right && candidate.Right > left
            && candidate.Top < bottom && candidate.Bottom > top;
    }

    private static Rect Intersect(Rect first, Rect second)
    {
        first.Intersect(second);
        return first;
    }

    private static bool TryCandidate(double x, double y, double size, out Rect candidate)
    {
        candidate = Rect.Empty;
        if (!double.IsFinite(x) || !double.IsFinite(y)
            || !double.IsFinite(size) || size <= 0
            || !double.IsFinite(x + size) || !double.IsFinite(y + size))
        {
            return false;
        }

        candidate = new Rect(x, y, size, size);
        return true;
    }

    private static bool IsValid(Rect bounds) => !bounds.IsEmpty
        && bounds.Width > 0 && bounds.Height > 0
        && double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top)
        && double.IsFinite(bounds.Right) && double.IsFinite(bounds.Bottom)
        && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height);
}
