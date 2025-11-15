using System.Globalization;
using AirPlayCasting.Contracts;
using Zeroconf;

namespace AirPlayCasting;

public class AirPlayDeviceLocator
{
    // Bit 31 (0x80000000) indicates AirPlay authentication required
    private const long AuthRequired = 0x80000000;

    // Bit 25 (0x02000000) indicates password required
    private const long PwRequired = 0x02000000;
    private CancellationTokenSource? _cts;
    public List<AirPlayDevice> DiscoveredDevices { get; private set; } = new();

    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        await Scan();
    }

    public void StopAsync()
    {
        _cts?.Cancel();
    }

    public event EventHandler<AirPlayDeviceArg>? OnDeviceDiscovered;

    public EventHandler? OnCollectionChanged;

    private async Task Scan()
    {
        var domains = await ZeroconfResolver.BrowseDomainsAsync();
        var protocolsOfInterest = domains.Where(x => x.Key.Contains("airplay"))
            .Select(g => g.Key)
            .ToList();

        var scannedDevices = new List<AirPlayDevice>();
        scannedDevices.Clear();

        var devices = await ZeroconfResolver.ResolveAsync(protocolsOfInterest);
        foreach (var device in devices)
        {
            if (device.Services.Count == 0)
            {
                continue;
            }

            var service = device.Services.Values.FirstOrDefault();
            if (service == null)
            {
                continue;
            }

            var isPinRequired = IsDevicePinProtected(service.Properties);

            var airplayDevice = new AirPlayDevice
            {
                Port = service.Port,
                DisplayName = device.DisplayName,
                Ip = device.IPAddress,
                PinRequired = isPinRequired
            };

            scannedDevices.Add(airplayDevice);
            if (DiscoveredDevices.Contains(airplayDevice))
            {
                continue;
            }

            DiscoveredDevices.Add(airplayDevice);
            OnDeviceDiscovered?.Invoke(this, new AirPlayDeviceArg(airplayDevice));
        }

        if (TryConsolidateDevices(scannedDevices))
        {
            OnCollectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool TryConsolidateDevices(List<AirPlayDevice> scannedDevices)
    {
        var collectionModified = false;
        var i = 0;
        while (i < scannedDevices.Count)
        {
            var device = scannedDevices[i];
            if (!scannedDevices.Contains(device))
            {
                DiscoveredDevices.RemoveAt(i);
                collectionModified = true;
            }
            else
            {
                i++;
            }
        }

        return collectionModified;
    }

    private static bool IsDevicePinProtected(IReadOnlyCollection<IReadOnlyDictionary<string, string>> properties)
    {
        // Method 1: Check for "pw" property
        if (TryGetPropertyValue(properties, "pw", out var pwValue))
        {
            if (pwValue == "1")
            {
                return true;
            }
        }

        // Method 2: Check for "PIN" property
        if (TryGetPropertyValue(properties, "PIN", out _))
        {
            return true;
        }

        // Method 3: Check for pairing requirement in device features
        if (!TryGetPropertyValue(properties, "features", out var featuresHexString))
        {
            return false;
        }

        var featuresHex = featuresHexString.Replace("0x", string.Empty)
            .Split(',');

        foreach (var featureHex in featuresHex)
        {
            if (!long.TryParse(featureHex, NumberStyles.HexNumber, null, out var features))
            {
                continue;
            }

            if ((features & AuthRequired) != 0 || (features & PwRequired) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetPropertyValue(IReadOnlyCollection<IReadOnlyDictionary<string, string>> properties,
        string key,
        out string value)
    {
        value = string.Empty;
        foreach (var property in properties)
        {
            if (property.TryGetValue(key, out var propertyValue))
            {
                value = propertyValue;
                return true;
            }
        }

        return false;
    }
}