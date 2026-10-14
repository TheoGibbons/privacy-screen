using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using PrivacyScreen;
using Forms = System.Windows.Forms;

internal static partial class Program
{
    private static int[] SnapshotCursor()
    {
        using var bitmap = new Bitmap(128, 128);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Magenta);
            var dc = graphics.GetHdc();
            try { Check(DrawIconEx(dc, 0, 0, LoadCursor(IntPtr.Zero, new IntPtr(32512)), 0, 0, 0, IntPtr.Zero, 3), "cursor snapshot"); }
            finally { graphics.ReleaseHdc(dc); }
        }
        return Enumerable.Range(0, 128 * 128).Select(i => bitmap.GetPixel(i % 128, i / 128).ToArgb()).ToArray();
    }

    private static void PublishedSmoke(string executable)
    {
        var original = SnapshotCursor();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        using var host = Process.Start(start)!;
        try
        {
            var windows = new List<IntPtr>();
            for (int i = 0; i < 100; i++)
            {
                Check(!host.HasExited, "published app remains active");
                windows.Clear();
                EnumWindows((hwnd, _) =>
                {
                    GetWindowThreadProcessId(hwnd, out uint owner);
                    if (owner == host.Id && IsWindowVisible(hwnd) &&
                        GetWindowDisplayAffinity(hwnd, out uint affinity) && affinity == 0)
                        windows.Add(hwnd);
                    return true;
                }, IntPtr.Zero);
                if (windows.Count == Forms.Screen.AllScreens.Length &&
                    SnapshotCursor().All(pixel => pixel == Color.Magenta.ToArgb())) break;
                Thread.Sleep(100);
            }
            Check(windows.Count == Forms.Screen.AllScreens.Length, "published single file covers every monitor");
            Check(SnapshotCursor().All(pixel => pixel == Color.Magenta.ToArgb()), "published app blanks cursors");
            Check(PrivacyCursor.Restore(), "reload the scheme while the published app stays active");
            bool repaired = false;
            for (int i = 0; i < 40 && !repaired; i++)
            {
                Thread.Sleep(100);
                Check(!host.HasExited, "cursor recovery keeps the published app active");
                repaired = SnapshotCursor().All(pixel => pixel == Color.Magenta.ToArgb());
            }
            Check(repaired, "published app automatically hides cursors again after a silent scheme reload");
            var bounds = Forms.Screen.PrimaryScreen!.Bounds;
            using (var capture = new Bitmap(32, 32))
            {
                using (var graphics = Graphics.FromImage(capture))
                    graphics.CopyFromScreen(bounds.X + 20, bounds.Y + 20, 0, 0, capture.Size);
                Check(capture.GetPixel(0, 0).ToArgb() == Color.Black.ToArgb(), "published cover appears in desktop capture");
            }
            using (var duplicate = Process.Start(start)!)
                Check(duplicate.WaitForExit(5000) && duplicate.ExitCode == 0, "duplicate launch exits silently");
            host.Kill(); // Keep its helper alive to verify the actual bundled guardian.
            Check(host.WaitForExit(5000), "published app exits after abrupt termination");
            bool restored = false;
            for (int i = 0; i < 30 && !restored; i++)
            {
                Thread.Sleep(100);
                restored = SnapshotCursor().SequenceEqual(original);
            }
            Check(restored, "bundled helper restores cursors after published app crashes");
            Check(windows.All(hwnd => !IsWindow(hwnd)), "published overlays close after process exit");
            using var unlockedHost = Process.Start(start)!;
            try
            {
                bool ready = false;
                for (int i = 0; i < 100 && !ready; i++)
                {
                    Check(!unlockedHost.HasExited, "second published launch remains active");
                    ready = SnapshotCursor().All(pixel => pixel == Color.Magenta.ToArgb());
                    Thread.Sleep(100);
                }
                Check(ready, "second published launch activates privacy");
                var unicode = UnicodeKeys("unlock", RemoteInputTag);
                Check(SendInput((uint)unicode.Length, unicode, Marshal.SizeOf<TestInput>()) == unicode.Length,
                    "Remote Desktop Unicode unlock submitted to published app");
                Check(unlockedHost.WaitForExit(5000) && unlockedHost.ExitCode == 0, "remote unlock closes published app successfully");
                Check(SnapshotCursor().SequenceEqual(original), "remote unlock restores cursors");
            }
            finally
            {
                if (!unlockedHost.HasExited) { unlockedHost.Kill(); unlockedHost.WaitForExit(5000); }
            }
            Console.WriteLine("PASS: published single-file launch, duplicate rejection, overlay cleanup and bundled crash restoration");
            Console.WriteLine("PASS: published app automatically repairs a cursor scheme reload without restarting privacy");
            Console.WriteLine("PASS: published screen is captured and Remote Desktop Unicode typing unlocks it");
        }
        finally
        {
            if (!host.HasExited) { host.Kill(); host.WaitForExit(5000); }
            PrivacyCursor.Restore();
        }
    }

    private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);
}
