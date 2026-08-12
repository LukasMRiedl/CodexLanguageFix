# Codex Language Fix

**English** · [Deutsch](README.de.md)

A standalone Windows app for one-click prompt correction in OpenAI Codex Desktop and Antigravity. It uses Windows UI Automation and the free public LanguageTool API. No Codex plugin, browser extension, or self-hosted server is required.

> This is an independent project and is not affiliated with OpenAI, Google, or LanguageTool.

## Features

- A subtle `Aa` button dynamically appears inside the active prompt bar.
- One click checks spelling, grammar, punctuation, and style.
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
4. On first launch, acknowledge that prompts are sent to LanguageTool only when you click the correction button.

The app does not require administrator privileges. Its system tray menu lets you pause or exit the app and enable or disable automatic startup with Windows.

The published executable is currently not code-signed. Windows may therefore display a security warning the first time you launch it.

## Privacy

Only after you click the correction button does the app send the current prompt over HTTPS to `https://api.languagetool.org/v2/check`. Prompt text and API responses are never logged. Local diagnostic logs contain only technical metadata such as timestamp, duration, status code, character count, and match count. They are deleted after seven days.

See [PRIVACY.md](PRIVACY.md) for more information.

## Public API limits

The app locally limits usage to 20 requests and 75,000 characters per minute, with no more than 20,000 characters per request. It creates no queue, performs no background checks, and makes no automatic retries.

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
