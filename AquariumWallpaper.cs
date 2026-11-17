// ============================================================
//  ASCII 水族馆 · 桌面壁纸宿主
//  把 aquarium.html（Edge 应用窗口）塞进 Windows 桌面壁纸层，
//  小鱼游在「壁纸之上、桌面图标之下」，鼠标完全穿透，不挡工作。
//
//  编译：build.bat   （只用系统自带 .NET Framework，无需安装任何东西）
//  运行：AquariumWallpaper.exe → 托盘小鱼，双击 / 右键打开控制面板
//  改属性不重启窗口：C# 写 aquarium-cmd.js（命令队列），页面每 60ms 加载执行。
// ============================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Win32
{
    [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    internal static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
    internal delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp,
                                                     uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] internal static extern IntPtr SetParent(IntPtr hWnd, IntPtr parent);
    [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] internal static extern bool UpdateWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr hIcon);

    internal const int WH_MOUSE_LL  = 14;
    internal const int WM_LBUTTONUP = 0x0202;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT {
      public POINT pt; public uint mouseData; public uint flags;
      public uint time; public IntPtr dwExtraInfo;
    }
    internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr GetModuleHandle(string modName);

    internal const int  GWL_STYLE   = -16;
    internal const int  GWL_EXSTYLE = -20;
    internal const uint WS_CHILD    = 0x40000000;
    internal const uint WS_VISIBLE  = 0x10000000;
    internal const uint WS_POPUP    = 0x80000000;
    internal const uint WS_EX_TOOLWINDOW  = 0x00000080;
    internal const uint WS_EX_TRANSPARENT = 0x00000020;
    internal const uint WS_EX_NOACTIVATE  = 0x08000000;
    internal const uint SWP_FLAGS = 0x0004 | 0x0010 | 0x0020 | 0x0040;
    internal const uint WM_CLOSE = 0x0010;
    internal const int  SW_SHOW  = 5;

    internal static IntPtr FindWallpaperLayer()
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr ignored;
        if (progman != IntPtr.Zero)
            SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0x0002, 1000, out ignored);

        IntPtr host = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lp) {
            if (FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) {
                host = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return host != IntPtr.Zero ? host : progman;
    }

    internal static IntPtr FindAquariumWindow()
    {
        IntPtr found = IntPtr.Zero;
        EnumProc check = delegate(IntPtr hWnd, IntPtr lp) {
            var title = new StringBuilder(256);
            GetWindowText(hWnd, title, title.Capacity);
            if (title.ToString().IndexOf("ASCII Aquarium", StringComparison.OrdinalIgnoreCase) >= 0) {
                var cls = new StringBuilder(128);
                GetClassName(hWnd, cls, cls.Capacity);
                if (cls.ToString().IndexOf("Chrome_WidgetWin", StringComparison.Ordinal) >= 0) {
                    uint pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    try {
                        if (!Process.GetProcessById((int)pid).ProcessName
                                .Equals("msedge", StringComparison.OrdinalIgnoreCase)) return true;
                    } catch { return true; }
                    found = hWnd;
                    return false;
                }
            }
            return true;
        };
        var tops = new System.Collections.Generic.List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr lp) { tops.Add(h); return true; }, IntPtr.Zero);
        foreach (var h in tops) {
            check(h, IntPtr.Zero);
            if (found != IntPtr.Zero) break;
            EnumChildWindows(h, check, IntPtr.Zero);
            if (found != IntPtr.Zero) break;
        }
        return found;
    }
}

// ============================================================
//  控制面板：放大 + 宽松间距，所有文字完整；浅色主题
//  背景色按钮显示「实际背景色 + 中文主题名」，不再是看不出的小黑块
// ============================================================
internal sealed class ControlPanel : Form
{
    readonly AquariumContext owner;
    readonly TrackBar fishBar, decorBar, zoomBar, speedBar;
    readonly Label fishLbl, decorLbl, zoomLbl, speedLbl;
    readonly CheckBox autoBox;
    readonly CheckBox[] toggles;
    readonly ToolTip tips = new ToolTip();
    readonly Button[] themeButtons = new Button[8];
    bool loading;

