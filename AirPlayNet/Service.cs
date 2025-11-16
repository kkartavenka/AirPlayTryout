using System.Net;
using System.Net.NetworkInformation;
using Zeroconf;

namespace AirPlayNet;

public class Service
{
    public string Name { get; set; }
    public string Hostname { get; set; }
    public int Port { get; set; }
    
    public Service(string hostname)
        : this(hostname, AirPlay.Port)
    {
    }
    
    public Service(string hostname, int port)
        : this(hostname, port, hostname)
    {
    }
    
    public Service(string hostname, int port, string name)
    {
        this.Hostname = hostname;
        this.Port = port;
        this.Name = name;
    }
    
    public override bool Equals(object? obj)
    {
        if (obj is Service other)
        {
            return Hostname == other.Hostname && Port == other.Port && Name == other.Name;
        }
        return false;
    }
    
    public override int GetHashCode()
    {
        return HashCode.Combine(Hostname, Port, Name);
    }
}

public static class ServiceDiscovery
{
    public static async Task<List<Service>> SearchAsync()
    {
        return await SearchAsync(1000);
    }
    
    public static List<IPAddress> ListNetworkAddresses()
    {
        List<IPAddress> validAddresses = new List<IPAddress>();
        
        try
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            
            foreach (NetworkInterface ni in interfaces)
            {
                IPInterfaceProperties properties = ni.GetIPProperties();
                foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                {
                    if (IPAddress.IsLoopback(address.Address))
                    {
                        continue;
                    }
                    validAddresses.Add(address.Address);
                }
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error listing network addresses: {e.Message}");
        }
        
        return validAddresses;
    }
    
    public static async Task<List<Service>> SearchAsync(int timeout)
    {
        List<Service> availableServices = new List<Service>();
        
        try
        {
            var domains = await ZeroconfResolver.BrowseDomainsAsync();
            var protocolsOfInterest = domains.Where(x => x.Key.Contains("airplay", StringComparison.OrdinalIgnoreCase))
                .Select(g => g.Key)
                .ToList();
            
            if (protocolsOfInterest.Count == 0)
            {
                return availableServices;
            }
            
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
                
                // Only use IPv4 addresses
                if (IPAddress.TryParse(device.IPAddress, out IPAddress? ip) && 
                    ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    Service airplayService = new Service(device.IPAddress, service.Port, device.DisplayName);
                    if (!availableServices.Contains(airplayService))
                    {
                        availableServices.Add(airplayService);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Service discovery error: {e.Message}");
        }
        
        return availableServices;
    }
}

