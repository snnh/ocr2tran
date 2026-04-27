namespace Ocr2Tran.Windows;

public static class HotkeyParser
{
    public static HotkeySpec Parse(string value)
    {
        var modifiers = HotkeyModifiers.NoRepeat;
        Keys key = Keys.None;

        foreach (var raw in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = raw.Trim();
            if (token.Equals("ctrl", StringComparison.OrdinalIgnoreCase) || token.Equals("control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Control;
            }
            else if (token.Equals("alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Alt;
            }
            else if (token.Equals("shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Shift;
            }
            else if (token.Equals("win", StringComparison.OrdinalIgnoreCase) || token.Equals("windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Win;
            }
            else if (Enum.TryParse(token, ignoreCase: true, out Keys parsed))
            {
                key = parsed;
            }
            else if (token.Length == 1)
            {
                key = (Keys)char.ToUpperInvariant(token[0]);
            }
        }

        if (key == Keys.None)
        {
            throw new FormatException($"Invalid hotkey: {value}");
        }

        return new HotkeySpec(modifiers, key);
    }
}