    static readonly Color PanelBg = Color.FromArgb(245, 249, 254);
    static readonly Color Ink     = Color.FromArgb(24, 38, 56);
    static readonly Color Accent  = Color.FromArgb(18, 105, 175);
    static readonly Color BtnBg   = Color.FromArgb(224, 236, 250);

    static readonly string[] THEME_NAMES   = { "深蓝", "夜蓝", "墨绿", "紫夜", "午夜", "青蓝", "棕夜", "纯黑" };
    static readonly string[] FEATURE_NAMES = { "水草", "气泡", "摆件", "微粒", "水面", "发光" };
    static readonly string[] FEATURE_KEYS  = { "plants", "bubbles", "decor", "motes", "surface", "glow" };

    public ControlPanel(AquariumContext o)
    {
        owner = o;
        Text = "ASCII 水族馆 · 控制面板";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(560, 974);
        Location = new Point(300, 40);
        TopMost = true;
        ShowInTaskbar = false;
        MinimizeBox = false; MaximizeBox = false;
        BackColor = PanelBg;
        ForeColor = Ink;
        Font = new Font("Microsoft YaHei", 11f);

        const int L = 24, W = 512;
        var head = new Label {
            Text = "ASCII 水族馆   ><>", Left = L, Top = 16, Width = W, Height = 46,
            Font = new Font("Consolas", 17f, FontStyle.Bold),
            ForeColor = Accent, BackColor = Color.Transparent
        };
        Controls.Add(head);

        fishLbl = Caption("鱼数量", L, 78);
        fishBar = Track(L, 130, 1, 90, 22);
        fishBar.ValueChanged += delegate {
            if (loading) return;
            fishLbl.Text = "鱼数量     " + fishBar.Value;
            owner.SetFish(fishBar.Value);
        };

        decorLbl = Caption("装饰物数量", L, 186);
        decorBar = Track(L, 238, 0, 14, 6);
        decorBar.ValueChanged += delegate {
            if (loading) return;
            decorLbl.Text = "装饰物数量     " + decorBar.Value;
            owner.SetDecorCount(decorBar.Value);
        };

        zoomLbl = Caption("整体缩放", L, 294);
        zoomBar = Track(L, 346, 40, 300, 100);
        zoomBar.ValueChanged += delegate {
            if (loading) return;
            double z = zoomBar.Value / 100.0;
            zoomLbl.Text = "整体缩放     " + z.ToString("0.00") + "×";
            owner.SetZoom(z);
        };

        speedLbl = Caption("游动速度", L, 402);
        speedBar = Track(L, 454, 20, 300, 100);
        speedBar.ValueChanged += delegate {
            if (loading) return;
            double s = speedBar.Value / 100.0;
            speedLbl.Text = "游动速度     " + s.ToString("0.00") + "×";
            owner.SetSpeed(s);
        };

        Caption("背景色（点色块切换）", L, 512);
        for (int i = 0; i < THEME_NAMES.Length; i++) {
            int idx = i;
            Color themeColor = ColorTranslator.FromHtml("#" + AquariumContext.THEMES[i]);
            var b = new Button {
                Text = "",                       // 只显示颜色本身，不放任何文字
                Left = L + (i % 4) * 128, Top = 576 + (i / 4) * 62,
                Width = 120, Height = 48,
                FlatStyle = FlatStyle.Flat,
                BackColor = themeColor,
                UseVisualStyleBackColor = false
            };
            // 鼠标移过 / 按下时也要保持真实主题色，否则会被系统画成浅色
            b.FlatAppearance.MouseOverBackColor = themeColor;
            b.FlatAppearance.MouseDownBackColor = themeColor;
            b.FlatAppearance.BorderColor = Color.FromArgb(160, 180, 205);
            b.FlatAppearance.BorderSize = 1;
            tips.SetToolTip(b, THEME_NAMES[i] + "  #" + AquariumContext.THEMES[i]);
            b.Click += delegate { owner.SetTheme(idx); MarkTheme(idx); };
            Controls.Add(b);
            themeButtons[i] = b;
        }
        MarkTheme(owner.ThemeIndex);

        Caption("显示开关", L, 708);
        toggles = new CheckBox[FEATURE_NAMES.Length];
        for (int i = 0; i < FEATURE_NAMES.Length; i++) {
            int idx = i;
            var c = new CheckBox {
                Text = FEATURE_NAMES[i], BackColor = PanelBg, ForeColor = Ink,
                FlatStyle = FlatStyle.Standard,
                Font = new Font("Microsoft YaHei", 11f),
                Left = L + (i % 3) * 172, Top = 756 + (i / 3) * 48,
                Width = 160, Height = 36, Checked = true
            };
            c.CheckedChanged += delegate { if (!loading) owner.SendToggle(FEATURE_KEYS[idx]); };
            toggles[i] = c;
            Controls.Add(c);
        }

        autoBox = new CheckBox {
            Text = "开机自动运行", BackColor = PanelBg, ForeColor = Ink,
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Microsoft YaHei", 11f),
            Left = L, Top = 860, Width = 260, Height = 36
        };
        autoBox.CheckedChanged += delegate { if (!loading) owner.SetAutoStart(autoBox.Checked); };
        Controls.Add(autoBox);
        var quit = MakeButton("退出", "退出水族馆", L + 300, 860, 180);
        quit.Click += delegate { owner.Exit(); };

        MakeButton("重投", "重新投鱼（连装饰一起重新随机）", L, 916, 160)
            .Click += delegate { owner.Respawn(); };
        MakeButton("暂停", "暂停 / 继续", L + 176, 916, 160)
            .Click += delegate { owner.TogglePause(); };
        MakeButton("收起", "收起面板", L + 352, 916, 160)
            .Click += delegate { Hide(); };
    }

