using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace RamFpsOverlay
{
    /// <summary>
    /// Counts real DirectX presents via the Microsoft-Windows-DXGI ETW provider.
    ///
    /// Event 42 ("Present/Start" = task Present + opcode Start) fires inside the
    /// presenting process, so the ETW header PID identifies the app that drew the
    /// frame. This works windowed, borderless and exclusive fullscreen, unlike
    /// IDXGIOutput.GetFrameStatistics (fullscreen only) or the DWM compositor
    /// counter (unavailable on some machines, and blind to DirectX games).
    ///
    /// Rates are measured from the event timestamps, not from delivery time. An
    /// ETW real-time session hands events over in ~1 second bursts (the default
    /// flush timer, which this API does not expose), so a wall-clock window can
    /// include a partial burst and report anywhere from 0.5x to 1.5x the true
    /// rate. Counting the frames whose own timestamps fall inside the window is
    /// immune to that.
    ///
    /// Requires an elevated process: enabling a manifest-based provider is
    /// restricted to Administrators / Performance Log Users.
    /// </summary>
    internal sealed class DxgiFpsCounter : IDisposable
    {
        private const string ProviderName = "Microsoft-Windows-DXGI";
        private static readonly Guid ProviderGuid = new Guid("ca11c036-0102-4a2d-a6ad-f03cfed5d3c9");
        private const string SessionName = "RamFpsOverlayDxgi";

        private const int PresentStartId = 42;
        private const uint DxgiPresentTest = 0x00000001;

        // 2 seconds of headroom at 1024 fps. Slower apps simply keep more history.
        private const int RingCapacity = 2048;

        private TraceEventSession? session;
        private Thread? pump;

        private readonly ConcurrentDictionary<SwapChainKey, ChainHistory> chains =
            new ConcurrentDictionary<SwapChainKey, ChainHistory>();

        private readonly int ownPid = Process.GetCurrentProcess().Id;
        private static readonly ConcurrentDictionary<int, string> nameCache = new ConcurrentDictionary<int, string>();

        // Event-time clock. ETW event times are monotonic QPC values with no
        // fixed relation to wall-clock, so "now" is the newest event time plus
        // the wall-clock delay since that event was handed to us.
        private readonly object clockLock = new object();
        private double newestRelMs;
        private DateTime newestRelWall = DateTime.MinValue;
        private double firstRelMs = double.NaN;

        private long eventsSeen;
        private long presentsSeen;

        private readonly struct SwapChainKey : IEquatable<SwapChainKey>
        {
            public readonly int Pid;
            public readonly long SwapChain;

            public SwapChainKey(int pid, long swapChain) { Pid = pid; SwapChain = swapChain; }

            public bool Equals(SwapChainKey other) => Pid == other.Pid && SwapChain == other.SwapChain;
            public override bool Equals(object? obj) => obj is SwapChainKey k && Equals(k);
            public override int GetHashCode() => (Pid * 397) ^ SwapChain.GetHashCode();
        }

        /// <summary>Ring of the most recent present timestamps for one swapchain.</summary>
        private sealed class ChainHistory
        {
            private readonly double[] times = new double[RingCapacity];
            private readonly object gate = new object();
            private int head;
            private int count;

            public void Add(double timeMs)
            {
                lock (gate)
                {
                    times[head] = timeMs;
                    head = (head + 1) % RingCapacity;
                    if (count < RingCapacity) count++;
                }
            }

            /// <summary>Frames with a timestamp in (fromMs, toMs].</summary>
            public int CountBetween(double fromMs, double toMs)
            {
                lock (gate)
                {
                    if (count == 0) return 0;

                    int newest = (head - 1 + RingCapacity) % RingCapacity;
                    if (times[newest] < fromMs) return 0;      // chain went quiet

                    int n = 0;
                    for (int i = 0; i < count; i++)
                    {
                        int idx = (newest - i + RingCapacity * 2) % RingCapacity;
                        double t = times[idx];
                        if (t < fromMs) break;
                        if (t <= toMs) n++;
                    }
                    return n;
                }
            }
        }

        public bool IsRunning { get; private set; }
        public string LastError { get; private set; } = "";
        public string TargetProcess { get; private set; } = "";

        /// <summary>All DXGI ETW events received (any id). Zero means the provider is silent.</summary>
        public long EventsSeen => Interlocked.Read(ref eventsSeen);

        /// <summary>Present_Start events counted as real frames.</summary>
        public long PresentsSeen => Interlocked.Read(ref presentsSeen);

        public bool Start()
        {
            if (IsRunning) return true;

            if (TraceEventSession.IsElevated() != true)
            {
                LastError = "not elevated";
                return false;
            }

            try
            {
                // A previous run that was killed leaves the session behind, and
                // a leftover session with this name blocks the next start.
                try
                {
                    var stale = TraceEventSession.GetActiveSession(SessionName);
                    if (stale != null)
                    {
                        try { stale.Stop(); } catch { }
                        try { stale.Dispose(); } catch { }
                    }
                }
                catch { }

                session = new TraceEventSession(SessionName) { StopOnDispose = true };
                // matchAnyKeywords = ulong.MaxValue: these events live in the
                // DXGI/Analytic channel (keyword 0x8000000000000002) and a
                // narrower mask silently delivers nothing.
                session.EnableProvider(ProviderGuid, TraceEventLevel.Verbose, ulong.MaxValue);
                session.Source.Dynamic.All += OnEvent;

                var bound = session;
                pump = new Thread(() =>
                {
                    try { bound.Source.Process(); }
                    catch (Exception ex) { LastError = ex.Message; }
                })
                { IsBackground = true, Name = "dxgi-etw" };
                pump.Start();

                IsRunning = true;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                IsRunning = false;
                return false;
            }
        }

        private void OnEvent(TraceEvent e)
        {
            Interlocked.Increment(ref eventsSeen);
            if ((int)e.ID != PresentStartId) return;

            // DXGI_PRESENT_TEST is a "would this present?" probe (DWM uses it on
            // idle monitors) and does not draw a frame.
            try
            {
                var flags = e.PayloadByName("Flags");
                if (flags != null && (Convert.ToUInt32(flags) & DxgiPresentTest) != 0) return;
            }
            catch { }

            long swapChain = 0;
            try
            {
                var chain = e.PayloadByName("pIDXGISwapChain");
                if (chain != null) swapChain = Convert.ToInt64(chain);
            }
            catch { }

            double relMs = e.TimeStampRelativeMSec;
            chains.GetOrAdd(new SwapChainKey(e.ProcessID, swapChain), _ => new ChainHistory()).Add(relMs);

            lock (clockLock)
            {
                if (double.IsNaN(firstRelMs)) firstRelMs = relMs;
                if (relMs > newestRelMs || newestRelWall == DateTime.MinValue)
                {
                    newestRelMs = relMs;
                    newestRelWall = DateTime.UtcNow;
                }
            }

            Interlocked.Increment(ref presentsSeen);
        }

        /// <summary>Event-clock "now": newest event time plus the delay since it arrived.</summary>
        private bool TryGetNow(out double nowMs, out double historyMs)
        {
            lock (clockLock)
            {
                if (newestRelWall == DateTime.MinValue || double.IsNaN(firstRelMs))
                {
                    nowMs = 0;
                    historyMs = 0;
                    return false;
                }
                nowMs = newestRelMs + (DateTime.UtcNow - newestRelWall).TotalMilliseconds;
                historyMs = nowMs - firstRelMs;
                return true;
            }
        }

        /// <summary>
        /// FPS of the foreground application over the last
        /// <paramref name="windowSeconds"/> seconds of event time. Returns false
        /// while the window is still filling, or when the foreground app is not
        /// presenting (the desktop, a GDI/OpenGL app, or a store app drawing
        /// through a child process that cannot be matched).
        /// </summary>
        public bool TrySample(double windowSeconds, out int fps, out string processName)
        {
            fps = 0;
            processName = TargetProcess = "";

            if (!TryGetNow(out double nowMs, out double historyMs)) return false;

            // Do not report a rate over more history than actually exists yet.
            double spanSeconds = Math.Min(windowSeconds, historyMs / 1000.0);
            if (spanSeconds < 0.5) return false;

            double fromMs = nowMs - spanSeconds * 1000.0;

            // A process can own several swapchains (one per monitor, plus UI
            // chains). Rate is per swapchain, so take the busiest one rather than
            // summing, which would double-count a dual-monitor game.
            var perPid = new Dictionary<int, double>();
            foreach (var kv in chains)
            {
                int pid = kv.Key.Pid;
                if (pid == ownPid || pid <= 0) continue;

                int frames = kv.Value.CountBetween(fromMs, nowMs);
                if (frames <= 0) continue;

                double rate = frames / spanSeconds;
                perPid.TryGetValue(pid, out double best);
                if (rate > best) perPid[pid] = rate;
            }

            PruneDeadSwapChains();

            if (perPid.Count == 0) return false;

            int fgPid = ForegroundPid();
            string fgName = ProcessNameOf(fgPid);

            double targetRate = 0;
            int targetPid = 0;

            // Exact match first: games present from their own process.
            if (fgPid > 0 && perPid.TryGetValue(fgPid, out double direct))
            {
                targetRate = direct;
                targetPid = fgPid;
            }
            else if (fgName.Length > 0 && !IsShell(fgName))
            {
                // Otherwise match on image name: Chromium and several engines
                // present from a child GPU/render process.
                foreach (var kv in perPid)
                {
                    if (!string.Equals(ProcessNameOf(kv.Key), fgName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (kv.Value > targetRate) { targetRate = kv.Value; targetPid = kv.Key; }
                }
            }

            if (targetPid == 0) return false;

            TargetProcess = ProcessNameOf(targetPid);
            processName = TargetProcess;
            fps = (int)Math.Round(targetRate);
            return true;
        }

        private void PruneDeadSwapChains()
        {
            if (chains.Count <= 256) return;

            foreach (var key in chains.Keys)
            {
                if (key.Pid == ownPid) continue;
                try { using var _ = Process.GetProcessById(key.Pid); }
                catch { chains.TryRemove(key, out _); }
            }
        }

        private static bool IsShell(string name) =>
            name.Equals("dwm", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("explorer", StringComparison.OrdinalIgnoreCase);

        private static int ForegroundPid()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(hwnd, out uint pid);
            return (int)pid;
        }

        private static string ProcessNameOf(int pid)
        {
            if (pid <= 0) return "";
            if (nameCache.TryGetValue(pid, out string? cached) && cached != null) return cached;

            string name = "";
            try { name = Process.GetProcessById(pid).ProcessName; } catch { }

            if (nameCache.Count > 256) nameCache.Clear();
            nameCache[pid] = name;
            return name;
        }

        public void Dispose()
        {
            IsRunning = false;
            try
            {
                if (session != null)
                {
                    try { session.Source.StopProcessing(); } catch { }
                    try { session.Dispose(); } catch { }
                    session = null;
                }
                pump?.Join(1500);
                pump = null;
            }
            catch { }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
