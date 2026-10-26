using System.Runtime.InteropServices;
using System.Text;

namespace CodexLanguageFix.Core;

public readonly record struct CorrectionShortcut(uint Modifiers, uint VirtualKey)
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint AllowedModifiers = ModAlt | ModControl | ModShift;

    public static CorrectionShortcut Default { get; } = new(ModControl | ModAlt, 0xBC);

    public bool IsValid
    {
        get
        {
            if ((Modifiers & ~AllowedModifiers) != 0
                || !IsSupportedVirtualKey(VirtualKey)
                || IsModifierKey(VirtualKey)
                || VirtualKey is 0x7B or 0xE5 or 0xE7) // F12, VK_PROCESSKEY, VK_PACKET
            {
                return false;
            }

            var usesControlOrAlt = (Modifiers & (ModControl | ModAlt)) != 0;
            var isFunctionKey = VirtualKey is >= 0x70 and <= 0x87;
            return usesControlOrAlt || isFunctionKey;
        }
    }

    public string GetDisplayText(AppLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        var parts = new List<string>(4);
        if ((Modifiers & ModControl) != 0)
        {
            parts.Add(localizer.IsGerman ? "Strg" : "Ctrl");
        }

        if ((Modifiers & ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((Modifiers & ModShift) != 0)
        {
            parts.Add(localizer.IsGerman ? "Umschalt" : "Shift");
        }

        parts.Add(GetKeyDisplayName(VirtualKey));
        return string.Join("+", parts);
    }

    private static bool IsModifierKey(uint virtualKey) =>
        virtualKey is 0x10 or 0x11 or 0x12 // VK_SHIFT, VK_CONTROL, VK_MENU
            or 0x5B or 0x5C // VK_LWIN, VK_RWIN
            or >= 0xA0 and <= 0xA5; // left/right Shift, Control and Menu

    private static bool IsSupportedVirtualKey(uint virtualKey)
    {
        if (virtualKey > 0xFE)
        {
            return false;
        }

        return virtualKey switch
        {
            0x08 or 0x09 or 0x0C or 0x0D => true,
            >= 0x13 and <= 0x2F => true,
            >= 0x30 and <= 0x39 => true,
            >= 0x41 and <= 0x5A => true,
            0x5D or 0x5F => true,
            >= 0x60 and <= 0x6F => true,
            >= 0x70 and <= 0x87 => true,
            0x90 or 0x91 => true,
            >= 0xA6 and <= 0xB7 => true,
            >= 0xBA and <= 0xC0 => true,
            >= 0xDB and <= 0xE4 => true,
            0xE6 => true,
            >= 0xE9 and <= 0xFE => true,
            _ => false
        };
    }

    private static string GetKeyDisplayName(uint virtualKey)
    {
        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x6F}";
        }

        if (virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        var scanCode = MapVirtualKeyEx(virtualKey, 4, GetKeyboardLayout(0));
        if (scanCode != 0)
        {
            var keyName = new StringBuilder(64);
            var keyNameLParam = unchecked((int)((scanCode & 0xFF) << 16));
            if ((scanCode & 0xFF00) != 0)
            {
                keyNameLParam |= 1 << 24;
            }

            if (GetKeyNameText(keyNameLParam, keyName, keyName.Capacity) > 0)
            {
                return keyName.ToString();
            }
        }

        return $"VK 0x{virtualKey:X2}";
    }

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, nint keyboardLayout);

    [DllImport("user32.dll", EntryPoint = "GetKeyNameTextW", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int lParam, StringBuilder keyName, int maxCount);
}
