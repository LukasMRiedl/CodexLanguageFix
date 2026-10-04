# Codex Language Fix

**English** · [Deutsch](README.de.md)

A standalone Windows app for one-click prompt correction in OpenAI Codex Desktop, Antigravity, and Hermes. Choose between the free public LanguageTool API and Luna through the official Codex ChatGPT OAuth session. No Codex plugin, browser extension, OpenAI API key, additional API billing, or self-hosted server is required.

> This is an independent project and is not affiliated with OpenAI, Google, or LanguageTool.

The current source is **version 1.4.0**. It selects the newest Luna model offered by the installed Codex runtime that supports low reasoning with Fast Mode (`priority`). The `low` production profile passed live qualification with `gpt-6-luna`, 128 corpus cases and unchanged safety gates; see the [runtime report](docs/desktop-runtime-2026-09-30.md) for evidence and limitations. The earlier RC7 memory comparison measured approximately 49% less private memory and 43% less working set, short of the **80% target**. See the [memory report](docs/ram-optimization-2026-09-13.md) for its evidence and limitations. Version 1.4.0 is available as a stable GitHub release.

## Features

- A subtle `Aa` button dynamically appears inside the active prompt bar.
- One click checks spelling, grammar, punctuation, and style.
- LanguageTool remains the default provider. OpenAI Luna can be selected from the system tray. Production requests the `baseline / low / full-v1` profile with Fast Mode (`priority`) on the newest compatible model offered by the installed Codex runtime. This profile was tested live with `gpt-6-luna` on 30 September 2026. Experimental profiles never activate automatically.
- Luna uses the official Codex App Server and its managed ChatGPT OAuth flow. The app never reads or stores OAuth tokens.
- German and English are detected automatically, with `de-DE` and `en-US` as the preferred variants.
- The entire interface is available in English and German. It follows the Windows display language by default and can be switched at any time from the system tray menu.
- Markdown code blocks, inline code, URLs, email addresses, file paths, command-line options, and common code identifiers remain unchanged.
- The clipboard is never read or modified.
- Prompt text is transmitted only after an explicit click.
- Paragraphs, blank lines, indentation, list markers, checkboxes and Markdown line breaks are validated before insertion. Unsafe Luna responses are rejected; unsafe LanguageTool edits are skipped.
- Corrections use verified text ranges, without a typing animation or whole-editor retyping. Undo is restricted to the original editor and exact corrected text.
- The compact button is placed only in a verified free area belonging to a uniquely identified composer. It hides when identification or placement is ambiguous. Positioning uses the target monitor's DPI, including negative monitor coordinates.
- Requests are cancelled when the source editor or text changes, the provider changes, or correction is disabled. Partial write failures never trigger a blind full-text retry. Verified own changes are rolled back when safe; no recovery window opens.
- No background service and no automatic updater.

The expanded Aa field recognition and placement are documented in the [field diagnosis report](docs/aa-coverage-2026-10-04.md). Main-chat correction and exact undo were verified live in Codex and Hermes, including real monitor transitions between 175% and 200%. Version 1.4.0 is installed locally; additional inaccessible field families are explicitly listed as unverified in the report.

## Installation

