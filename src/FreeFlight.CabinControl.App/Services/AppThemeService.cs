using System.Windows;
using System.Windows.Media;

namespace FreeFlight.CabinControl.App.Services;

/// <summary>
/// Applies Ember's shared operational palette before the main workspace is
/// created.  WPF freezes XAML resources, so the palette is safely replaced in
/// its source dictionary rather than mutating a read-only brush at startup.
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

        var resources = FindThemeResources();
        if (resources is null)
        {
            return;
        }

        SetBrush(resources, "AppBackgroundBrush", palette.AppBackground);
        SetBrush(resources, "SidebarBrush", palette.Sidebar);
        SetBrush(resources, "SurfaceBrush", palette.Surface);
        SetBrush(resources, "SurfaceRaisedBrush", palette.SurfaceRaised);
        SetBrush(resources, "BorderBrush", palette.Border);
        SetBrush(resources, "BorderSoftBrush", palette.BorderSoft);
        SetBrush(resources, "PrimaryBrush", palette.Primary);
        SetBrush(resources, "PrimaryDeepBrush", palette.PrimaryDeep);
        SetBrush(resources, "CyanBrush", palette.Cyan);
        SetBrush(resources, "SuccessBrush", palette.Success);
        SetBrush(resources, "WarningBrush", palette.Warning);
        SetBrush(resources, "DangerBrush", palette.Danger);
        SetBrush(resources, "TextPrimaryBrush", palette.TextPrimary);
        SetBrush(resources, "TextSecondaryBrush", palette.TextSecondary);
        SetBrush(resources, "TextMutedBrush", palette.TextMuted);
        SetBrush(resources, "InfoSurfaceBrush", palette.InfoSurface);
        SetBrush(resources, "InfoBorderBrush", palette.InfoBorder);
        SetBrush(resources, "SuccessSurfaceBrush", palette.SuccessSurface);
        SetBrush(resources, "SuccessBorderBrush", palette.SuccessBorder);
        SetBrush(resources, "SuccessTextBrush", palette.SuccessText);
        SetGradient(resources, "PrimaryGradientBrush", palette.PrimaryGradientStart, palette.PrimaryGradientEnd);
        SetGradient(resources, "AmbientGlowBrush", palette.GlowStart, palette.GlowMiddle, "#00000000");
    }

    private static ResourceDictionary? FindThemeResources()
    {
        var applicationResources = Application.Current?.Resources;
        return applicationResources?.MergedDictionaries.FirstOrDefault(dictionary =>
                   dictionary.Contains("AppBackgroundBrush")) ?? applicationResources;
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hex) =>
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);

    private static void SetGradient(ResourceDictionary resources, string key, params string[] colors)
    {
        if (resources[key] is not GradientBrush source)
        {
            return;
        }

        var replacement = source.Clone();
        for (var index = 0; index < Math.Min(replacement.GradientStops.Count, colors.Length); index++)
        {
            replacement.GradientStops[index].Color = (Color)ColorConverter.ConvertFromString(colors[index])!;
        }

        resources[key] = replacement;
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
        string GlowMiddle,
        string InfoSurface,
        string InfoBorder,
        string SuccessSurface,
        string SuccessBorder,
        string SuccessText)
    {
        public static Palette Light { get; } = new(
            "#F2F6FA", "#062B4C", "#FFFFFF", "#F8FAFD", "#D8E2EC", "#E8EEF4",
            "#0B73B9", "#07528E", "#117DBC", "#13865B", "#B66A09", "#C42F52",
            "#102A43", "#52677D", "#7A8DA1", "#1689CF", "#07518C", "#142F73B9", "#0BF2F6FA",
            "#EAF5FF", "#B8DDF5", "#EAF8F1", "#62BE93", "#0C704C");

        public static Palette Dark { get; } = new(
            "#071321", "#061729", "#102033", "#16283D", "#2B415B", "#1E344D",
            "#318EDB", "#1766A3", "#54BBF4", "#47D895", "#F2B84B", "#FA6D8D",
            "#F3F8FF", "#B8CAE0", "#7F9AB5", "#3E9FEA", "#164D82", "#18318EDB", "#00071321",
            "#142C47", "#3675A8", "#173D2B", "#4FC98C", "#86E9B9");
    }
}
