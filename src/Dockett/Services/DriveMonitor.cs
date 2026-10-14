using System.Runtime.InteropServices;

namespace Dockett.Services;

/// <summary>
/// Notices drives being added or removed (USB sticks, card readers, mapped network drives) by polling the
/// logical drive bitmask, which is a single cheap call. Also nudges periodically so free-space bars stay current.
/// Events are raised on a thread-pool thread.
/// </summary>
public sealed class DriveMonitor : IDisposable
{
    readonly Func<uint> _readMask;
    readonly Timer _timer;
    readonly int _refreshEveryTicks;
    uint _lastMask;
    int _ticks;

    /// <param name="interval">How often to check for added or removed drives.</param>
    /// <param name="usageRefresh">How often to raise <see cref="Changed"/> anyway, to refresh free space.</param>
    /// <param name="readMask">Source of the drive bitmask; tests substitute their own.</param>
    public DriveMonitor(TimeSpan? interval = null, TimeSpan? usageRefresh = null, Func<uint>? readMask = null)
    {
        var period = interval ?? TimeSpan.FromSeconds(1.5);
        _refreshEveryTicks = Math.Max(1, (int)((usageRefresh ?? TimeSpan.FromSeconds(30)) / period));
        _readMask = readMask ?? GetLogicalDrives;
        _lastMask = _readMask();
        _timer = new Timer(_ => Tick(), null, period, period);
    }

    /// <summary>Raised when drives were added or removed (<c>true</c>) or on the periodic usage refresh (<c>false</c>).</summary>
    public event Action<bool>? Changed;

    void Tick()
    {
        var mask = _readMask();
        if (mask != _lastMask)
        {
            _lastMask = mask;
            _ticks = 0;
            Changed?.Invoke(true);
        }
        else if (++_ticks >= _refreshEveryTicks)
        {
            _ticks = 0;
            Changed?.Invoke(false);
        }
    }

    public void Dispose() => _timer.Dispose();

    [DllImport("kernel32.dll")]
    static extern uint GetLogicalDrives();
}
