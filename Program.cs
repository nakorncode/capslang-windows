using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;
using UiaCom = Interop.UIAutomationClient;

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
    private static AppSettings appSettings = new();
    private static readonly Lazy<UiaCom.IUIAutomation> nativeAutomation = new(() => new UiaCom.CUIAutomation8());

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        appSettings = SettingsStore.Load();

        languagePopup = new LanguagePopup();
        languagePopupTimer = new System.Windows.Forms.Timer { Interval = 90 };
        languagePopupTimer.Tick += (_, _) =>
        {
            languagePopupTimer.Stop();
            ShowIndicator(GetForegroundInputLanguageCode());
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
        var enabledItem = new ToolStripMenuItem("CapsLang Enabled") { CheckOnClick = true, Checked = appSettings.IsCapsLangEnabled };
        var indicatorItem = new ToolStripMenuItem("Show Language Indicator") { CheckOnClick = true, Checked = appSettings.ShowLanguageIndicator };
        var startupItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = StartupShortcut.IsEnabled() };
        var followCaretItem = new ToolStripMenuItem("Follow Text Caret");
        var screenCornerItem = new ToolStripMenuItem("Screen Corner");

        void SaveSettings()
        {
            SettingsStore.Save(appSettings);
        }

        void RefreshPositionMenu()
        {
            followCaretItem.Checked = appSettings.IndicatorPlacement == IndicatorPlacement.FollowCaret;
            screenCornerItem.Checked = appSettings.IndicatorPlacement == IndicatorPlacement.ScreenCorner;
        }

        enabledItem.CheckedChanged += (_, _) =>
        {
            appSettings.IsCapsLangEnabled = enabledItem.Checked;
            SaveSettings();
            ShowIndicator(appSettings.IsCapsLangEnabled ? "ON" : "OFF");
        };

        indicatorItem.CheckedChanged += (_, _) =>
        {
            appSettings.ShowLanguageIndicator = indicatorItem.Checked;
            SaveSettings();
            ShowIndicator(appSettings.ShowLanguageIndicator ? "UI ON" : "UI OFF", force: true);
        };

        startupItem.CheckedChanged += (_, _) =>
        {
            if (startupItem.Checked)
            {
                StartupShortcut.Enable();
            }
            else
            {
                StartupShortcut.Disable();
            }
        };

        followCaretItem.Click += (_, _) =>
        {
            appSettings.IndicatorPlacement = IndicatorPlacement.FollowCaret;
            RefreshPositionMenu();
            SaveSettings();
            ShowIndicator("CARET");
        };

        screenCornerItem.Click += (_, _) =>
        {
            appSettings.IndicatorPlacement = IndicatorPlacement.ScreenCorner;
            RefreshPositionMenu();
            SaveSettings();
            ShowIndicator("CORNER");
        };

        RefreshPositionMenu();

        var positionMenu = new ToolStripMenuItem("Indicator Position");
        positionMenu.DropDownItems.Add(followCaretItem);
        positionMenu.DropDownItems.Add(screenCornerItem);

        menu.Items.Add(enabledItem);
        menu.Items.Add(indicatorItem);
        menu.Items.Add(positionMenu);
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Turn CapsLock Off", null, (_, _) =>
        {
            ForceCapsLockOff();
            ShowIndicator("CAPS OFF", force: true, pointerInitiated: true);
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
        if (!appSettings.IsCapsLangEnabled)
        {
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

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
                        ShowIndicator("CAPS OFF", force: true);
                    }
                    else if (IsKeyDown(VK_SHIFT))
                    {
                        ToggleCapsLock();
                        ShowIndicator(IsCapsLockOn() ? "CAPS ON" : "CAPS OFF", force: true);
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
            return FormatLanguageLabel(CultureInfo.GetCultureInfo((int)languageId), languageId);
        }
        catch (CultureNotFoundException)
        {
            return $"0x{languageId:x4}".ToUpperInvariant();
        }
    }

    private static string FormatLanguageLabel(CultureInfo cultureInfo, long languageId)
    {
        if (!string.IsNullOrWhiteSpace(cultureInfo.Name))
        {
            return cultureInfo.Name.ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(cultureInfo.TwoLetterISOLanguageName) &&
            cultureInfo.TwoLetterISOLanguageName.Length == 2 &&
            !string.Equals(cultureInfo.TwoLetterISOLanguageName, "iv", StringComparison.OrdinalIgnoreCase))
        {
            return cultureInfo.TwoLetterISOLanguageName.ToUpperInvariant();
        }

        var nativeName = Regex.Replace(cultureInfo.NativeName, @"\s*\(.+?\)\s*", " ").Trim();
        if (!string.IsNullOrWhiteSpace(nativeName))
        {
            return nativeName;
        }

        return $"0x{languageId:x4}".ToUpperInvariant();
    }

    private static void ShowIndicator(string text, bool force = false, bool pointerInitiated = false)
    {
        if (languagePopup is null || (!force && !appSettings.ShowLanguageIndicator))
        {
            return;
        }

        var anchor = pointerInitiated
            ? GetPointerPopupAnchor()
            : GetIndicatorAnchor();
        languagePopup.ShowLanguage(text, anchor);
    }

    private static Point GetIndicatorAnchor()
    {
        return appSettings.IndicatorPlacement switch
        {
            IndicatorPlacement.ScreenCorner => GetScreenCornerPopupAnchor(),
            _ => GetInputPopupAnchor()
        };
    }

    private static Point GetScreenCornerPopupAnchor()
    {
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
        return new Point(screen.Right - 72, screen.Bottom - 54);
    }

    private static Point GetInputPopupAnchor()
    {
        if (TryGetNativeAutomationCaretAnchor(out var nativeAutomationCaretPoint))
        {
            return nativeAutomationCaretPoint;
        }

        if (TryGetAutomationCaretAnchor(out var automationCaretPoint))
        {
            return automationCaretPoint;
        }

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

    private static bool TryGetNativeAutomationCaretAnchor(out Point anchor)
    {
        anchor = Point.Empty;

        try
        {
            var focusedElement = nativeAutomation.Value.GetFocusedElement();
            var textPatternObject = focusedElement.GetCurrentPattern(UiaCom.UIA_PatternIds.UIA_TextPattern2Id);

            if (textPatternObject is not UiaCom.IUIAutomationTextPattern2 textPattern)
            {
                return false;
            }

            var caretRange = textPattern.GetCaretRange(out var isActive);
            if (isActive == 0)
            {
                return false;
            }

            return TryGetNativeTextRangeAnchor(caretRange, out anchor);
        }
        catch (COMException)
        {
        }
        catch (InvalidCastException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return false;
    }

    private static bool TryGetNativeTextRangeAnchor(UiaCom.IUIAutomationTextRange? range, out Point anchor)
    {
        anchor = Point.Empty;
        if (range is null)
        {
            return false;
        }

        var rectangles = range.GetBoundingRectangles();
        for (var index = 0; index + 3 < rectangles.Length; index += 4)
        {
            var x = rectangles[index];
            var y = rectangles[index + 1];
            var width = rectangles[index + 2];
            var height = rectangles[index + 3];

            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
            {
                continue;
            }

            if (width < 0 || height <= 0)
            {
                continue;
            }

            anchor = new Point((int)Math.Round(x + Math.Min(width, 2)), (int)Math.Round(y + height));
            return true;
        }

        return false;
    }

    private static bool TryGetAutomationCaretAnchor(out Point anchor)
    {
        anchor = Point.Empty;

        try
        {
            var focusedElement = AutomationElement.FocusedElement;
            if (focusedElement is null)
            {
                return false;
            }

            if (focusedElement.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObject) &&
                textPatternObject is TextPattern textPattern)
            {
                foreach (var selectionRange in textPattern.GetSelection())
                {
                    if (TryGetTextRangeAnchor(selectionRange, out anchor))
                    {
                        return true;
                    }
                }
            }
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (COMException)
        {
        }

        return false;
    }

    private static bool TryGetTextRangeAnchor(TextPatternRange? range, out Point anchor)
    {
        anchor = Point.Empty;
        if (range is null)
        {
            return false;
        }

        foreach (var rectangle in range.GetBoundingRectangles())
        {
            var x = rectangle.X;
            var y = rectangle.Y;
            var width = rectangle.Width;
            var height = rectangle.Height;

            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
            {
                continue;
            }

            if (width < 0 || height <= 0)
            {
                continue;
            }

            anchor = new Point((int)Math.Round(x + Math.Min(width, 2)), (int)Math.Round(y + height));
            return true;
        }

        return false;
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

internal enum IndicatorPlacement
{
    FollowCaret,
    ScreenCorner
}

internal sealed class AppSettings
{
    public bool IsCapsLangEnabled { get; set; } = true;
    public bool ShowLanguageIndicator { get; set; } = true;
    public IndicatorPlacement IndicatorPlacement { get; set; } = IndicatorPlacement.FollowCaret;
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CapsLang");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}

internal static class StartupShortcut
{
    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "CapsLang.lnk");

    public static bool IsEnabled()
    {
        return File.Exists(ShortcutPath);
    }

    public static void Enable()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            throw new InvalidOperationException("WScript.Shell is not available.");
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(ShortcutPath);
        shortcut.TargetPath = Application.ExecutablePath;
        shortcut.WorkingDirectory = AppContext.BaseDirectory;
        shortcut.Description = "Use CapsLock as input language switcher";
        shortcut.Save();
    }

    public static void Disable()
    {
        if (File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }
    }
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
