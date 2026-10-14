using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace PrivacyScreen;

/// <summary>An opaque, non-activating window visible to desktop capture.</summary>
internal sealed class PrivacyOverlayWindow : Forms.Form
{
    private readonly Font _captionFont = new("Segoe UI", 12);
    internal PrivacyOverlayWindow(Rectangle bounds)
    {
        FormBorderStyle = Forms.FormBorderStyle.None;
        StartPosition = Forms.FormStartPosition.Manual;
        AutoScaleMode = Forms.AutoScaleMode.None;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Bounds = bounds;
        Controls.Add(new Forms.Label
        {
            Text = "type unlock",
            ForeColor = Color.FromArgb(150, 150, 150),
            BackColor = Color.Black,
            Font = _captionFont,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = Forms.DockStyle.Fill,
        });
    }

    protected override bool ShowWithoutActivation => true;
    protected override void WndProc(ref Forms.Message message)
    {
        // Hover activation and child controls can ask to activate the form even
        // with WS_EX_NOACTIVATE. Keep input focus on the underlying application.
        if (message.Msg == 0x0021) // WM_MOUSEACTIVATE
        {
            message.Result = new IntPtr(3); // MA_NOACTIVATE
            return;
        }
        base.WndProc(ref message);
    }

    protected override Forms.CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // An ordinary opaque window is included in desktop captures.
            // Keep it non-activating and off the taskbar.
            cp.ExStyle |= 0x08000000 | 0x00000080;
            return cp;
        }
    }

    internal void ShowCovered(Rectangle bounds)
    {
        // WinForms focuses a TopMost form even with ShowWithoutActivation.
        // Cover sets native topmost order with NOACTIVATE instead.
        Show();
        Cover(bounds);
    }

    internal void Cover(Rectangle bounds)
    {
        if (!SetWindowPos(Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height,
                0x0010 | 0x0040)) // NOACTIVATE | SHOWWINDOW, physical monitor pixels
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _captionFont.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
