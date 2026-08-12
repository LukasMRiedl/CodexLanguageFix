# Privacy

Codex Language Fix processes text only after you explicitly click the `Aa` button.

## Data transmitted

The current prompt is sent over HTTPS to the public LanguageTool endpoint at `https://api.languagetool.org/v2/check`. Processing is subject to the privacy terms of the LanguageTool service provider.

## Data not transmitted

- No prompt is transmitted unless you click the correction button.
- The clipboard is never read or modified.
- The app does not transmit credentials or API keys.
- Technical sections such as code, URLs, and file paths are protected from correction, but they remain part of the complete text sent to LanguageTool.

## Local logs

Prompt text, corrected text, and API responses are never logged. Diagnostic logs contain only the timestamp, operation type, duration, HTTP status code, character count, and match count. These files are deleted after seven days.

## Uninstallation

Exit the app from its system tray menu, then delete the application folder. Settings and diagnostic logs are stored under `%LOCALAPPDATA%\CodexLanguageFix` and can be deleted separately.
