using System.Windows;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class ComposerLayoutCacheTests
{
    private static ComposerSnapshot Snapshot() => new(new object(), [1, 2], "Text",
        new Rect(0, 0, 300, 160), 42, new Rect(200, 120, 60, 30),
        HostWindow: 123, EditorBounds: new Rect(0, 0, 300, 100),
        OccupiedBounds: [new Rect(0, 0, 300, 100)], PlacementBounds: new Rect(-100, -100, 800, 600),
        EditorTextBounds: [new Rect(4, 4, 80, 20)]);

    [Fact]
    public void ReusesOnlyGeometryAndKeepsFreshNativeElement()
    {
        var previous = Snapshot();
        var current = previous with { NativeElement = new object(), Bounds = previous.EditorBounds!.Value };
        var cache = new ComposerLayoutCache();
        cache.Store(previous, 96);
        var result = Assert.IsType<ComposerSnapshot>(cache.TryApply(current, 96));
        Assert.Same(current.NativeElement, result.NativeElement);
        Assert.Equal(previous.Bounds, result.Bounds);
        Assert.Equal(current.Text, result.Text);
        Assert.Equal(previous.PlacementBounds, result.PlacementBounds);
        Assert.Equal(previous.EditorTextBounds, result.EditorTextBounds);
    }

    [Fact]
    public void EditorTextWindowLayoutAndDpiChangesDiscardAssociation()
    {
        var previous = Snapshot();
        var changed = new[]
        {
            previous with { RuntimeId = [1, 3] },
            previous with { RuntimeId = [] },
            previous with { ProcessId = 43 },
            previous with { HostWindow = 124 },
            previous with { Host = ComposerHost.Antigravity },
            previous with { Text = "Text\n" },
            previous with { FieldCategory = "prose" },
            previous with { EditorBounds = new Rect(-100, 0, 300, 100) }
        };
        foreach (var current in changed)
        {
            var cache = new ComposerLayoutCache();
            cache.Store(previous, 96);
            Assert.Null(cache.TryApply(current, 96));
            Assert.Null(cache.TryApply(previous, 96));
        }
        foreach (uint dpi in new uint[] { 0, 144, 192 })
        {
            var cache = new ComposerLayoutCache();
            cache.Store(previous, 96);
            Assert.Null(cache.TryApply(previous, dpi));
        }
    }

    [Fact]
    public void LayoutOrFocusEventClearsOtherwiseUnchangedAssociation()
    {
        var previous = Snapshot();
        var cache = new ComposerLayoutCache();
        cache.Store(previous, 96);
        cache.Clear();
        Assert.Null(cache.TryApply(previous, 96));
    }
}
