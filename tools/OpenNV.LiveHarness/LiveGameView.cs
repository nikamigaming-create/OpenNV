using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenNV.LiveHarness;

internal sealed class LiveGameView : Panel
{
    private int _processId;
    private nint _sourceWindow;
    private readonly bool _compositor;
    private nint _thumbnail, _thumbnailSource, _thumbnailDestination;
    private string? _compositorError;
    private Bitmap? _nativePreview;
    private long _previewSequence;
    private long _paintedSequence;
    private long _previewPaints;
    private long _sourceNanoseconds;
    private readonly Queue<long> _recentPaints = new();

    internal LiveGameView(int processId, bool compositor = false)
    {
        _processId = processId;
        _compositor = compositor;
        BackColor = Color.FromArgb(9, 12, 17);
        Dock = DockStyle.Fill;
        TabStop = true;
        DoubleBuffered = true;
    }

    internal bool Connected => _sourceWindow != 0;
    internal string CompositorStatus => _compositorError ?? (_sourceWindow == 0 ? "source window unavailable" :
        _thumbnail == 0 ? "compositor disconnected" : "compositor preview · display cadence unmeasured");

    internal object Presentation => new
    {
        mode = _compositor ? "desktop-compositor;display-cadence-unmeasured" : "native-frame-arrival",
        compositorConnected = _thumbnail != 0,
        compositorError = _compositorError,
        sourceWindowPresent = Connected,
        sourceSequence = _previewSequence,
        sourceAgeMilliseconds = _sourceNanoseconds == 0 ? (double?)null : SourceAgeMilliseconds,
        paintedSequence = _paintedSequence,
        nativePreviewPaints = _previewPaints,
        recentFramesPerSecond = _recentPaints.Count > 1
            ? (_recentPaints.Count - 1) * (double)Stopwatch.Frequency / (_recentPaints.Last() - _recentPaints.Peek()) : 0,
    };

    private double SourceAgeMilliseconds => _sourceNanoseconds == 0 ? double.PositiveInfinity :
        Math.Max(0, Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency) - _sourceNanoseconds / 1_000_000.0);

    internal void SetNativePreview(Bitmap image, long sequence, long nanoseconds)
    {
        var previous = _nativePreview;
        _nativePreview = image;
        _previewSequence = sequence;
        _sourceNanoseconds = nanoseconds;
        previous?.Dispose();
        Invalidate();
        Update();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_nativePreview is not null)
        {
            e.Graphics.DrawImage(_nativePreview, DisplayBounds(_nativePreview.Width, _nativePreview.Height));
            if (_paintedSequence != _previewSequence)
            {
                _paintedSequence = _previewSequence;
                ++_previewPaints;
                var now = Stopwatch.GetTimestamp();
                _recentPaints.Enqueue(now);
                while (_recentPaints.Count > 1 && now - _recentPaints.Peek() > Stopwatch.Frequency)
                    _recentPaints.Dequeue();
            }
            if (SourceAgeMilliseconds > 1000)
            {
                using var background = new SolidBrush(Color.FromArgb(220, 20, 20, 20));
                e.Graphics.FillRectangle(background, 0, 0, ClientSize.Width, Font.Height + 12);
                e.Graphics.DrawString($"STALE · last native frame {SourceAgeMilliseconds / 1000:0.0}s ago", Font, Brushes.OrangeRed, 6, 6);
            }
        }
    }

    internal void SetProcess(int processId)
    {
        if (_processId == processId || processId <= 0) return;
        _sourceWindow = 0;
        _processId = processId;
    }

    internal void RefreshWindow()
    {
        try
        {
            using var process = Process.GetProcessById(_processId);
            _sourceWindow = process.MainWindowHandle;
        }
        catch (ArgumentException) { _sourceWindow = 0; }
        if (_compositor) RefreshThumbnail();
        if (_nativePreview is not null && SourceAgeMilliseconds > 1000) Invalidate();
    }

    private void RefreshThumbnail()
    {
        var form = FindForm();
        var destination = form is { IsHandleCreated: true } ? form.Handle : 0;
        if (_thumbnailSource != _sourceWindow || _thumbnailDestination != destination) ReleaseThumbnail();
        if (_sourceWindow == 0 || form is null || !form.IsHandleCreated || !IsHandleCreated) return;
        if (_thumbnail == 0)
        {
            var result = DwmRegisterThumbnail(form.Handle, _sourceWindow, out _thumbnail);
            if (result != 0) { _compositorError = $"DWM registration 0x{result:X8}"; return; }
            _thumbnailSource = _sourceWindow;
            _thumbnailDestination = destination;
        }
        if (!GetClientRect(_sourceWindow, out var source) || source.Right <= 0 || source.Bottom <= 0) return;
        var fitted = DisplayBounds(source.Right, source.Bottom);
        var origin = form.PointToClient(PointToScreen(fitted.Location));
        var properties = new ThumbnailProperties
        {
            Flags = 1 | 4 | 8 | 16,
            Destination = new() { Left = origin.X, Top = origin.Y, Right = origin.X + fitted.Width, Bottom = origin.Y + fitted.Height },
            Opacity = 255, Visible = Visible && form.Visible, ClientOnly = true,
        };
        var updated = DwmUpdateThumbnailProperties(_thumbnail, ref properties);
        _compositorError = updated == 0 ? null : $"DWM update 0x{updated:X8}";
        if (updated != 0) ReleaseThumbnail();
    }

    private void ReleaseThumbnail()
    {
        if (_thumbnail != 0) _ = DwmUnregisterThumbnail(_thumbnail);
        _thumbnail = 0; _thumbnailSource = 0; _thumbnailDestination = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbnailProperties
    {
        internal uint Flags;
        internal NativeRect Destination, Source;
        internal byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] internal bool Visible;
        [MarshalAs(UnmanagedType.Bool)] internal bool ClientOnly;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out NativeRect rectangle);

    internal Rectangle DisplayBounds(int width, int height)
    {
        var scale = Math.Min((double)ClientSize.Width / width, (double)ClientSize.Height / height);
        var fitted = new Size((int)(width * scale), (int)(height * scale));
        return new Rectangle((ClientSize.Width - fitted.Width) / 2, (ClientSize.Height - fitted.Height) / 2, fitted.Width, fitted.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { ReleaseThumbnail(); _nativePreview?.Dispose(); _nativePreview = null; }
        base.Dispose(disposing);
    }
}
