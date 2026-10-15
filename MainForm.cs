using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RamFpsOverlay
{
    public class MainForm : Form
    {
        private System.Windows.Forms.Timer timer;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;

        private string ramText = "RAM: --";
        private string fpsText = "FPS: --";

        private PerformanceCounter ramAvailable;
        private ulong totalRam;

        private uint lastDwmFrame;
        private DateTime lastFpsTime;
        private int currentFps;

        private bool isDragging;
        private Point dragStart;
        private bool isHovered;
        private Point mousePos;

        private Font textFont;

        private static readonly Color AccentLime = Color.FromArgb(0xFF, 0xC8, 0xFF, 0x00);
        private static readonly Color GlassSurface = Color.FromArgb(60, 15, 15, 20);
        private static readonly Color GlassSurfaceHover = Color.FromArgb(85, 20, 20, 28);
        private static readonly Color GlassRim = Color.FromArgb(80, 255, 255, 255);
        private static readonly Color GlassRimHover = Color.FromArgb(120, 255, 255, 255);
        private static readonly Color GlassRimInner = Color.FromArgb(40, 255, 255, 255);
        private static readonly Color SeparatorLine = Color.FromArgb(40, 255, 255, 255);

        public MainForm()
        {
            InitializeComponent();
            InitializeRam();
            InitializeTimer();
        }

        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED;
                return cp;
            }
        }

        private void InitializeComponent()
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            Size = new Size(300, 120);
            Location = new Point(
                Screen.PrimaryScreen.WorkingArea.Right - Width - 20,
                Screen.PrimaryScreen.WorkingArea.Bottom - Height - 20
            );

            textFont = new Font("Segoe UI", 13f, FontStyle.Bold);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Exit", null, (s, e) => Application.Exit());
            ContextMenuStrip = menu;

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            MouseEnter += (s, e) => { isHovered = true; Render(); };
            MouseLeave += (s, e) => { isHovered = false; Render(); };
            VisibleChanged += (s, e) => { if (Visible) Render(); };

            InitializeTray();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Render();
        }

        private void InitializeTray()
        {
            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(15, 15, 20));
                g.DrawRectangle(new Pen(Color.FromArgb(80, 255, 255, 255)), 0, 0, 15, 15);
                g.DrawLine(new Pen(AccentLime, 2), 2, 1, 13, 1);
                g.DrawLine(Pens.White, 3, 6, 12, 6);
                g.DrawLine(new Pen(AccentLime, 1), 3, 10, 12, 10);

                IntPtr hIcon = bmp.GetHicon();
                Icon icon;
                using (var tmp = Icon.FromHandle(hIcon))
                    icon = (Icon)tmp.Clone();
                DestroyIcon(hIcon);

                trayIcon = new NotifyIcon
                {
                    Icon = icon,
                    Text = "RAM + FPS Overlay",
                    Visible = true
                };
            }

            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Exit", null, (s, e) => Application.Exit());
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += (s, e) => { Visible = !Visible; };
        }

        private void InitializeRam()
        {
            ramAvailable = new PerformanceCounter("Memory", "Available MBytes");
            ramAvailable.NextValue();
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            GlobalMemoryStatusEx(ref status);
            totalRam = status.ullTotalPhys;
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
            float availableMB = ramAvailable.NextValue();
            ulong availableBytes = (ulong)(availableMB * 1024 * 1024);
            ulong usedBytes = availableBytes < totalRam ? totalRam - availableBytes : 0;

            double usedMB = usedBytes / 1024.0 / 1024.0;
            double totalMB = totalRam / 1024.0 / 1024.0;
            double pct = (double)usedBytes / totalRam * 100;
            ramText = string.Format("RAM: {0:F0} / {1:F0} MB ({2:F1}%)", usedMB, totalMB, pct);

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
                fpsText = string.Format("FPS: {0}", currentFps);
            }
            catch { fpsText = "FPS: --"; }

            Render();
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                dragStart = e.Location;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            mousePos = e.Location;
            if (isDragging)
                Location = new Point(Left + e.X - dragStart.X, Top + e.Y - dragStart.Y);
            else
                Render();
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            isDragging = false;
        }

        private void Render()
        {
            if (!IsHandleCreated || !Visible) return;

            using (var bmp = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                using (var path = GetRoundedRect(0, 0, Width - 1, Height - 1, 20))
                using (var brush = new SolidBrush(isHovered ? GlassSurfaceHover : GlassSurface))
                    g.FillPath(brush, path);

                using (var path = GetRoundedRect(0.5f, 0.5f, Width - 2, Height - 2, 20))
                using (var pen = new Pen(isHovered ? GlassRimHover : GlassRim, isHovered ? 2.5f : 2f))
                    g.DrawPath(pen, path);

                using (var path = GetRoundedRect(2.5f, 2.5f, Width - 6, Height - 6, 18))
                using (var pen = new Pen(GlassRimInner, 1f))
                    g.DrawPath(pen, path);

                if (isHovered)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(25, 255, 255, 255)))
                        g.FillEllipse(brush, mousePos.X - 30, mousePos.Y - 30, 60, 60);
                }

                using (var pen = new Pen(SeparatorLine, 1f))
                    g.DrawLine(pen, 24, Height / 2, Width - 24, Height / 2);
                using (var pen = new Pen(AccentLime, 2.5f))
                    g.DrawLine(pen, 24, 2, Width - 24, 2);

                var topRect = new RectangleF(0, 0, Width, Height / 2f);
                var botRect = new RectangleF(0, Height / 2f, Width, Height / 2f);
                using (var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                using (var shadow = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                using (var white = new SolidBrush(Color.White))
                using (var lime = new SolidBrush(AccentLime))
                {
                    g.DrawString(ramText, textFont, shadow, new RectangleF(topRect.X + 1, topRect.Y + 1, topRect.Width, topRect.Height), sf);
                    g.DrawString(ramText, textFont, white, topRect, sf);
                    g.DrawString(fpsText, textFont, shadow, new RectangleF(botRect.X + 1, botRect.Y + 1, botRect.Width, botRect.Height), sf);
                    g.DrawString(fpsText, textFont, lime, botRect, sf);
                }

                SetLayeredBitmap(bmp);
            }
        }

        private void SetLayeredBitmap(Bitmap bmp)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBmp = IntPtr.Zero;
            IntPtr oldBmp = IntPtr.Zero;
            try
            {
                hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                oldBmp = SelectObject(memDc, hBmp);

                var size = new Size(bmp.Width, bmp.Height);
                var src = new Point(0, 0);
                var topPos = new Point(Left, Top);
                var blend = new BLENDFUNCTION
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = 1
                };

                UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref src, 0, ref blend, 2);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
                if (hBmp != IntPtr.Zero)
                {
                    SelectObject(memDc, oldBmp);
                    DeleteObject(hBmp);
                }
                DeleteDC(memDc);
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
            textFont?.Dispose();
            trayIcon?.Dispose();
            base.OnFormClosing(e);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize,
            IntPtr hdcSrc, ref Point pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

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
