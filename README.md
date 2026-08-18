# Codex Language Fix

**English** · [Deutsch](README.de.md)

A standalone Windows app for one-click prompt correction in OpenAI Codex Desktop and Antigravity. Choose between the free public LanguageTool API and GPT-5.6 Luna with Low reasoning through the official Codex ChatGPT OAuth session. No Codex plugin, browser extension, API key, or self-hosted server is required.

> This is an independent project and is not affiliated with OpenAI, Google, or LanguageTool.

## Features

- A subtle `Aa` button dynamically appears inside the active prompt bar.
- One click checks spelling, grammar, punctuation, and style.
- LanguageTool remains the default provider. OpenAI Luna can be selected from the system tray and is always invoked as `gpt-5.6-luna` with Low reasoning.
- Luna uses the official Codex App Server and its managed ChatGPT OAuth flow. The app never reads or stores OAuth tokens.
- German and English are detected automatically, with `de-DE` and `en-US` as the preferred variants.
- The entire interface is available in English and German. It follows the Windows display language by default and can be switched at any time from the system tray menu.
- Markdown code blocks, inline code, URLs, email addresses, file paths, command-line options, and common code identifiers remain unchanged.
- The clipboard is never read or modified.
- Prompt text is transmitted only after an explicit click.
- Corrections can be undone immediately.
- Different window widths, DPI scaling levels, and Chat, Work, and Plan layouts are detected dynamically.
- No background service and no automatic updater.

## Installation

1. Download `CodexLanguageFix-win-x64.zip` from the [latest release](https://github.com/LukasMRiedl/CodexLanguageFix/releases/latest).
2. Extract the ZIP archive to a permanent folder.
3. Run `CodexLanguageFix.exe`.
4. On first launch, acknowledge that prompts are sent to the selected provider only when you click the correction button.

The app does not require administrator privileges. Its system tray menu lets you select the correction provider, connect OpenAI, test the selected provider, pause or exit the app, and change automatic startup.

LanguageTool works without additional software. The optional Luna provider requires an installed, current official Codex runtime and a ChatGPT account available to Codex. If Luna Low is not enabled in Codex, the prompt remains unchanged; Codex Language Fix never silently falls back to another model, reasoning level, or provider.

The published executable is currently not code-signed. Windows may therefore display a security warning the first time you launch it.

## Privacy

Only after you click the correction button does the app send the current prompt to the selected provider. LanguageTool requests go over HTTPS to `https://api.languagetool.org/v2/check`. Luna requests use an ephemeral Codex App Server thread with `gpt-5.6-luna`, Low reasoning, a read-only/no-network sandbox, and explicit instructions prohibiting tools. Any observed tool activity rejects the complete response. Technical spans are replaced locally with random placeholders before Luna sees the text and restored only after strict validation.

Prompt text, corrected text, provider responses, OAuth tokens, email addresses, and account details are never logged. Local diagnostic logs contain only technical metadata such as provider, timestamp, duration, status code, character count, and change count. They are deleted after seven days.

See [PRIVACY.md](PRIVACY.md) for more information.

## Provider limits

LanguageTool is locally limited to 20 requests and 75,000 characters per minute. Both providers accept at most 20,000 characters per correction. The app creates no queue and performs no background corrections. Luna usage is subject to the limits of the ChatGPT account connected to Codex.

## Build and test

The .NET 8 SDK on Windows is required.

```powershell
dotnet test .\CodexLanguageFix.sln
dotnet publish .\src\CodexLanguageFix\CodexLanguageFix.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

The resulting self-contained executable is written to:

`src\CodexLanguageFix\bin\Release\net8.0-windows\win-x64\publish\CodexLanguageFix.exe`

## Supported applications

- OpenAI Codex Desktop for Windows
- Antigravity for Windows

Application detection relies on accessible UI structures rather than hard-coded screen coordinates.

## Legal status

The source code is publicly available, but no open-source license has been granted. Unless an explicit license is added, standard copyright law applies.