    Label Caption(string text, int x, int y)
    {
        var l = new Label {
            Text = text, Left = x, Top = y, AutoSize = true,
            ForeColor = Ink, BackColor = PanelBg,
            Font = new Font("Microsoft YaHei", 12f, FontStyle.Bold)
        };
        Controls.Add(l);
        l.BringToFront();
        return l;
    }
    TrackBar Track(int x, int y, int min, int max, int val)
    {
        var t = new TrackBar {
            Left = x, Top = y, Width = 512, Height = 46, AutoSize = false,
            Minimum = min, Maximum = max, Value = val,
            TickStyle = TickStyle.None, BackColor = PanelBg, ForeColor = Ink
        };
        Controls.Add(t);
        return t;
    }
    Button MakeButton(string text, string tip, int x, int y, int w)
    {
        var b = new Button {
            Text = text, Left = x, Top = y, Width = w, Height = 40,
            FlatStyle = FlatStyle.Standard, BackColor = BtnBg, ForeColor = Ink,
            Font = new Font("Microsoft YaHei", 11f, FontStyle.Bold)
        };
        tips.SetToolTip(b, tip);
        Controls.Add(b);
        return b;
    }

    // 当前背景对应的色块用金色粗边标出来，深色调之间也好分辨
    void MarkTheme(int active)
    {
        for (int i = 0; i < themeButtons.Length; i++) {
            if (themeButtons[i] == null) continue;
            themeButtons[i].FlatAppearance.BorderColor = (i == active)
                ? Color.FromArgb(255, 190, 80) : Color.FromArgb(160, 180, 205);
            themeButtons[i].FlatAppearance.BorderSize = (i == active) ? 3 : 1;
        }
    }

    public void SyncFromOwner()
    {
        loading = true;
        fishBar.Value  = Math.Max(fishBar.Minimum, Math.Min(fishBar.Maximum, owner.FishCount));
        decorBar.Value = Math.Max(decorBar.Minimum, Math.Min(decorBar.Maximum, owner.DecorCount));
        zoomBar.Value  = Math.Max(zoomBar.Minimum, Math.Min(zoomBar.Maximum, (int)Math.Round(owner.Zoom * 100)));
        speedBar.Value = Math.Max(speedBar.Minimum, Math.Min(speedBar.Maximum, (int)Math.Round(owner.Speed * 100)));
        fishLbl.Text  = "鱼数量     " + owner.FishCount;
        decorLbl.Text = "装饰物数量     " + owner.DecorCount;
        zoomLbl.Text  = "整体缩放     " + owner.Zoom.ToString("0.00") + "×";
        speedLbl.Text = "游动速度     " + owner.Speed.ToString("0.00") + "×";
        for (int i = 0; i < toggles.Length; i++) toggles[i].Checked = owner.FeatureOn(i);
        autoBox.Checked = owner.AutoStartEnabled();
        MarkTheme(owner.ThemeIndex);
        loading = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }
}

