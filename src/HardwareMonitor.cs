using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.IO.Compression;
using System.Net;
using System.Windows.Forms;

public class HardwareMonitor : Form
{
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);
    [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
    static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdProcessId);

    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20;

    const int SNAP_DISTANCE = 30;
    const int REFRESH_INTERVAL = 2000;
    const int CORNER_RADIUS = 16;
    const int RIGHT_SNAP_OFFSET = 1;

    Label lblCpuName, lblCpuPct, lblCpuTemp;
    Label lblRamName, lblRamPct, lblRamGb;
    Label lblGpuName, lblGpuPct, lblGpuTemp, lblGpuGb;
    Label lblDiskName, lblDiskPct, lblDiskTemp, lblDiskGb;
    Label lblIO;
    Label lblFpsName, lblFpsValue;
    System.Windows.Forms.Timer timer;
    System.Windows.Forms.Timer rightClickTimer;

    // PresentMon
    Process presentMonProc;
    System.Threading.Thread pmThread;
    System.Collections.Generic.Dictionary<int, float> fpsByPid = new System.Collections.Generic.Dictionary<int, float>();
    System.Collections.Generic.Dictionary<int, string> nameByPid = new System.Collections.Generic.Dictionary<int, string>();
    int colApp = -1, colPid = -1, colMsBetween = -1;
    bool pmReady = false;
    bool locked = false;
    bool dragging = false;
    Point dragOffset;

    const int COL1_X = 12;
    const int COL2_X = 55;
    const int COL3_X = 100;
    const int COL4_X = 155;

    public HardwareMonitor()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.StartPosition = FormStartPosition.Manual;
        this.Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - 260 + RIGHT_SNAP_OFFSET, 100);
        this.Size = new Size(250, 162);
        this.Opacity = 0.82;
        this.TopMost = true;
        this.ShowInTaskbar = false;
        this.DoubleBuffered = true;

        Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, CORNER_RADIUS, CORNER_RADIUS));

        lblCpuName = MakeLabel(COL1_X, 12, 40); lblCpuName.Text = "CPU";
        lblCpuPct = MakeLabel(COL2_X, 12, 42);
        lblCpuTemp = MakeLabel(COL3_X, 12, 50);
        lblRamName = MakeLabel(COL1_X, 36, 40); lblRamName.Text = "RAM";
        lblRamPct = MakeLabel(COL2_X, 36, 42);
        lblRamGb = MakeLabel(COL4_X, 36, 90);
        lblGpuName = MakeLabel(COL1_X, 60, 40); lblGpuName.Text = "GPU";
        lblGpuPct = MakeLabel(COL2_X, 60, 42);
        lblGpuTemp = MakeLabel(COL3_X, 60, 50);
        lblGpuGb = MakeLabel(COL4_X, 60, 90);
        lblDiskName = MakeLabel(COL1_X, 84, 40); lblDiskName.Text = "DISK";
        lblDiskPct = MakeLabel(COL2_X, 84, 42);
        lblDiskTemp = MakeLabel(COL3_X, 84, 50);
        lblDiskGb = MakeLabel(COL4_X, 84, 90);
        lblIO = MakeLabel(COL2_X - 14, 108, 195);
        lblFpsName = MakeLabel(COL1_X, 132, 40); lblFpsName.Text = "FPS";
        lblFpsValue = MakeLabel(COL2_X, 132, 80);

        this.Controls.Add(lblCpuName); this.Controls.Add(lblCpuPct); this.Controls.Add(lblCpuTemp);
        this.Controls.Add(lblRamName); this.Controls.Add(lblRamPct); this.Controls.Add(lblRamGb);
        this.Controls.Add(lblGpuName); this.Controls.Add(lblGpuPct); this.Controls.Add(lblGpuTemp); this.Controls.Add(lblGpuGb);
        this.Controls.Add(lblDiskName); this.Controls.Add(lblDiskPct); this.Controls.Add(lblDiskTemp); this.Controls.Add(lblDiskGb);
        this.Controls.Add(lblIO);
        this.Controls.Add(lblFpsName);
        this.Controls.Add(lblFpsValue);

        var menu = new ContextMenu();
        var lockItem = new MenuItem("锁定");
        lockItem.Click += (s, e) => { locked = !locked; lockItem.Text = locked ? "解锁" : "锁定"; UpdateClickThrough(); };
        var exitItem = new MenuItem("退出");
        exitItem.Click += (s, e) => { this.Close(); };
        menu.MenuItems.Add(lockItem);
        menu.MenuItems.Add(exitItem);
        this.ContextMenu = menu;

        BindDrag(this);
        foreach (Control c in this.Controls) BindDrag(c);

        rightClickTimer = new System.Windows.Forms.Timer();
        rightClickTimer.Interval = 150;
        rightClickTimer.Tick += (s, e) =>
        {
            if (locked && (GetAsyncKeyState(0x02) & 0x8000) != 0)
            {
                var p = Cursor.Position;
                if (p.X >= this.Location.X && p.X <= this.Location.X + this.Width &&
                    p.Y >= this.Location.Y && p.Y <= this.Location.Y + this.Height)
                {
                    locked = false;
                    UpdateClickThrough();
                    foreach (MenuItem mi in this.ContextMenu.MenuItems)
                        if (mi.Text == "解锁") mi.Text = "锁定";
                    this.ContextMenu.Show(this, this.PointToClient(p));
                }
            }
        };
        rightClickTimer.Start();

        timer = new System.Windows.Forms.Timer();
        timer.Interval = REFRESH_INTERVAL;
        timer.Tick += (s, e) => UpdateStats();
        timer.Start();

        StartPresentMon();

        this.BringToFront();
        SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        UpdateClickThrough();
        UpdateStats();
    }

    void BindDrag(Control ctl)
    {
        ctl.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left && !locked)
            {
                dragging = true;
                dragOffset = new Point(Cursor.Position.X - this.Location.X, Cursor.Position.Y - this.Location.Y);
            }
        };
        ctl.MouseMove += (s, e) =>
        {
            if (dragging && !locked)
                this.Location = new Point(Cursor.Position.X - dragOffset.X, Cursor.Position.Y - dragOffset.Y);
        };
        ctl.MouseUp += (s, e) =>
        {
            if (dragging) { dragging = false; SnapToEdge(); }
        };
    }

    Label MakeLabel(int x, int y, int w)
    {
        var lbl = new Label();
        lbl.Location = new Point(x, y);
        lbl.Size = new Size(w, 22);
        lbl.Font = new Font("Consolas", 10f, FontStyle.Bold);
        lbl.ForeColor = Color.FromArgb(0, 200, 80);
        lbl.BackColor = Color.Transparent;
        return lbl;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try { if (presentMonProc != null && !presentMonProc.HasExited) presentMonProc.Kill(); }
        catch { }
        base.OnFormClosing(e);
    }

    void UpdateClickThrough()
    {
        int style = GetWindowLong(this.Handle, GWL_EXSTYLE);
        if (locked) style |= WS_EX_TRANSPARENT;
        else style &= ~WS_EX_TRANSPARENT;
        SetWindowLong(this.Handle, GWL_EXSTYLE, style);
    }

    void SnapToEdge()
    {
        var area = Screen.PrimaryScreen.WorkingArea;
        int x = this.Location.X;
        int y = this.Location.Y;

        if (x < area.Left + SNAP_DISTANCE) x = area.Left;
        else if (x + this.Width > area.Right - SNAP_DISTANCE) x = area.Right - this.Width + RIGHT_SNAP_OFFSET;

        if (y < area.Top + SNAP_DISTANCE) y = area.Top;
        else if (y + this.Height > area.Bottom - SNAP_DISTANCE) y = area.Bottom - this.Height;

        this.Location = new Point(x, y);
    }

    void UpdateStats()
    {
        try
        {
            // HWiNFO 数据源
            HwInfoData hw = HwInfoData.Read();

            double cpuUse = hw.Value("CPU", "Total CPU Usage", "Core Ultra");
            double cpuTemp = hw.Value("Enhanced", "CPU Package", "Enhanced");
            lblCpuPct.Text = string.Format("{0,3:F0}%", cpuUse);
            lblCpuTemp.Text = string.Format("{0:F0}°C", cpuTemp);

            // 内存（系统API，准确）
            var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
            ulong usedRAM = ci.TotalPhysicalMemory - ci.AvailablePhysicalMemory;
            double usedGB = Math.Round((double)usedRAM / GB, 1);
            double totalGB = Math.Round((double)ci.TotalPhysicalMemory / GB, 1);
            int ramPct = (int)((double)usedRAM / ci.TotalPhysicalMemory * 100);
            lblRamPct.Text = string.Format("{0,3}%", ramPct);
            lblRamGb.Text = string.Format("{0}/{1}GB", usedGB, totalGB);

            // GPU（HWiNFO 温度/占用，nvidia-smi 显存GB）
            double gpuUse = hw.Value("dGPU", "GPU Core Load", "dGPU");
            double gpuTemp = hw.Value("dGPU", "GPU Temperature", "dGPU");
            lblGpuPct.Text = string.Format("{0,3:F0}%", gpuUse);
            lblGpuTemp.Text = string.Format("{0:F0}°C", gpuTemp);
            lblGpuGb.Text = GetGpuMemGb();

            // 硬盘
            double diskAct = hw.MaxValue("Drive:", "Total Activity");
            double diskTemp = hw.MaxSmartTemp();
            lblDiskPct.Text = string.Format("{0,3:F0}%", diskAct);
            lblDiskTemp.Text = string.Format("{0:F0}°C", diskTemp);
            lblDiskGb.Text = GetTotalDiskUsage();

            var io = GetDiskSpeed();
            lblIO.Text = string.Format("R:{0}  W:{1}", io.r, io.w);

            UpdateFpsDisplay();
        }
        catch { }
    }

    const double GB = 1024.0 * 1024 * 1024;

    string GetGpuMemGb()
    {
        try
        {
            var p = new Process();
            p.StartInfo.FileName = "nvidia-smi";
            p.StartInfo.Arguments = "--query-gpu=memory.used,memory.total --format=csv,noheader,nounits";
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.CreateNoWindow = true;
            p.Start();
            string output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            var parts = output.Split(',');
            if (parts.Length >= 2)
            {
                double used = Math.Round(double.Parse(parts[0].Trim()) / 1024.0, 1);
                double total = Math.Round(double.Parse(parts[1].Trim()) / 1024.0, 0);
                return string.Format("{0}/{1}GB", used, total);
            }
        }
        catch { }
        return "";
    }

    string GetTotalDiskUsage()
    {
        try
        {
            long total = 0, used = 0;
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    total += drive.TotalSize;
                    used += drive.TotalSize - drive.AvailableFreeSpace;
                }
            }
            return string.Format("{0}/{1}GB",
                Math.Round((double)used / GB, 0), Math.Round((double)total / GB, 0));
        }
        catch { return ""; }
    }

    struct RW { public string r; public string w; }
    RW GetDiskSpeed()
    {
        var res = new RW();
        try
        {
            using (var readPc = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total"))
            using (var writePc = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total"))
            {
                readPc.NextValue(); writePc.NextValue();
                System.Threading.Thread.Sleep(100);
                res.r = FormatSpeed(readPc.NextValue());
                res.w = FormatSpeed(writePc.NextValue());
            }
        }
        catch { res.r = "0"; res.w = "0"; }
        return res;
    }

    string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= GB) return (bytesPerSec / GB).ToString("F2") + "GB/s";
        if (bytesPerSec >= 1024 * 1024) return (bytesPerSec / (1024 * 1024)).ToString("F1") + "MB/s";
        return (bytesPerSec / 1024).ToString("F0") + "KB/s";
    }

    void StartPresentMon()
    {
        try
        {
            presentMonProc = new Process();
            presentMonProc.StartInfo.FileName = Path.Combine(
                Path.GetDirectoryName(Application.ExecutablePath), "PresentMon.exe");
            if (!File.Exists(presentMonProc.StartInfo.FileName))
            {
                // 没带 PresentMon.exe 就跳过帧率功能（旧版这里会回退到写死的本机路径，已移除）
                return;
            }
            presentMonProc.StartInfo.Arguments = "--output_stdout --no_csv --no_console_stats --stop_existing_session";
            presentMonProc.StartInfo.UseShellExecute = false;
            presentMonProc.StartInfo.RedirectStandardOutput = true;
            presentMonProc.StartInfo.CreateNoWindow = true;
            presentMonProc.EnableRaisingEvents = true;
            presentMonProc.Start();

            pmThread = new System.Threading.Thread(PresentMonReadLoop);
            pmThread.IsBackground = true;
            pmThread.Start();
        }
        catch { }
    }

    void PresentMonReadLoop()
    {
        try
        {
            string line;
            while ((line = presentMonProc.StandardOutput.ReadLine()) != null)
            {
                var parts = line.Split(',');
                if (colApp == -1)
                {
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string h = parts[i].Trim();
                        if (h == "Application") colApp = i;
                        else if (h == "ProcessID") colPid = i;
                        else if (h == "MsBetweenPresents") colMsBetween = i;
                    }
                    if (colApp >= 0 && colPid >= 0 && colMsBetween >= 0) pmReady = true;
                    continue;
                }
                if (!pmReady || parts.Length <= colMsBetween) continue;
                try
                {
                    string appName = parts[colApp].Trim();
                    int pid = int.Parse(parts[colPid].Trim());
                    double msBetween = double.Parse(parts[colMsBetween].Trim());
                    if (msBetween <= 0) continue;
                    float fps = (float)(1000.0 / msBetween);
                    if (fps > 1000) continue;
                    lock (fpsByPid) { fpsByPid[pid] = fps; nameByPid[pid] = appName; }
                }
                catch { }
            }
        }
        catch { }
    }

    int GetForegroundPid()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return (int)pid;
        }
        catch { return 0; }
    }

    void UpdateFpsDisplay()
    {
        try
        {
            int fgPid = GetForegroundPid();
            float fps = 0;
            lock (fpsByPid)
            {
                if (fgPid > 0 && fpsByPid.ContainsKey(fgPid)) fps = fpsByPid[fgPid];
                else
                {
                    float best = 0;
                    foreach (var kv in fpsByPid) if (kv.Value > best) best = kv.Value;
                    fps = best;
                }
            }
            lblFpsValue.Text = fps > 0 ? string.Format("{0:F0} FPS", fps) : "-- FPS";
        }
        catch { }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new LinearGradientBrush(this.ClientRectangle,
            Color.FromArgb(35, 35, 50), Color.FromArgb(15, 15, 25), 90f))
            g.FillRectangle(brush, this.ClientRectangle);
        using (var pen = new Pen(Color.FromArgb(0, 200, 80), 1.5f))
        using (var path = new GraphicsPath())
        {
            int d = CORNER_RADIUS;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(this.Width - d - 1, 0, d, d, 270, 90);
            path.AddArc(this.Width - d - 1, this.Height - d - 1, d, d, 0, 90);
            path.AddArc(0, this.Height - d - 1, d, d, 90, 90);
            path.CloseFigure();
            g.DrawPath(pen, path);
        }
    }

    [STAThread]
    public static void Main()
    {
        if (!EnsureHwInfoReady())
        {
            if (MessageBox.Show(
                    "没有 HWiNFO 就取不到温度（CPU / GPU / 硬盘温度会为空）。\r\n\r\n仍要继续运行吗？",
                    "硬件监控", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
        }

        EnsureHwInfo();
        Application.EnableVisualStyles();
        Application.Run(new HardwareMonitor());
    }

    // ===== 没有 HWiNFO 时：尝试自动获取 =====

    /// <summary>共享内存模式的 HWiNFO 配置模板（用户拿到的 HWiNFO 没有 INI 时靠它工作）。</summary>
    static readonly string[] HWINFO_INI_LINES = new string[]
    {
        "[Settings]",
        "SensorsOnly=1",
        "Autorun=1",
        "OpenSystemSummary=0",
        "ShowWelcomeAndProgress=0",
        "MinimalizeMainWnd=1",
        "MinimalizeSensors=1",
        "MinimalizeSensorsClose=1",
        "SensorsSM=1",
        "OpenSensors=1",
        "SMMemSize=2097152",
        "AutoUpdate=0",
        "StartMinimized=1",
        "NoInfo=1",
        "AutoStart=1",
        "MinMainOnStart=1",
        "Theme=3",
        "SensorsFontFace=Segoe UI",
        "SensorsFontHeight=16",
    };

    static void EnsureHwInfoIni(string hwDir)
    {
        try
        {
            Directory.CreateDirectory(hwDir);
            string ini = Path.Combine(hwDir, "HWiNFO64.INI");
            if (!File.Exists(ini))
                File.WriteAllLines(ini, HWINFO_INI_LINES, Encoding.ASCII);
        }
        catch { }
    }

    /// <summary>HWiNFO64.exe 是否已就位；没有就尝试下载或引导用户提供。</summary>
    static bool EnsureHwInfoReady()
    {
        string appDir = Path.GetDirectoryName(Application.ExecutablePath);
        string hwDir = Path.Combine(appDir, "HWiNFO");
        string exe = Path.Combine(hwDir, "HWiNFO64.exe");
        EnsureHwInfoIni(hwDir);
        if (File.Exists(exe)) return true;

        // ① 先试直接下载（有些网络能过官方 CDN；被挡就走 ②）
        if (TryDownloadHwInfo(hwDir, exe)) return true;

        // ② 引导用户：打开下载页 → 选文件 → 自动解压安装
        var r = MessageBox.Show(
            "缺少 HWiNFO64.exe —— 它是免费的硬件传感器工具，温度数据要靠它。\r\n\r\n" +
            "点【是】：打开官网下载页（下 Portable 便携版），下载完回来选文件\r\n" +
            "点【否】：直接选择我已经下载好的文件（.zip 或 HWiNFO64.exe）\r\n" +
            "点【取消】：这次不装（其它数据照常显示，温度为空）",
            "硬件监控 —— 需要 HWiNFO", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);

        if (r == DialogResult.Cancel) return false;

        if (r == DialogResult.Yes)
        {
            try { Process.Start("https://www.hwinfo.com/download/"); } catch { }
            if (MessageBox.Show(
                    "下载完成后点【确定】，然后选中下载到的文件（zip 或 HWiNFO64.exe），程序会自动装好。",
                    "硬件监控", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return false;
        }

        using (var dlg = new OpenFileDialog())
        {
            dlg.Title = "选择已下载的 HWiNFO（.zip 或 HWiNFO64.exe）";
            dlg.Filter = "HWiNFO 压缩包或程序 (*.zip;*.exe)|*.zip;*.exe|所有文件 (*.*)|*.*";
            if (dlg.ShowDialog() != DialogResult.OK) return false;
            return InstallHwInfoFrom(dlg.FileName, hwDir, exe);
        }
    }

    /// <summary>从 zip 或单个 exe 安装到 hwDir。</summary>
    static bool InstallHwInfoFrom(string file, string hwDir, string exe)
    {
        try
        {
            Directory.CreateDirectory(hwDir);
            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using (var zip = ZipFile.OpenRead(file))
                {
                    bool got = false;
                    foreach (var e in zip.Entries)
                    {
                        if (e.Name.Equals("HWiNFO64.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            e.ExtractToFile(exe, true);
                            got = true;
                        }
                        else if (e.Name.Equals("HWiNFO64.INI", StringComparison.OrdinalIgnoreCase))
                        {
                            string dst = Path.Combine(hwDir, "HWiNFO64.INI");
                            if (!File.Exists(dst)) e.ExtractToFile(dst, true);
                        }
                    }
                    if (!got)
                    {
                        MessageBox.Show("这个压缩包里没找到 HWiNFO64.exe，请确认下载的是 HWiNFO 的 Portable 版。",
                            "硬件监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
            }
            else
            {
                File.Copy(file, exe, true);
            }
            EnsureHwInfoIni(hwDir);
            return File.Exists(exe);
        }
        catch (Exception ex)
        {
            MessageBox.Show("安装 HWiNFO 失败：" + ex.Message, "硬件监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    /// <summary>尝试直接下载官方便携包（失败返回 false，不抛异常）。</summary>
    static bool TryDownloadHwInfo(string hwDir, string exe)
    {
        try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }
        catch { }
        string[] urls = new string[]
        {
            "https://www.hwinfo.com/files/hwi_854.zip",
        };
        foreach (var u in urls)
        {
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "hwinfo-auto.zip");
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    wc.DownloadFile(u, tmp);
                }
                if (InstallHwInfoFrom(tmp, hwDir, exe))
                {
                    try { File.Delete(tmp); } catch { }
                    return true;
                }
            }
            catch { }
        }
        return false;
    }

    static void EnsureHwInfo()
    {
        string appDir = Path.GetDirectoryName(Application.ExecutablePath);
        string log = Path.Combine(appDir, "hwinfo_launch.log");
        try
        {
            foreach (var p in Process.GetProcessesByName("HWiNFO64"))
            {
                try { p.Kill(); } catch { }
            }
            System.Threading.Thread.Sleep(2000);

            string hwDir = Path.Combine(appDir, "HWiNFO");
            EnsureHwInfoIni(hwDir);
            string exe = Path.Combine(hwDir, "HWiNFO64.exe");
            if (File.Exists(exe))
            {
                System.Diagnostics.Process.Start("cmd.exe",
                    "/c start \"\" \"" + exe + "\"");
                File.WriteAllText(log, "launched at " + DateTime.Now);
            }
            else
            {
                File.WriteAllText(log, "exe not found: " + exe);
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(log, "error: " + ex.Message + "\n" + ex.StackTrace);
        }
    }
}

// ===== HWiNFO 共享内存读取 =====
class Reading
{
    public string sensor;
    public string label;
    public double value;
}

class HwInfoData
{
    System.Collections.Generic.List<Reading> list = new System.Collections.Generic.List<Reading>();

    public static HwInfoData Read()
    {
        var d = new HwInfoData();
        try
        {
            using (var mmf = MemoryMappedFile.OpenExisting("Global\\HWiNFO_SENS_SM2", MemoryMappedFileRights.Read))
            using (var view = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read))
            {
                byte[] buf = new byte[view.Length];
                view.Read(buf, 0, buf.Length);
                if (BitConverter.ToUInt32(buf, 0) != 0x53695748) return d;

                uint offSensor = BitConverter.ToUInt32(buf, 20);
                uint szSensor = BitConverter.ToUInt32(buf, 24);
                uint nSensor = BitConverter.ToUInt32(buf, 28);
                uint offReading = BitConverter.ToUInt32(buf, 32);
                uint szReading = BitConverter.ToUInt32(buf, 36);
                uint nReading = BitConverter.ToUInt32(buf, 40);

                string[] sensorNames = new string[nSensor];
                for (int i = 0; i < nSensor; i++)
                {
                    int b = (int)(offSensor + i * szSensor);
                    sensorNames[i] = AStr(buf, b + 8, 256);
                }

                for (int j = 0; j < nReading; j++)
                {
                    int b = (int)(offReading + j * szReading);
                    uint sidx = BitConverter.ToUInt32(buf, b + 4);
                    string label = AStr(buf, b + 12, 256);
                    double val = BitConverter.ToDouble(buf, b + 284);
                    string sn = sidx < sensorNames.Length ? sensorNames[sidx] : "";
                    d.list.Add(new Reading { sensor = sn, label = label, value = val });
                }
            }
        }
        catch { }
        return d;
    }

    // 取匹配 sensor关键字 且 label精确 的第一个值；sensorMustContain 用于区分CPU主区
    public double Value(string sensorKey, string labelExact, string sensorMust)
    {
        foreach (var r in list)
            if (r.sensor.Contains(sensorMust) && r.label.Trim() == labelExact)
                return r.value;
        return double.NaN;
    }

    public double MaxValue(string sensorKey, string labelExact)
    {
        double max = 0;
        foreach (var r in list)
            if (r.sensor.Contains(sensorKey) && r.label.Trim() == labelExact && r.value > max)
                max = r.value;
        return max;
    }

    // 所有物理盘 Drive Temperature 的最大值
    public double MaxSmartTemp()
    {
        double max = 0;
        foreach (var r in list)
            if (r.sensor.Contains("S.M.A.R.T") && r.label.Trim() == "Drive Temperature" && r.value > max)
                max = r.value;
        return max;
    }

    static string AStr(byte[] buf, int off, int n)
    {
        int max = Math.Min(off + n, buf.Length);
        var sb = new StringBuilder();
        for (int i = off; i < max; i++)
        {
            if (buf[i] == 0) break;
            sb.Append((char)buf[i]);
        }
        return sb.ToString();
    }
}
