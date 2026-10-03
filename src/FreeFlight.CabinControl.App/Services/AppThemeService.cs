using System.Windows;
using System.Windows.Media;

namespace FreeFlight.CabinControl.App.Services;

/// <summary>
/// Applies Ember's shared operational palette without replacing any live
/// page or ACARS state.  Every common brush is mutated in place so existing
/// pages update immediately when a pilot changes appearance.
/// </summary>
public sealed class AppThemeService
{
    public const string Light = "Light";
    public const string Dark = "Dark";

    public static string Normalize(string? theme) =>
        string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase)
            ? Dark
            : Light;

    public void Apply(string? theme)
    {
        var isDark = string.Equals(Normalize(theme), Dark, StringComparison.Ordinal);
        var palette = isDark ? Palette.Dark : Palette.Light;

        SetBrush("AppBackgroundBrush", palette.AppBackground);
        SetBrush("SidebarBrush", palette.Sidebar);
        SetBrush("SurfaceBrush", palette.Surface);
        SetBrush("SurfaceRaisedBrush", palette.SurfaceRaised);
        SetBrush("BorderBrush", palette.Border);
        SetBrush("BorderSoftBrush", palette.BorderSoft);
        SetBrush("PrimaryBrush", palette.Primary);
        SetBrush("PrimaryDeepBrush", palette.PrimaryDeep);
        SetBrush("CyanBrush", palette.Cyan);
        SetBrush("SuccessBrush", palette.Success);
        SetBrush("WarningBrush", palette.Warning);
        SetBrush("DangerBrush", palette.Danger);
        SetBrush("TextPrimaryBrush", palette.TextPrimary);
        SetBrush("TextSecondaryBrush", palette.TextSecondary);
        SetBrush("TextMutedBrush", palette.TextMuted);
        SetGradient("PrimaryGradientBrush", palette.PrimaryGradientStart, palette.PrimaryGradientEnd);
        SetGradient("AmbientGlowBrush", palette.GlowStart, palette.GlowMiddle, "#00000000");
    }

    private static void SetBrush(string key, string hex)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            brush.Color = (Color)ColorConverter.ConvertFromString(hex)!;
        }
    }

    private static void SetGradient(string key, params string[] colors)
    {
        if (Application.Current?.TryFindResource(key) is not GradientBrush brush)
        {
            return;
        }

        for (var index = 0; index < Math.Min(brush.GradientStops.Count, colors.Length); index++)
        {
            brush.GradientStops[index].Color = (Color)ColorConverter.ConvertFromString(colors[index])!;
        }
    }

    private sealed record Palette(
        string AppBackground,
        string Sidebar,
        string Surface,
        string SurfaceRaised,
        string Border,
        string BorderSoft,
        string Primary,
        string PrimaryDeep,
        string Cyan,
        string Success,
        string Warning,
        string Danger,
        string TextPrimary,
        string TextSecondary,
        string TextMuted,
        string PrimaryGradientStart,
        string PrimaryGradientEnd,
        string GlowStart,
        string GlowMiddle)
    {
        public static Palette Light { get; } = new(
            "#F2F6FA", "#062B4C", "#FFFFFF", "#F8FAFD", "#D8E2EC", "#E8EEF4",
            "#0B73B9", "#07528E", "#117DBC", "#13865B", "#B66A09", "#C42F52",
            "#102A43", "#52677D", "#7A8DA1", "#1689CF", "#07518C", "#142F73B9", "#0BF2F6FA");

        public static Palette Dark { get; } = new(
            "#071321", "#061729", "#102033", "#16283D", "#2B415B", "#1E344D",
            "#318EDB", "#1766A3", "#54BBF4", "#47D895", "#F2B84B", "#FA6D8D",
            "#F3F8FF", "#B8CAE0", "#7F9AB5", "#3E9FEA", "#164D82", "#18318EDB", "#00071321");
    }
}
