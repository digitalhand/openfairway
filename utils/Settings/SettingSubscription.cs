using System;
using System.Collections.Generic;

/// <summary>
/// Lightweight subscription wrapper for a Setting's SettingChanged signal.
/// Disposes cleanly to prevent signal leaks.
/// </summary>
public sealed class SettingSubscription : IDisposable
{
    private readonly Setting _setting;
    private readonly Setting.SettingChangedEventHandler _handler;
    private bool _disposed;

    public SettingSubscription(Setting setting, Setting.SettingChangedEventHandler handler)
    {
        _setting = setting;
        _handler = handler;
        _setting.SettingChanged += _handler;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _setting.SettingChanged -= _handler;
    }

    /// <summary>
    /// Disposes all subscriptions in the list and clears it.
    /// </summary>
    public static void DisposeAll(List<SettingSubscription> subscriptions)
    {
        foreach (var sub in subscriptions)
            sub.Dispose();

        subscriptions.Clear();
    }
}
