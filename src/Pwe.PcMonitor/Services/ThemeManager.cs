using Microsoft.Win32;
using System.Windows.Media;
using Pwe.PcMonitor.Models;
using MediaColor = System.Windows.Media.Color;

namespace Pwe.PcMonitor.Services;

public static class ThemeManager
{
    private static readonly MediaColor Navy = FromHex("#0E1729");
    private static readonly MediaColor Amber = FromHex("#F5B335");
    private static readonly MediaColor AmberDeep = FromHex("#A16207");
    private static readonly MediaColor Paper = FromHex("#F7F5F2");
    private static readonly MediaColor Coral = FromHex("#E8654E");
    private static readonly MediaColor CoralDeep = FromHex("#B03A24");

    public static bool IsDark { get; private set; } = true;

    public static void Apply(ThemePreference preference)
    {
        IsDark = preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => IsSystemDark()
        };

        var resources = System.Windows.Application.Current.Resources;
        Set(resources, "BackgroundBrush", IsDark ? Navy : Paper);
        Set(resources, "CardBrush", IsDark ? FromHex("#152239") : Colors.White);
        Set(resources, "StrokeBrush", IsDark ? FromHex("#26344B") : FromHex("#E3DFD8"));
        Set(resources, "RailBrush", IsDark ? FromHex("#2C3A51") : FromHex("#EDEAE4"));
        Set(resources, "TextBrush", IsDark ? Paper : Navy);
        Set(resources, "MutedBrush", IsDark ? FromHex("#9098A8") : FromHex("#6B7280"));
        Set(resources, "AccentBrush", IsDark ? Amber : AmberDeep);
        Set(resources, "HotBrush", IsDark ? Coral : CoralDeep);
        Set(resources, "BadgeHotBrush", WithAlpha(IsDark ? Coral : CoralDeep, 0.16));
        Set(resources, "BadgeWarmBrush", WithAlpha(IsDark ? Amber : AmberDeep, 0.16));
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return true;
        }
    }

    private static void Set(System.Windows.ResourceDictionary resources, string key, MediaColor color) =>
        resources[key] = new SolidColorBrush(color);

    private static MediaColor FromHex(string value) => (MediaColor)ColorConverter.ConvertFromString(value);

    private static MediaColor WithAlpha(MediaColor color, double alpha) =>
        MediaColor.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), color.R, color.G, color.B);
}
