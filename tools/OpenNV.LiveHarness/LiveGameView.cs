using System.Diagnostics;

namespace OpenNV.LiveHarness;

internal sealed class LiveGameView : Panel
{
    private int _processId;
    private nint _sourceWindow;
    private Bitmap? _nativePreview;
    private long _previewSequence;
    private long _paintedSequence;
    private long _previewPaints;
    private long _sourceNanoseconds;
    private readonly Queue<long> _recentPaints = new();

    internal LiveGameView(int processId)
    {
        _processId = processId;
        BackColor = Color.FromArgb(9, 12, 17);
        Dock = DockStyle.Fill;
        TabStop = true;
        DoubleBuffered = true;
    }

    internal bool Connected => _sourceWindow != 0;

    internal object Presentation => new
    {
        mode = "native-frame-arrival",
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
        if (_nativePreview is not null && SourceAgeMilliseconds > 1000) Invalidate();
    }

    internal Rectangle DisplayBounds(int width, int height)
    {
        var scale = Math.Min((double)ClientSize.Width / width, (double)ClientSize.Height / height);
        var fitted = new Size((int)(width * scale), (int)(height * scale));
        return new Rectangle((ClientSize.Width - fitted.Width) / 2, (ClientSize.Height - fitted.Height) / 2, fitted.Width, fitted.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _nativePreview?.Dispose(); _nativePreview = null; }
        base.Dispose(disposing);
    }
}
