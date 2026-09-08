using System.Windows;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;

namespace CodexLanguageFix.Tests;

public sealed class ComposerEditPlanTests
{
    [Theory]
    [InlineData("Siehe https://example.test", "Siehe https://example.test.")]
    [InlineData("Nutze --output=result.txt", "Nutze --output=result.txt.")]
    [InlineData("korekt `x`\r\nklein Fehler https://example.test", "korrekt `x`.\r\nkleiner Fehler https://example.test.")]
    [InlineData("Ein guter Satz `x`.\r\nZwei kurze Wörter `y`", "Ein Satz `x`\r\nZwei sehr kurze Wörter `y`.")]
    [InlineData("🙂 `x`\r\nText", "🙃! `x`.\r\nText.")]
    public void ReverseUsesCorrectedOffsetsWithoutReclassifyingProtectedSpans(string original, string corrected)
    {
        var plan = Assert.IsType<ComposerEditPlan>(ComposerEditPlan.Create(original, corrected));
        var reverse = plan.Reverse();
        Assert.Equal(corrected, reverse.Original);
        Assert.Equal(original, reverse.Corrected);
        Assert.Equal(reverse.Edits.OrderByDescending(edit => edit.Start), reverse.Edits);
        var target = new MemoryTarget(original);
        Assert.Equal(ComposerWriteState.Applied, ComposerWriteTransaction.Apply(plan, target).State);
        Assert.Equal(corrected, target.Text);
        Assert.Equal(ComposerWriteState.Applied, ComposerWriteTransaction.Apply(reverse, target).State);
        Assert.Equal(original, target.Text);
        Assert.Equal(plan.Edits, reverse.Reverse().Edits);
    }

    [Fact]
    public void ReverseAdjustsEveryLaterStartByAllEarlierLengthChanges()
    {
        var plan = new ComposerEditPlan("abc def ghi", "a defXYZ g",
        [new ComposerTextEdit(8, "ghi", "g"), new ComposerTextEdit(4, "def", "defXYZ"), new ComposerTextEdit(0, "abc", "a")]);
        var reverse = plan.Reverse();
        Assert.Equal(new[]
        {
            new ComposerTextEdit(9, "g", "ghi"),
            new ComposerTextEdit(2, "defXYZ", "def"),
            new ComposerTextEdit(0, "a", "abc")
        }, reverse.Edits);
        var target = new MemoryTarget(plan.Corrected);
        Assert.Equal(ComposerWriteState.Applied, ComposerWriteTransaction.Apply(reverse, target).State);
        Assert.Equal(plan.Original, target.Text);
    }

    [Theory]
    [InlineData("abc", "Xabc", 0, "a", "Xa")]
    [InlineData("abc", "aXbc", 0, "a", "aX")]
    [InlineData("Das ist ein Test", "Das ist ein Test.", 15, "t", "t.")]
    [InlineData("Das ist ein Test\n", "Das ist ein Test.\n", 15, "t", "t.")]
    [InlineData("a\r\nb", "a.\r\nb", 0, "a", "a.")]
    [InlineData("a\r\nb", "a\r\nXb", 3, "b", "Xb")]
    [InlineData("a\u0308", "a\u0308!", 0, "a\u0308", "a\u0308!")]
    [InlineData("🙂", "🙂!", 0, "🙂", "🙂!")]
    [InlineData("👩\u200d💻", "👩\u200d💻!", 0, "👩\u200d💻", "👩\u200d💻!")]
    [InlineData("`x`abc", "`x`Xabc", 3, "a", "Xa")]
    [InlineData("abc`x`", "abcd`x`", 2, "c", "cd")]
    [InlineData("• abc\r\n", "• Xabc\r\n", 2, "a", "Xa")]
    public void OneSidedEditsUseAnUnprotectedWholeGraphemeInBothDirections(
        string original, string corrected, int start, string expectedOriginal, string expectedReplacement)
    {
        var plan = Assert.IsType<ComposerEditPlan>(ComposerEditPlan.Create(original, corrected));
        var edit = Assert.Single(plan.Edits);
        Assert.Equal(new ComposerTextEdit(start, expectedOriginal, expectedReplacement), edit);
        Assert.NotEmpty(edit.Original);
        Assert.NotEmpty(edit.Replacement);

        var reverse = Assert.IsType<ComposerEditPlan>(ComposerEditPlan.Create(corrected, original));
        Assert.Equal(new ComposerTextEdit(start, expectedReplacement, expectedOriginal), Assert.Single(reverse.Edits));
        var target = new MemoryTarget(original);
        Assert.Equal(ComposerWriteState.Applied, ComposerWriteTransaction.Apply(plan, target).State);
        Assert.Equal(corrected, target.Text);
        Assert.Equal(ComposerWriteState.Applied, ComposerWriteTransaction.Apply(reverse, target).State);
        Assert.Equal(original, target.Text);
    }

