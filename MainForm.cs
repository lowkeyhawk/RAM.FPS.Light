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
        private Label ramLabel;
        private Label fpsLabel;
        private System.Windows.Forms.Timer timer;

        // RAM
        private PerformanceCounter ramAvailable;
        private ulong totalRam;

        // FPS via DXGI
        private IntPtr dxgiFactory;
        private IntPtr dxgiOutput;
        private uint lastPresentCount;
        private DateTime lastFpsTime;
        private int currentFps;
        private bool fpsAvailable;

        // Dragging
        private bool isDragging;
        private Point dragStart;

        // Liquid glass design tokens
        private static readonly Color AccentColor = Color.FromArgb(0xFF, 0x00, 0x88, 0xFF);
        private static readonly Color GlassSurface = Color.FromArgb(102, 15, 15, 20); // dark 40%
        private static readonly Color GlassBorder = Color.FromArgb(60, 255, 255, 255); // subtle white edge
        private static readonly Color InnerHighlight = Color.FromArgb(40, 255, 255, 255); // top glow
        private static readonly Color TextColor = Color.White;

        public MainForm()
        {
            InitializeComponent();
            InitializeRam();
            InitializeFps();
            InitializeTimer();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            EnableAcrylic();
        }

        private void InitializeComponent()
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(280, 110);
            Location = new Point(
                Screen.PrimaryScreen.WorkingArea.Right - Width - 20,
                Screen.PrimaryScreen.WorkingArea.Bottom - Height - 20
            );
            DoubleBuffered = true;
            ShowInTaskbar = false;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            ramLabel = new Label
            {
                Text = "RAM: --",
                ForeColor = TextColor,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 55
            };

            fpsLabel = new Label
            {
                Text = "FPS: --",
                ForeColor = TextColor,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Bottom,
                Height = 55
            };

            Controls.Add(ramLabel);
            Controls.Add(fpsLabel);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Exit", null, (s, e) => Application.Exit());
            ContextMenuStrip = menu;

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
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
            fpsAvailable = false;
            try
            {
                var factoryGuid = new Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369");
                int hr = DxgiNative.CreateDXGIFactory1(ref factoryGuid, out dxgiFactory);
                if (hr != 0) return;

                hr = DxgiNative.EnumAdapters(dxgiFactory, 0, out IntPtr adapter);
                if (hr != 0) return;

                hr = DxgiNative.EnumOutputs(adapter, 0, out dxgiOutput);
                if (hr != 0) return;

                fpsAvailable = true;
                lastFpsTime = DateTime.Now;
            }
            catch { fpsAvailable = false; }
        }

        private void InitializeTimer()
        {
            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += OnTimerTick;
            timer.Start();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            float available = ramAvailable.NextValue();
            ulong used = totalRam - (ulong)(available * 1024 * 1024);
            double pct = (double)used / totalRam * 100;
            ramLabel.Text = string.Format("RAM: {0} / {1} GB ({2:F1}%)", used / 1024 / 1024, totalRam / 1024 / 1024, pct);

            if (fpsAvailable)
            {
                try
                {
                    DxgiNative.GetFrameStatistics(dxgiOutput, out var stats);
                    var now = DateTime.Now;
                    double elapsed = (now - lastFpsTime).TotalSeconds;
                    if (elapsed > 0.5)
                    {
                        uint delta = stats.PresentCount - lastPresentCount;
                        currentFps = (int)(delta / elapsed);
                        lastPresentCount = stats.PresentCount;
                        lastFpsTime = now;
                    }
                    fpsLabel.Text = string.Format("FPS: {0}", currentFps);
                }
                catch { fpsLabel.Text = "FPS: error"; }
            }
            else
            {
                fpsLabel.Text = "FPS: fullscreen only";
            }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { isDragging = true; dragStart = e.Location; }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
                Location = new Point(Left + e.X - dragStart.X, Top + e.Y - dragStart.Y);
        }

        private void OnMouseUp(object sender, MouseEventArgs e) { isDragging = false; }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Let DWM acrylic show through
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Dark glass surface - rounded rectangle
            using (var path = GetRoundedRect(0, 0, Width - 1, Height - 1, 20))
            using (var surfaceBrush = new SolidBrush(GlassSurface))
            {
                e.Graphics.FillPath(surfaceBrush, path);
            }

            // Inner highlight - subtle white glow at top
            using (var path = GetRoundedRect(2, 2, Width - 5, Height - 5, 18))
            using (var highlightBrush = new SolidBrush(InnerHighlight))
            {
                e.Graphics.DrawPath(new Pen(highlightBrush, 2), path);
            }

            // Glass border - subtle white edge
            using (var path = GetRoundedRect(0.5f, 0.5f, Width - 2, Height - 2, 20))
            using (var borderPen = new Pen(GlassBorder, 1.5f))
            {
                e.Graphics.DrawPath(borderPen, path);
            }

            // Accent line at top - liquid glass highlight
            using (var accentPen = new Pen(AccentColor, 2.5f))
            {
                e.Graphics.DrawLine(accentPen, 24, 1.5f, Width - 24, 1.5f);
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

        private void EnableAcrylic()
        {
            if (IsHandleCreated)
            {
                try
                {
                    var accent = new AccentPolicy
                    {
                        AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                        AccentFlags = 2,
                        GradientColor = 0x99000000,
                        AnimationId = 0
                    };
                    var data = Marshal.AllocHGlobal(Marshal.SizeOf(accent));
                    Marshal.StructureToPtr(accent, data, false);
                    var attr = new WindowCompositionAttributeData
                    {
                        Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                        Data = data,
                        SizeOfData = Marshal.SizeOf(accent)
                    };
                    SetWindowCompositionAttribute(Handle, ref attr);
                    Marshal.FreeHGlobal(data);
                }
                catch { }
            }
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

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

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

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_INVALID_STATE = 5
        }
    }

    internal static class DxgiNative
    {
        [DllImport("dxgi.dll", PreserveSig = true)]
        public static extern int CreateDXGIFactory1([In] ref Guid riid, [Out] out IntPtr ppFactory);

        [DllImport("dxgi.dll", PreserveSig = true)]
        public static extern int EnumAdapters(IntPtr pFactory, uint Adapter, out IntPtr ppAdapter);

        [DllImport("dxgi.dll", PreserveSig = true)]
        public static extern int EnumOutputs(IntPtr pAdapter, uint Output, out IntPtr ppOutput);

        [DllImport("dxgi.dll", PreserveSig = true)]
        public static extern int GetFrameStatistics(IntPtr pOutput, out DXGI_FRAME_STATISTICS pStats);

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_FRAME_STATISTICS
        {
            public uint PresentCount;
            public uint PresentRefreshCount;
            public uint SyncRefreshCount;
            public long SyncQPCTime;
            public long SyncGPUTime;
        }
    }
}
