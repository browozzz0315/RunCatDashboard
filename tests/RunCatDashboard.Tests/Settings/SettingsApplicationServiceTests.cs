using System.IO;
using RunCatDashboard.App.Animation;
using RunCatDashboard.App.Interop;
using RunCatDashboard.App.Models;
using RunCatDashboard.App.Services;
using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Startup;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Settings;

public sealed class SettingsApplicationServiceTests
{
    [Fact]
    public async Task ApplyDraft_NewViewModelUsesSavedInteractionHotKey()
    {
        var settings = new FakeSettingsService([]);
        var hotKeys = new FakeHotKeyController([]);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            new WindowVisibilityCoordinator(),
            new FakeInteractionAction(),
            hotKeys,
            mainViewModel,
            new FakeRunAtLoginService(),
            new ImmediateDispatcher());
        var gesture = new OverlayHotKeyGesture(
            true, false, true, true, OverlayHotKeyKey.F9);

        var firstViewModel = new SettingsWindowViewModel(service)
        {
            HotKeyAlt = false,
            HotKeyWindows = true
        };
        firstViewModel.ApplyCapturedHotKeyKey(gesture.Key);
        firstViewModel.SaveCommand.Execute(null);
        await firstViewModel.SaveCommand.ExecutionTask!;
        var reopenedViewModel = new SettingsWindowViewModel(service);