    [Theory]
    [InlineData("Use `x`", "Use `x`.")]
    [InlineData("Use `x`.", "Use `x`")]
    [InlineData("`x`\r\n", "`x`.\r\n")]
    [InlineData("`x`.\r\n", "`x`\r\n")]
    public void OneSidedEditsWithoutAnUnprotectedAnchorRemainInsideTheFreeGap(string original, string corrected)
    {
        Assert.True(TextStructure.IsPreserved(original, corrected));
        var plan = Assert.IsType<ComposerEditPlan>(ComposerEditPlan.Create(original, corrected));
        var edit = Assert.Single(plan.Edits);
        Assert.True(edit.Original.Length == 0 || edit.Replacement.Length == 0);
        Assert.Equal(".", edit.Original + edit.Replacement);
        var target = new MemoryTarget(original);
        Assert.Equal(ComposerWriteState.Applied, ComposerWriteTransaction.Apply(plan, target).State);
        Assert.Equal(corrected, target.Text);
    }

    [Theory]
    [InlineData("Siehe https://example.test", "Siehe https://example.test.")]
    [InlineData("Nutze --output=result.txt", "Nutze --output=result.txt.")]
    [InlineData("Nutze `foo`", "Nutze `foo`.")]
    [InlineData("abc`x`", "abcX`x`")]
    [InlineData("ein wert ist korekt", "ein neuerWert ist korrekt")]
    [InlineData("Korekt `x` und `x`", "Korrekt `x` und `x`.")]
    [InlineData("- Korekt https://example.test\r\n- Nutze `foo`\r\n", "- Korrekt https://example.test.\r\n- Nutze `foo`.\r\n")]
    [InlineData("Korekt\n```\nvar x = 1;\n```\n", "Korrekt\n```\nvar x = 1;\n```\n")]
    public void PreservesSourceTechnicalSpansWithoutReclassifyingCorrectedProse(string original, string corrected)
    {
        var plan = Assert.IsType<ComposerEditPlan>(ComposerEditPlan.Create(original, corrected));
        var sourceSpans = ProtectedSpanDetector.Detect(original);
        Assert.All(plan.Edits, edit => Assert.All(sourceSpans, span =>
            Assert.True(edit.Start + edit.Original.Length <= span.Start || edit.Start >= span.End)));
        var target = new MemoryTarget(original);
        Assert.Equal(ComposerWriteState.Applied, ComposerWriteTransaction.Apply(plan, target).State);
        Assert.Equal(corrected, target.Text);
    }

    [Theory]
    [InlineData("Nutze `x`", "Nutze `x` oder `x`")]
    [InlineData("Nutze `x` und `x`", "Nutze `x`")]
    [InlineData("Nutze `x` und `y`", "Nutze `y` und `x`")]
    [InlineData("Nutze https://example.test", "Nutze https://other.test.")]
    [InlineData("Nutze --output=result.txt", "Nutze --output=other.txt.")]
    [InlineData("Korekt\n```\nvar x = 1;\n```\n", "Korrekt\n```\nvar x = 2;\n```\n")]
    public void RejectsAmbiguousReorderedDeletedOrMutatedSourceTechnicalSpans(string original, string corrected) =>
        Assert.Null(ComposerEditPlan.Create(original, corrected));

    [Fact]
    public void UnchangedTechnicalTextRequiresNoOccurrenceGuessing()
    {
        const string text = "Nutze foo_bar und foo_bar_long";
        Assert.Empty(Assert.IsType<ComposerEditPlan>(ComposerEditPlan.Create(text, text)).Edits);
    }

