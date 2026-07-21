using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Startup;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.ViewModels;

public sealed class SettingsWindowViewModelTests
{
    [Fact]
    public void Cancel_ClosesWithoutApplyingDraft()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            IsDashboardVisible = false,
            SamplingIntervalMilliseconds = 5000
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        viewModel.CancelCommand.Execute(null);

        Assert.Equal(0, application.ApplyCount);
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task Save_AppliesRuntimeReconcilesStartupAndCloses()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            IsDashboardVisible = false,
            InteractionMode = OverlayInteractionMode.Interactive,
            SamplingIntervalMilliseconds = 250,
            RunAtLoginRequested = true
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;

        Assert.Equal(1, application.ApplyCount);
        Assert.Equal((false, OverlayInteractionMode.Interactive, 250, true), application.LastDraft);
        Assert.True(viewModel.RunAtLoginApplied);
        Assert.Equal(1, closes);
    }

    private sealed class FakeSettingsApplicationService : ISettingsApplicationService
    {
        public AppSettings Current { get; } = AppSettings.Defaults;
        public RunAtLoginState RunAtLoginState { get; } = new(false, false, null);
        internal int ApplyCount { get; private set; }
        internal (bool, OverlayInteractionMode, int, bool) LastDraft { get; private set; }
        public Task<RunAtLoginState> ApplyDraftAsync(
            bool dashboardVisible,
            OverlayInteractionMode interactionMode,
            int samplingIntervalMilliseconds,
            bool runAtLoginRequested,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            LastDraft = (dashboardVisible, interactionMode, samplingIntervalMilliseconds, runAtLoginRequested);
            return Task.FromResult(new RunAtLoginState(runAtLoginRequested, runAtLoginRequested, null));
        }
    }
}
