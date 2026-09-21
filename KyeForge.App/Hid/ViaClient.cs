namespace KyeForge.App.Hid;

using System.IO;

/// <summary>VIA raw HID protocol - command and value IDs per QMK quantum/via.h.</summary>
public static class ViaCommands
{
    // Report buffer size used by VIA
    public const int BufferSize = 32;

    // ---- via_command_id ----
    public const byte GetProtocolVersion = 0x01;
    public const byte GetKeyboardValue = 0x02;
    public const byte SetKeyboardValue = 0x03;
    public const byte DynamicKeymapGetKeycode = 0x04;
    public const byte DynamicKeymapSetKeycode = 0x05;
    public const byte DynamicKeymapReset = 0x06;
    public const byte CustomSetValue = 0x07;   // lighting / custom channels - set
    public const byte CustomGetValue = 0x08;   // lighting / custom channels - get
    public const byte CustomSave = 0x09;       // lighting / custom channels - save
    public const byte EepromReset = 0x0A;
    public const byte BootloaderJump = 0x0B;
    public const byte GetMacroCount = 0x0C;
    public const byte GetMacroBufferSize = 0x0D;
    public const byte GetMacroBuffer = 0x0E;
    public const byte UpdateMacroBuffer = 0x0F;
    public const byte MacroReset = 0x10;
    public const byte GetLayerCount = 0x11;
    public const byte KeymapGetBuffer = 0x12;
    public const byte KeymapSetBuffer = 0x13;
    public const byte GetEncoder = 0x14;
    public const byte SetEncoder = 0x15;
    public const byte Unhandled = 0xFF;

    // ---- via_keyboard_value_id ----
    public const byte IdUptime = 0x01;
    public const byte IdLayoutOptions = 0x02;
    public const byte IdSwitchMatrixState = 0x03;
    public const byte IdFirmwareVersion = 0x04;
    public const byte IdDeviceIndication = 0x05;

    // ---- via_channel_id ----
    public const byte ChannelCustom = 0;
    public const byte ChannelQmkBacklight = 1;
    public const byte ChannelQmkRgblight = 2;
    public const byte ChannelQmkRgbMatrix = 3;
    public const byte ChannelQmkAudio = 4;
    public const byte ChannelQmkLedMatrix = 5;

    // ---- per-channel lighting value ids (backlight: 1..2, rgblight/rgb_matrix: 1..4) ----
    public const byte LightingBrightness = 1;
    public const byte LightingEffect = 2;
    public const byte LightingEffectSpeed = 3;
    public const byte LightingColor = 4;
}

public class KeyboardValueResult
{
    public bool Success { get; set; }
    public byte Value { get; set; }
    public byte[]? Extra { get; set; }
}

public class KeycodeResult
{
    public bool Success { get; set; }
    public ushort Keycode { get; set; }
}

public class MacroBufferResult
{
    public bool Success { get; set; }
    public byte Id { get; set; }
    public byte Offset { get; set; }
    public byte[]? Data { get; set; }
}

/// <summary>
/// High-level VIA client. Subscribes to a ConnectedKeyboard's report stream,
/// sends commands and matches responses to pending requests.
/// </summary>
public class ViaClient : IDisposable
{
    private readonly ConnectedKeyboard _kb;
    private readonly object _lock = new();
    private readonly Dictionary<string, TaskCompletionSource<byte[]>> _pending = new();
    private byte[] _lastRequest = Array.Empty<byte>();

    public double ProtocolVersion { get; private set; } = 0;

    /// <summary>True when the board answered the Vial handshake instead of the VIA one.</summary>
    public bool IsVial { get; private set; }

    public ViaClient(ConnectedKeyboard keyboard)
    {
        _kb = keyboard;
        _kb.DataReceived += OnData;
    }

    public void Dispose()
    {
        _kb.DataReceived -= OnData;
        lock (_lock)
        {
            foreach (var pending in _pending.Values)
                pending.TrySetCanceled();
            _pending.Clear();
        }
    }

