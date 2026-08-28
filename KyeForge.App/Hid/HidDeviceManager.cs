using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Encodings;

namespace KyeForge.App.Hid;

/// <summary>A discovered VIA-capable HID keyboard.</summary>
public class ConnectedKeyboard : IDisposable
{
    private readonly List<HidDevice> _candidates;

    public HidDevice Device { get; private set; }
    public HidStream? Stream { get; private set; }
    public IReadOnlyList<HidDevice> Candidates => _candidates;
    public int InterfaceCount => _candidates.Count;

    public string Name => !string.IsNullOrWhiteSpace(Device.GetFriendlyName())
        ? Device.GetFriendlyName()
        : $"HID Keyboard ({Device.VendorID:X4}:{Device.ProductID:X4})";

    public string Path => Device.DevicePath;
    public ushort VendorId => (ushort)Device.VendorID;
    public ushort ProductId => (ushort)Device.ProductID;
    public int InputReportLength => Device.GetMaxInputReportLength();
    public int OutputReportLength => Device.GetMaxOutputReportLength();

    public delegate void DataReceivedHandler(byte[] report);
    public event DataReceivedHandler? DataReceived;

    private CancellationTokenSource? _cts;
    private Task? _readerTask;
    private bool _disposed;

    public ConnectedKeyboard(HidDevice dev)
        : this(new[] { dev })
    {
    }

