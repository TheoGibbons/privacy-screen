using System.Drawing;
using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;
using System.Windows.Threading;
using PrivacyScreen;
using Forms = System.Windows.Forms;

internal static partial class Program
{
    // Same tag as the Remote Desktop app; it must no longer bypass privacy.
    private static readonly IntPtr RemoteInputTag = new(0x52445056);
    [STAThread]
    private static void Main(string[] args)
    {
        if (PrivacyCursorGuard.TryRun(args)) return;
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        if (args.Contains("--cursor-crash-host"))
        {
            _ = new PrivacyCursorGuard();
            _ = new PrivacyCursor();
            Environment.Exit(0); // intentionally skips Dispose; guardian must restore
        }
        var policy = new PrivacyInputPolicy();
        long now = 100;
        bool Type(string word)
        {
            bool unlocked = false;
            foreach (char c in word)
            {
                unlocked |= policy.OnKey(char.ToUpperInvariant(c), true, now++);
                policy.OnKey(char.ToUpperInvariant(c), false, now++);
            }
            return unlocked;
        }
        Check(!Type("unlXock"), "wrong letters reset the escape");
        Check(Type("noiseUNLOCK"), "escape is case insensitive and works after noise");
        Check(!Type("unl"), "partial escape stays private");
        now += 5001;
        Check(!Type("ock"), "a stale partial escape expires");
        policy = new PrivacyInputPolicy();
        Check(!policy.OnKey('U', true, now++), "first U stays private");
        Check(!policy.OnKey('U', true, now++), "auto-repeat does not advance or reset");
        policy.OnKey('U', false, now++);
        policy.OnKey(0xA0, true, now++);
        Check(Type("nlock"), "Shift does not interrupt the escape");
        Check(Type("unlock"), "matcher can be reused");
        var unicode = new PrivacyInputPolicy();
        bool unicodeUnlocked = false;
        foreach (char c in "noiseUnLoCk")
        {
            unicodeUnlocked |= unicode.OnKeyboardEvent(0xE7, c, true, now++);
            unicode.OnKeyboardEvent(0xE7, c, false, now++);
        }
        Check(unicodeUnlocked, "remote Unicode text unlocks case insensitively");
        Console.WriteLine("PASS: local keys and remote Unicode unlock recognition");

        int published = Array.IndexOf(args, "--published-smoke");
        if (published >= 0) PublishedSmoke(Path.GetFullPath(args[published + 1]));



        if (args.Contains("--native-smoke"))
        {
            CaptureSmoke();
            InputSmoke();
            CursorSmoke();
            ApplicationSmoke();
        }
    }

