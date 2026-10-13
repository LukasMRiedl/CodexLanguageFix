# Real shortcut speed probe

This opt-in Windows harness builds one probe executable against each supplied production project and runs 30 paired comparisons. Each child process starts a fresh Luna client, verifies a ChatGPT account, warms Luna, and uses `LunaCorrectionProvider` with its response cache disabled. Pair order alternates baseline/candidate and candidate/baseline; short, medium, and long synthetic fixtures cycle evenly.

The live command is deliberately split from the build. First prepare both binaries with `-BuildOnly`; later, after the user has focused an empty, dedicated Codex chat composer, run the prepared binaries with `-ExecuteLiveProbe`. The probe refuses any nonempty composer, wrong host/field, changed editor, lost foreground, non-German keyboard layout, held modifier, or global hotkey registration conflict. It never presses Enter or sends a message. It checks the exact synthetic fixture before editing and clears only that exact fixture after the coordinator's own Undo has restored it. Result JSON contains counts and timing aggregates, never prompt or correction text.

```powershell
.\scripts\run-shortcut-speed.ps1 `
  -BaselineProductionProject 'C:\path\to\baseline\src\CodexLanguageFix\CodexLanguageFix.csproj' `
  -CandidateProductionProject 'C:\Private Projects\Textkorrektur-App\src\CodexLanguageFix\CodexLanguageFix.csproj' `
  -BuildOnly

.\scripts\run-shortcut-speed.ps1 `
  -PreparedRunDirectory 'C:\Users\lukas\AppData\Local\Temp\CodexLanguageFix-SpeedProbe\<generated-id>' `
  -TargetWindowId <confirmed-window-handle> `
  -ExecuteLiveProbe
```

`ProductionProjectPath` is the probe's MSBuild interface: the same source project references whichever baseline or candidate `.csproj` the orchestrator passes. The probe deliberately uses baseline-compatible Composer and coordinator APIs. It uses reflection only for the existing internal keyboard-focused Composer capture, coordinator-owned hotkey state, and coordinator Undo state/path. The coordinator uses its normal `Start()` and `Dispose()` lifecycle, including its ordinary overlay refresh and layout work.

The probe requires the installed .NET 8 Desktop Runtime. Both test executables are framework-dependent; the production release remains self-contained. The confirmed window handle and the currently focused editor restrict the live test to that one composer. The harness does not enumerate the chat's accessibility tree.

Candidate-only modes `prepare-2s`, `prepare-5s`, and `prepare-edit` measure preparation separately. They do not contribute to the immediate-shortcut speed gate. Every measured manual sample also requires exact expected correction, verified Undo, and cleanup; errors remain release blockers.

The measured input-to-shortcut value is a conservative upper bound: it begins before injecting the final synthetic space and ends just before injecting the real right-Alt/comma shortcut. Samples above 300 ms are rejected. Trigger-to-write ends only after the same editor shows changed text and the actual coordinator has created its Undo state. A full report requires all 30 pairs (60 valid samples), verified Undo and cleanup for every sample, identical Luna profiles, and zero shortcut conflicts.
