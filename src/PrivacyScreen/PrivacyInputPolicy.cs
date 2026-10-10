namespace PrivacyScreen;

/// <summary>Recognizes local or remote unlock without forwarding keystrokes.</summary>
internal sealed class PrivacyInputPolicy
{
    private const string Unlock = "UNLOCK";
    private readonly HashSet<uint> _pressed = new();
    private int _matched;
    private long _lastKeyAt;

    internal bool OnKeyboardEvent(uint vk, uint scan, bool down, long now)
    {
        // SendInput(KEYEVENTF_UNICODE), used by Remote Desktop's TypeText,
        // arrives as VK_PACKET with the UTF-16 character in the scan code.
        if (vk == 0xE7 && scan <= char.MaxValue)
            vk = char.ToUpperInvariant((char)scan);
        return OnKey(vk, down, now);
    }

    // Called on the hook thread. Shift/Caps do not affect letter virtual keys.
    internal bool OnKey(uint vk, bool down, long now)
    {
        if (!down) { _pressed.Remove(vk); return false; }
        if (!_pressed.Add(vk)) return false; // ignore auto-repeat
        if (vk is 0x10 or 0xA0 or 0xA1 or 0x14) return false;
        if (now - _lastKeyAt > 5000) _matched = 0;
        _lastKeyAt = now;
        if (vk == Unlock[_matched]) _matched++;
        else _matched = vk == Unlock[0] ? 1 : 0;
        if (_matched != Unlock.Length) return false;
        _matched = 0;
        return true;
    }
}
