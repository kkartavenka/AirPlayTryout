using System.Globalization;
using Zeroconf;

namespace AirPlayVideoStreaming;

/// <summary>
/// Discovers AirPlay devices on the network and extracts their capabilities
/// </summary>
public class AirPlayDeviceDiscoverer
{
    /// <summary>
    /// Discover a specific device by IP address
    /// </summary>
    public static async Task<AirPlayDeviceInfo?> DiscoverDeviceAsync(string targetIp, int targetPort = 7000)
    {
        try
        {
            Console.WriteLine($"Discovering AirPlay device at {targetIp}:{targetPort}...");
            
            // Browse for AirPlay services
            var domains = await ZeroconfResolver.BrowseDomainsAsync();
            var airplayDomains = domains.Where(x => x.Key.Contains("airplay", StringComparison.OrdinalIgnoreCase))
                .Select(g => g.Key)
                .ToList();
            
            if (airplayDomains.Count == 0)
            {
                Console.WriteLine("No AirPlay services found on network");
                return null;
            }
            
            Console.WriteLine($"Found {airplayDomains.Count} AirPlay domain(s)");
            
            // Resolve all AirPlay devices
            var devices = await ZeroconfResolver.ResolveAsync(airplayDomains);
            
            foreach (var device in devices)
            {
                if (device.Services.Count == 0)
                {
                    continue;
                }
                
                // Check if this device matches our target IP
                if (!device.IPAddress.Equals(targetIp, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                var servicePair = device.Services.FirstOrDefault();
                if (servicePair.Equals(default(KeyValuePair<string, IService>)) || servicePair.Value == null)
                {
                    continue;
                }
                
                var service = servicePair.Value;
                
                // Check if port matches (or use default)
                if (service.Port != targetPort && targetPort != 7000)
                {
                    continue;
                }
                
                // Found the device! Extract information
                var deviceInfo = ExtractDeviceInfo(device, servicePair);
                return deviceInfo;
            }
            
            Console.WriteLine($"Device {targetIp}:{targetPort} not found via mDNS discovery");
            Console.WriteLine("Note: Device may not advertise via mDNS, or discovery may take time");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during device discovery: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Extract device information from mDNS service record
    /// </summary>
    private static AirPlayDeviceInfo ExtractDeviceInfo(IZeroconfHost device, KeyValuePair<string, IService> servicePair)
    {
        var service = servicePair.Value;
        var deviceInfo = new AirPlayDeviceInfo
        {
            DisplayName = device.DisplayName,
            IpAddress = device.IPAddress,
            Port = service.Port
        };
        
        // Extract properties from service
        if (service.Properties != null && service.Properties.Count > 0)
        {
            foreach (var propertyDict in service.Properties)
            {
                if (propertyDict == null) continue;
                
                foreach (var kvp in propertyDict)
                {
                    if (kvp.Key == null || kvp.Value == null) continue;
                    
                    var key = kvp.Key.ToLowerInvariant();
                    var value = kvp.Value;
                    
                    switch (key)
                    {
                        case "deviceid":
                            deviceInfo.DeviceId = value;
                            break;
                        case "model":
                            deviceInfo.Model = value;
                            break;
                        case "srcvers":
                            deviceInfo.SourceVersion = value;
                            break;
                        case "features":
                            deviceInfo.FeaturesHex = value;
                            break;
                        case "flags":
                            deviceInfo.FlagsHex = value;
                            break;
                        case "pk":
                            deviceInfo.PublicKey = value;
                            break;
                        case "pi":
                            deviceInfo.PairingId = value;
                            break;
                    }
                }
            }
        }
        
        // Parse features and flags
        deviceInfo.ParseFeatures();
        deviceInfo.ParseFlags();
        
        return deviceInfo;
    }
    
    /// <summary>
    /// Discover all AirPlay devices on the network
    /// </summary>
    public static async Task<List<AirPlayDeviceInfo>> DiscoverAllDevicesAsync()
    {
        var devices = new List<AirPlayDeviceInfo>();
        
        try
        {
            Console.WriteLine("Discovering all AirPlay devices on network...");
            
            var domains = await ZeroconfResolver.BrowseDomainsAsync();
            var airplayDomains = domains.Where(x => x.Key.Contains("airplay", StringComparison.OrdinalIgnoreCase))
                .Select(g => g.Key)
                .ToList();
            
            if (airplayDomains.Count == 0)
            {
                Console.WriteLine("No AirPlay services found on network");
                return devices;
            }
            
            var resolvedDevices = await ZeroconfResolver.ResolveAsync(airplayDomains);
            
            foreach (var device in resolvedDevices)
            {
                if (device.Services.Count == 0)
                {
                    continue;
                }
                
                var servicePair = device.Services.FirstOrDefault();
                if (servicePair.Equals(default(KeyValuePair<string, IService>)) || servicePair.Value == null)
                {
                    continue;
                }
                
                var deviceInfo = ExtractDeviceInfo(device, servicePair);
                devices.Add(deviceInfo);
            }
            
            Console.WriteLine($"Found {devices.Count} AirPlay device(s)");
            return devices;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during device discovery: {ex.Message}");
            return devices;
        }
    }
}