    private void OnData(byte[] report)
    {
        if (report.Length == 0) return;
        byte cmd = report[0];

        string key;
        lock (_lock)
        {
            // Build a match key from the command and identifying bytes
            key = BuildResponseKey(report);
            if (_pending.TryGetValue(key, out var tcs))
            {
                _pending.Remove(key);
                tcs.TrySetResult(report);
                return;
            }

            // Fallback: try matching by command only if request was a 2-byte value get
            if (_lastRequest.Length >= 2 && cmd == _lastRequest[0] && _pending.Count == 1)
            {
                var kv = _pending.First();
                _pending.Remove(kv.Key);
                kv.Value.TrySetResult(report);
                return;
            }
        }
    }

    private string BuildResponseKey(byte[] r)
    {
        byte cmd = r[0];
        switch (cmd)
        {
            case ViaCommands.DynamicKeymapGetKeycode:
                // keycode response: cmd, layer, row, col
                return $"KEYCODE:{r[1]}:{r[2]}:{r[3]}";
            case ViaCommands.GetKeyboardValue:
                return $"VALUE:{r[1]}";
            case ViaCommands.CustomGetValue:
                // lighting channel response: cmd, channel, valueId
                return $"QKVAL:{r[1]}:{r[2]}";
            case ViaCommands.GetMacroBuffer:
                return $"MACRO:{r[1]}:{r[2]}";
            case ViaCommands.GetProtocolVersion:
                return "PROTO";
            case 0xFE:
                // Vial channel
                return "VIAL";
            default:
                return $"CMD:{cmd}";
        }
    }

