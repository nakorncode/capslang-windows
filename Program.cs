using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapsLang;

internal static class Program
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_CAPITAL = 0x14;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const int INPUTLANGCHANGE_FORWARD = 0x0002;
    private static readonly IntPtr HKL_NEXT = new(1);

    private static IntPtr hookId = IntPtr.Zero;
    private static LowLevelKeyboardProc? hookProc;
    private static LanguagePopup? languagePopup;
    private static System.Windows.Forms.Timer? languagePopupTimer;

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        languagePopup = new LanguagePopup();
        languagePopupTimer = new System.Windows.Forms.Timer { Interval = 90 };
        languagePopupTimer.Tick += (_, _) =>
        {
            languagePopupTimer.Stop();
            languagePopup.ShowLanguage(GetForegroundInputLanguageCode(), GetInputPopupAnchor());
        };

        hookProc = HookCallback;
        hookId = SetHook(hookProc);
        ForceCapsLockOff();

        using var trayIcon = CreateTrayIcon();
        Application.ApplicationExit += (_, _) =>
        {
            trayIcon.Visible = false;
            languagePopup?.Close();
            if (hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookId);
            }
        };

        Application.Run();
    }

    private static NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Turn CapsLock Off", null, (_, _) =>
        {
            ForceCapsLockOff();
            languagePopup?.ShowLanguage("CAPS OFF", GetPointerPopupAnchor());
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());

        return new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Text = "CapsLang: CapsLock switches language",
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = currentModule?.ModuleName is { Length: > 0 }
            ? GetModuleHandle(currentModule.ModuleName)
            : IntPtr.Zero;

        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, moduleHandle, 0);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var vkCode = Marshal.ReadInt32(lParam);

            if (vkCode == VK_CAPITAL)
            {
                if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    if (IsKeyDown(VK_CONTROL))
                    {
                        ForceCapsLockOff();
                        languagePopup?.ShowLanguage("CAPS OFF", GetInputPopupAnchor());
                    }
                    else if (IsKeyDown(VK_SHIFT))
                    {
                        ToggleCapsLock();
                        languagePopup?.ShowLanguage(IsCapsLockOn() ? "CAPS ON" : "CAPS OFF", GetInputPopupAnchor());
                    }
                    else
                    {
                        ForceCapsLockOff();
                        SwitchToNextInputLanguage();
                        languagePopupTimer?.Stop();
                        languagePopupTimer?.Start();
                    }
                }

                if (message is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP)
                {
                    return new IntPtr(1);
                }
            }
        }

        return CallNextHookEx(hookId, nCode, wParam, lParam);
    }

    private static void SwitchToNextInputLanguage()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow != IntPtr.Zero)
        {
            PostMessage(foregroundWindow, WM_INPUTLANGCHANGEREQUEST, new IntPtr(INPUTLANGCHANGE_FORWARD), HKL_NEXT);
        }
    }

    private static void ForceCapsLockOff()
    {
        if (IsCapsLockOn())
        {
            ToggleCapsLock();
        }
    }

    private static bool IsCapsLockOn()
    {
        return (GetKeyState(VK_CAPITAL) & 1) != 0;
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static void ToggleCapsLock()
    {
        keybd_event(VK_CAPITAL, 0x45, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_CAPITAL, 0x45, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static string GetForegroundInputLanguageCode()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return "??";
        }

        var threadId = GetWindowThreadProcessId(foregroundWindow, out _);
        var layout = GetKeyboardLayout(threadId);
        var languageId = layout.ToInt64() & 0xffff;

        try
        {
            return CultureInfo.GetCultureInfo((int)languageId).TwoLetterISOLanguageName.ToUpperInvariant();
        }
        catch (CultureNotFoundException)
        {
            return "??";
        }
    }

    private static Point GetInputPopupAnchor()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow != IntPtr.Zero)
        {
            var threadId = GetWindowThreadProcessId(foregroundWindow, out _);
            var guiThreadInfo = new GUITHREADINFO();
            guiThreadInfo.cbSize = Marshal.SizeOf<GUITHREADINFO>();

            if (GetGUIThreadInfo(threadId, ref guiThreadInfo) && guiThreadInfo.hwndCaret != IntPtr.Zero)
            {
                var caretPoint = new Point(guiThreadInfo.rcCaret.Left, guiThreadInfo.rcCaret.Bottom);
                if (ClientToScreen(guiThreadInfo.hwndCaret, ref caretPoint))
                {
                    return caretPoint;
                }
            }

            var fallbackWindow = guiThreadInfo.hwndFocus != IntPtr.Zero
                ? guiThreadInfo.hwndFocus
                : foregroundWindow;

            if (GetWindowRect(fallbackWindow, out var focusedWindowRect))
            {
                return new Point(focusedWindowRect.Left + 16, focusedWindowRect.Bottom - 16);
            }
        }

        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
        return new Point(screen.Left + 24, screen.Top + 24);
    }

    private static Point GetPointerPopupAnchor()
    {
        return GetCursorPos(out var cursorPoint)
            ? cursorPoint
            : new Point(Screen.PrimaryScreen?.WorkingArea.Left + 24 ?? 24, Screen.PrimaryScreen?.WorkingArea.Top + 24 ?? 24);
    }

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(int bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}

internal sealed class LanguagePopup : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private readonly Label label;
    private readonly System.Windows.Forms.Timer hideTimer;

    public LanguagePopup()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(28, 28, 30);
        ClientSize = new Size(54, 34);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        label = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 12F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            UseMnemonic = false
        };
        Controls.Add(label);

        hideTimer = new System.Windows.Forms.Timer { Interval = 720 };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            Hide();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return createParams;
        }
    }

    public void ShowLanguage(string languageCode, Point anchor)
    {
        label.Text = languageCode;
        Width = Math.Max(54, TextRenderer.MeasureText(languageCode, label.Font).Width + 24);
        Location = GetPopupLocation(anchor);

        hideTimer.Stop();
        Show();
        BringToFront();
        hideTimer.Start();
    }

    private Point GetPopupLocation(Point anchor)
    {
        var screen = Screen.FromPoint(anchor).WorkingArea;
        var x = anchor.X + 12;
        var y = anchor.Y + 10;

        if (x + Width > screen.Right)
        {
            x = anchor.X - Width - 12;
        }

        if (y + Height > screen.Bottom)
        {
            y = anchor.Y - Height - 10;
        }

        x = Math.Clamp(x, screen.Left, screen.Right - Width);
        y = Math.Clamp(y, screen.Top, screen.Bottom - Height);

        return new Point(x, y);
    }
}
