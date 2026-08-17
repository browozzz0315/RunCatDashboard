using RunCatDashboard.App.Settings;

namespace RunCatDashboard.Tests.Settings;

public sealed class AnimationSettingsTests
{
    [Fact]
    public void Normalize_MissingAnimationSectionUsesV1Defaults()
    {
        AppSettings normalized = AppSettingsValidator.Normalize(
            AppSettings.Defaults with { Animation = null! });

        Assert.Equal(AnimationSettings.Defaults, normalized.Animation);
    }

    [Fact]
    public void Normalize_BlankIdAndUnknownSpeedUseBuiltInNormal()
    {
        AppSettings normalized = AppSettingsValidator.Normalize(
            AppSettings.Defaults with
            {
                Animation = new AnimationSettings(
                    "  ",
                    (AnimationSpeedPreference)(-1),
                    AnimationSettings.CurrentFormatVersion)
            });

        Assert.Equal(AnimationSettings.BuiltInDefaultAnimationId,
            normalized.Animation.SelectedAnimationId);
        Assert.Equal(AnimationSpeedPreference.Normal, normalized.Animation.SpeedPreference);
    }

    [Fact]
    public void Normalize_UnsupportedAnimationFormatSafelyUsesBuiltIn()
    {
        AppSettings normalized = AppSettingsValidator.Normalize(
            AppSettings.Defaults with
            {
                Animation = new AnimationSettings(
                    "custom-0123456789abcdef0123456789abcdef",
                    AnimationSpeedPreference.Fast,
                    99)
            });

        Assert.Equal(AnimationSettings.Defaults, normalized.Animation);
    }

    [Fact]
    public void Normalize_PreservesNonEmptyMissingIdForCatalogFallback()
    {
        const string missingId = "custom-0123456789abcdef0123456789abcdef";
        AppSettings normalized = AppSettingsValidator.Normalize(
            AppSettings.Defaults with
            {
                Animation = new AnimationSettings(
                    missingId,
                    AnimationSpeedPreference.Fast,
                    AnimationSettings.CurrentFormatVersion)
            });

        Assert.Equal(missingId, normalized.Animation.SelectedAnimationId);
        Assert.Equal(AnimationSpeedPreference.Fast, normalized.Animation.SpeedPreference);
    }
}
