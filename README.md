# Codex Language Fix

**English** · [Deutsch](README.de.md)

A standalone Windows app for one-click prompt correction in OpenAI Codex Desktop and Antigravity. Choose between the free public LanguageTool API and GPT-5.6 Luna through the official Codex ChatGPT OAuth session. No Codex plugin, browser extension, OpenAI API key, additional API billing, or self-hosted server is required.

> This is an independent project and is not affiliated with OpenAI, Google, or LanguageTool.

The current source contains the **1.3.1 release candidate** for format-safe insertion and overlay placement. Automated format checks passed for both providers; real-host UI acceptance is still open. See [repair verification](docs/format-repair-2026-09-08.md). It is not yet a fully accepted stable release.

## Features

- A subtle `Aa` button dynamically appears inside the active prompt bar.
- One click checks spelling, grammar, punctuation, and style.
- LanguageTool remains the default provider. OpenAI Luna can be selected from the system tray. Production invokes the qualified `baseline / none / full-v1` profile of `gpt-5.6-luna` with Fast mode; experimental profiles never activate automatically.
- Luna uses the official Codex App Server and its managed ChatGPT OAuth flow. The app never reads or stores OAuth tokens.
- German and English are detected automatically, with `de-DE` and `en-US` as the preferred variants.
- The entire interface is available in English and German. It follows the Windows display language by default and can be switched at any time from the system tray menu.
- Markdown code blocks, inline code, URLs, email addresses, file paths, command-line options, and common code identifiers remain unchanged.
- The clipboard is never read or modified.
- Prompt text is transmitted only after an explicit click.
- Paragraphs, blank lines, indentation, list markers, checkboxes and Markdown line breaks are validated before insertion. Unsafe Luna responses are rejected; unsafe LanguageTool edits are skipped.
- Corrections use verified text ranges, without a typing animation or whole-editor retyping. Undo is restricted to the original editor and exact corrected text.
- The compact button is placed only in a verified free area belonging to a uniquely identified composer. It hides when identification or placement is ambiguous. Positioning uses the target monitor's DPI, including negative monitor coordinates.
- Requests are cancelled when the source editor or text changes, the provider changes, or correction is disabled. Partial write failures never trigger a blind full-text retry; the original remains available in a local recovery window when safe rollback is impossible.
- No background service and no automatic updater.

## Installation

1. Download `CodexLanguageFix-win-x64.zip` from the [latest release](https://github.com/LukasMRiedl/CodexLanguageFix/releases/latest).
2. Extract the ZIP archive to a permanent folder.
3. Run `CodexLanguageFix.exe`.
4. On first launch, acknowledge that prompts are sent to the selected provider only when you click the correction button.

The app does not require administrator privileges. Its system tray menu lets you select the correction provider, connect OpenAI, test the selected provider, pause or exit the app, and change automatic startup.

LanguageTool works without additional software. The optional Luna provider requires an installed, current official Codex runtime and a ChatGPT account available to Codex. If Luna or Fast mode is unavailable, the prompt remains unchanged; Codex Language Fix never silently falls back to another model, mode, or provider.

If the active Codex model catalog deliberately omits Luna's no-reasoning mode, Codex Language Fix creates an app-private catalog copy under `%LOCALAPPDATA%\CodexLanguageFix\luna-runtime` and enables it only for its own App Server process. The global Codex catalog is never modified, so this additional mode does not appear in Codex Desktop.

If the external catalog setting is later removed, an existing valid app-private copy is reused without modification. The app does not import or convert the global model cache; an explicitly broken catalog setting remains an error.

The published executable is currently not code-signed. Windows may therefore display a security warning the first time you launch it.

## Privacy

Only after you click the correction button does the app send the current prompt to the selected provider. LanguageTool requests go over HTTPS to `https://api.languagetool.org/v2/check`. Luna requests use ChatGPT OAuth through the official Codex App Server; the app has no API-key path. Production uses the qualified `baseline / none / full-v1` profile in an ephemeral `gpt-5.6-luna` thread with Fast mode, a read-only/no-network sandbox, and explicit instructions prohibiting tools. The App Server and one empty single-use thread may be prepared without composer content, but no correction or local draft starts while you type. Segment and span-edit protocols as well as `low` and `medium` reasoning remain disabled. Technical spans are replaced locally with random placeholders before Luna sees the text and restored only after strict validation.

Prompt text, corrected text, provider responses, OAuth tokens, email addresses, and account details are never logged. Up to 64 validated Luna results are cached only in memory and discarded when the app exits. Local diagnostic logs contain only technical metadata such as provider, timestamp, duration, status code, character count, and change count. They are deleted after seven days.

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

The external quality and latency benchmark is intentionally opt-in and requires the Codex ChatGPT OAuth session. Its fixed, deterministic four-stage search evaluates all 15 prompt/reasoning combinations, selects one prompt per reasoning level, compares all six output protocols on long texts, and directly verifies the winner against `baseline / none / full-v1`. Search stages use two concurrent Luna requests; the final bilingual LanguageTool comparison runs without competing Luna load and respects LanguageTool's public rate limit. There is no global time cutoff: every stage completes and writes a durable schema-v3 report without raw text. With the observed roughly seven seconds per Luna correction, the qualified run completed 144 planned measurements in 15 minutes 44 seconds.

```powershell
.\scripts\run-full-luna-qualification.ps1
```

The qualification of 20 August 2026 selected `baseline / none / full-v1`. It passed every safety gate and achieved an F0.5 score of 0.75 versus LanguageTool's 0.25 on the final paired sample. Luna was not faster: its warm p50 was 7.97 seconds versus 0.72 seconds for LanguageTool, about 11.1 times slower. Segment and span-edit protocols therefore were not activated. A future profile is described as faster only if both paired p50 and p95 latency gates pass; otherwise maximum safe quality decides the Luna profile.

The resulting self-contained executable is written to:

`src\CodexLanguageFix\bin\Release\net8.0-windows\win-x64\publish\CodexLanguageFix.exe`

## Supported applications

- OpenAI Codex Desktop for Windows
- Antigravity for Windows

Application detection relies on accessible UI structures rather than hard-coded screen coordinates.

## Legal status

The source code is publicly available, but no open-source license has been granted. Unless an explicit license is added, standard copyright law applies.