        Assert.Equal(gesture, reopenedViewModel.InteractionHotKey);
        Assert.Equal(gesture.DisplayText, reopenedViewModel.InteractionHotKeyDisplayText);
    }

    [Fact]
    public async Task ApplyDraft_PersistsInteractionHotKeyThatCanBeReloaded()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "RunCatDashboard.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonSettingsStore(directory, new PhysicalSettingsFileSystem());
            await using var settings = new SettingsService(store);
            await settings.LoadAsync();
            var hotKeys = new FakeHotKeyController([]);
            await using MainWindowViewModel mainViewModel = CreateMainViewModel();
            var service = new SettingsApplicationService(
                settings,
                new WindowVisibilityCoordinator(),
                new FakeInteractionAction(),
                hotKeys,
                mainViewModel,
                new FakeRunAtLoginService(),
                new ImmediateDispatcher());
            var gesture = new OverlayHotKeyGesture(
                false, true, true, true, OverlayHotKeyKey.F11);

            var viewModel = new SettingsWindowViewModel(service)
            {
                IsDashboardVisible = false,
                InteractionMode = OverlayInteractionMode.Interactive,
                HotKeyControl = false,
                HotKeyWindows = true,
                SamplingIntervalMilliseconds = 500,
                RunAtLoginRequested = true
            };
            viewModel.ApplyCapturedHotKeyKey(gesture.Key);
            viewModel.SaveCommand.Execute(null);
            await viewModel.SaveCommand.ExecutionTask!;
            SettingsLoadResult reloaded = await store.LoadAsync();

            Assert.Equal(AppSettings.CurrentVersion, reloaded.Settings.Version);
            Assert.Equal(gesture, reloaded.Settings.Overlay.InteractionHotKey);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ApplyDraft_HotKeySucceeds_PersistsOnlyAfterRuntimeApply()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        var hotKeys = new FakeHotKeyController(operations)
        {
            Result = new GlobalHotKeyApplyResult(true, false, true, false, null)
        };
        var visibility = new WindowVisibilityCoordinator();
        var interaction = new FakeInteractionAction();
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            visibility,
            interaction,
            hotKeys,
            mainViewModel,
            new FakeRunAtLoginService(),
            new ImmediateDispatcher());
        var gesture = new OverlayHotKeyGesture(
            true, false, true, false, OverlayHotKeyKey.F9);

        await service.ApplyDraftAsync(
            false,
            OverlayInteractionMode.Interactive,
            gesture,
            OverlayHotKeyGesture.DashboardVisibilityDefault,
            500,
            true);

        Assert.Equal(["apply-hotkey", "save-settings"], operations);
        Assert.Equal(gesture, settings.Current.Overlay.InteractionHotKey);
        Assert.False(settings.Current.Window.IsDashboardVisible);
        Assert.Equal(OverlayInteractionMode.Interactive,
            settings.Current.Overlay.InteractionMode);
        Assert.Equal(500, settings.Current.Metrics.SamplingIntervalMilliseconds);
        Assert.True(settings.Current.Startup.RunAtLoginRequested);

    }

    [Fact]
    public async Task ApplyDraft_HotKeyFails_DoesNotOverwriteOldSettings()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        AppSettings original = settings.Current;
        var hotKeys = new FakeHotKeyController(operations)
        {
            Result = new GlobalHotKeyApplyResult(
                false, false, true, false, "新快捷鍵失敗，已 rollback。")
        };
        var visibility = new WindowVisibilityCoordinator();
        var interaction = new FakeInteractionAction();
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            visibility,
            interaction,
            hotKeys,
            mainViewModel,
            new FakeRunAtLoginService(),
            new ImmediateDispatcher());

        HotKeyConfigurationException exception = await Assert.ThrowsAsync<HotKeyConfigurationException>(
            () => service.ApplyDraftAsync(
                false,
                OverlayInteractionMode.Interactive,
                new OverlayHotKeyGesture(true, false, false, false, OverlayHotKeyKey.F8),
                OverlayHotKeyGesture.DashboardVisibilityDefault,
                250,
                true));

        Assert.Contains("rollback", exception.Message);
        Assert.Equal(["apply-hotkey"], operations);
        Assert.Equal(original, settings.Current);
        Assert.Empty(settings.PersistedSettings);
    }

    [Fact]
    public async Task ApplyDraft_DuplicateGestures_DoesNotCallHotKeyController()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        var hotKeys = new FakeHotKeyController(operations);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            new WindowVisibilityCoordinator(),
            new FakeInteractionAction(),
            hotKeys,
            mainViewModel,
            new FakeRunAtLoginService(),
            new ImmediateDispatcher());
        var gesture = new OverlayHotKeyGesture(
            true, true, true, false, OverlayHotKeyKey.D);

        HotKeyConfigurationException exception =
            await Assert.ThrowsAsync<HotKeyConfigurationException>(() =>
                service.ApplyDraftAsync(
                    true,
                    OverlayInteractionMode.ClickThrough,
                    gesture,
                    gesture,
                    1000,
                    false));

        Assert.Equal(OverlayHotKeyGesture.DuplicateGestureMessage, exception.Message);
        Assert.Empty(operations);
        Assert.Equal(AppSettings.Defaults, settings.Current);
    }

    [Fact]
    public async Task Save_DuplicateGestures_ShowsSpecificUiErrorAndStaysOpen()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            new WindowVisibilityCoordinator(),
            new FakeInteractionAction(),
            new FakeHotKeyController(operations),
            mainViewModel,
            new FakeRunAtLoginService(),
            new ImmediateDispatcher());
        var viewModel = new SettingsWindowViewModel(service)
        {
            HotKeyKey = OverlayHotKeyKey.D
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;

        Assert.Equal(
            OverlayHotKeyGesture.DuplicateGestureMessage,
            viewModel.ValidationError);
        Assert.Equal(0, closes);
        Assert.Empty(operations);
        Assert.DoesNotContain("Parameter", viewModel.ValidationError);
        Assert.DoesNotContain("interactionHotKey", viewModel.ValidationError);
    }

    [Fact]
    public async Task Save_NoModifier_ShowsErrorThenCorrectedGestureClearsErrorAndCloses()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            new WindowVisibilityCoordinator(),
            new FakeInteractionAction(),
            new FakeHotKeyController(operations),
            mainViewModel,
            new FakeRunAtLoginService(),
            new ImmediateDispatcher());
        var viewModel = new SettingsWindowViewModel(service)
        {
            HotKeyControl = false,
            HotKeyAlt = false,
            HotKeyShift = false,
            HotKeyWindows = false
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        Assert.False(viewModel.HotKeyControl);
        Assert.False(viewModel.HotKeyAlt);
        Assert.False(viewModel.HotKeyShift);
        Assert.False(viewModel.HotKeyWindows);

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;

        Assert.Equal("快捷鍵至少需要一個 modifier。", viewModel.ValidationError);
        Assert.Equal(0, closes);
        Assert.Empty(operations);

        viewModel.HotKeyControl = true;

        Assert.Null(viewModel.ValidationError);
        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;

        Assert.Null(viewModel.ValidationError);
        Assert.Equal(1, closes);
        Assert.Equal(["apply-hotkey", "save-settings"], operations);
    }

    [Fact]
    public async Task ApplyDraft_HotKeyAndRollbackFail_EntersVisibleInteractiveSafeState()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        var hotKeys = new FakeHotKeyController(operations)
        {
            Result = new GlobalHotKeyApplyResult(
                false, false, false, true, "新舊快捷鍵都失敗。")
        };
        var visibility = new WindowVisibilityCoordinator();
        visibility.SetUserRequestedVisibility(false);
        var interaction = new FakeInteractionAction();
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            visibility,
            interaction,
            hotKeys,
            mainViewModel,
            new FakeRunAtLoginService(),
            new ImmediateDispatcher());

        await Assert.ThrowsAsync<HotKeyConfigurationException>(() =>
            service.ApplyDraftAsync(
                false,
                OverlayInteractionMode.ClickThrough,
                new OverlayHotKeyGesture(true, false, false, false, OverlayHotKeyKey.F8),
                OverlayHotKeyGesture.DashboardVisibilityDefault,
                1000,
                false));

        Assert.True(visibility.State.IsUserRequestedVisible);
        Assert.Equal(OverlayInteractionMode.Interactive, interaction.LastRequestedMode);
        Assert.Equal(AppSettings.Defaults, settings.Current);
    }

    [Fact]
    public async Task ApplyDraft_CustomVisibilityHotKey_PersistsAndKeepsInteractionGesture()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        var hotKeys = new FakeHotKeyController(operations);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            new WindowVisibilityCoordinator(),
            new FakeInteractionAction(),
            hotKeys,
            mainViewModel,
            new FakeRunAtLoginService(),
            new ImmediateDispatcher());
        var visibilityGesture = new OverlayHotKeyGesture(
            true, false, true, true, OverlayHotKeyKey.F9);

        await service.ApplyDraftAsync(
            true,
            OverlayInteractionMode.ClickThrough,
            OverlayHotKeyGesture.Default,
            visibilityGesture,
            1000,
            false);

        Assert.Equal(visibilityGesture, settings.Current.Window.VisibilityHotKey);
        Assert.Equal(OverlayHotKeyGesture.Default,
            settings.Current.Overlay.InteractionHotKey);
        Assert.Single(settings.PersistedSettings);
    }

    [Fact]
    public async Task ApplyDraft_SaveFails_RollsBackRuntimeAndDoesNotPublishSnapshot()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations) { SaveSucceeds = false };
        AppSettings original = settings.Current;
        var hotKeys = new FakeHotKeyController(operations);
        var visibility = new WindowVisibilityCoordinator();
        var interaction = new FakeInteractionAction();
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            visibility,
            interaction,
            hotKeys,
            mainViewModel,
            new FakeRunAtLoginService(),
            new ImmediateDispatcher());
        var visibilityGesture = new OverlayHotKeyGesture(
            true, false, true, true, OverlayHotKeyKey.F9);

        HotKeyConfigurationException exception =
            await Assert.ThrowsAsync<HotKeyConfigurationException>(() =>
                service.ApplyDraftAsync(
                    false,
                    OverlayInteractionMode.Interactive,
                    OverlayHotKeyGesture.Default,
                    visibilityGesture,
                    250,
                    true));

        Assert.Equal("設定無法保存，已恢復先前的快捷鍵與設定。", exception.Message);
        Assert.Equal(original, settings.Current);
        Assert.Empty(settings.PersistedSettings);
        Assert.Equal(OverlayHotKeyGesture.DashboardVisibilityDefault,
            hotKeys.VisibilityGesture);
        Assert.True(visibility.State.IsUserRequestedVisible);
        Assert.Null(interaction.LastRequestedMode);
    }

    [Fact]
    public async Task ApplyDraft_OnlyDashboardGestureChanged_DoesNotApplyInteractionGesture()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        var hotKeys = new FakeHotKeyController(operations);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = CreateService(settings, hotKeys, mainViewModel);
        var dashboardGesture = new OverlayHotKeyGesture(
            true, false, true, true, OverlayHotKeyKey.F9);

        await service.ApplyDraftAsync(
            true,
            OverlayInteractionMode.ClickThrough,
            OverlayHotKeyGesture.Default,
            dashboardGesture,
            1000,
            false);

        Assert.Equal(0, hotKeys.InteractionApplyCount);
        Assert.Equal(1, hotKeys.VisibilityApplyCount);
    }

    [Fact]
    public async Task ApplyDraft_OnlyInteractionGestureChanged_DoesNotApplyDashboardGesture()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        var hotKeys = new FakeHotKeyController(operations);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = CreateService(settings, hotKeys, mainViewModel);
        var interactionGesture = new OverlayHotKeyGesture(
            true, false, true, false, OverlayHotKeyKey.F8);

        await service.ApplyDraftAsync(
            true,
            OverlayInteractionMode.ClickThrough,
            interactionGesture,
            OverlayHotKeyGesture.DashboardVisibilityDefault,
            1000,
            false);

        Assert.Equal(1, hotKeys.InteractionApplyCount);
        Assert.Equal(0, hotKeys.VisibilityApplyCount);
    }

    [Fact]
    public async Task ApplyDraft_UnchangedUnregisteredOtherGesture_DoesNotRecoverIt()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        var hotKeys = new FakeHotKeyController(operations)
        {
            InteractionIsRegistered = false
        };
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = CreateService(settings, hotKeys, mainViewModel);

        await service.ApplyDraftAsync(
            true,
            OverlayInteractionMode.ClickThrough,
            OverlayHotKeyGesture.Default,
            new OverlayHotKeyGesture(true, false, true, true, OverlayHotKeyKey.F9),
            1000,
            false);

        Assert.Equal(0, hotKeys.InteractionApplyCount);
        Assert.False(hotKeys.InteractionIsRegistered);
    }

    [Fact]
    public async Task ApplyDraft_OnlyNonHotKeySettingsChanged_DoesNotApplyEitherGesture()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        var hotKeys = new FakeHotKeyController(operations);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = CreateService(settings, hotKeys, mainViewModel);

        await service.ApplyDraftAsync(
            false,
            OverlayInteractionMode.Interactive,
            OverlayHotKeyGesture.Default,
            OverlayHotKeyGesture.DashboardVisibilityDefault,
            500,
            true);

        Assert.Equal(0, hotKeys.InteractionApplyCount);
        Assert.Equal(0, hotKeys.VisibilityApplyCount);
    }

    [Fact]
    public async Task ApplyDraft_BothGesturesChanged_AppliesBoth()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        var hotKeys = new FakeHotKeyController(operations);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = CreateService(settings, hotKeys, mainViewModel);

        await service.ApplyDraftAsync(
            true,
            OverlayInteractionMode.ClickThrough,
            new OverlayHotKeyGesture(true, false, true, false, OverlayHotKeyKey.F8),
            new OverlayHotKeyGesture(true, false, true, true, OverlayHotKeyKey.F9),
            1000,
            false);

        Assert.Equal(1, hotKeys.InteractionApplyCount);
        Assert.Equal(1, hotKeys.VisibilityApplyCount);
    }

    [Fact]
    public async Task ApplyDraft_SecondGestureFails_RollsBackOnlyFirstAppliedGesture()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        var hotKeys = new FakeHotKeyController(operations);
        hotKeys.InteractionResults.Enqueue(SuccessfulApply);
        hotKeys.InteractionResults.Enqueue(SuccessfulApply);
        hotKeys.VisibilityResults.Enqueue(new GlobalHotKeyApplyResult(
            false, false, true, false, "Dashboard 快捷鍵無法套用。"));
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = CreateService(settings, hotKeys, mainViewModel);

        await Assert.ThrowsAsync<HotKeyConfigurationException>(() => service.ApplyDraftAsync(
            true,
            OverlayInteractionMode.ClickThrough,
            new OverlayHotKeyGesture(true, false, true, false, OverlayHotKeyKey.F8),
            new OverlayHotKeyGesture(true, false, true, true, OverlayHotKeyKey.F9),
            1000,
            false));

        Assert.Equal(2, hotKeys.InteractionApplyCount);
        Assert.Equal(1, hotKeys.VisibilityApplyCount);
        Assert.Equal(OverlayHotKeyGesture.Default, hotKeys.InteractionGesture);
        Assert.Equal(AppSettings.Defaults, settings.Current);
    }

    [Fact]
    public async Task ApplyDraft_PersistenceFails_RollsBackOnlyChangedAppliedGesture()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations) { SaveSucceeds = false };
        var hotKeys = new FakeHotKeyController(operations);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = CreateService(settings, hotKeys, mainViewModel);

        await Assert.ThrowsAsync<HotKeyConfigurationException>(() => service.ApplyDraftAsync(
            true,
            OverlayInteractionMode.ClickThrough,
            OverlayHotKeyGesture.Default,
            new OverlayHotKeyGesture(true, false, true, true, OverlayHotKeyKey.F9),
            1000,
            false));

        Assert.Equal(0, hotKeys.InteractionApplyCount);
        Assert.Equal(2, hotKeys.VisibilityApplyCount);
        Assert.Equal(OverlayHotKeyGesture.DashboardVisibilityDefault,
            hotKeys.VisibilityGesture);
    }

    [Fact]
    public async Task ApplyDraft_PersistenceAndRollbackFail_ReturnsDegradedMessageAndSynchronizesActualGesture()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations) { SaveSucceeds = false };
        var hotKeys = new FakeHotKeyController(operations);
        hotKeys.VisibilityResults.Enqueue(SuccessfulApply);
        hotKeys.VisibilityResults.Enqueue(new GlobalHotKeyApplyResult(
            false, false, false, true, "原快捷鍵無法恢復。"));
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = CreateService(settings, hotKeys, mainViewModel);
        var dashboardGesture = new OverlayHotKeyGesture(
            true, false, true, true, OverlayHotKeyKey.F9);

        HotKeyConfigurationException exception =
            await Assert.ThrowsAsync<HotKeyConfigurationException>(() => service.ApplyDraftAsync(
                true,
                OverlayInteractionMode.ClickThrough,
                OverlayHotKeyGesture.Default,
                dashboardGesture,
                1000,
                false));

        Assert.Equal(
            "設定無法保存，且部分快捷鍵未能恢復。請使用系統匣控制 Dashboard，並重新開啟設定確認目前狀態。",
            exception.Message);
        Assert.Equal(dashboardGesture, hotKeys.VisibilityGesture);
        Assert.Equal(dashboardGesture, settings.Current.Window.VisibilityHotKey);
        Assert.Contains("update-settings", operations);
    }

    [Fact]
    public async Task ApplyDraft_TransactionalMutationPreservesConcurrentWindowAndModeUpdates()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations)
        {
            BeforeReplacement = current => current with
            {
                Window = current.Window with
                {
                    Left = 444.5,
                    Top = -25.75,
                    IsDashboardVisible = false
                },
                Overlay = current.Overlay with
                {
                    InteractionMode = OverlayInteractionMode.Interactive
                }
            }
        };
        var hotKeys = new FakeHotKeyController(operations);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = CreateService(settings, hotKeys, mainViewModel);

        await service.ApplyDraftAsync(
            true,
            OverlayInteractionMode.ClickThrough,
            OverlayHotKeyGesture.Default,
            OverlayHotKeyGesture.DashboardVisibilityDefault,
            1000,
            false);

        Assert.Equal(444.5, settings.Current.Window.Left);
        Assert.Equal(-25.75, settings.Current.Window.Top);
        Assert.False(settings.Current.Window.IsDashboardVisible);
        Assert.Equal(
            OverlayInteractionMode.Interactive,
            settings.Current.Overlay.InteractionMode);
        Assert.Equal(0, hotKeys.InteractionApplyCount);
        Assert.Equal(0, hotKeys.VisibilityApplyCount);
    }

    [Fact]
    public async Task ApplyDraft_PresentationSaveSucceeds_AppliesWithoutHotKeyReplacement()
    {
        var settings = new FakeSettingsService([]);
        var hotKeys = new FakeHotKeyController([]);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = CreateService(settings, hotKeys, mainViewModel);
        OverlayFieldSettings fields = OverlayFieldSettings.ForMode(OverlaySizeMode.Expanded) with
        {
            ShowCpu = false,
            ShowHotKeyHints = false
        };

        await service.ApplyDraftAsync(
            true,
            OverlayInteractionMode.ClickThrough,
            OverlayHotKeyGesture.Default,
            OverlayHotKeyGesture.DashboardVisibilityDefault,
            1000,
            false,
            OverlaySizeMode.Expanded,
            fields,
            OverlayDisplayPolicy.NeverTopmost);

        Assert.Equal(OverlaySizeMode.Expanded, settings.Current.Overlay.SizeMode);
        Assert.Equal(fields, settings.Current.Overlay.Fields);
        Assert.Equal(OverlaySizeMode.Expanded, mainViewModel.SizeMode);
        Assert.Equal(fields, mainViewModel.OverlayFields);
        Assert.Equal(OverlayDisplayPolicy.NeverTopmost, mainViewModel.RequestedDisplayPolicy);
        Assert.Equal(0, hotKeys.InteractionApplyCount);
        Assert.Equal(0, hotKeys.VisibilityApplyCount);
        Assert.False(mainViewModel.IsSampling);
    }

    [Fact]
    public async Task ApplyDraft_PresentationPersistenceFails_DoesNotApplyPresentation()
    {
        var settings = new FakeSettingsService([]) { SaveSucceeds = false };
        var hotKeys = new FakeHotKeyController([]);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = CreateService(settings, hotKeys, mainViewModel);

        await Assert.ThrowsAsync<HotKeyConfigurationException>(() => service.ApplyDraftAsync(
            true,
            OverlayInteractionMode.ClickThrough,
            OverlayHotKeyGesture.Default,
            OverlayHotKeyGesture.DashboardVisibilityDefault,
            1000,
            false,
            OverlaySizeMode.Compact,
            OverlayFieldSettings.ForMode(OverlaySizeMode.Compact)));

        Assert.Equal(OverlaySizeMode.Standard, settings.Current.Overlay.SizeMode);
        Assert.Equal(OverlaySizeMode.Standard, mainViewModel.SizeMode);
        Assert.Equal(0, hotKeys.InteractionApplyCount);
        Assert.Equal(0, hotKeys.VisibilityApplyCount);
    }

    [Fact]
    public async Task ApplyDraft_ConcurrentPresentationAndPositionWinOverStaleDraft()
    {
        OverlayFieldSettings concurrentFields =
            OverlayFieldSettings.ForMode(OverlaySizeMode.Expanded) with { ShowCpu = false };
        var settings = new FakeSettingsService([])
        {
            BeforeReplacement = current => current with
            {
                Window = current.Window with { Left = -300, Top = 45 },
                Overlay = current.Overlay with
                {
                    InteractionMode = OverlayInteractionMode.Interactive,
                    SizeMode = OverlaySizeMode.Expanded,
                    Fields = concurrentFields
                }
            }
        };
        var hotKeys = new FakeHotKeyController([]);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = CreateService(settings, hotKeys, mainViewModel);

        await service.ApplyDraftAsync(
            true,
            OverlayInteractionMode.ClickThrough,
            OverlayHotKeyGesture.Default,
            OverlayHotKeyGesture.DashboardVisibilityDefault,
            1000,
            false,
            OverlaySizeMode.Compact,
            OverlayFieldSettings.ForMode(OverlaySizeMode.Compact));

        Assert.Equal(-300, settings.Current.Window.Left);
        Assert.Equal(45, settings.Current.Window.Top);
        Assert.Equal(OverlayInteractionMode.Interactive, settings.Current.Overlay.InteractionMode);
        Assert.Equal(OverlaySizeMode.Expanded, settings.Current.Overlay.SizeMode);
        Assert.Equal(concurrentFields, settings.Current.Overlay.Fields);
        Assert.Equal(OverlaySizeMode.Expanded, mainViewModel.SizeMode);
    }

    [Fact]
    public async Task ApplyDraft_UnchangedUnregisteredInteraction_PerformsNoNativeRecoveryRegistration()
    {
        var native = new IsolationNativeHotKeyApi();
        using var hotKeys = new GlobalHotKeyController(native);
        hotKeys.RegisterAll(new nint(1234));
        int interactionRegistrationsBeforeSave = native.RegisterCalls.Count(call =>
            call.Identifier == GlobalHotKeyController.InteractionHotKeyIdentifier);
        var settings = new FakeSettingsService([]);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = CreateService(settings, hotKeys, mainViewModel);

        await service.ApplyDraftAsync(
            true,
            OverlayInteractionMode.ClickThrough,
            OverlayHotKeyGesture.Default,
            new OverlayHotKeyGesture(true, false, true, true, OverlayHotKeyKey.F9),
            1000,
            false);

        Assert.Equal(interactionRegistrationsBeforeSave,
            native.RegisterCalls.Count(call =>
                call.Identifier == GlobalHotKeyController.InteractionHotKeyIdentifier));
        Assert.DoesNotContain(
            GlobalHotKeyController.InteractionHotKeyIdentifier,
            native.UnregisterCalls);
    }

    private static readonly GlobalHotKeyApplyResult SuccessfulApply =
        new(true, false, true, false, null);

    private static SettingsApplicationService CreateService(
        ISettingsService settings,
        IGlobalHotKeyController hotKeys,
        MainWindowViewModel mainViewModel) => new(
            settings,
            new WindowVisibilityCoordinator(),
            new FakeInteractionAction(),
            hotKeys,
            mainViewModel,
            new FakeRunAtLoginService(),
            new ImmediateDispatcher());

    private static MainWindowViewModel CreateMainViewModel() => new(
        new NoOpMetricsService(),
        new ImmediateDispatcher(),
        new NoOpAnimationController());

    private sealed class FakeSettingsService(List<string> operations) : ISettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Defaults;
        internal bool SaveSucceeds { get; init; } = true;
        internal Func<AppSettings, AppSettings>? BeforeReplacement { get; init; }
        internal List<AppSettings> PersistedSettings { get; } = [];
        public string? LastDiagnostic => null;
        public event Action<AppSettings>? Changed;
        public event Action<string?>? DiagnosticChanged { add { } remove { } }
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool Update(Func<AppSettings, AppSettings> update)
        {
            operations.Add("update-settings");
            Current = AppSettingsValidator.Normalize(update(Current));
            Changed?.Invoke(Current);
            return true;
        }
        public Task<bool> TryReplaceCurrentAsync(
            Func<AppSettings, AppSettings> replacement,
            CancellationToken cancellationToken = default)
        {
            operations.Add("save-settings");
            if (!SaveSucceeds)
            {
                return Task.FromResult(false);
            }
            if (BeforeReplacement is not null)
            {
                Current = AppSettingsValidator.Normalize(BeforeReplacement(Current));
            }
            Current = AppSettingsValidator.Normalize(replacement(Current));
            PersistedSettings.Add(Current);
            Changed?.Invoke(Current);
            return Task.FromResult(true);
        }
        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            operations.Add("flush-settings");
            PersistedSettings.Add(Current);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeHotKeyController(List<string> operations) : IGlobalHotKeyController
    {
        public GlobalHotKeyApplyResult Result { get; init; } =
            new(true, false, true, false, null);
        public OverlayHotKeyGesture InteractionGesture { get; private set; } =
            OverlayHotKeyGesture.Default;
        public OverlayHotKeyGesture VisibilityGesture { get; private set; } =
            OverlayHotKeyGesture.DashboardVisibilityDefault;
        internal Queue<GlobalHotKeyApplyResult> InteractionResults { get; } = [];
        internal Queue<GlobalHotKeyApplyResult> VisibilityResults { get; } = [];
        internal int InteractionApplyCount { get; private set; }
        internal int VisibilityApplyCount { get; private set; }
        internal bool InteractionIsRegistered { get; set; } = true;
        internal bool VisibilityIsRegistered { get; set; } = true;
        public IReadOnlyList<GlobalHotKeyRegistrationState> Registrations =>
        [
            new(
                GlobalHotKeyAction.ToggleInteractionMode,
                GlobalHotKeyController.InteractionHotKeyIdentifier,
                InteractionGesture.DisplayText,
                InteractionIsRegistered,
                Result.Fault,
                null),
            new(
                GlobalHotKeyAction.ToggleDashboardVisibility,
                GlobalHotKeyController.VisibilityHotKeyIdentifier,
                VisibilityGesture.DisplayText,
                VisibilityIsRegistered,
                Result.Fault,
                null)
        ];
        public IReadOnlyList<GlobalHotKeyRegistrationState> RegisterAll(nint windowHandle) =>
            Registrations;
        public GlobalHotKeyApplyResult ApplyInteractionGesture(OverlayHotKeyGesture gesture)
        {
            operations.Add("apply-hotkey");
            InteractionApplyCount++;
            GlobalHotKeyApplyResult result = InteractionResults.TryDequeue(out var queued)
                ? queued
                : Result;
            if (result.IsSuccess)
            {
                InteractionGesture = gesture;
                InteractionIsRegistered = true;
            }
            else if (result.RequiresSafeRecovery)
            {
                InteractionIsRegistered = false;
            }
            return result;
        }
        public GlobalHotKeyApplyResult ApplyVisibilityGesture(OverlayHotKeyGesture gesture)
        {
            operations.Add("apply-visibility-hotkey");
            VisibilityApplyCount++;
            GlobalHotKeyApplyResult result = VisibilityResults.TryDequeue(out var queued)
                ? queued
                : Result;
            if (result.IsSuccess)
            {
                VisibilityGesture = gesture;
                VisibilityIsRegistered = true;
            }
            else if (result.RequiresSafeRecovery)
            {
                VisibilityIsRegistered = false;
            }
            return result;
        }
        public bool TryGetAction(int message, nint parameter, out GlobalHotKeyAction action)
        {
            action = default;
            return false;
        }
        public void Dispose() { }
    }

    private sealed class FakeInteractionAction : IInteractionModeToggleAction
    {
        public OverlayInteractionMode? LastRequestedMode { get; private set; }
        public OverlayWindowState State { get; private set; } = new(
            OverlayInteractionMode.ClickThrough,
            OverlayInteractionMode.ClickThrough,
            true,
            false,
            null);
        public event Action<OverlayWindowState>? StateChanged { add { } remove { } }
        public void RequestToggle() { }
        public void RequestMode(OverlayInteractionMode mode)
        {
            LastRequestedMode = mode;
            State = State with { RequestedMode = mode, AppliedMode = mode };
        }
    }

    private sealed class FakeRunAtLoginService : IRunAtLoginService
    {
        public RunAtLoginState State { get; private set; } = new(false, false, null);
        public Task<RunAtLoginState> ReconcileAsync(
            bool requested,
            CancellationToken cancellationToken = default)
        {
            State = new(requested, requested, null);
            return Task.FromResult(State);
        }
    }

    private sealed class IsolationNativeHotKeyApi : INativeGlobalHotKeyApi
    {
        internal List<(int Identifier, uint Modifiers, uint VirtualKey)> RegisterCalls { get; } = [];
        internal List<int> UnregisterCalls { get; } = [];

        public void Register(nint windowHandle, int identifier, uint modifiers, uint virtualKey)
        {
            RegisterCalls.Add((identifier, modifiers, virtualKey));
            if (identifier == GlobalHotKeyController.InteractionHotKeyIdentifier)
            {
                throw new IOException("configured interaction registration failure");
            }
        }

        public void Unregister(nint windowHandle, int identifier) =>
            UnregisterCalls.Add(identifier);
    }

    private sealed class NoOpMetricsService : ISystemMetricsService
    {
        public ValueTask<SystemMetricsSnapshot> SampleAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<SystemMetricsSnapshot>(
                new InvalidOperationException("Sampling is not used by this test."));
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            action();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpAnimationController : IRunCatAnimationController
    {
        public int FrameCount => 1;
        public int FrameIndex => 0;
        public TimeSpan Interval { get; private set; }
        public bool IsRunning => false;
        public string? LastFault => null;
        public event Action<int>? FrameChanged { add { } remove { } }
        public event Action<string>? Faulted { add { } remove { } }
        public bool Start() => false;
        public void Stop() { }
        public bool UpdateInterval(TimeSpan interval)
        {
            Interval = interval;
            return true;
        }
        public void Dispose() { }
    }
}
