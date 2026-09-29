using System.Windows.Media;
using KyeForge.App.Hid;
using KyeForge.App.Models;
using KyeForge.App.ViewModels;

namespace KyeForge.App.Services;

/// <summary>
/// Writes lighting values to the keyboard outside LightingView
/// (tray flyout quick actions). Live values only, no EEPROM writes.
/// </summary>
public static class LightingControl
{
    private static int? _savedBrightness;

    private static (ViaClient Client, QkLighting Qk)? Resolve(AppState state)
    {
        var qk = state.Definition?.QkLighting;
        var client = state.Client;
        if (qk == null || client == null) return null;
        return (client, qk);
    }

    /// <summary>Pushes a profile's stored lighting to the keyboard + mirrors settings.</summary>
    public static async Task<bool> ApplyProfileAsync(AppState state, AppProfile profile)
    {
        if (Resolve(state) is not var (client, qk)) return false;
        try
        {
            int? effectId = profile.LightingEffect;
            if (effectId.HasValue)
                await client.SetQkValueAsync(qk.Group, qk.EffectSub,
                    (byte)Math.Clamp(effectId.Value, 0, 255));
            if (profile.LightingBrightness.HasValue)
                await client.SetQkValueAsync(qk.Group, qk.BrightnessSub,
                    (byte)Math.Clamp(profile.LightingBrightness.Value, 0, 255));
            if (profile.LightingSpeed.HasValue)
                await client.SetQkValueAsync(qk.Group, qk.SpeedSub,
                    (byte)Math.Clamp(profile.LightingSpeed.Value, 0, 255));
            if (!string.IsNullOrWhiteSpace(profile.LightingColor) &&
                Customization.TryParse(profile.LightingColor, out var c))
            {
                ColorToHsv(c, out var hue, out var sat);
                await client.SetQkValueDataAsync(qk.Group, qk.ColorSub, hue, sat);
            }

            var s = AppSettings.Load();
            if (profile.LightingEffect.HasValue) s.LightingEffect = profile.LightingEffect;
            if (profile.LightingBrightness.HasValue) s.LightingBrightness = profile.LightingBrightness;
            if (profile.LightingSpeed.HasValue) s.LightingSpeed = profile.LightingSpeed;
            if (!string.IsNullOrWhiteSpace(profile.LightingColor)) s.LightingColor = profile.LightingColor;
            s.Save();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Reads backlight state from the keyboard (null = unknown / offline).</summary>
    public static async Task<bool?> GetBacklightOnAsync(AppState state)
    {
        if (Resolve(state) is not var (client, qk)) return null;
        try
        {
            var r = await client.GetQkValueAsync(qk.Group, qk.BrightnessSub);
            if (r == null || !r.Success) return null;
            return r.Value > 0;
        }
        catch { return null; }
    }

    /// <summary>Toggles backlight via brightness 0 / restore. Returns new state (null = failed).</summary>
    public static async Task<bool?> ToggleBacklightAsync(AppState state)
    {
        var on = await GetBacklightOnAsync(state);
        if (on == null) return null;
        if (Resolve(state) is not var (client, qk)) return null;
        try
        {
            if (on.Value)
            {
                var cur = await client.GetQkValueAsync(qk.Group, qk.BrightnessSub);
                if (cur != null && cur.Success && cur.Value > 0) _savedBrightness = cur.Value;
                await client.SetQkValueAsync(qk.Group, qk.BrightnessSub, 0);
                MirrorBrightness(0);
                return false;
            }
            int restore = _savedBrightness
                ?? (int)Math.Clamp(AppSettings.Load().LightingBrightness ?? 80, 1, 255);
            await client.SetQkValueAsync(qk.Group, qk.BrightnessSub, (byte)restore);
            MirrorBrightness(restore);
            return true;
        }
        catch { return null; }
    }

    private static void MirrorBrightness(int value)
    {
        try
        {
            var s = AppSettings.Load();
            s.LightingBrightness = value;
            s.Save();
        }
        catch { }
    }

    private static void ColorToHsv(Color c, out byte hue, out byte sat)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }
        if (h < 0) h += 360;

        double s = max <= 0 ? 0 : delta / max;
        hue = (byte)Math.Round(h / 360.0 * 255.0);
        sat = (byte)Math.Round(s * 255.0);
    }
}
