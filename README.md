# Codex Language Fix

**English** · [Deutsch](README.de.md)

The current development branch implements Codex-only text correction natively in C++20/Win32. It continues to use the official Codex App Server and existing Codex sign-in. Version 2.0.0 is installed locally; correction, exact Undo, the updated overlay, native automated tests, and 3,945 differential cases have been checked. At the user's explicit request, full long-duration and resource acceptance was deferred in favor of observing everyday use. A short reading does not establish the sustained 50 MB target. C# remains a test reference and is excluded from the native product package. See the [native development notes](native/README.de.md) for build and verification details (German).

The [acceptance status](docs/native-port-2026-11-14.md) separates completed checks from the remaining release blockers and measurement limits (German).

A standalone Windows app for one-click prompt correction in OpenAI Codex Desktop. The reference implementation uses Codex through its official ChatGPT OAuth session and existing Luna model profile. No Codex plugin, OpenAI API key, additional API billing, or self-hosted server is required.

> This is an independent project and is not affiliated with OpenAI or Google.

The C# reference source in this repository is **version 1.5.4**. It selects the newest Luna model offered by the installed Codex runtime that supports low reasoning with Fast Mode (`priority`). The `low` production profile passed live qualification with `gpt-6-luna`, 128 corpus cases and unchanged safety gates; see the [runtime report](docs/desktop-runtime-2026-09-30.md) for evidence and limitations. The earlier RC7 memory comparison measured approximately 49% less private memory and 43% less working set, short of the **80% target**. See the [memory report](docs/ram-optimization-2026-09-13.md) for its evidence and limitations. These reports describe the C# reference and dated runs, not native acceptance. Version 1.4.0 is the stable GitHub release.

The C# reference 1.5.4 starts its isolated UI automation worker before WPF, promptly releases unused Codex sessions owned by the app, and keeps native editor references out of the geometry cache and write identity. The [verification report](docs/codex-only-2026-11-13.md) documents those changes and the limits of the 50 MB target; it does not establish native memory results or feature parity.

Version 1.5.0 passed 30 live paired immediate-shortcut comparisons: median wait fell from 4.53 to 2.97 seconds (34.5%), with all 60 corrections exact and a better overall p95. That version was installed locally with optional Luna preparation enabled. See the [speed report](docs/shortcut-speed-2026-10-13.md) for method and limitations.

The C# reference's Codex-only migration preserves existing shortcuts and startup settings. Automatic preparation stays enabled for existing Codex users who enabled it; it is disabled for legacy LanguageTool, missing or invalid provider settings. Newly saved settings use schema version 2 so an explicit preparation choice survives later restarts.

## C# reference: features and operation

The following feature, installation, and privacy notes describe the C# reference source. They do not establish feature parity or live behavior for the native port.

- No automatic Windows notifications. Correction status and Undo remain next to Aa; the last message is available through **system tray → Settings…**.
- Choose **Settings… → Record shortcut** to capture and save a combination. It takes effect without restarting and persists across app restarts. Combinations already registered by another application are rejected when saving. Ctrl and/or Alt plus another key, optionally Shift, and function keys with optional Shift are supported; F12 and Windows-key combinations are excluded. AltGr is technically Ctrl+Alt and may conflict with ordinary special characters depending on the chosen key. **Restore default** selects Ctrl+Alt+comma again.

