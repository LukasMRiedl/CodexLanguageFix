# Privacy

These notes describe the **native Windows preview** of Codex Language Fix. See the [v1.4.0 privacy notes](https://github.com/LukasMRiedl/CodexLanguageFix/blob/v1.4.0/PRIVACY.md) for the earlier published release.

## When text is read and sent

A local Windows UI Automation worker reads supported draft fields in Codex Desktop and LearnHarness. Field text and verified edits pass between the app and this worker over private process streams.

- **Manual correction.** Click **Aa** or use the configured shortcut to send the active draft to Codex. LearnHarness uses manual correction exclusively.
- **Optional preparation in Codex Desktop.** With **Prepare Codex corrections** enabled, the app may send an unsent draft after 1.2 seconds of text stability. This setting is off by default. You still apply the prepared result manually. Continuing to type can cancel pending work, but cannot undo a transmission that has already taken place.
- **Authentication.** Corrections use the official Codex App Server and the ChatGPT account already connected to Codex. The runtime handles OAuth. Requests may count against that account's usage limits.

Recognized code, URLs, paths and other technical spans are replaced with opaque placeholders locally and restored after validation. Correction threads are ephemeral and use a read-only sandbox with network and tools disabled.

## What stays on the device

- Drafts, results and rollback data stay in process memory outside the correction request. The native app writes no diagnostic logs containing draft text, responses or credentials. The in-memory cache is cleared on exit.
- The app leaves the Windows clipboard untouched. It checks the editor and draft again before inserting a change. LearnHarness fields with embedded attachments or quotations are excluded.
- Settings such as the shortcut and startup preference are kept under `%LOCALAPPDATA%\CodexLanguageFix`. Turn off startup in the tray menu before exiting and deleting the app folder. You can remove local settings separately.

Use Codex Desktop to manage or sign out of the shared Codex account.
