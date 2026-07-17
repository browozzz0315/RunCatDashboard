namespace RunCatDashboard.App.Services;

public interface IUiDispatcher
{
    ValueTask InvokeAsync(
        Action action,
        CancellationToken cancellationToken = default);
}
