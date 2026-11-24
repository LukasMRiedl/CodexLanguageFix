# Codex Language Fix

**English** · [Deutsch](README.de.md)

**Native preview 2.1.5 · Windows x64**

Proofread drafts directly in **Codex Desktop** and **LearnHarness**. Click **Aa** or use a keyboard shortcut for spelling, grammar and punctuation corrections. The preview uses the official Codex runtime and your existing Codex sign-in.

| App | Supported fields |
| --- | --- |
| **Codex Desktop for Windows** | Chat composer and supported prose fields |
| **LearnHarness Desktop 0.7.0** | New-session prompts, chat, free-text lesson answers and side questions in the standard Windows installation |

## Use the app

1. Focus a supported draft, then click **Aa** or press <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>,</kbd> (<kbd>AltGr</kbd>+<kbd>,</kbd> on a German keyboard).
2. Review the corrected text in place. The app leaves submission to you.
3. Use **Undo** to restore the draft while the corrected text is still unchanged.

For LearnHarness, run correction before adding embedded attachments or quotations. Use **Settings** in the Windows system tray to change the shortcut or startup behavior.

## Build the preview

The native preview is distributed as source. Build it on Windows with **Visual Studio Build Tools 2022**, the **Desktop development with C++** workload, the Windows SDK and CMake. Codex Desktop and a signed-in ChatGPT account in Codex are required to run corrections.

```powershell
git clone --branch codex/native-ram-20261114 https://github.com/LukasMRiedl/CodexLanguageFix.git
Set-Location CodexLanguageFix
.\scripts\build-native.ps1
```

The script builds the app, runs the native tests and writes the portable package to `dist\native-win-x64`. Run `CodexLanguageFix.exe` from that folder. For the earlier packaged version, see the [v1.4.0 release](https://github.com/LukasMRiedl/CodexLanguageFix/releases/tag/v1.4.0).

## Privacy and development

LearnHarness corrections are sent to Codex only when requested. Codex Desktop users can opt in to automatic preparation for stable drafts. The app protects detected technical text before transmission and keeps drafts and correction results out of its logs. See the [privacy notes](PRIVACY.md).

The native app uses C++20, Win32 and Windows UI Automation. The C# implementation remains a test reference. See the [native build and test notes](native/README.de.md) (German) and [third-party notices](native/THIRD_PARTY_NOTICES.md).

[Report a bug](https://github.com/LukasMRiedl/CodexLanguageFix/issues/new) · [Security policy](SECURITY.md)

Codex Language Fix is an independent project. The project source is publicly viewable under standard copyright.