- A subtle `Aa` button dynamically appears inside the active prompt bar. Click it or use your configured shortcut (default: `AltGr+,` / `Ctrl+Alt+,`) to correct a non-empty supported prompt while Codex Language Fix is active.
- One click checks spelling, grammar, punctuation, and style.
- Codex is the sole correction provider. Production requests the `baseline / low / full-v1` profile with Fast Mode (`priority`) on the newest compatible model offered by the installed Codex runtime. This profile was tested live with `gpt-6-luna` on 30 September 2026. Experimental profiles never activate automatically.
- Luna uses the official Codex App Server and its managed ChatGPT OAuth flow. The app never reads or stores OAuth tokens.
- German and English are detected automatically, with `de-DE` and `en-US` as the preferred variants.
- The entire interface is available in English and German. It follows the Windows display language by default and can be switched at any time from the system tray menu.
- Markdown code blocks, inline code, URLs, email addresses, file paths, command-line options, and common code identifiers remain unchanged.
- The clipboard is never read or modified.
- No prompt is sent automatically by default. If you enable “Prepare Codex corrections” from the system tray, the app sends the prompt to Luna after 1.2 seconds of observed text stability to prepare a correction draft. You insert the draft manually; it is never applied automatically. This may count toward your connected ChatGPT account's usage limits.
- Paragraphs, blank lines, indentation, list markers, checkboxes and Markdown line breaks are validated before insertion. Unsafe Codex responses are rejected.
- Corrections use verified text ranges, without a typing animation or whole-editor retyping. Undo is restricted to the original editor and exact corrected text.
- The compact button is placed only in a verified free area belonging to a uniquely identified composer. It hides when identification or placement is ambiguous. Positioning uses the target monitor's DPI, including negative monitor coordinates.
- Requests are cancelled when the source editor or text changes, or correction is disabled. Partial write failures never trigger a blind full-text retry. Verified own changes are rolled back when safe; no recovery window opens.
- No background service and no automatic updater.

The [field diagnosis report](docs/aa-coverage-2026-10-04.md) records earlier checks. Main-chat correction and exact undo were verified live in Codex and Hermes at that time, including real monitor transitions between 175% and 200%. Version 1.4.0 was installed during that validation. These historical findings do not describe Hermes support in version 1.5.4; additional inaccessible field families are explicitly listed as unverified in the report.

## C# reference: installation

The download link points to the stable C# release (version 1.4.0); it is not the repository's 1.5.4 C# reference source described by the operation and privacy notes.

