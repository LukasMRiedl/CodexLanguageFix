# Privacy

Codex Language Fix processes text only after you explicitly click the `Aa` button or press `AltGr+,` (`Ctrl+Alt+,` on a German layout), or when the optional Luna preparation setting is enabled and the app observes a stable prompt.

## Data transmitted

The current prompt is sent to the provider selected in the system tray when you request a correction. There is also an optional preparation setting that sends prompts to Luna automatically:

- **LanguageTool:** the prompt is sent over HTTPS to the public endpoint at `https://api.languagetool.org/v2/check`. Processing is subject to LanguageTool's privacy terms.
- **OpenAI Luna:** prose is sent through the official Codex App Server using Codex's ChatGPT OAuth session. At server startup, the app selects the newest Luna model offered by the installed official runtime that supports low reasoning and Fast Mode (`priority`), then uses that profile for the runtime. The app resolves it again after a runtime or account-context reset, so corrections do not repeat model-list discovery. Production requests the `baseline / low / full-v1` profile. Every request uses a fresh ephemeral thread. Processing and usage limits are subject to the terms of the ChatGPT account connected to Codex. The official Codex Desktop installation must be present, but Codex Desktop can remain closed while this app runs.
- **Automatic Luna preparation:** the “Prepare Luna corrections” setting in the system tray is off by default. When enabled with Luna selected, the active app sends the current prompt after 1.2 seconds of observed text stability to prepare a correction. This may count toward the usage limits of the ChatGPT account connected to Codex. The app shows “Correction ready” when preparation finishes. The result is inserted only when you click `Aa` or press `AltGr+,`, after checking that the editor and text still match.

## Data not transmitted

Automatic preparation can transmit unsent intermediate drafts whenever typing pauses. Continuing to type cancels the preparation, but cannot undo a transmission that has already occurred.

- Nothing is sent automatically while “Prepare Luna corrections” is off (the default). If it is enabled, no prompt is sent while LanguageTool is selected; with Luna selected, an observed stable prompt is sent after 1.2 seconds to prepare a draft.
- Luna may warm the App Server and one empty thread without transmitting composer content.
- The clipboard is never read or modified.
- During insertion, the original and verified changes are held in memory for safe rollback. No original-text recovery window opens. These texts are not written to logs or recovery files.
- Plugins and configured MCP servers are disabled for the app's correction threads. This does not change the user's global Codex configuration or other apps.
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
