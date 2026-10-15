using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RamFpsOverlay
{
    public class MainForm : Form
    {
        private TableLayoutPanel table;
        private Label ramLabel;
        private Label fpsLabel;
        private System.Windows.Forms.Timer timer;

        // RAM
        private PerformanceCounter ramAvailable;
        private ulong totalRam;

        // FPS via DWM (Desktop Window Manager) — works everywhere
        private uint lastDwmFrame;
        private DateTime lastFpsTime;
        private int currentFps;
        private bool fpsAvailable;

        // Dragging
        private bool isDragging;
        private Point dragStart;

        // Hover/press state
        private bool isHovered;
        private bool isPressed;
        private Point mousePos;

        // Glassmorphism tokens
        private static readonly Color AccentLime = Color.FromArgb(0xFF, 0xC8, 0xFF, 0x00);
        private static readonly Color GlassSurface = Color.FromArgb(220, 15, 15, 20);
        private static readonly Color GlassSurfaceHover = Color.FromArgb(230, 20, 20, 28);
        private static readonly Color GlassRim = Color.FromArgb(80, 255, 255, 255);
        private static readonly Color GlassRimHover = Color.FromArgb(120, 255, 255, 255);
        private static readonly Color GlassRimInner = Color.FromArgb(40, 255, 255, 255);
        private static readonly Color SeparatorLine = Color.FromArgb(30, 255, 255, 255);
        private static readonly Color TextWhite = Color.White;
        private static readonly Color TextLime = AccentLime;

        public MainForm()
        {
            InitializeComponent();
            InitializeRam();
            InitializeFps();
            InitializeTimer();
        }

        private void InitializeComponent()
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(300, 120);
            Location = new Point(
                Screen.PrimaryScreen.WorkingArea.Right - Width - 20,
                Screen.PrimaryScreen.WorkingArea.Bottom - Height - 20
            );
            DoubleBuffered = true;
            ShowInTaskbar = false;
            Visible = true;
            WindowState = FormWindowState.Normal;
            BackColor = Color.FromArgb(15, 15, 20);

            table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            ramLabel = new Label
            {
                Text = "RAM: --",
                ForeColor = TextWhite,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };

            fpsLabel = new Label
            {
                Text = "FPS: --",
                ForeColor = TextLime,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };

            table.Controls.Add(ramLabel, 0, 0);
            table.Controls.Add(fpsLabel, 0, 1);
            Controls.Add(table);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Exit", null, (s, e) => Application.Exit());
            ContextMenuStrip = menu;

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            MouseEnter += OnMouseEnter;
            MouseLeave += OnMouseLeave;
        }

        private void InitializeRam()
        {
            ramAvailable = new PerformanceCounter("Memory", "Available MBytes");
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            GlobalMemoryStatusEx(ref status);
            totalRam = status.ullTotalPhys;
        }

        private void InitializeFps()
        {
            // DWM is always available on Windows 8+
            fpsAvailable = true;
            lastFpsTime = DateTime.Now;
        }

        private void InitializeTimer()
        {
            timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += OnTimerTick;
            timer.Start();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            // RAM in MB
            float availableMB = ramAvailable.NextValue();
            ulong availableBytes = (ulong)(availableMB * 1024 * 1024);
            ulong usedBytes = totalRam - availableBytes;

            double usedMB = usedBytes / 1024.0 / 1024.0;
            double totalMB = totalRam / 1024.0 / 1024.0;
            double pct = (double)usedBytes / totalRam * 100;

            ramLabel.Text = string.Format("RAM: {0:F0} / {1:F0} MB ({2:F1}%)", usedMB, totalMB, pct);

            // FPS via DWM — works everywhere (desktop, browser, games)
            if (fpsAvailable)
            {
                try
                {
                    var timing = new DwmNative.DWM_TIMING_INFO();
                    timing.cbSize = Marshal.SizeOf(timing);
                    DwmNative.DwmGetCompositionTimingInfo(IntPtr.Zero, ref timing);
                    var now = DateTime.Now;
                    double elapsed = (now - lastFpsTime).TotalSeconds;
                    if (elapsed > 0.5)
                    {
                        uint delta = timing.cFrame - lastDwmFrame;
                        currentFps = (int)(delta / elapsed);
                        lastDwmFrame = timing.cFrame;
                        lastFpsTime = now;
                    }
                    fpsLabel.Text = string.Format("FPS: {0}", currentFps);
                }
                catch { fpsLabel.Text = "FPS: --"; }
            }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                isPressed = true;
                dragStart = e.Location;
                Invalidate();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            mousePos = e.Location;
            if (isDragging)
            {
                Location = new Point(Left + e.X - dragStart.X, Top + e.Y - dragStart.Y);
            }
            if (!isHovered)
            {
                isHovered = true;
                Invalidate();
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            isDragging = false;
            isPressed = false;
            Invalidate();
        }

        private void OnMouseEnter(object sender, EventArgs e)
        {
            isHovered = true;
            Invalidate();
        }

        private void OnMouseLeave(object sender, EventArgs e)
        {
            isHovered = false;
            isPressed = false;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Glass surface - dark frosted glass
            var surfaceColor = isHovered ? GlassSurfaceHover : GlassSurface;
            using (var path = GetRoundedRect(0, 0, Width - 1, Height - 1, 20))
            using (var surfaceBrush = new SolidBrush(surfaceColor))
            {
                e.Graphics.FillPath(surfaceBrush, path);
            }

            // Drop shadow
            for (int i = 1; i <= 4; i++)
            {
                using (var path = GetRoundedRect(i * 0.5f, i * 0.5f, Width - 1 - i, Height - 1 - i, 20))
                using (var shadowPen = new Pen(Color.FromArgb(15, 0, 0, 0), 1f))
                {
                    e.Graphics.DrawPath(shadowPen, path);
                }
            }

            // Rim - brighter on hover
            var rimColor = isHovered ? GlassRimHover : GlassRim;
            using (var path = GetRoundedRect(0.5f, 0.5f, Width - 2, Height - 2, 20))
            using (var rimPen = new Pen(rimColor, isHovered ? 2.5f : 2f))
            {
                e.Graphics.DrawPath(rimPen, path);
            }

            // Inner glow
            using (var path = GetRoundedRect(2.5f, 2.5f, Width - 6, Height - 6, 18))
            using (var innerPen = new Pen(GlassRimInner, 1f))
            {
                e.Graphics.DrawPath(innerPen, path);
            }

            // Specular highlight on hover
            if (isHovered)
            {
                using (var specularBrush = new SolidBrush(Color.FromArgb(25, 255, 255, 255)))
                {
                    e.Graphics.FillEllipse(specularBrush, mousePos.X - 30, mousePos.Y - 30, 60, 60);
                }
            }

            // Separator line
            using (var sepPen = new Pen(SeparatorLine, 1f))
            {
                e.Graphics.DrawLine(sepPen, 24, Height / 2, Width - 24, Height / 2);
            }

            // Lime accent line at top
            using (var accentPen = new Pen(AccentLime, 2.5f))
            {
                e.Graphics.DrawLine(accentPen, 24, 2, Width - 24, 2);
            }
        }

        private static GraphicsPath GetRoundedRect(float x, float y, float w, float h, float r)
        {
            var path = new GraphicsPath();
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            timer?.Stop();
            ramAvailable?.Dispose();
            base.OnFormClosing(e);
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }

    internal static class DwmNative
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DWM_TIMING_INFO timingInfo);

        [StructLayout(LayoutKind.Sequential)]
        public struct DWM_TIMING_INFO
        {
            public int cbSize;
            public long qpcRefreshPeriod;
            public long qpcComposeTime;
            public long qpcFrameDuration;
            public long qpcFramePresentationTime;
            public long qpcRefresh;
            public long qpcCompositionTime;
            public uint cRefresh;
            public uint cDXRefresh;
            public uint cFrame;
            public uint cDXPresent;
            public long qpcCompositionStart;
            public uint cRefreshesSinceLastModeChange;
            public uint cRefreshes;
            public long qpcPresentationTime;
            public uint cFrames;
            public uint cDXFrame;
            public long qpcFrameStart;
            public uint cFramesSinceLastModeChange;
            public uint cFramesRefreshed;
        }
    }
}