1. Download `CodexLanguageFix-win-x64.zip` from the [latest release](https://github.com/LukasMRiedl/CodexLanguageFix/releases/latest).
2. Extract the ZIP archive to a permanent folder.
3. Run `CodexLanguageFix.exe`.
4. On first launch, acknowledge the privacy notice. Automatic Luna preparation is off by default. When enabled, a supported prompt is sent to Luna automatically after 1.2 seconds of observed text stability. You insert the resulting draft manually.

The app does not require administrator privileges. Its system tray menu lets you connect Codex, test the Codex connection, pause or exit the app, and change automatic startup.

Codex correction requires the official Codex Desktop installation and a ChatGPT account available to Codex; Codex Desktop can remain closed while the app runs. At App Server startup, the app selects the newest Luna model offered by the installed official runtime that supports low reasoning and Fast Mode (`priority`). It keeps this selection for the runtime and resolves it again after the runtime or account context resets, so corrections do not repeat model-list discovery. If no compatible model is available, the prompt remains unchanged; Codex Language Fix never silently falls back to another mode or provider.

The published executable is currently not code-signed. Windows may therefore display a security warning the first time you launch it.

## C# reference: privacy

With automatic preparation enabled, unsent intermediate drafts can be transmitted whenever typing pauses. Cancellation after further editing cannot undo a transmission that has already occurred.

When Codex Language Fix is active and a non-empty supported prompt is recognized, clicking the correction button or pressing the configured shortcut (default: `AltGr+,` / `Ctrl+Alt+,`) sends the current prompt to Codex. The system tray also has an optional “Prepare Codex corrections” setting, which is off by default. When enabled, the app sends the prompt to Luna after 1.2 seconds of observed text stability to prepare a correction draft. The draft is never applied automatically; you insert it manually. These requests may count toward the usage limits of the ChatGPT account connected to Codex. Luna requests use ChatGPT OAuth through the official Codex App Server; the app has no API-key path. At App Server startup, the app selects the newest Luna model offered by the installed official runtime that supports low reasoning and Fast Mode (`priority`), then keeps that profile for the server runtime. It resolves the profile again after a runtime or account-context reset, so corrections do not repeat model-list discovery. Production requests `baseline / low / full-v1`. Each correction uses an ephemeral thread with a read-only/no-network sandbox and explicit instructions prohibiting tools. The App Server and one empty single-use thread may be prepared without composer content. Segment and span-edit protocols as well as `medium` reasoning remain disabled. Technical spans are replaced locally with random placeholders before Luna sees the text and restored only after strict validation.

Prompt text, corrected text, provider responses, OAuth tokens, email addresses, and account details are never recorded in production logs. Up to 64 validated Luna results are cached only in memory and discarded when the app exits. Local diagnostic logs contain only technical metadata such as provider, timestamp, duration, status code, character count, and change count. They are deleted after seven days. Explicitly enabled UI diagnostics may output only pre-verified synthetic fixture data.

See [PRIVACY.md](PRIVACY.md) for more information.

## C# reference: provider limits

Codex accepts at most 20,000 characters per correction. The app creates no queue. Automatic Luna preparation is optional and off by default; its requests may count toward the usage limits of the ChatGPT account connected to Codex.

## Native build and automated verification

Use the native build script on Windows with Visual Studio Build Tools 2022, MSVC v143, Windows SDK, and CMake:

```powershell
.\scripts\build-native.ps1
```

The script builds and tests the C++20/Win32 targets and writes a package to `dist/native-win-x64`. Building alone neither installs nor publishes the app and does not establish resource acceptance. The [native development notes](native/README.de.md) list the differential and worker-protocol commands and their limits. Local live checks and outstanding long-duration evidence are recorded in the [acceptance status](docs/native-port-2026-11-14.md) (German).

The C# project is retained as a test reference. Its .NET tests and qualification tools do not build or publish the native product. Historical C# measurements remain in the dated reports linked above.

### C# reference tests and historical qualification

The .NET 8 SDK on Windows is required for these C# reference checks. The focused live format regression is opt-in (12 Codex cases, no raw-text logging):

```powershell
$env:CODEX_LANGUAGE_FIX_FORMAT_LIVE_TEST = '1'
dotnet test .\CodexLanguageFix.sln -c Release --filter FullyQualifiedName~LiveFormatRegressionTests
```

```powershell
dotnet test .\CodexLanguageFix.sln
```

The external quality and latency benchmark is intentionally opt-in and requires the official Codex Desktop installation and its ChatGPT OAuth session; Codex Desktop can remain closed. The production qualification script measures `baseline / low / full-v1` on the newest Luna model offered by the installed runtime that supports low reasoning and Fast Mode (`priority`). The [30 September 2026 run](docs/desktop-runtime-2026-09-30.md) passed every safety gate with `gpt-6-luna`; Luna was more accurate, but slower, than LanguageTool on the paired sample. Reports contain no raw text.

```powershell
.\scripts\run-full-luna-qualification.ps1
```

The earlier qualification run on 20 August 2026 measured the then-fixed `gpt-5.6-luna` profile `baseline / none / full-v1`. It passed every safety gate and achieved an F0.5 score of 0.75 versus LanguageTool's 0.25 on the final paired sample. Luna was not faster: its warm p50 was 7.97 seconds versus 0.72 seconds for LanguageTool, about 11.1 times slower. These are dated C# reference measurements, not native results.

The C# reference's standalone executable was written to `src\CodexLanguageFix\bin\Release\net8.0-windows\win-x64\publish\CodexLanguageFix.exe`. Its process-lifetime diagnosis and optional independent-start checks are documented in the [process-lifetime report](docs/process-lifetime-2026-10-10.md). This is not the native build procedure.

## C# reference: supported applications

- OpenAI Codex Desktop for Windows

The C# reference and native app process only supported text fields within Codex Desktop. Other Windows applications, Antigravity, Hermes, and browsers are not supported.

## Legal status

The source code is publicly available, but no open-source license has been granted. Unless an explicit license is added, standard copyright law applies.
