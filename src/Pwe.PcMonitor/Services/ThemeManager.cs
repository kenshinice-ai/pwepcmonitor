using Microsoft.Win32;
using System.Windows.Media;
using System.ComponentModel;
using System.Windows;
using Windows.UI.ViewManagement;
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
    public static bool AnimationsEnabled { get; private set; } = SystemParameters.ClientAreaAnimation;
    public static event EventHandler? AppearanceChanged;
    private static ThemePreference _preference = ThemePreference.System;
    private static UISettings? _systemSettings;
    private static bool _following;

    public static void StartFollowingSystem()
    {
        if (_following) return;
        _following = true;
        SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
        SystemParameters.StaticPropertyChanged += SystemParameterChanged;
        try
        {
            _systemSettings = new UISettings();
            _systemSettings.AdvancedEffectsEnabledChanged += UiSettingsChanged;
            _systemSettings.AnimationsEnabledChanged += UiSettingsChanged;
            _systemSettings.ColorValuesChanged += UiSettingsChanged;
        }
        catch (Exception exception) { AppDiagnostics.Write("System effects preferences unavailable; using opaque surfaces", exception); }
        Apply(_preference);
    }

    public static void StopFollowingSystem()
    {
        _following = false;
        SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= SystemParameterChanged;
        if (_systemSettings is not null)
        {
            _systemSettings.AdvancedEffectsEnabledChanged -= UiSettingsChanged;
            _systemSettings.AnimationsEnabledChanged -= UiSettingsChanged;
            _systemSettings.ColorValuesChanged -= UiSettingsChanged;
            _systemSettings = null;
        }
    }

    private static void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => QueueRefresh();
    private static void SystemParameterChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();
    private static void UiSettingsChanged(UISettings sender, object args) => QueueRefresh();
    private static void QueueRefresh()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(new Action(() => { if (_following) Apply(_preference); }));
    }

    public static void Apply(ThemePreference preference)
    {
        _preference = preference;
        IsDark = preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => IsSystemDark()
        };

        var transparent = false;
        AnimationsEnabled = SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        try
        {
            transparent = _systemSettings?.AdvancedEffectsEnabled == true;
            AnimationsEnabled &= _systemSettings?.AnimationsEnabled ?? true;
        }
        catch { /* Retain opaque surfaces if the settings service is unavailable. */ }
        ApplyPalette(IsDark, SystemParameters.HighContrast, transparent);
        AppearanceChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyPalette(bool dark, bool highContrast, bool transparent)
    {
        IsDark = dark;
        var resources = System.Windows.Application.Current.Resources;
        if (highContrast)
        {
            foreach (var key in new[] { "BackgroundBrush", "CardBrush", "WidgetSurfaceBrush", "WidgetTileBrush", "BadgeHotBrush", "BadgeWarmBrush", "RailBrush" })
                Set(resources, key, SystemColors.WindowColor);
            foreach (var key in new[] { "TextBrush", "MutedBrush", "WidgetMutedBrush", "StrokeBrush" })
                Set(resources, key, SystemColors.WindowTextColor);
            Set(resources, "AccentBrush", SystemColors.HighlightColor);
            Set(resources, "HotBrush", SystemColors.WindowTextColor);
            return;
        }
        Set(resources, "BackgroundBrush", WithAlpha(IsDark ? Navy : Paper, transparent ? 0.94 : 1));
        Set(resources, "CardBrush", WithAlpha(IsDark ? FromHex("#152239") : Colors.White, transparent ? 0.90 : 1));
        Set(resources, "WidgetSurfaceBrush", WithAlpha(IsDark ? FromHex("#152239") : Colors.White, transparent ? 0.96 : 1));
        Set(resources, "WidgetTileBrush", IsDark ? FromHex("#1D2B42") : FromHex("#F1F3F6"));
        Set(resources, "WidgetMutedBrush", IsDark ? FromHex("#BCC7D5") : FromHex("#536174"));
        Set(resources, "StrokeBrush", WithAlpha(IsDark ? FromHex("#26344B") : FromHex("#E3DFD8"), 0.72));
        Set(resources, "RailBrush", WithAlpha(IsDark ? FromHex("#2C3A51") : FromHex("#EDEAE4"), 0.68));
        Set(resources, "TextBrush", IsDark ? Paper : Navy);
        Set(resources, "MutedBrush", IsDark ? FromHex("#ABB7C9") : FromHex("#536174"));
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
