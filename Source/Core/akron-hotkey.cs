using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.Akron;

internal readonly struct AkronHotkey {
    private const uint KeyEventKeyUp = 0x0002;
    private readonly Keys[] keys;

    private AkronHotkey(IEnumerable<Keys> keys) {
        this.keys = NormalizeKeys(keys).ToArray();
    }

    public IReadOnlyList<Keys> KeyList => keys ?? Array.Empty<Keys>();
    public bool IsValid => KeyList.Count > 0 && KeyList.Any(key => !IsModifierKey(key));

    public static bool TryFromKeyboardState(Keys[] pressedKeys, out AkronHotkey hotkey) {
        hotkey = new AkronHotkey(pressedKeys ?? Array.Empty<Keys>());
        return hotkey.IsValid;
    }

    public static bool TryParse(string value, out AkronHotkey hotkey) {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        List<Keys> parsedKeys = new List<Keys>();
        foreach (string rawPart in value.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)) {
            if (!TryParseKey(rawPart.Trim(), out Keys key)) {
                return false;
            }

            parsedKeys.Add(key);
        }

        hotkey = new AkronHotkey(parsedKeys);
        return hotkey.IsValid;
    }

    public static string Describe(string value) {
        return TryParse(value, out AkronHotkey hotkey) ? hotkey.ToDisplayString() : "Unbound";
    }

    public bool TrySend(out string error) {
        error = string.Empty;
        if (!IsValid) {
            error = "set the same hotkey you use for Discord Toggle Deafen first.";
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return TrySendWindows(out error);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            return TrySendLinux(out error);
        }

        error = "Auto Deafen hotkey injection is only implemented on Windows and Linux.";
        return false;
    }

    public string ToStorageString() {
        return string.Join("+", KeyList.Select(key => key.ToString()));
    }

    public string ToDisplayString() {
        return string.Join("+", KeyList.Select(SimplifyKeyToken));
    }

    private bool TrySendWindows(out string error) {
        error = string.Empty;
        List<byte> virtualKeys = new List<byte>();
        foreach (Keys key in KeyList) {
            if (!TryGetWindowsVirtualKey(key, out byte virtualKey)) {
                error = "Auto Deafen cannot send unsupported key " + key + " on Windows.";
                return false;
            }

            virtualKeys.Add(virtualKey);
        }

        foreach (byte virtualKey in virtualKeys) {
            keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        }

        for (int index = virtualKeys.Count - 1; index >= 0; index--) {
            keybd_event(virtualKeys[index], 0, KeyEventKeyUp, UIntPtr.Zero);
        }

        return true;
    }

    private bool TrySendLinux(out string error) {
        error = string.Empty;
        List<string> xdotoolTokens = new List<string>();
        List<string> x11KeySymNames = new List<string>();
        foreach (Keys key in KeyList) {
            if (!TryGetXdotoolKey(key, out string xdotoolToken) ||
                !TryGetX11KeySymName(key, out string x11KeySymName)) {
                error = "Auto Deafen cannot send unsupported key " + key + " through xdotool.";
                return false;
            }

            xdotoolTokens.Add(xdotoolToken);
            x11KeySymNames.Add(x11KeySymName);
        }

        if (TrySendLinuxXTest(x11KeySymNames, out string xtestError)) {
            return true;
        }

        string combo = string.Join("+", xdotoolTokens);
        List<string> processErrors = new List<string>();
        if (TryRunHotkeyProcess("/usr/bin/flatpak-spawn", new[] { "--host", "xdotool", "key", "--clearmodifiers", combo }, out string flatpakSpawnPathError)) {
            return true;
        }
        processErrors.Add("/usr/bin/flatpak-spawn: " + flatpakSpawnPathError);

        if (TryRunHotkeyProcess("flatpak-spawn", new[] { "--host", "xdotool", "key", "--clearmodifiers", combo }, out string flatpakSpawnError)) {
            return true;
        }
        processErrors.Add("flatpak-spawn: " + flatpakSpawnError);

        if (TryRunHotkeyProcess("/usr/bin/xdotool", new[] { "key", "--clearmodifiers", combo }, out string xdotoolPathError)) {
            return true;
        }
        processErrors.Add("/usr/bin/xdotool: " + xdotoolPathError);

        if (TryRunHotkeyProcess("xdotool", new[] { "key", "--clearmodifiers", combo }, out string xdotoolError)) {
            return true;
        }
        processErrors.Add("xdotool: " + xdotoolError);

        error = "Linux hotkey injection failed. XTest: " + xtestError + " Fallbacks: " + string.Join(" | ", processErrors);
        return false;
    }

    private static bool TrySendLinuxXTest(IReadOnlyList<string> keySymNames, out string error) {
        error = string.Empty;
        IntPtr display = IntPtr.Zero;
        try {
            display = XOpenDisplay(null);
            if (display == IntPtr.Zero) {
                display = XOpenDisplay(Environment.GetEnvironmentVariable("DISPLAY"));
            }

            if (display == IntPtr.Zero) {
                display = XOpenDisplay(":0");
            }

            if (display == IntPtr.Zero) {
                error = "could not open the X display.";
                return false;
            }

            List<uint> keyCodes = new List<uint>();
            foreach (string keySymName in keySymNames) {
                UIntPtr keySym = XStringToKeysym(keySymName);
                if (keySym == UIntPtr.Zero) {
                    error = "unknown X keysym " + keySymName + ".";
                    return false;
                }

                byte keyCode = XKeysymToKeycode(display, keySym);
                if (keyCode == 0) {
                    error = "X keysym " + keySymName + " has no keycode.";
                    return false;
                }

                keyCodes.Add(keyCode);
            }

            foreach (uint keyCode in keyCodes) {
                SendLinuxXTestKey(display, keyCode, true);
            }

            Thread.Sleep(30);

            for (int index = keyCodes.Count - 1; index >= 0; index--) {
                SendLinuxXTestKey(display, keyCodes[index], false);
            }

            XSync(display, false);
            return true;
        } catch (Exception exception) when (
            exception is DllNotFoundException ||
            exception is EntryPointNotFoundException ||
            exception is BadImageFormatException ||
            exception is InvalidOperationException) {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        } finally {
            if (display != IntPtr.Zero) {
                XCloseDisplay(display);
            }
        }
    }

    private static void SendLinuxXTestKey(IntPtr display, uint keyCode, bool isPress) {
        // Discord/Vesktop's global shortcut listener can miss a tight press burst
        // where modifiers and the regular key are only flushed once. Sync each
        // synthetic event so the X server updates modifier state before the next key.
        XTestFakeKeyEvent(display, keyCode, isPress, UIntPtr.Zero);
        XSync(display, false);
        Thread.Sleep(20);
    }

    private static bool TryRunHotkeyProcess(string fileName, IReadOnlyList<string> arguments, out string error) {
        error = string.Empty;
        try {
            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (string argument in arguments) {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo);
            if (process == null) {
                error = "process did not start.";
                return false;
            }

            if (!process.WaitForExit(2000)) {
                error = "timed out.";
                return false;
            }

            if (process.ExitCode == 0) {
                return true;
            }

            string stderr = process.StandardError.ReadToEnd().Trim();
            string stdout = process.StandardOutput.ReadToEnd().Trim();
            error = stderr.Length > 0 ? stderr : (stdout.Length > 0 ? stdout : "exit " + process.ExitCode.ToString());
            return false;
        } catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception) {
            error = exception.Message;
            return false;
        }
    }

    private static IEnumerable<Keys> NormalizeKeys(IEnumerable<Keys> source) {
        HashSet<Keys> seen = new HashSet<Keys>();
        List<Keys> modifiers = new List<Keys>();
        List<Keys> regular = new List<Keys>();
        foreach (Keys key in source) {
            Keys normalized = key;
            if (normalized == Keys.None || !seen.Add(normalized)) {
                continue;
            }

            if (IsModifierKey(normalized)) {
                modifiers.Add(normalized);
            } else {
                regular.Add(normalized);
            }
        }

        modifiers.Sort((left, right) => ModifierRank(left).CompareTo(ModifierRank(right)));
        regular.Sort();
        return modifiers.Concat(regular);
    }

    private static bool TryParseKey(string value, out Keys key) {
        key = Keys.None;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        switch (value.Trim().ToLowerInvariant()) {
            case "ctrl":
            case "control":
            case "leftcontrol":
                key = Keys.LeftControl;
                return true;
            case "rightcontrol":
                key = Keys.RightControl;
                return true;
            case "alt":
            case "leftalt":
                key = Keys.LeftAlt;
                return true;
            case "rightalt":
                key = Keys.RightAlt;
                return true;
            case "shift":
            case "leftshift":
            case "mayus":
                key = Keys.LeftShift;
                return true;
            case "rightshift":
                key = Keys.RightShift;
                return true;
            case "space":
                key = Keys.Space;
                return true;
            case "plus":
            case "oemplus":
                key = Keys.OemPlus;
                return true;
            case "minus":
            case "oemminus":
                key = Keys.OemMinus;
                return true;
            case "comma":
                key = Keys.OemComma;
                return true;
            case "period":
                key = Keys.OemPeriod;
                return true;
            case "slash":
                key = Keys.OemQuestion;
                return true;
            case "backslash":
                key = Keys.OemPipe;
                return true;
            case "semicolon":
                key = Keys.OemSemicolon;
                return true;
            case "quote":
            case "apostrophe":
                key = Keys.OemQuotes;
                return true;
            case "tilde":
            case "grave":
                key = Keys.OemTilde;
                return true;
            case "leftbracket":
                key = Keys.OemOpenBrackets;
                return true;
            case "rightbracket":
                key = Keys.OemCloseBrackets;
                return true;
        }

        if (value.Length == 1) {
            char character = char.ToUpperInvariant(value[0]);
            if (character >= 'A' && character <= 'Z') {
                key = Keys.A + (character - 'A');
                return true;
            }

            if (character >= '0' && character <= '9') {
                key = Keys.D0 + (character - '0');
                return true;
            }
        }

        return Enum.TryParse(value, ignoreCase: true, out key) && key != Keys.None;
    }

    private static bool TryGetWindowsVirtualKey(Keys key, out byte virtualKey) {
        virtualKey = key switch {
            Keys.LeftControl => 0xA2,
            Keys.RightControl => 0xA3,
            Keys.LeftAlt => 0xA4,
            Keys.RightAlt => 0xA5,
            Keys.LeftShift => 0xA0,
            Keys.RightShift => 0xA1,
            Keys.OemSemicolon => 0xBA,
            Keys.OemPlus => 0xBB,
            Keys.OemComma => 0xBC,
            Keys.OemMinus => 0xBD,
            Keys.OemPeriod => 0xBE,
            Keys.OemQuestion => 0xBF,
            Keys.OemTilde => 0xC0,
            Keys.OemOpenBrackets => 0xDB,
            Keys.OemPipe => 0xDC,
            Keys.OemCloseBrackets => 0xDD,
            Keys.OemQuotes => 0xDE,
            _ => (byte) key
        };

        return virtualKey != 0;
    }

    private static bool TryGetXdotoolKey(Keys key, out string token) {
        token = key switch {
            Keys.LeftControl => "ctrl",
            Keys.RightControl => "Control_R",
            Keys.LeftAlt => "alt",
            Keys.RightAlt => "Alt_R",
            Keys.LeftShift => "shift",
            Keys.RightShift => "Shift_R",
            >= Keys.A and <= Keys.Z => ((char) ('a' + (key - Keys.A))).ToString(),
            >= Keys.D0 and <= Keys.D9 => ((char) ('0' + (key - Keys.D0))).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => "KP_" + (key - Keys.NumPad0),
            >= Keys.F1 and <= Keys.F24 => "F" + (key - Keys.F1 + 1),
            Keys.Space => "space",
            Keys.Tab => "Tab",
            Keys.Enter => "Return",
            Keys.Escape => "Escape",
            Keys.Back => "BackSpace",
            Keys.Delete => "Delete",
            Keys.Insert => "Insert",
            Keys.Home => "Home",
            Keys.End => "End",
            Keys.PageUp => "Page_Up",
            Keys.PageDown => "Page_Down",
            Keys.Left => "Left",
            Keys.Right => "Right",
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.OemSemicolon => "semicolon",
            Keys.OemPlus => "plus",
            Keys.OemComma => "comma",
            Keys.OemMinus => "minus",
            Keys.OemPeriod => "period",
            Keys.OemQuestion => "slash",
            Keys.OemTilde => "grave",
            Keys.OemOpenBrackets => "bracketleft",
            Keys.OemPipe => "backslash",
            Keys.OemCloseBrackets => "bracketright",
            Keys.OemQuotes => "apostrophe",
            _ => string.Empty
        };

        return token.Length > 0;
    }

    private static bool TryGetX11KeySymName(Keys key, out string token) {
        token = key switch {
            Keys.LeftControl => "Control_L",
            Keys.RightControl => "Control_R",
            Keys.LeftAlt => "Alt_L",
            Keys.RightAlt => "Alt_R",
            Keys.LeftShift => "Shift_L",
            Keys.RightShift => "Shift_R",
            >= Keys.A and <= Keys.Z => ((char) ('a' + (key - Keys.A))).ToString(),
            >= Keys.D0 and <= Keys.D9 => ((char) ('0' + (key - Keys.D0))).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => "KP_" + (key - Keys.NumPad0),
            >= Keys.F1 and <= Keys.F24 => "F" + (key - Keys.F1 + 1),
            Keys.Space => "space",
            Keys.Tab => "Tab",
            Keys.Enter => "Return",
            Keys.Escape => "Escape",
            Keys.Back => "BackSpace",
            Keys.Delete => "Delete",
            Keys.Insert => "Insert",
            Keys.Home => "Home",
            Keys.End => "End",
            Keys.PageUp => "Page_Up",
            Keys.PageDown => "Page_Down",
            Keys.Left => "Left",
            Keys.Right => "Right",
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.OemSemicolon => "semicolon",
            Keys.OemPlus => "plus",
            Keys.OemComma => "comma",
            Keys.OemMinus => "minus",
            Keys.OemPeriod => "period",
            Keys.OemQuestion => "slash",
            Keys.OemTilde => "grave",
            Keys.OemOpenBrackets => "bracketleft",
            Keys.OemPipe => "backslash",
            Keys.OemCloseBrackets => "bracketright",
            Keys.OemQuotes => "apostrophe",
            _ => string.Empty
        };

        return token.Length > 0;
    }

    private static bool IsModifierKey(Keys key) {
        return key == Keys.LeftControl ||
               key == Keys.RightControl ||
               key == Keys.LeftAlt ||
               key == Keys.RightAlt ||
               key == Keys.LeftShift ||
               key == Keys.RightShift;
    }

    private static int ModifierRank(Keys key) {
        return key switch {
            Keys.LeftControl => 0,
            Keys.RightControl => 1,
            Keys.LeftAlt => 2,
            Keys.RightAlt => 3,
            Keys.LeftShift => 4,
            Keys.RightShift => 5,
            _ => 6
        };
    }

    private static string SimplifyKeyToken(Keys key) {
        return key switch {
            Keys.LeftControl => "Ctrl",
            Keys.RightControl => "RCtrl",
            Keys.LeftAlt => "Alt",
            Keys.RightAlt => "RAlt",
            Keys.LeftShift => "Shift",
            Keys.RightShift => "RShift",
            Keys.OemPlus => "+",
            Keys.OemMinus => "-",
            Keys.OemPipe => "\\",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemComma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.OemQuotes => "'",
            Keys.OemSemicolon => ";",
            Keys.OemTilde => "~",
            Keys.PageUp => "PgUp",
            Keys.PageDown => "PgDn",
            >= Keys.D0 and <= Keys.D9 => ((char) ('0' + (key - Keys.D0))).ToString(),
            _ => key.ToString()
        };
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
    private static extern IntPtr XOpenDisplay(string displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
    private static extern UIntPtr XStringToKeysym(string keySymName);

    [DllImport("libX11.so.6")]
    private static extern byte XKeysymToKeycode(IntPtr display, UIntPtr keySym);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XSync(IntPtr display, bool discard);

    [DllImport("libXtst.so.6")]
    private static extern int XTestFakeKeyEvent(IntPtr display, uint keyCode, bool isPress, UIntPtr delay);
}
