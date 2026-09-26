namespace Pwe.PcMonitor.Services;

internal sealed class CpuCounterBaseline
{
    private (ulong Idle, ulong Kernel, ulong User)? _previous;

    public void Reset() => _previous = null;

    public double? Read(ulong idle, ulong kernel, ulong user)
    {
        var previous = _previous;
        _previous = (idle, kernel, user);
        if (previous is not { } p || idle < p.Idle || kernel < p.Kernel || user < p.User) return null;
        var total = (double)(kernel - p.Kernel) + (user - p.User);
        var idleDelta = idle - p.Idle;
        return total <= 0 || idleDelta > total ? null : Math.Clamp((total - idleDelta) * 100 / total, 0, 100);
    }
}

internal sealed class NetworkCounterBaseline
{
    private (string Id, long Down, long Up, DateTimeOffset At)? _previous;
    public void Reset() => _previous = null;

    public (double? Down, double? Up) Read(string id, long down, long up, DateTimeOffset at)
    {
        var previous = _previous;
        _previous = (id, down, up, at);
        if (previous is not { } p || p.Id != id || down < p.Down || up < p.Up || at <= p.At)
            return (null, null);
        var seconds = (at - p.At).TotalSeconds;
        return ((down - p.Down) / seconds, (up - p.Up) / seconds);
    }
}
