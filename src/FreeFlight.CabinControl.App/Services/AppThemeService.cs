using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using FreeFlight.CabinControl.Core.Configuration;
using Microsoft.Win32;

namespace FreeFlight.CabinControl.App.Services;

/// <summary>
/// Applies Ember's local presentation choices before a workspace is created.
/// These resources affect visual chrome only: the current website assignment,
/// simulator session and every ACARS record remain untouched.
/// </summary>
public sealed class AppThemeService
{
    public const string Light = "Light";
    public const string Dark = "Dark";
    public const string Auto = "Auto";

    public static string Normalize(string? theme) =>
        string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase)
            ? Dark
            : string.Equals(theme, Auto, StringComparison.OrdinalIgnoreCase)
                ? Auto
                : Light;

    public static string Resolve(string? theme)
    {
        var selected = Normalize(theme);
        return selected == Auto && IsWindowsUsingLightAppearance() ? Light :
            selected == Auto ? Dark : selected;
    }

    public void Apply(string? theme) => Apply(new AppSettings { Theme = theme ?? Light });

    public void Apply(AppSettings settings, double windowWidth = 0)
    {
        var isDark = string.Equals(Resolve(settings.Theme), Dark, StringComparison.Ordinal);
        var palette = isDark ? Palette.Dark : Palette.Light;
        var resources = FindThemeResources();
        if (resources is null)
        {
            return;
        }

        ApplyPalette(resources, palette, ResolveAccent(settings, palette), isDark);
        ApplyLayout(resources, settings, windowWidth, palette);
    }

    private static void ApplyPalette(ResourceDictionary resources, Palette palette, AccentPalette accent, bool isDark)
    {
        SetBrush(resources, "AppBackgroundBrush", palette.AppBackground);
        SetBrush(resources, "SidebarBrush", palette.Sidebar);
        SetBrush(resources, "SurfaceBrush", palette.Surface);
        SetBrush(resources, "SurfaceRaisedBrush", palette.SurfaceRaised);
        SetBrush(resources, "BorderBrush", palette.Border);
        SetBrush(resources, "BorderSoftBrush", palette.BorderSoft);
        SetBrush(resources, "PrimaryBrush", accent.Primary);
        SetBrush(resources, "PrimaryDeepBrush", accent.Deep);
        SetBrush(resources, "CyanBrush", accent.Cyan);
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
        SetBrush(resources, "CardHoverBorderBrush", accent.Cyan);
        SetGradient(resources, "PrimaryGradientBrush", accent.GradientStart, accent.GradientEnd);
        SetGradient(
            resources,
            "SidebarGradientBrush",
            isDark ? "#04101F" : "#041E38",
            palette.Sidebar,
            isDark ? "#0B385B" : "#0A426C");
        SetGradient(
            resources,
            "PremiumHeroBrush",
            isDark ? "#061B30" : "#082E50",
            accent.Deep,
            accent.Cyan);
        SetGradient(resources, "AmbientGlowBrush", palette.GlowStart, palette.GlowMiddle, "#00000000");
    }

    private static void ApplyLayout(ResourceDictionary resources, AppSettings settings, double windowWidth, Palette palette)
    {
        var density = settings.CompactMode ? "Compact" : NormalizeDensity(settings.CardDensity);
        var spacing = density switch
        {
            "Spacious" => new Density(new Thickness(24), new Thickness(21), new Thickness(30)),
            "Compact" => new Density(new Thickness(14), new Thickness(13), new Thickness(18)),
            _ => new Density(new Thickness(20), new Thickness(18), new Thickness(26))
        };

        var rounded = settings.UseRoundedCorners;
        resources["CardPadding"] = spacing.CardPadding;
        resources["MetricCardPadding"] = spacing.MetricCardPadding;
        resources["HeroPanelPadding"] = spacing.HeroPadding;
        resources["CardCornerRadius"] = new CornerRadius(rounded ? 16 : 6);
        resources["MetricCardCornerRadius"] = new CornerRadius(rounded ? 14 : 5);
        resources["HeroCornerRadius"] = new CornerRadius(rounded ? 22 : 7);
        resources["ControlCornerRadius"] = new CornerRadius(rounded ? 12 : 5);

        var scale = Math.Clamp(settings.UiScalePercent, 90, 150) / 100d;
        resources["BaseFontSize"] = ScaleFont(14d, scale);
        resources["PageTitleFontSize"] = ScaleFont(40d, scale);
        resources["PageSubtitleFontSize"] = ScaleFont(16d, scale);
        resources["SectionTitleFontSize"] = ScaleFont(18d, scale);
        resources["BodyFontSize"] = ScaleFont(14d, scale);
        resources["MutedFontSize"] = ScaleFont(13d, scale);
        resources["MetricLabelFontSize"] = ScaleFont(11d, scale);
        resources["ButtonFontSize"] = ScaleFont(14d, scale);

        var sidebarMode = NormalizeSidebarStyle(settings.SidebarStyle);
        var iconNavigation = sidebarMode == "Icons only" ||
            sidebarMode == "Auto (adaptive)" && windowWidth > 0 && windowWidth < 1280;
        resources["SidebarWidth"] = new GridLength(iconNavigation ? 82 : 224);
        resources["SidebarBrandWidth"] = iconNavigation ? 48d : 158d;
        resources["SidebarLabelVisibility"] = iconNavigation ? Visibility.Collapsed : Visibility.Visible;
        resources["SidebarCaptionVisibility"] = iconNavigation ? Visibility.Collapsed : Visibility.Visible;
        resources["SidebarFooterTextVisibility"] = iconNavigation ? Visibility.Collapsed : Visibility.Visible;

        var immersiveEffects = string.Equals(NormalizeVisualEffects(settings.VisualEffectsProfile), "Immersive", StringComparison.Ordinal);
        resources["WorkspaceAmbientOpacity"] = immersiveEffects ? 0.46d : 0.30d;
        resources["CardShadowEffect"] = new DropShadowEffect
        {
            BlurRadius = immersiveEffects ? 27 : 19,
            ShadowDepth = immersiveEffects ? 8 : 5,
            Opacity = immersiveEffects ? 0.24 : 0.16,
            Color = (Color)ColorConverter.ConvertFromString(palette.Shadow)!
        };
        resources["HeroShadowEffect"] = new DropShadowEffect
        {
            BlurRadius = immersiveEffects ? 36 : 28,
            ShadowDepth = immersiveEffects ? 12 : 8,
            Opacity = immersiveEffects ? 0.32 : 0.22,
            Color = (Color)ColorConverter.ConvertFromString(palette.HeroShadow)!
        };
    }

    public static string NormalizeDensity(string? density) =>
        string.Equals(density, "Compact", StringComparison.OrdinalIgnoreCase)
            ? "Compact"
            : string.Equals(density, "Spacious", StringComparison.OrdinalIgnoreCase)
                ? "Spacious"
                : "Comfortable";

    public static string NormalizeSidebarStyle(string? style) =>
        string.Equals(style, "Icons only", StringComparison.OrdinalIgnoreCase)
            ? "Icons only"
            : string.Equals(style, "Auto (adaptive)", StringComparison.OrdinalIgnoreCase)
                ? "Auto (adaptive)"
                : "Full labels";

    public static string NormalizeDashboardImageStyle(string? style) =>
        string.Equals(style, "Minimal", StringComparison.OrdinalIgnoreCase)
            ? "Minimal"
            : string.Equals(style, "Subtle aircraft", StringComparison.OrdinalIgnoreCase)
                ? "Subtle aircraft"
                : "Aircraft & sky";

    public static string NormalizeVisualEffects(string? profile) =>
        string.Equals(profile, "Immersive", StringComparison.OrdinalIgnoreCase)
            ? "Immersive"
            : "Signature";

    private static bool IsWindowsUsingLightAppearance()
    {
        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(personalize?.GetValue("AppsUseLightTheme", 1)) != 0;
        }
        catch
        {
            // Auto should always leave Ember usable, even on older Windows
            // versions where the personalisation registry key is unavailable.
            return true;
        }
    }

    private static AccentPalette ResolveAccent(AppSettings settings, Palette palette)
    {
        var preset = settings.AccentPreset?.Trim() ?? "BAV Blue";
        if (string.Equals(preset, "Custom", StringComparison.OrdinalIgnoreCase) &&
            TryReadColor(settings.AccentColor, out var custom))
        {
            return AccentPalette.From(custom);
        }

        return preset.ToUpperInvariant() switch
        {
            "CRIMSON" => AccentPalette.From("#D93D62"),
            "VIOLET" => AccentPalette.From("#7C63D9"),
            "EMERALD" => AccentPalette.From("#168D70"),
            "AMBER" => AccentPalette.From("#C87813"),
            "SLATE" => AccentPalette.From("#60748C"),
            _ => new AccentPalette(palette.Primary, palette.PrimaryDeep, palette.Cyan, palette.PrimaryGradientStart, palette.PrimaryGradientEnd)
        };
    }

    private static bool TryReadColor(string? value, out Color color)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
            // The setting remains editable; an invalid value simply retains
            // the dependable British Airways Virtual blue until corrected.
        }

        color = default;
        return false;
    }

    private static ResourceDictionary? FindThemeResources()
    {
        var applicationResources = Application.Current?.Resources;
        return applicationResources?.MergedDictionaries.FirstOrDefault(dictionary =>
                   dictionary.Contains("AppBackgroundBrush")) ?? applicationResources;
    }

    private static double ScaleFont(double size, double scale) =>
        Math.Round(size * scale, MidpointRounding.AwayFromZero);

    private static void SetBrush(ResourceDictionary resources, string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            // Update the existing Freezable so both DynamicResource and the
            // established view styles that reference this brush redraw safely.
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static void SetGradient(ResourceDictionary resources, string key, params string[] colors)
    {
        if (resources[key] is not GradientBrush source)
        {
            return;
        }

        if (!source.IsFrozen)
        {
            for (var index = 0; index < Math.Min(source.GradientStops.Count, colors.Length); index++)
            {
                source.GradientStops[index].Color = (Color)ColorConverter.ConvertFromString(colors[index])!;
            }

            return;
        }

        var replacement = source.Clone();
        for (var index = 0; index < Math.Min(replacement.GradientStops.Count, colors.Length); index++)
        {
            replacement.GradientStops[index].Color = (Color)ColorConverter.ConvertFromString(colors[index])!;
        }

        resources[key] = replacement;
    }

    private sealed record Density(Thickness CardPadding, Thickness MetricCardPadding, Thickness HeroPadding);

    private sealed record AccentPalette(string Primary, string Deep, string Cyan, string GradientStart, string GradientEnd)
    {
        public static AccentPalette From(string color) => From((Color)ColorConverter.ConvertFromString(color)!);

        public static AccentPalette From(Color color) => new(
            ToHex(color),
            ToHex(Blend(color, Colors.Black, 0.30)),
            ToHex(Blend(color, Colors.White, 0.20)),
            ToHex(Blend(color, Colors.White, 0.12)),
            ToHex(Blend(color, Colors.Black, 0.22)));

        private static Color Blend(Color baseColor, Color target, double amount) =>
            Color.FromRgb(
                (byte)Math.Round(baseColor.R + (target.R - baseColor.R) * amount),
                (byte)Math.Round(baseColor.G + (target.G - baseColor.G) * amount),
                (byte)Math.Round(baseColor.B + (target.B - baseColor.B) * amount));

        private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
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
        string SuccessText,
        string Shadow,
        string HeroShadow)
    {
        public static Palette Light { get; } = new(
            "#F2F6FA", "#062B4C", "#FFFFFF", "#F8FAFD", "#D8E2EC", "#E8EEF4",
            "#0B73B9", "#07528E", "#117DBC", "#13865B", "#B66A09", "#C42F52",
            "#102A43", "#52677D", "#7A8DA1", "#1689CF", "#07518C", "#142F73B9", "#0BF2F6FA",
            "#EAF5FF", "#B8DDF5", "#EAF8F1", "#62BE93", "#0C704C", "#16324F", "#07213B");

        public static Palette Dark { get; } = new(
            "#071321", "#061729", "#102033", "#16283D", "#2B415B", "#1E344D",
            "#318EDB", "#1766A3", "#54BBF4", "#47D895", "#F2B84B", "#FA6D8D",
            "#F3F8FF", "#B8CAE0", "#7F9AB5", "#3E9FEA", "#164D82", "#18318EDB", "#00071321",
            "#142C47", "#3675A8", "#173D2B", "#4FC98C", "#86E9B9", "#000000", "#000000");
    }
}
