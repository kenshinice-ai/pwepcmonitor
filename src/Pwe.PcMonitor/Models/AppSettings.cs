namespace Pwe.PcMonitor.Models;

public enum ThemePreference
{
    System,
    Dark,
    Light
}

public sealed record AppSettings
{
    public double RefreshSeconds { get; init; } = 2;
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    public bool ShowAllSensors { get; init; }
}
