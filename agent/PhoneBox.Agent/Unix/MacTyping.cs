using System.Runtime.InteropServices;

namespace PhoneBox.Agent;

/// <summary>
/// Types text on macOS as Unicode keyboard events (CGEventKeyboardSetUnicodeString), so any character reaches the app
/// in front as it is. System Events' <c>keystroke</c>, used before, can only type what the current keyboard layout has
/// keys for: on a U.S. layout "Mấy giờ rồi" arrived as "May gia rai". Posting events needs the same Accessibility
/// permission as before.
/// </summary>
internal static class MacTyping
{
    /// <summary>Gap between characters: some apps (Electron, terminals) drop events that arrive all at once.</summary>
    private const int GapMs = 4;

    /// <summary>Test seam: receives each event's text instead of posting it.</summary>
    internal static Action<string>? TestSink;

    /// <summary>
    /// The events to post, one per character: a character outside the BMP (an emoji) stays one event of two UTF-16
    /// units, and combining marks stay with the character before them, so no app sees half a character.
    /// </summary>
    internal static List<string> Units(string text)
    {
        var units = new List<string>();
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext()) units.Add(e.GetTextElement());
        return units;
    }

    /// <summary>Types the text; throws with the Accessibility hint when macOS doesn't let the agent post events.</summary>
    public static void Type(string text, CancellationToken ct)
    {
        if (TestSink is null && !AXIsProcessTrusted()) throw new InvalidOperationException(KeySender.AccessibilityHint);
        foreach (var unit in Units(text))
        {
            ct.ThrowIfCancellationRequested();
            if (TestSink is { } sink) { sink(unit); continue; }
            Post(unit, true);
            Post(unit, false);
            Thread.Sleep(GapMs);
        }
    }

    private static void Post(string unit, bool down)
    {
        var ev = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, down);
        if (ev == IntPtr.Zero) throw new InvalidOperationException("macOS could not create a keyboard event");
        try
        {
            var chars = unit.Select(c => (ushort)c).ToArray();
            CGEventKeyboardSetUnicodeString(ev, (nuint)chars.Length, chars);
            CGEventSetFlags(ev, 0); // a modifier still held on the Mac must not turn the text into shortcuts
            CGEventPost(0 /* kCGHIDEventTap */, ev);
        }
        finally { CFRelease(ev); }
    }

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort keyCode, [MarshalAs(UnmanagedType.U1)] bool keyDown);
    [DllImport(CoreGraphics)] private static extern void CGEventKeyboardSetUnicodeString(IntPtr ev, nuint length, ushort[] chars);
    [DllImport(CoreGraphics)] private static extern void CGEventSetFlags(IntPtr ev, ulong flags);
    [DllImport(CoreGraphics)] private static extern void CGEventPost(uint tap, IntPtr ev);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr obj);
    [DllImport(ApplicationServices)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool AXIsProcessTrusted();
}
