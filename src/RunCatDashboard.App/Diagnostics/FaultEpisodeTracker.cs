namespace RunCatDashboard.App.Diagnostics;

internal enum FaultEpisodeTransition
{
    None,
    Failed,
    Recovered
}

internal sealed class FaultEpisodeTracker
{
    private readonly object _gate = new();
    private bool _isFaulted;

    internal bool IsFaulted
    {
        get
        {
            lock (_gate)
            {
                return _isFaulted;
            }
        }
    }

    internal FaultEpisodeTransition Observe(bool isFaulted)
    {
        lock (_gate)
        {
            if (_isFaulted == isFaulted)
            {
                return FaultEpisodeTransition.None;
            }

            _isFaulted = isFaulted;
            return isFaulted
                ? FaultEpisodeTransition.Failed
                : FaultEpisodeTransition.Recovered;
        }
    }
}