internal sealed class AquariumContext : ApplicationContext
{
    internal static readonly string[] THEMES = {
        "0a1a2f", "061b2b", "08271f", "15102a",
        "101018", "04263c", "1c1a12", "000000"
    };
    const string RunKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "ASCIIAquariumWallpaper";

    readonly string baseDir = AppDomain.CurrentDomain.BaseDirectory;
    string htmlPath, edgeExe, profileDir, cmdPath;
    int width, height, theme = 0, cmdSeq = 0, fishCount = 22, decorCount = 6, taskbarPx = 0;
    double zoom = 1.0, speed = 1.0;
    readonly bool[] featureOn = { true, true, true, true, true, true };
    IntPtr edgeHwnd = IntPtr.Zero, hookHandle = IntPtr.Zero;
    Process edgeProc;
    NotifyIcon tray;
    ToolStripMenuItem autoMenuItem;
    Win32.HookProc hookProc;
    ControlPanel panel;
    readonly object cmdLock = new object();
    // 命令队列：滑块拖动与「鼠标松开」会接连写命令，只写一条会被后一条覆盖，
    // 导致属性调整看起来没生效。保留最近若干条，页面按 seq 跳过旧的。
    readonly System.Collections.Generic.List<string> cmdQueue =
        new System.Collections.Generic.List<string>();
    bool embedded;