    public ConnectedKeyboard(IEnumerable<HidDevice> devices)
    {
        _candidates = devices
            .OrderByDescending(ScoreDevice)
            .ThenBy(d => d.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Device = _candidates.First();
    }

    public bool Open()
        => Open(Device);

    public bool Open(HidDevice device)
    {
        try
        {
            CloseStream();
            Device = device;
            if (!device.TryOpen(out var stream))
                return false;
            stream.ReadTimeout = 500;
            stream.WriteTimeout = 500;
            Stream = stream;

            int reportLen = device.GetMaxInputReportLength();
            if (reportLen <= 0) reportLen = 32;

            _cts = new CancellationTokenSource();
            _readerTask = Task.Run(() => ReadLoop(stream, reportLen, _cts.Token));
            return true;
        }
        catch
        {
            CloseStream();
            return false;
        }
    }

    private void ReadLoop(HidStream stream, int reportLen, CancellationToken ct)
    {
        var buffer = new byte[reportLen];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int n = stream.Read(buffer, 0, buffer.Length);
                if (n <= 0) continue;
                var slice = new byte[n];
                Array.Copy(buffer, slice, n);
                // Windows prepends the report id (always 0 for VIA rawhid).
                // No command id is ever 0x00, so a zero prefix can be stripped unconditionally
                // (covers VIA replies and the Vial 0xFE channel alike).
                if (slice.Length > 1 && slice[0] == 0)
                    slice = slice[1..];
                DataReceived?.Invoke(slice);
            }
            catch (OperationCanceledException) { break; }
            catch (TimeoutException) { }
            catch (Exception)
            {
                try { Task.Delay(50, ct).Wait(ct); } catch { break; }
            }
        }
    }

    public bool Send(byte[] data)
    {
        if (Stream == null) return false;

        // WebHID-equivalent write: report id 0 + payload, sized to the output report.
        try
        {
            var len = Device.GetMaxOutputReportLength();
            if (len > 0 && len != data.Length)
            {
                var padded = new byte[len];
                padded[0] = 0;
                Array.Copy(data, 0, padded, 1, Math.Min(data.Length, len - 1));
                Stream.Write(padded);
                return true;
            }
        }
        catch { }

        // Fallback: raw payload without a report-id byte.
        try
        {
            var raw = new byte[data.Length];
            Array.Copy(data, raw, data.Length);
            Stream.Write(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void CloseStream()
    {
        try { _cts?.Cancel(); } catch { }
        try { _readerTask?.Wait(250); } catch { }
        try { Stream?.Dispose(); } catch { }
        Stream = null;
        _cts = null;
        _readerTask = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }

    public void Close()
    {
        CloseStream();
    }

    internal static int ScoreDevice(HidDevice device)
    {
        var score = 0;
        try
        {
            var inLen = device.GetMaxInputReportLength();
            var outLen = device.GetMaxOutputReportLength();

            if (inLen is 32 or 33) score += 500;
            else if (inLen is >= 16 and <= 65) score += 180;
            else if (inLen <= 8) score -= 350;

            if (outLen is 32 or 33) score += 600;
            else if (outLen is >= 16 and <= 65) score += 220;
            else if (outLen == 0) score -= 150;
        }
        catch { score -= 500; }

        // The VIA rawhid collection (usage page 0xFF60, usage 0x61) is the only
        // interface that actually speaks the protocol - always try it first.
        if (HasViaRawHidUsage(device)) score += 5000;

        var name = Safe(() => device.GetFriendlyName()).ToLowerInvariant();
        if (name.Contains("via") || name.Contains("raw")) score += 300;
        if (name.Contains("keyboard")) score += 40;

        return score;
    }

    /// <summary>Detects the vendor-defined rawhid collection used by VIA/QMK/Vial.</summary>
    internal static bool HasViaRawHidUsage(HidDevice device)
    {
        try
        {
            var descriptor = device.GetReportDescriptor();
            foreach (var item in descriptor.DeviceItems)
            {
                // Indexes.GetAllValues() yields full 32-bit usages (page << 16 | id).
                foreach (var usage in item.Usages.GetAllValues())
                {
                    if ((usage & 0xFFFF0000) == 0xFF600000 && (usage & 0xFFFF) == 0x61)
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static string Safe(Func<string> read)
    {
        try { return read() ?? ""; } catch { return ""; }
    }
}

/// <summary>Enumerates HID devices on the VIA raw-HID usage (page 0xFF60, usage 0x61).</summary>
public static class HidDeviceManager
{
    public static IReadOnlyList<ConnectedKeyboard> ListViaDevices(ushort? vendorId = null, ushort? productId = null)
    {
        var result = new List<HidDevice>();
        try
        {
            // VIA raw HID is on vendor usage page 0xFF60 (usage 0x61), 32-byte reports.
            // We enumerate all HID devices and keep controllers (non-boot) with mid-size reports,
            // which is characteristic of VIA/QMK raw-HID keyboards. Exact match happens on connect
            // via the protocol-version handshake.
            foreach (var device in DeviceList.Local.GetHidDevices())
            {
                try
                {
                    if (vendorId.HasValue && productId.HasValue &&
                        (device.VendorID != vendorId.Value || device.ProductID != productId.Value))
                        continue;

                    int inLen = device.GetMaxInputReportLength();
                    int outLen = device.GetMaxOutputReportLength();
                    // VIA raw HID uses 32-byte reports both ways, but many boards advertise
                    // no output report (input-only). Accept those too.
                    var looksLikeVia = inLen >= 16 && inLen <= 65 && outLen >= 0 && outLen <= 65;
                    var exactConfigMatch = vendorId.HasValue && productId.HasValue;
                    if (looksLikeVia || exactConfigMatch)
                        result.Add(device);
                }
                catch { }
            }
        }
        catch { }
        return result
            // Group ALL interfaces of one physical keyboard together (by VID:PID),
            // regardless of per-interface naming, so the handshake can try every one.
            .GroupBy(d => $"{d.VendorID:X4}:{d.ProductID:X4}", StringComparer.OrdinalIgnoreCase)
            .Select(g => new ConnectedKeyboard(g))
            .OrderByDescending(k => vendorId.HasValue && productId.HasValue &&
                k.VendorId == vendorId.Value && k.ProductId == productId.Value)
            .ThenBy(k => k.Name)
            .ToList();
    }

    private static string Safe(Func<string> read)
    {
        try { return read() ?? ""; } catch { return ""; }
    }
}