    // Opt-in: briefly shows a small test window; never blocks physical input.
    private static void CaptureSmoke()
    {

        var primary = Forms.Screen.PrimaryScreen!.Bounds;
        var bounds = new Rectangle(primary.X + 40, primary.Y + 40, 300, 160);
        using var desktop = new TestWindow { Bounds = bounds, BackColor = Color.Magenta };
        desktop.Show();
        SetWindowPos(desktop.Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x50);
        Pump();
        using var overlay = new PrivacyOverlayWindow(bounds);
        overlay.ShowCovered(bounds);
        Pump();
        Check(GetWindowDisplayAffinity(overlay.Handle, out uint affinity) && affinity == 0,
            "overlay is included in captures");
        var point = new NativePoint { X = bounds.X + 20, Y = bounds.Y + 20 };
        var hit = WindowFromPoint(point);
        Check(GetAncestor(hit, 2) == overlay.Handle, "overlay covers hit testing of the desktop");
        // Other applications can change focus during this interactive check.
        Check(GetForegroundWindow() != overlay.Handle, "overlay never takes focus");

        using var surface = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(surface))
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        Check(surface.GetPixel(20, 20).ToArgb() == Color.Black.ToArgb(),
            "GDI capture sees the privacy cover instead of the desktop");
        Console.WriteLine("PASS: captured privacy cover and no-focus overlay behavior");
    }

    // Downstream hooks consume every test event so nothing reaches the user's
    // applications. Both tagged and untagged input must be consumed by privacy.
    private static void InputSmoke()
    {
        int keys = 0, mouse = 0;
        HookProc keyboardProc = (code, message, data) =>
        {
            if (code >= 0) { keys++; return new IntPtr(1); }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        };
        HookProc mouseProc = (code, message, data) =>
        {
            if (code >= 0) { mouse++; return new IntPtr(1); }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        };
        var module = GetModuleHandle(null);
        var keyboardHook = SetWindowsHookEx(13, keyboardProc, module, 0);
        var mouseHook = SetWindowsHookEx(14, mouseProc, module, 0);
        try
        {
            Check(keyboardHook != IntPtr.Zero && mouseHook != IntPtr.Zero, "observer hooks install");
            int unlocks = 0;
            using (var blocker = new PrivacyInputBlocker(() => Interlocked.Increment(ref unlocks)))
            {
                var tagged = VirtualKeys("NOISE", RemoteInputTag)
                    .Append(new TestInput { Type = 0, Mouse = new TestMouse { Flags = 0x800, Data = 120, Extra = RemoteInputTag } }).ToArray();
                Check(SendInput((uint)tagged.Length, tagged, Marshal.SizeOf<TestInput>()) == tagged.Length,
                    "tagged test input was submitted");
                Pump();
                Check(keys == 0 && mouse == 0, "tagged remote input cannot reach desktop apps");
                Check(unlocks == 0, "unrelated remote typing does not unlock");
                var untagged = new[]
                {
                    new TestInput { Type = 1, Key = new TestKey { Vk = 0x87 } }, // F24
                    new TestInput { Type = 1, Key = new TestKey { Vk = 0x87, Flags = 2 } },
                    new TestInput { Type = 0, Mouse = new TestMouse { Flags = 0x800, Data = 120 } },
                };
                Check(SendInput((uint)untagged.Length, untagged, Marshal.SizeOf<TestInput>()) == 3,
                    "untagged test input was submitted");
                Pump();
                Check(keys == 0 && mouse == 0, "untagged input is blocked");
                foreach (var input in new[]
                {
                    UnicodeKeys("UnLoCk", RemoteInputTag),
                    VirtualKeys("UNLOCK", RemoteInputTag),
                    UnicodeKeys("unlock", IntPtr.Zero),
                    VirtualKeys("UNLOCK", IntPtr.Zero),
                })
                {
                    int previous = unlocks;
                    Check(SendInput((uint)input.Length, input, Marshal.SizeOf<TestInput>()) == input.Length, "remote unlock submitted");
                    Pump();
                    Check(unlocks == previous + 1, "remote unlock reaches callback exactly once");
                    Check(keys == 0 && mouse == 0, "unlock keystrokes are consumed");
                }
            }
            Console.WriteLine("PASS: all remote input is blocked; tagged and untagged key/text unlock works without leaking keystrokes");
        }
        finally
        {
            if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
            GC.KeepAlive(keyboardProc);
            GC.KeepAlive(mouseProc);
        }
    }

    private static void Pump()
    {
        Forms.Application.DoEvents();
        Thread.Sleep(100);
        Forms.Application.DoEvents();
    }

    private static TestInput[] VirtualKeys(string text, IntPtr tag) => text.SelectMany(c => new[]
    {
        new TestInput { Type = 1, Key = new TestKey { Vk = (ushort)c, Extra = tag } },
        new TestInput { Type = 1, Key = new TestKey { Vk = (ushort)c, Flags = 2, Extra = tag } },
    }).ToArray();

    private static TestInput[] UnicodeKeys(string text, IntPtr tag) => text.SelectMany(c => new[]
    {
        new TestInput { Type = 1, Key = new TestKey { Scan = c, Flags = 4, Extra = tag } },
        new TestInput { Type = 1, Key = new TestKey { Scan = c, Flags = 6, Extra = tag } },
    }).ToArray();

    private static void CursorSmoke()
    {
        int[] RenderCursor(uint id)
        {
            var cursor = LoadCursor(IntPtr.Zero, new IntPtr(id));
            Check(cursor != IntPtr.Zero, "system cursor is available: " + id);
            using var bitmap = new Bitmap(128, 128, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Magenta);
                var dc = graphics.GetHdc();
                try { Check(DrawIconEx(dc, 0, 0, cursor, 0, 0, 0, IntPtr.Zero, 3), "cursor can be rendered"); }
                finally { graphics.ReleaseHdc(dc); }
            }
            return Enumerable.Range(0, 128 * 128).Select(i => bitmap.GetPixel(i % 128, i / 128).ToArgb()).ToArray();
        }
        var original = PrivacyCursor.CursorIds.ToDictionary(id => id, RenderCursor);
        using (var guard = new PrivacyCursorGuard())
        using (var cursor = new PrivacyCursor())
        {
            Check(guard.IsAlive, "crash restoration helper is running");
            foreach (uint id in PrivacyCursor.CursorIds)
                Check(RenderCursor(id).All(pixel => pixel == Color.Magenta.ToArgb()), "privacy cursor is blank: " + id);
        }
        foreach (uint id in PrivacyCursor.CursorIds)
            Check(RenderCursor(id).SequenceEqual(original[id]), "original cursor is restored: " + id);
        Console.WriteLine("PASS: standard Windows cursors are blank during privacy and restored afterward");

        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add("--cursor-crash-host");
        using var host = System.Diagnostics.Process.Start(start)!;
        try
        {
            Check(host.WaitForExit(10_000) && host.ExitCode == 0, "simulated privacy host exits");
            bool restored = false;
            for (int i = 0; i < 30 && !restored; i++)
            {
                Thread.Sleep(100);
                restored = RenderCursor(32512).SequenceEqual(original[32512]);
            }
            Check(restored, "guardian restores cursors after the host exits without cleanup");
            Console.WriteLine("PASS: cursor scheme is restored after an abrupt host exit");
        }
        finally { PrivacyCursor.Restore(); }
    }
    private static void ApplicationSmoke()
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var app = new PrivacyApplication();
        Exception? failure = null;
        PrivacyOverlayWindow[] windows = [];
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var manager = (PrivacyOverlayManager)typeof(PrivacyApplication).GetField("_privacy", fields)!.GetValue(app)!;
                Check(manager.IsEnabled, "launch immediately enables privacy");
                windows = ((List<PrivacyOverlayWindow>)typeof(PrivacyOverlayManager).GetField("_windows", fields)!.GetValue(manager)!).ToArray();
                var screens = Forms.Screen.AllScreens;
                Check(windows.Length == screens.Length, "every monitor is covered");
                for (int i = 0; i < screens.Length; i++)
                {
                    Check(windows[i].Visible && windows[i].Bounds == screens[i].Bounds, "overlay matches monitor bounds");
                    Check(windows[i].BackColor == Color.Black && windows[i].Controls[0].Text == "type unlock", "original privacy appearance");
                }
                var blocker = (PrivacyInputBlocker)typeof(PrivacyOverlayManager).GetField("_blocker", fields)!.GetValue(manager)!;
                var callback = typeof(PrivacyInputBlocker).GetMethod("KeyboardHook", fields)!;
                // Exercise physical keyboard events as well as the injected
                // remote events covered by InputSmoke and PublishedSmoke.
                IntPtr data = Marshal.AllocHGlobal(32);
                try
                {
                    for (int i = 0; i < 32; i++) Marshal.WriteByte(data, i, 0);
                    foreach (char c in "UNLOCK")
                    {
                        Marshal.WriteInt32(data, c);
                        Check((IntPtr)callback.Invoke(blocker, [0, new IntPtr(0x100), data])! == new IntPtr(1), "unlock keydown is consumed");
                        callback.Invoke(blocker, [0, new IntPtr(0x101), data]);
                    }
                }
                finally { Marshal.FreeHGlobal(data); }
            }
            catch (Exception ex)
            {
                failure = ex;
                app.Shutdown(1);
            }
        };
        // A separate watchdog ensures even a regression cannot leave the test
        // covering the desktop indefinitely.
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        watchdog.Tick += (_, _) =>
        {
            failure = new Exception("Unlock did not close the app within five seconds.");
            app.Shutdown(1);
        };
        timer.Start();
        watchdog.Start();
        try
        {
            int code = app.Run();
            if (failure != null) throw failure;
            Check(code == 0, "unlock exits successfully without Enter");
            Check(windows.Length > 0 && windows.All(w => w.IsDisposed), "unlock disposes every overlay");
            Console.WriteLine("PASS: immediate all-monitor privacy, physical unlock callback and complete app shutdown");
        }
        finally
        {
            timer.Stop();
            watchdog.Stop();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private sealed class TestWindow : Forms.Form
    {
        internal TestWindow()
        {
            FormBorderStyle = Forms.FormBorderStyle.None;
            StartPosition = Forms.FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
        }
        protected override bool ShowWithoutActivation => true;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]
    private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
    [StructLayout(LayoutKind.Sequential)]
    private struct TestKey { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TestMouse { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)]
    private struct TestUnion
    {
        [FieldOffset(0)] public TestKey Key;
        [FieldOffset(0)] public TestMouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TestInput
    {
        public uint Type;
        public TestUnion Union;
        public TestKey Key { set => Union.Key = value; }
        public TestMouse Mouse { set => Union.Mouse = value; }
    }
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, TestInput[] inputs, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr cursor, int width, int height,
        uint step, IntPtr brush, uint flags);
}
