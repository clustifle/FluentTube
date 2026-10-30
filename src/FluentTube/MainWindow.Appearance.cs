using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
namespace FluentTube;

public sealed partial class MainWindow
{
    private ResourceDictionary? xpResources;
    private bool xpApplied;
    private static Color Palette(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    private static SolidColorBrush Paint(uint rgb) => new(Palette(rgb));
    private static LinearGradientBrush Gloss(uint top, uint bottom) => new()
    {
        StartPoint = new(0, 0), EndPoint = new(0, 1),
        GradientStops = { new GradientStop { Color = Palette(top), Offset = 0 }, new GradientStop { Color = Palette(bottom), Offset = 1 } }
    };
    private static ResourceDictionary XpPalette(bool dark)
    {
        var resources = new ResourceDictionary();
        resources["ContentControlThemeFontFamily"] = new FontFamily("Tahoma");
        resources["TextControlThemeFontFamily"] = new FontFamily("Tahoma");
        resources["XamlAutoFontFamily"] = new FontFamily("Tahoma");
        resources["LayerFillColorDefaultBrush"] = Paint(dark ? 0x292929u : 0xECE9D8u);
        resources["CardBackgroundFillColorDefaultBrush"] = Paint(dark ? 0x303030u : 0xF8F6ECu);
        resources["CardBackgroundFillColorSecondaryBrush"] = Paint(dark ? 0x242424u : 0xF1EFE2u);
        resources["CardStrokeColorDefaultBrush"] = Paint(dark ? 0x555555u : 0xA7A28Du);
        resources["ControlFillColorDefaultBrush"] = Gloss(dark ? 0x505050u : 0xFFFFFFu, dark ? 0x303030u : 0xE1E5EEu);
        resources["ControlFillColorSecondaryBrush"] = Paint(dark ? 0x444444u : 0xEAF2FFu);
        resources["ControlFillColorTertiaryBrush"] = Paint(dark ? 0x242424u : 0xD6E4F8u);
        resources["ControlStrokeColorDefaultBrush"] = Paint(dark ? 0x777777u : 0x7F9DB9u);
        resources["TextFillColorPrimaryBrush"] = Paint(dark ? 0xF4F4F4u : 0x17253Cu);
        resources["TextFillColorSecondaryBrush"] = Paint(dark ? 0xC5C5C5u : 0x4A5261u);
        resources["SubtleFillColorSecondaryBrush"] = Paint(dark ? 0x393939u : 0xDDE9FAu);
        resources["SubtleFillColorTertiaryBrush"] = Paint(dark ? 0x4A3A2Bu : 0xCBDDF7u);
        resources["AccentFillColorDefaultBrush"] = Paint(dark ? 0xD97922u : 0x245EDBu);
        resources["AccentFillColorSecondaryBrush"] = Paint(dark ? 0xEE963Fu : 0x3878EDu);
        resources["AccentTextFillColorPrimaryBrush"] = Paint(dark ? 0xFFA653u : 0x184CB0u);
        resources["AccentTextFillColorSecondaryBrush"] = resources["AccentTextFillColorPrimaryBrush"];
        resources["TextOnAccentFillColorPrimaryBrush"] = Paint(0xFFFFFF);
        return resources;
    }
    private void ApplyAppearance()
    {
        var xp = preferences.Appearance == "Windows XP";
        var theme = preferences.Theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        if (xp != xpApplied)
        {
            if (xp)
            {
                xpResources = new ResourceDictionary();
                xpResources.ThemeDictionaries["Light"] = XpPalette(false);
                xpResources.ThemeDictionaries["Dark"] = XpPalette(true);
                Root.Resources.MergedDictionaries.Add(xpResources);
            }
            else if (xpResources is not null)
            {
                Root.Resources.MergedDictionaries.Remove(xpResources); xpResources = null;
            }
            xpApplied = xp;
            // Refresh existing ThemeResource references when the palette is replaced.
            Root.RequestedTheme = Root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        }
        Root.RequestedTheme = theme;
        var font = new FontFamily(xp ? "Tahoma" : "Segoe UI Variable");
        Navigation.FontFamily = font;
        ApplyAppearanceFont(Root);
        UpdateAppearanceChrome();
    }
    private void ApplyAppearanceFont(DependencyObject element)
    {
        if (element is IconElement) return;
        var font = new FontFamily(preferences.Appearance == "Windows XP" ? "Tahoma" : "Segoe UI Variable");
        if (element is Control control) control.FontFamily = font;
        if (element is TextBlock text) text.FontFamily = font;
        if (element is RichTextBlock rich) rich.FontFamily = font;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) ApplyAppearanceFont(VisualTreeHelper.GetChild(element, i));
    }
    private void UpdateAppearanceChrome()
    {
        if (AppTitleText is null) return;
        var xp = preferences.Appearance == "Windows XP";
        var dark = Root.ActualTheme == ElementTheme.Dark;
        Root.Background = xp ? Paint(dark ? 0x1F1F1Fu : 0xECE9D8u) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Navigation.Background = xp ? Paint(dark ? 0x222222u : 0xD6DFF7u) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AppTitleBar.Background = xp ? Gloss(dark ? 0x525252u : 0x3986F5u, dark ? 0x161616u : 0x0751C4u) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AppTitleText.Foreground = xp || dark ? Paint(0xFFFFFF) : Paint(0x171717);
        AppTitleText.FontFamily = new FontFamily(xp ? "Tahoma" : "Segoe UI Variable");
        AppTitleText.FontWeight = xp ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
    }
}