    void Log(string s)
    {
        try {
            File.AppendAllText(Path.Combine(baseDir, "aquarium.log"),
                DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
        } catch { }
    }

    public AquariumContext(bool showPanel)
    {
        Win32.SetProcessDPIAware();
        Log("=== 启动 ===");
        var bounds = Screen.PrimaryScreen.Bounds;
        var work = Screen.PrimaryScreen.WorkingArea;
        width = bounds.Width; height = bounds.Height;
        taskbarPx = Math.Max(0, bounds.Bottom - work.Bottom);
        Log("屏幕 " + width + "x" + height + "；任务栏占用 " + taskbarPx + "px");

        htmlPath = Path.Combine(baseDir, "aquarium.html");
        profileDir = Path.Combine(baseDir, ".aquarium-profile");
        cmdPath = Path.Combine(baseDir, "aquarium-cmd.js");
        edgeExe = FindEdge();

        tray = new NotifyIcon {
            Icon = MakeIcon(),
            Text = "ASCII 水族馆 · 双击打开控制面板",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        tray.DoubleClick += delegate { OpenPanel(); };

        if (!File.Exists(htmlPath)) {
            MessageBox.Show("找不到 aquarium.html，请把 exe 和 html 放在同一个文件夹里。",
                            "ASCII 水族馆", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Exit(); return;
        }
        if (edgeExe == null) {
            MessageBox.Show("没找到 Microsoft Edge。也可以直接双击 aquarium.html 在浏览器里看鱼。",
                            "ASCII 水族馆", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Exit(); return;
        }

        EnsureProfile();
        SendCmd("none", "");
        Start();
        InstallHook();
        Log("showPanel=" + showPanel);
        if (showPanel) {
            var t = new System.Windows.Forms.Timer { Interval = 600 };
            t.Tick += delegate { t.Stop(); t.Dispose(); OpenPanel(); };
            t.Start();
        }
    }

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开控制面板…", null, delegate { OpenPanel(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("重新投鱼", null, delegate { Respawn(); });
        menu.Items.Add("增加鱼数量  +", null, delegate { SetFish(fishCount + 3); });
        menu.Items.Add("减少鱼数量  -", null, delegate { SetFish(fishCount - 3); });
        menu.Items.Add("换个背景色", null, delegate { SetTheme(theme + 1); });
        menu.Items.Add("放大画面", null, delegate { SetZoom(zoom + 0.15); });
        menu.Items.Add("缩小画面", null, delegate { SetZoom(zoom - 0.15); });
        menu.Items.Add("暂停 / 继续", null, delegate { TogglePause(); });
        autoMenuItem = new ToolStripMenuItem("开机自动运行") {
            CheckOnClick = true, Checked = AutoStartEnabled()
        };
        autoMenuItem.Click += delegate { SetAutoStart(autoMenuItem.Checked); };
        menu.Items.Add(autoMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, delegate { Exit(); });
        return menu;
    }

    /* ---------- 状态 + 命令（都不重启窗口） ---------- */
    internal int FishCount { get { return fishCount; } }
    internal int ThemeIndex { get { return theme; } }
    internal int DecorCount { get { return decorCount; } }
    internal double Zoom { get { return zoom; } }
    internal double Speed { get { return speed; } }
    internal bool FeatureOn(int i) { return i >= 0 && i < featureOn.Length && featureOn[i]; }
    string ZoomStr() { return zoom.ToString("0.00", CultureInfo.InvariantCulture); }

    void SendCmd(string cmd, string value)
    {
        lock (cmdLock) {
            cmdSeq++;
            cmdQueue.Add("window.__aqApply&&window.__aqApply({seq:" + cmdSeq +
                         ",cmd:'" + cmd + "',value:'" + value + "'});");
            if (cmdQueue.Count > 24) cmdQueue.RemoveAt(0);
            try { File.WriteAllText(cmdPath, string.Join("\n", cmdQueue.ToArray())); }
            catch (Exception ex) { Log("写命令失败: " + ex.Message); }
        }
    }

    internal void SetFish(int n)
    {
        fishCount = Math.Max(1, Math.Min(90, n));
        SendCmd("fish", fishCount.ToString(CultureInfo.InvariantCulture));
    }
    internal void SetDecorCount(int n)
    {
        decorCount = Math.Max(0, Math.Min(14, n));
        SendCmd("decorcount", decorCount.ToString(CultureInfo.InvariantCulture));
    }
    internal void SetZoom(double z)
    {
        zoom = Math.Max(0.4, Math.Min(3.0, z));
        SendCmd("zoom", ZoomStr());
    }
    internal void SetSpeed(double s)
    {
        speed = Math.Max(0.2, Math.Min(3.0, s));
        SendCmd("speed", speed.ToString("0.00", CultureInfo.InvariantCulture));
    }
    internal void SetTheme(int i)
    {
        theme = (i % THEMES.Length + THEMES.Length) % THEMES.Length;
        SendCmd("bg", THEMES[theme]);
    }
    internal void SendToggle(string key)
    {
        string[] keys = { "plants", "bubbles", "decor", "motes", "surface", "glow" };
        int idx = Array.IndexOf(keys, key);
        if (idx >= 0) featureOn[idx] = !featureOn[idx];
        SendCmd("toggle", key);
    }
    internal void Respawn() { SendCmd("respawn", ""); }
    internal void TogglePause() { SendCmd("pause", ""); }

    internal void OpenPanel()
    {
        Log("OpenPanel");
        if (panel == null || panel.IsDisposed) panel = new ControlPanel(this);
        panel.SyncFromOwner();
        if (!panel.Visible) panel.Show();
        panel.Activate();
    }

    /* ---------- 开机自启（HKCU\...\Run，不需要管理员） ---------- */
    internal bool AutoStartEnabled()
    {
        try {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                return k != null && k.GetValue(RunName) != null;
        } catch { return false; }
    }
    internal void SetAutoStart(bool on)
    {
        try {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) {
                if (k == null) return;
                if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --autostart");
                else k.DeleteValue(RunName, false);
            }
            Log("开机自动运行 = " + on);
            if (autoMenuItem != null) autoMenuItem.Checked = on;
        } catch (Exception ex) { Log("自启设置失败: " + ex.Message); }
    }

    void EnsureProfile()
    {
        try {
            Directory.CreateDirectory(profileDir);
            string firstRun = Path.Combine(profileDir, "First Run");
            if (!File.Exists(firstRun)) File.WriteAllText(firstRun, "");
        } catch (Exception ex) { Log("profile: " + ex.Message); }
    }

    static string FindEdge()
    {
        string[] dirs = {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        foreach (var d in dirs) {
            var p = Path.Combine(d, @"Microsoft\Edge\Application\msedge.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    string Args()
    {
        string url = new Uri(htmlPath).AbsoluteUri + "?wallpaper=1&bg=" + THEMES[theme]
                   + "&zoom=" + ZoomStr()
                   + "&fish=" + fishCount.ToString(CultureInfo.InvariantCulture)
                   + "&speed=" + speed.ToString("0.00", CultureInfo.InvariantCulture)
                   + "&decorcount=" + decorCount.ToString(CultureInfo.InvariantCulture)
                   + "&tb=" + taskbarPx.ToString(CultureInfo.InvariantCulture);
        return string.Format(
            "--app=\"{0}\" --user-data-dir=\"{1}\" --window-size={2},{3} --window-position=0,0 " +
            "--no-first-run --no-default-browser-check --disable-search-engine-choice-screen " +
            "--hide-crash-restore-bubble --force-device-scale-factor=1 " +
            "--disable-gpu " +
            "--allow-file-access-from-files " +
            "--disable-background-timer-throttling --disable-backgrounding-occluded-windows " +
            "--disable-renderer-backgrounding --disable-features=CalculateNativeWinOcclusion",
            url, profileDir, width, height);
    }

    void CloseStale()
    {
        for (int i = 0; i < 4; i++) {
            IntPtr old = Win32.FindAquariumWindow();
            if (old == IntPtr.Zero) break;
            Win32.PostMessage(old, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(350);
        }
    }

    void InstallHook()
    {
        hookProc = MouseHook;
        hookHandle = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, hookProc,
                                            Win32.GetModuleHandle(null), 0);
        Log("鼠标钩子 hwnd=" + hookHandle);
    }

    IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)Win32.WM_LBUTTONUP) {
            try {
                var ms = (Win32.MSLLHOOKSTRUCT)
                    Marshal.PtrToStructure(lParam, typeof(Win32.MSLLHOOKSTRUCT));
                SendCmd("splash", ms.pt.X + "," + ms.pt.Y);
            } catch (Exception ex) { Log("hook err: " + ex.Message); }
        }
        return Win32.CallNextHookEx(hookHandle, nCode, wParam, lParam);
    }

    void Start()
    {
        try {
            CloseStale();
            Log("准备启动 Edge；profile=" + profileDir);
            edgeProc = Process.Start(new ProcessStartInfo(edgeExe, Args()) { UseShellExecute = false });
            Log("Process.Start -> pid=" + (edgeProc == null ? -1 : edgeProc.Id));

            edgeHwnd = IntPtr.Zero;
            for (int i = 0; i < 120; i++) {
                edgeHwnd = Win32.FindAquariumWindow();
                if (edgeHwnd != IntPtr.Zero) break;
                Thread.Sleep(100);
            }
            Log("窗口轮询结果 hwnd=" + edgeHwnd);
            if (edgeHwnd == IntPtr.Zero) {
                tray.ShowBalloonTip(4000, "ASCII 水族馆",
                    "没找到 Edge 应用窗口，可以双击 aquarium.html 预览。", ToolTipIcon.Warning);
                Log("没找到窗口，放弃嵌入");
                return;
            }

            IntPtr layer = IntPtr.Zero;
            for (int i = 0; i < 6 && layer == IntPtr.Zero; i++) {
                layer = Win32.FindWallpaperLayer();
                if (layer == IntPtr.Zero) Thread.Sleep(1000);
            }
            IntPtr defView = layer != IntPtr.Zero
                ? Win32.FindWindowEx(layer, IntPtr.Zero, "SHELLDLL_DefView", null) : IntPtr.Zero;
            Log("壁纸宿主=" + layer + "；DefView=" + defView);
            embedded = layer != IntPtr.Zero && Win32.SetParent(edgeHwnd, layer) != IntPtr.Zero;
            Log("SetParent -> embedded=" + embedded);

            int style = (int)(embedded ? (Win32.WS_CHILD | Win32.WS_VISIBLE)
                                       : (Win32.WS_POPUP | Win32.WS_VISIBLE));
            Win32.SetWindowLong(edgeHwnd, Win32.GWL_STYLE, style);
            Win32.SetWindowLong(edgeHwnd, Win32.GWL_EXSTYLE,
                (int)(Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE));

            IntPtr after = defView != IntPtr.Zero ? defView : new IntPtr(1);
            const int TOP_CROP = 48, EDGE_CROP = 16;
            uint flags = Win32.SWP_FLAGS & ~0x0004U;
            Win32.SetWindowPos(edgeHwnd, after, -EDGE_CROP, -TOP_CROP,
                               width + EDGE_CROP * 2, height + TOP_CROP + 16, flags);
            Win32.ShowWindow(edgeHwnd, Win32.SW_SHOW);
            Win32.InvalidateRect(edgeHwnd, IntPtr.Zero, true);
            Win32.UpdateWindow(edgeHwnd);
            Log("已置于图标层之下、四周铺满");
        } catch (Exception ex) {
            Log("异常: " + ex);
            tray.ShowBalloonTip(5000, "ASCII 水族馆", "启动出错，详情见 aquarium.log", ToolTipIcon.Error);
        }
    }

    internal void Exit()
    {
        if (hookHandle != IntPtr.Zero) { Win32.UnhookWindowsHookEx(hookHandle); hookHandle = IntPtr.Zero; }
        if (panel != null && !panel.IsDisposed) panel.Dispose();
        if (edgeHwnd != IntPtr.Zero && Win32.IsWindow(edgeHwnd))
            Win32.PostMessage(edgeHwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        CleanProfileCache();
        ExitThread();
    }

    // 退出时清掉 Edge 可再生的缓存 / 组件 / 指标，让 .aquarium-profile 不越用越大。
    // 关键文件（First Run 哨兵、Preferences、Storage、Extensions）一律不动，
    // 否则下次启动会重新跑首次运行向导。删除失败（文件仍被占用）就跳过，下次再清。
    void CleanProfileCache()
    {
        try {
            if (edgeProc != null) { try { edgeProc.WaitForExit(2000); } catch { } }
            Thread.Sleep(300);
            string[] cacheDirs = {
                "component_crx_cache", "extensions_crx_cache", "ShaderCache", "GrShaderCache",
                "BrowserMetrics", "Edge Entity Extraction", "EdgeLanguageDetectionModel",
                Path.Combine("Default", "Cache"), Path.Combine("Default", "Code Cache"),
                Path.Combine("Default", "GPUCache"), Path.Combine("Default", "DawnGraphiteCache"),
                Path.Combine("Default", "DawnWebGPUCache")
            };
            foreach (string rel in cacheDirs) {
                try {
                    string full = Path.Combine(profileDir, rel);
                    if (Directory.Exists(full)) Directory.Delete(full, true);
                } catch { }
            }
            Log("退出：已清理可再生缓存");
        } catch (Exception ex) { Log("清理缓存失败: " + ex.Message); }
    }

    static Icon MakeIcon()
    {
        // 优先用项目里的 aquarium.ico（编译时也通过 /win32icon 嵌进 exe）
        try {
            string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aquarium.ico");
            if (File.Exists(p)) return new Icon(p);
        } catch { }
        // 图标文件不在时回退：用字符画一条 ><>
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.FromArgb(10, 26, 47));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var f = new Font("Consolas", 13, FontStyle.Bold))
                g.DrawString("><>", f, new SolidBrush(Color.FromArgb(255, 178, 107)), -4, 6);
        }
        IntPtr h = bmp.GetHicon();
        Icon ico = (Icon)Icon.FromHandle(h).Clone();
        Win32.DestroyIcon(h);
        bmp.Dispose();
        return ico;
    }
}

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool created;
        using (var mutex = new Mutex(true, @"Local\ASCIIAquariumWallpaper", out created)) {
            if (!created) {
                MessageBox.Show("水族馆已经在运行啦，双击系统托盘里的小鱼可打开控制面板～",
                                "ASCII 水族馆", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (args != null && Array.IndexOf(args, "--autostart") >= 0) Thread.Sleep(6000);
            Application.EnableVisualStyles();
            bool showPanel = args != null && Array.IndexOf(args, "--panel") >= 0;
            Application.Run(new AquariumContext(showPanel));
        }
    }
}
