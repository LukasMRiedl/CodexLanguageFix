# Privacy

Codex Language Fix processes text only after you explicitly click the `Aa` button or press your configured correction shortcut (default: `AltGr+,` / `Ctrl+Alt+,` on a German layout), or when the optional Luna preparation setting is enabled and the app observes a stable prompt.

## Data transmitted

Codex is the only correction provider. There are no LanguageTool requests or provider fallbacks. During migration, automatic preparation remains enabled only for a previously selected Codex provider with preparation enabled. Legacy LanguageTool, missing or invalid provider settings disable it. New settings explicitly identify schema version 2 so the preparation choice can be saved and restored.

The current prompt is sent to Codex when you request a correction. There is also an optional preparation setting that sends prompts to Luna automatically:

- **OpenAI Luna:** prose is sent through the official Codex App Server using Codex's ChatGPT OAuth session. At server startup, the app selects the newest Luna model offered by the installed official runtime that supports low reasoning and Fast Mode (`priority`), then uses that profile for the runtime. The app resolves it again after a runtime or account-context reset, so corrections do not repeat model-list discovery. Production requests the `baseline / low / full-v1` profile. Every request uses a fresh ephemeral thread. Processing and usage limits are subject to the terms of the ChatGPT account connected to Codex. The official Codex Desktop installation must be present, but Codex Desktop can remain closed while this app runs.
- **Automatic Luna preparation:** the “Prepare Codex corrections” setting in the system tray is off by default. When enabled, the active app sends the current prompt after 1.2 seconds of observed text stability to prepare a correction. This may count toward the usage limits of the ChatGPT account connected to Codex. The app shows “Correction ready” when preparation finishes. The result is inserted only when you click `Aa` or use the configured shortcut, after checking that the editor and text still match.

## Data not transmitted

Automatic preparation can transmit unsent intermediate drafts whenever typing pauses. Continuing to type cancels the preparation, but cannot undo a transmission that has already occurred.

- Nothing is sent automatically while “Prepare Codex corrections” is off (the default). If enabled, an observed stable prompt is sent to Codex after 1.2 seconds to prepare a draft.
- Luna may warm the App Server and one empty thread without transmitting composer content.
- The clipboard is never read or modified.
- During insertion, the original and verified changes are held in memory for safe rollback. No original-text recovery window opens. These texts are not written to logs or recovery files.
- Plugins and configured MCP servers are disabled for the app's correction threads. This does not change the user's global Codex configuration or other apps.
- The app has no OpenAI API-key path and never reads, stores, or logs OAuth tokens. OAuth is owned and refreshed by the official Codex runtime; Luna therefore causes no separate OpenAI API-key billing through this app.
- With Luna, code, URLs, email addresses, link targets, file paths, command-line options, and code identifiers are replaced locally with random opaque placeholders. Their original contents are not sent to Luna.
- Up to 64 validated Luna corrections are cached only in process memory. This cache is never written to disk and is discarded when the app exits.

## Local logs

The native implementation does not write diagnostic log files. Its latest status remains in memory and is available in Settings. Prompt text, corrected text, provider responses, OAuth tokens, email addresses, and account details are not logged. Explicit test probes expose fixed technical events and numeric counters without editor content or account data.

The retained C# reference can write technical diagnostic logs under the local application-data folder and deletes them after seven days. Existing files from that reference are not part of the native package.

Opt-in benchmark reports likewise contain only technical timings, hashes, aggregate quality results, protocol metadata, and pass/fail states. They do not contain prompt text, corrected text, or provider responses.

The app does not offer an OpenAI logout command because the OAuth session is shared with the official Codex installation. Sign-out remains under Codex's control.

## Uninstallation

Exit the app from its system tray menu, then delete the application folder. Settings and diagnostic logs are stored under `%LOCALAPPDATA%\CodexLanguageFix` and can be deleted separately.

The shortcut recorder handles key combinations only while its manually opened recording UI is active. Only the selected modifiers and virtual-key code are stored in the existing local settings file. This does not add a global keyboard hook or a keystroke log. The app does not display automatic Windows notification balloons; its latest status is kept in memory and is available in the manually opened Settings dialog.

## Codex Desktop field access

The native app and its Codex-only reference version 1.5.4 read only supported text fields within Codex Desktop. They do not inspect general Windows text fields, browsers, Antigravity, or Hermes. The correction and optional preparation behavior described above applies only to Codex Desktop. Version 2.0.0 was installed locally after functional checks; at the user's explicit request, full long-duration resource acceptance remains deferred.

Codex UI Automation runs in a separate local child process. Field text and verified changes cross private redirected process streams and remain in memory. Native UI Automation objects stay inside that process. Diagnostic phase markers use a fixed set of technical names and contain no field text. Failed or interrupted writes are not automatically replayed.

## Historical Windows and Chrome development candidates

Earlier unreleased candidates explored general Windows text fields and browser pages, including an optional Chrome extension. Those features are not part of version 1.5.4. The desktop app no longer starts a browser bridge. The verified local deactivation is recorded in the [verification report](docs/codex-only-2026-11-13.md).

The former extension used Chrome Native Messaging and a current-user-only named pipe. Its text handling, preparation, undo/recovery behavior, supported-page limits, and test descriptions in [`browser-extension/README.md`](browser-extension/README.md) describe that historical implementation only. The earlier acceptance report at [`docs/windows-chrome-1.6.0.md`](docs/windows-chrome-1.6.0.md) likewise records the development state at that time and is not a current support statement.