1. Download `CodexLanguageFix-win-x64.zip` from the [latest release](https://github.com/LukasMRiedl/CodexLanguageFix/releases/latest).
2. Extract the ZIP archive to a permanent folder.
3. Run `CodexLanguageFix.exe`.
4. On first launch, acknowledge that prompts are sent to the selected provider only when you click the correction button.

The app does not require administrator privileges. Its system tray menu lets you select the correction provider, connect OpenAI, test the selected provider, pause or exit the app, and change automatic startup.

LanguageTool works without additional software. The optional Luna provider requires the official Codex Desktop installation and a ChatGPT account available to Codex; Codex Desktop can remain closed while the app runs. At App Server startup, the app selects the newest Luna model offered by the installed official runtime that supports low reasoning and Fast Mode (`priority`). It keeps this selection for the runtime and resolves it again after the runtime or account context resets, so corrections do not repeat model-list discovery. If no compatible model is available, the prompt remains unchanged; Codex Language Fix never silently falls back to another mode or provider.

The published executable is currently not code-signed. Windows may therefore display a security warning the first time you launch it.

## Privacy

Only after you click the correction button does the app send the current prompt to the selected provider. LanguageTool requests go over HTTPS to `https://api.languagetool.org/v2/check`. Luna requests use ChatGPT OAuth through the official Codex App Server; the app has no API-key path. At App Server startup, the app selects the newest Luna model offered by the installed official runtime that supports low reasoning and Fast Mode (`priority`), then keeps that profile for the server runtime. It resolves the profile again after a runtime or account-context reset, so corrections do not repeat model-list discovery. Production requests `baseline / low / full-v1`. Each correction uses an ephemeral thread with a read-only/no-network sandbox and explicit instructions prohibiting tools. The App Server and one empty single-use thread may be prepared without composer content, but no correction or local draft starts while you type. Segment and span-edit protocols as well as `medium` reasoning remain disabled. Technical spans are replaced locally with random placeholders before Luna sees the text and restored only after strict validation.

Prompt text, corrected text, provider responses, OAuth tokens, email addresses, and account details are never recorded in production logs. Up to 64 validated Luna results are cached only in memory and discarded when the app exits. Local diagnostic logs contain only technical metadata such as provider, timestamp, duration, status code, character count, and change count. They are deleted after seven days. Explicitly enabled UI diagnostics may output only pre-verified synthetic fixture data.

See [PRIVACY.md](PRIVACY.md) for more information.

## Provider limits

LanguageTool is locally limited to 20 requests and 75,000 characters per minute. Both providers accept at most 20,000 characters per correction. The app creates no queue and performs no background corrections. Luna usage is subject to the limits of the ChatGPT account connected to Codex.

## Build and test

The .NET 8 SDK on Windows is required.

The focused live format regression is opt-in (12 cases per provider, no raw-text logging):

```powershell
$env:CODEX_LANGUAGE_FIX_FORMAT_LIVE_TEST = '1'
dotnet test .\CodexLanguageFix.sln -c Release --filter FullyQualifiedName~LiveFormatRegressionTests
```

```powershell
dotnet test .\CodexLanguageFix.sln
dotnet publish .\src\CodexLanguageFix\CodexLanguageFix.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

The external quality and latency benchmark is intentionally opt-in and requires the official Codex Desktop installation and its ChatGPT OAuth session; Codex Desktop can remain closed. The production qualification script measures only `baseline / low / full-v1` on the newest Luna model offered by the installed runtime that supports low reasoning and Fast Mode (`priority`). It records the selected model in its schema-v3 report and includes no fallback to unsupported reasoning levels or protocols. The [30 September 2026 run](docs/desktop-runtime-2026-09-30.md) passed every safety gate with `gpt-6-luna`; Luna was more accurate, but slower, than LanguageTool on the paired sample. Exploratory matrix settings are checked against the same runtime selection and unsupported efforts fail closed. Reports contain no raw text.

```powershell
.\scripts\run-full-luna-qualification.ps1
```

The historical qualification run of 20 August 2026 measured the then-fixed `gpt-5.6-luna` profile `baseline / none / full-v1`. It passed every safety gate and achieved an F0.5 score of 0.75 versus LanguageTool's 0.25 on the final paired sample. Luna was not faster: its warm p50 was 7.97 seconds versus 0.72 seconds for LanguageTool, about 11.1 times slower. Segment and span-edit protocols therefore were not activated. These measurements describe that dated run, not a hard-coded model choice in the current app.

The resulting self-contained executable is written to:

`src\CodexLanguageFix\bin\Release\net8.0-windows\win-x64\publish\CodexLanguageFix.exe`

## Supported applications

- OpenAI Codex Desktop for Windows
- Antigravity for Windows
- Hermes for Windows

Application detection relies on accessible UI structures rather than hard-coded screen coordinates.

## Legal status

The source code is publicly available, but no open-source license has been granted. Unless an explicit license is added, standard copyright law applies.