    [Theory]
    [InlineData("- korekt\r\n\r\n  2. Änderug\r\n", "- korrekt\r\n\r\n  2. Änderung\r\n")]
    [InlineData("Korekt `var x = 1` korekt", "Korrekt `var x = 1` korrekt")]
    [InlineData("🙂 korekt 🙂 korekt", "🙂 korrekt 🙂 korrekt")]
    [InlineData("a\u0308", "ä")]
    [InlineData("🙂", "🙃")]
    [InlineData("schön\ufffc korekt", "schöner\ufffc korrekt")]
    [InlineData("Ein Test.", "Ein guter Test.")]
    [InlineData("Ein guter Test.", "Ein Test.")]
    [InlineData("korekt\tkorekt", "korrekt\tkorrekt")]
    public void AppliesOnlySafeBackwardEditsAndReconstructsExactly(string original, string corrected)
    {
        var plan = Assert.IsType<ComposerEditPlan>(ComposerEditPlan.Create(original, corrected));
        var target = new MemoryTarget(original);
        var result = ComposerWriteTransaction.Apply(plan, target);

        Assert.Equal(ComposerWriteState.Applied, result.State);
        Assert.Equal(corrected, target.Text);
        Assert.Equal(plan.Edits.OrderByDescending(edit => edit.Start), plan.Edits);
        Assert.All(plan.Edits, edit => Assert.DoesNotContain('\r', edit.Original + edit.Replacement));
        Assert.All(plan.Edits, edit => Assert.DoesNotContain('\n', edit.Original + edit.Replacement));
        Assert.All(plan.Edits, edit => Assert.DoesNotContain('\t', edit.Original + edit.Replacement));
        Assert.All(plan.Edits, edit => Assert.DoesNotContain('`', edit.Original + edit.Replacement));
        Assert.All(plan.Edits, edit => Assert.DoesNotContain('\ufffc', edit.Original + edit.Replacement));
        var undo = Assert.IsType<ComposerEditPlan>(ComposerEditPlan.Create(corrected, original));
        Assert.Equal(ComposerWriteState.Applied, ComposerWriteTransaction.Apply(undo, target).State);
        Assert.Equal(original, target.Text);
    }

    [Theory]
    [InlineData("- Text", "Text")]
    [InlineData("Text\nText", "Text Text")]
    [InlineData("Nutze `x`", "Nutze `y`")]
    [InlineData("Vor\ufffc nach", "Vor nach")]
    [InlineData("[Text](https://example.test)", "[Text](https://other.test)")]
    [InlineData("Text\tText", "Text Text")]
    public void RejectsMutatedStructureTechnicalSpansAndObjects(string original, string corrected) =>
        Assert.Null(ComposerEditPlan.Create(original, corrected));

    [Fact]
    public void UnicodeEditsDoNotSplitSurrogatesOrCombiningSequences()
    {
        var emoji = Assert.IsType<ComposerEditPlan>(ComposerEditPlan.Create("🙂", "🙃"));
        Assert.Equal("🙂", Assert.Single(emoji.Edits).Original);
        Assert.Equal("🙃", emoji.Edits[0].Replacement);
        var combining = Assert.IsType<ComposerEditPlan>(ComposerEditPlan.Create("a\u0308", "a\u0301"));
        Assert.Equal("a\u0308", Assert.Single(combining.Edits).Original);
    }

    [Fact]
    public void HandlesRepeatedTextAndTwentyThousandCharacters()
    {
        var original = string.Concat(Enumerable.Repeat("- korekt\r\n", 2_000));
        var corrected = original.Replace("korekt", "korrekt", StringComparison.Ordinal);
        var plan = Assert.IsType<ComposerEditPlan>(ComposerEditPlan.Create(original, corrected));
        Assert.Equal(2_000, plan.Edits.Count);
        var target = new MemoryTarget(original);
        Assert.Equal(ComposerWriteState.Applied, ComposerWriteTransaction.Apply(plan, target).State);
        Assert.Equal(corrected, target.Text);
    }

