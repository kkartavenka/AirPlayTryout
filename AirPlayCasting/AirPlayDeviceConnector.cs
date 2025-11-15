using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AirPlayCasting.Contracts;

namespace AirPlayCasting;

public class AirPlayDeviceConnector
{
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    
    public AirPlayDeviceConnector()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
            
        _httpClient = new HttpClient(handler)
        {
            Timeout = _timeout
        };
            
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AirPlayScanner/1.0");
        _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
    }
    
    public async Task<bool> ConnectAsync(AirPlayDevice device)
    {
        Console.WriteLine($"Connecting to {device.DisplayName} at {device.Ip}:{device.Port}");

        try
        {
            if (!await IsDeviceReachable(device))
            {
                Console.WriteLine($"Device {device.DisplayName} is not reachable.");
                return false;
            }

            if (device.PinRequired)
            {
                Console.Write($"PIN required for {device.DisplayName}. Enter PIN: ");
                var pin = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(pin))
                {
                    Console.WriteLine("PIN cannot be empty.");
                    return false;
                }

                // Authenticate with PIN
                if (!await AuthenticateWithPinAsync(device, pin))
                {
                    Console.WriteLine("Authentication failed. Incorrect PIN or connection issue.");
                    return false;
                }

                Console.WriteLine("Authentication successful!");
            }

            // Get device info to verify connection
            var deviceInfo = await GetDeviceInfoAsync(device);
            if (deviceInfo != null)
            {
                Console.WriteLine("Connection successful!");
                Console.WriteLine("Device Info:");
                foreach (var info in deviceInfo)
                {
                    Console.WriteLine($"  {info.Key}: {info.Value}");
                }

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection error: {ex.Message}");
            return false;
        }
    }
    
    private async Task<bool> IsDeviceReachable(AirPlayDevice device)
    {
        try
        {
            // Try to ping the device with a simple request
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://{device.Ip}:{device.Port}/server-info");
            var response = await _httpClient.SendAsync(request);
                
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    
    private async Task<bool> AuthenticateWithPinAsync(AirPlayDevice device, string pin)
    {
        try
        {
            // Step 1: Get authentication challenge from device
            var challenge = await GetAuthenticationChallengeAsync(device);
            if (challenge == null)
            {
                Console.WriteLine("Failed to get authentication challenge");
                return false;
            }
                
            // Step 2: Generate response with PIN
            var authResponse = GenerateAuthResponse(challenge, pin);
                
            // Step 3: Send the authentication response
            return await SendAuthenticationResponseAsync(device, authResponse);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Authentication error: {ex.Message}");
            return false;
        }
    }
    
    private async Task<string?> GetAuthenticationChallengeAsync(AirPlayDevice device)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://{device.Ip}:{device.Port}/pair-pin-start");
            
        // The body can be empty for the initial challenge request
        request.Content = new StringContent("", Encoding.UTF8, "application/x-apple-binary-plist");
            
        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
            
        // Parse the response to extract the challenge
        // Note: In a real implementation, proper binary plist parsing would be needed
        // This is simplified for demonstration
        var responseContent = await response.Content.ReadAsByteArrayAsync();
            
        // Extract challenge from the binary plist (simplified)
        // In a real implementation, you would use a proper plist parser
        string challenge = Convert.ToBase64String(responseContent);
        return challenge;
    }
    
    private string GenerateAuthResponse(string challenge, string pin)
    {
        // In a real implementation, you would implement the proper cryptographic operations
        // This is a simplified placeholder
        using var md5 = MD5.Create();

        // Combine the challenge and PIN
        byte[] inputBytes = Encoding.UTF8.GetBytes(challenge + pin);
        byte[] hashBytes = md5.ComputeHash(inputBytes);
                
        // Convert the hash to a string
        return Convert.ToBase64String(hashBytes);
    }
    
    private async Task<bool> SendAuthenticationResponseAsync(AirPlayDevice device, string authResponse)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://{device.Ip}:{device.Port}/pair-pin-validate");
            
        // The body contains the authentication response
        // In a real implementation, you would construct a proper binary plist
        request.Content = new StringContent(authResponse, Encoding.UTF8, "application/x-apple-binary-plist");
            
        var response = await _httpClient.SendAsync(request);
        return response.IsSuccessStatusCode;
    }
    
    private async Task<Dictionary<string, string>?> GetDeviceInfoAsync(AirPlayDevice device)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://{device.Ip}:{device.Port}/server-info");
            var response = await _httpClient.SendAsync(request);
                
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
                
            var content = await response.Content.ReadAsStringAsync();
                
            // Parse the XML response (simplified)
            var info = new Dictionary<string, string>();
                
            try
            {
                var doc = XDocument.Parse(content);
                var dict = doc.Descendants("dict").FirstOrDefault();
                if (dict != null)
                {
                    // Extract key-value pairs
                    var keys = dict.Elements("key").Select(k => k.Value).ToList();
                    var values = dict.Elements().Where(e => e.Name != "key").Select(v => v.Value).ToList();
                        
                    for (int i = 0; i < Math.Min(keys.Count, values.Count); i++)
                    {
                        info[keys[i]] = values[i];
                    }
                }
            }
            catch
            {
                // If XML parsing fails, just return basic info
                info["Status"] = "Connected";
            }
                
            return info;
        }
        catch
        {
            return null;
        }
    }
}