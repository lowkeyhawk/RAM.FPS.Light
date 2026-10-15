using System;
using System.Diagnostics;
using System.Drawing;
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

        // Dragging
        private bool isDragging;
        private Point dragStart;

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
            Opacity = 0.85;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(240, 80);
            Location = new Point(
                Screen.PrimaryScreen.WorkingArea.Right - Width - 20,
                Screen.PrimaryScreen.WorkingArea.Bottom - Height - 20
            );
            DoubleBuffered = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;

            ramLabel = new Label
            {
                Text = "RAM: --",
                ForeColor = Color.Lime,
                BackColor = Color.Transparent,
                Font = new Font("Consolas", 12f, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 40
            };

            fpsLabel = new Label
            {
                Text = "FPS: --",
                ForeColor = Color.Cyan,
                BackColor = Color.Transparent,
                Font = new Font("Consolas", 12f, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Bottom,
                Height = 40
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
            try
            {
                var factoryGuid = new Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369");
                int hr = DxgiNative.CreateDXGIFactory1(ref factoryGuid, out dxgiFactory);
                if (hr != 0) { dxgiOutput = IntPtr.Zero; return; }

                hr = DxgiNative.EnumAdapters(dxgiFactory, 0, out IntPtr adapter);
                if (hr != 0) { dxgiOutput = IntPtr.Zero; return; }

                hr = DxgiNative.EnumOutputs(adapter, 0, out dxgiOutput);
                if (hr != 0) { dxgiOutput = IntPtr.Zero; return; }

                lastFpsTime = DateTime.Now;
            }
            catch { dxgiOutput = IntPtr.Zero; }
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

            if (dxgiOutput != IntPtr.Zero)
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
                }
                catch { }
            }
            fpsLabel.Text = string.Format("FPS: {0}", currentFps);
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