    [Fact]
    public void FailsPreflightBeforeAnyWriteIfOneSelectionIsUnavailable()
    {
        var plan = TwoChanges();
        var target = new MemoryTarget(plan.Original) { InvalidSelection = plan.Edits[1].Start };
        var result = ComposerWriteTransaction.Apply(plan, target);
        Assert.Equal(ComposerWriteState.Unchanged, result.State);
        Assert.Equal(0, target.WriteCalls);
        Assert.Equal(plan.Original, target.Text);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FailureBeforeWriteOrAfterOneVerifiedWriteRestoresOriginal(int failingCall)
    {
        var plan = TwoChanges();
        var target = new MemoryTarget(plan.Original) { FailOnWrite = failingCall };
        var result = ComposerWriteTransaction.Apply(plan, target);
        Assert.Equal(ComposerWriteState.Unchanged, result.State);
        Assert.Equal(plan.Original, target.Text);
    }

    [Fact]
    public void ExceptionAfterAppliedWriteRollsBackOnlyVerifiedOwnText()
    {
        var plan = TwoChanges();
        var target = new MemoryTarget(plan.Original) { ThrowAfterWrite = 1 };
        var result = ComposerWriteTransaction.Apply(plan, target);
        Assert.Equal(ComposerWriteState.Unchanged, result.State);
        Assert.Equal(plan.Original, target.Text);
    }

    [Theory]
    [InlineData(0, ComposerWriteState.Unchanged)]
    [InlineData(1, ComposerWriteState.PartiallyApplied)]
    public void PersistentReadFailureReportsPartialStateOnlyAfterAWriteAttempt(
        int failureAfterWrites, ComposerWriteState expectedState)
    {
        var plan = TwoChanges();
        var target = new MemoryTarget(plan.Original) { ReadFailureAfterWrites = failureAfterWrites };

        var result = ComposerWriteTransaction.Apply(plan, target);

        Assert.Equal(expectedState, result.State);
        Assert.Equal(failureAfterWrites, target.WriteCalls);
        Assert.Equal(plan.Original, result.OriginalText);
        Assert.Null(result.ObservedText);
        if (failureAfterWrites == 0) Assert.Equal(plan.Original, target.Text);
        else Assert.NotEqual(plan.Original, target.Text);
    }

    [Fact]
    public void UserChangesDuringWriteArePreservedAndOriginalRemainsAvailable()
    {
        var plan = TwoChanges();
        var target = new MemoryTarget(plan.Original) { UserChangeAfterWrite = 1 };
        var result = ComposerWriteTransaction.Apply(plan, target);
        Assert.Equal(ComposerWriteState.PartiallyApplied, result.State);
        Assert.Equal("Eigene neue Eingabe", target.Text);
        Assert.Equal(plan.Original, result.OriginalText);
        Assert.Equal(1, target.WriteCalls);
    }

    [Fact]
    public void FocusLossDoesNotWriteIntoAnotherEditorOrRollBackThere()
    {
        var plan = TwoChanges();
        var target = new MemoryTarget(plan.Original) { LoseFocusAfterWrite = 1 };
        var result = ComposerWriteTransaction.Apply(plan, target);
        Assert.Equal(ComposerWriteState.PartiallyApplied, result.State);
        Assert.Equal(1, target.WriteCalls);
    }

    [Fact]
    public void SameTextInDifferentEditorIsNotTheSameEditor()
    {
        var first = new ComposerSnapshot(new object(), [1, 2], "Text", new Rect(0, 0, 100, 40), 100, HostWindow: 10);
        Assert.True(first.SameEditor(first with { Text = "Andere Eingabe" }));
        Assert.False(first.SameEditor(first with { RuntimeId = [1, 3] }));
        Assert.False(first.SameEditor(first with { HostWindow = 20 }));
        Assert.False(first.SameEditor(first with { ProcessId = 200 }));
        Assert.False(first.SameEditor(first with { Host = ComposerHost.Antigravity }));
    }

    private static ComposerEditPlan TwoChanges() => Assert.IsType<ComposerEditPlan>(
        ComposerEditPlan.Create("- korekt\n- korekt", "- korrekt\n- korrekt"));

    private sealed class MemoryTarget(string text) : IComposerEditTarget
    {
        public string Text { get; private set; } = text;
        public bool IsCurrentEditor { get; private set; } = true;
        public int? InvalidSelection { get; init; }
        public int? FailOnWrite { get; init; }
        public int? ThrowAfterWrite { get; init; }
        public int? UserChangeAfterWrite { get; init; }
        public int? LoseFocusAfterWrite { get; init; }
        public int? ReadFailureAfterWrites { get; init; }
        public int WriteCalls { get; private set; }
        public string? Read() => ReadFailureAfterWrites is { } failureAfterWrites && WriteCalls >= failureAfterWrites
            ? throw new InvalidOperationException("Editor text is unavailable.")
            : Text;
        public bool CanReplace(int start, string expected) => start != InvalidSelection
            && Text.AsSpan(start, expected.Length).SequenceEqual(expected);
        public bool Replace(int start, string expected, string replacement)
        {
            WriteCalls++;
            if (WriteCalls == FailOnWrite)
            {
                return false;
            }

            Assert.True(IsCurrentEditor);
            Assert.Equal(expected, Text.Substring(start, expected.Length));
            Text = Text.Remove(start, expected.Length).Insert(start, replacement);
            if (WriteCalls == LoseFocusAfterWrite) IsCurrentEditor = false;
            if (WriteCalls == UserChangeAfterWrite) Text = "Eigene neue Eingabe";
            if (WriteCalls == ThrowAfterWrite) throw new InvalidOperationException("Write completed before transport failed.");
            return true;
        }
    }
}