    private Task<byte[]> SendCommand(byte[] request, string responseKey)
    {
        var packet = new byte[ViaCommands.BufferSize];
        Array.Copy(request, packet, Math.Min(request.Length, packet.Length));

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _lastRequest = packet;
            _pending[responseKey] = tcs;
        }
        if (!_kb.Send(packet))
        {
            lock (_lock) { _pending.Remove(responseKey); }
            tcs.TrySetException(new IOException("Failed to write to HID device"));
        }
        return tcs.Task;
    }

    public async Task<double> GetProtocolVersionAsync(CancellationToken ct = default)
    {
        var req = new byte[] { ViaCommands.GetProtocolVersion };
        var value = await SendSafe(() => SendCommand(req, "PROTO"), "PROTO", ct: ct).ConfigureAwait(false);
        if (value != null && value.Length >= 3 && value[0] == ViaCommands.GetProtocolVersion)
        {
            int major = value[1];
            int minor = value[2];
            // VIA protocol version is a single 16-bit number (e.g. 0x000C = 12)
            ProtocolVersion = (major << 8) | minor;
            return ProtocolVersion;
        }
        return 0;
    }

    /// <summary>
    /// Fallback handshake for Vial-only firmware: {0xFE, 0x01} = vial_get_keyboard_id.
    /// Pure-VIA boards ignore it; Vial boards echo a frame starting with 0xFE.
    /// Keymap read/write commands are identical in both protocols.
    /// </summary>
    public async Task<bool> ProbeVialAsync(CancellationToken ct = default)
    {
        var req = new byte[] { 0xFE, 0x01 };
        var r = await SendSafe(() => SendCommand(req, "VIAL"), "VIAL", ct: ct).ConfigureAwait(false);
        if (r != null && r.Length >= 2 && r[0] == 0xFE)
        {
            IsVial = true;
            return true;
        }
        return false;
    }

    public async Task<KeyboardValueResult?> GetKeyboardValueAsync(byte id, CancellationToken ct = default)
    {
        var req = new byte[] { ViaCommands.GetKeyboardValue, id };
        var r = await SendSafe(() => SendCommand(req, $"VALUE:{id}"), $"VALUE:{id}", ct: ct).ConfigureAwait(false);
        if (r == null) return null;
        return new KeyboardValueResult
        {
            Success = r[1] == id,
            Value = r.Length > 2 ? r[2] : (byte)0,
            Extra = r.Length > 3 ? r[3..] : null
        };
    }

    public Task<bool> SetKeyboardValueAsync(byte id, byte value, CancellationToken ct = default)
    {
        var req = new byte[] { ViaCommands.SetKeyboardValue, id, value };
        return SendAndForget(req);
    }

    /// <summary>Lighting / custom channel value set: {0x07, channel, valueId, value}.</summary>
    public Task<bool> SetQkValueAsync(byte channel, byte valueId, byte value, CancellationToken ct = default)
    {
        var req = new byte[] { ViaCommands.CustomSetValue, channel, valueId, value, 0, 0, 0 };
        return SendAndForget(req);
    }

    /// <summary>Lighting / custom channel multi-byte set (e.g. color = hue+sat).</summary>
    public Task<bool> SetQkValueDataAsync(byte channel, byte valueId, byte data0, byte data1, CancellationToken ct = default)
    {
        var req = new byte[] { ViaCommands.CustomSetValue, channel, valueId, data0, data1, 0, 0 };
        return SendAndForget(req);
    }

    /// <summary>Lighting / custom channel value get: {0x08, channel, valueId}.</summary>
    public async Task<KeyboardValueResult?> GetQkValueAsync(byte channel, byte valueId, CancellationToken ct = default)
    {
        var req = new byte[] { ViaCommands.CustomGetValue, channel, valueId };
        var key = $"QKVAL:{channel}:{valueId}";
        var r = await SendSafe(() => SendCommand(req, key), key, ct: ct).ConfigureAwait(false);
        if (r == null || r.Length < 4) return null;
        return new KeyboardValueResult
        {
            Success = r[1] == channel && r[2] == valueId,
            Value = r[3],
            Extra = r.Length > 4 ? r[4..] : null
        };
    }

    /// <summary>Persists the lighting channel to EEPROM: {0x09, channel}.</summary>
    public Task<bool> SaveQkChannelAsync(byte channel, CancellationToken ct = default)
    {
        var req = new byte[] { ViaCommands.CustomSave, channel, 0 };
        return SendAndForget(req);
    }

    public async Task<KeycodeResult?> GetKeycodeAsync(byte layer, byte row, byte col, CancellationToken ct = default)
    {
        var req = new byte[] { ViaCommands.DynamicKeymapGetKeycode, layer, row, col };
        var r = await SendSafe(() => SendCommand(req, $"KEYCODE:{layer}:{row}:{col}"), $"KEYCODE:{layer}:{row}:{col}", ct: ct).ConfigureAwait(false);
        if (r == null || r.Length < 6) return null;
        ushort kc = (ushort)((r[4] << 8) | r[5]);
        return new KeycodeResult { Success = true, Keycode = kc };
    }

    public Task<bool> SetKeycodeAsync(byte layer, byte row, byte col, ushort keycode, CancellationToken ct = default)
    {
        var req = new byte[] {
            ViaCommands.DynamicKeymapSetKeycode, layer, row, col,
            (byte)(keycode >> 8), (byte)(keycode & 0xFF)
        };
        return SendAndForget(req);
    }

    private Task<bool> SendAndForget(byte[] request)
    {
        try
        {
            var packet = new byte[ViaCommands.BufferSize];
            Array.Copy(request, packet, request.Length);
            return Task.FromResult(_kb.Send(packet));
        }
        catch { return Task.FromResult(false); }
    }

    private async Task<byte[]?> SendSafe(Func<Task<byte[]>> send, string key, byte[]? expected = null, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(1600 + attempt * 900);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                var response = await send().WaitAsync(linked.Token).ConfigureAwait(false);
                if (response.Length > 0) return response;
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException)
            {
                lock (_lock) { _pending.Remove(key); }
                if (ct.IsCancellationRequested) return null;
                await Task.Delay(140 + attempt * 140, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                lock (_lock) { _pending.Remove(key); }
                return null;
            }
        }

        return null;
    }
}
