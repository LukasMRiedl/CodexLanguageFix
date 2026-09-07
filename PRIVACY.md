# Privacy

Codex Language Fix processes text only after you explicitly click the `Aa` button.

## Data transmitted

The current prompt is sent only to the provider selected in the system tray:

- **LanguageTool:** the prompt is sent over HTTPS to the public endpoint at `https://api.languagetool.org/v2/check`. Processing is subject to LanguageTool's privacy terms.
- **OpenAI Luna:** prose is sent through the official Codex App Server to `gpt-5.6-luna` using Codex's ChatGPT OAuth session. The qualified production profile is `baseline / none / full-v1` with Fast mode, and every request uses a fresh ephemeral thread. Processing and usage limits are subject to the terms of the ChatGPT account connected to Codex.

## Data not transmitted

- No prompt is transmitted and no correction or local draft starts unless you click the correction button.
- Luna may warm the App Server and one empty thread without transmitting composer content.
- The clipboard is never read or modified.
- During insertion, the original and verified changes are held in memory for safe rollback. If an uncertain partial write cannot be rolled back without risking user edits, a local read-only recovery window retains the original. These texts are not written to logs or recovery files.
- The app has no OpenAI API-key path and never reads, stores, or logs OAuth tokens. OAuth is owned and refreshed by the official Codex runtime; Luna therefore causes no separate OpenAI API-key billing through this app.
- With LanguageTool, technical sections are protected from correction but remain part of the annotated request.
- With Luna, code, URLs, email addresses, link targets, file paths, command-line options, and code identifiers are replaced locally with random opaque placeholders. Their original contents are not sent to Luna.
- Up to 64 validated Luna corrections are cached only in process memory. This cache is never written to disk and is discarded when the app exits.

## Local logs

Prompt text, corrected text, provider responses, OAuth tokens, email addresses, and account details are never logged. Diagnostic logs contain only the provider, timestamp, operation type, duration, optional HTTP status code, character count, and change count. These files are deleted after seven days.

Opt-in benchmark reports likewise contain only technical timings, hashes, aggregate quality results, protocol metadata, and pass/fail states. They do not contain prompt text, corrected text, or provider responses.

The app does not offer an OpenAI logout command because the OAuth session is shared with the official Codex installation. Sign-out remains under Codex's control.

## Uninstallation

Exit the app from its system tray menu, then delete the application folder. Settings and diagnostic logs are stored under `%LOCALAPPDATA%\CodexLanguageFix` and can be deleted separately.
