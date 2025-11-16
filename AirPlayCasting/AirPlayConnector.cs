using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AirPlayCasting.Contracts;
using System.Collections.Generic;

namespace AirPlayCasting;

public class AirPlayConnector
{
    private readonly HttpClient _httpClient;
    
    public AirPlayConnector()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
            
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
            
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AirPlayScanner/1.0");
        _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
    }
    
    public async Task<bool> ConnectAsync(AirPlayDevice device)
    {
        if (!await IsDeviceReachable(device))
        {
            Console.WriteLine($"Device {device.DisplayName} is not reachable.");
            return false;
        }

        if (device.PinRequired && !await PairWithPinAsync(device))
        {
            Console.WriteLine("Authentication failed. PIN is required or connection issue!");
        }
        
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
    
    public async Task<bool> SubmitPin(string deviceIp, string pin, int port = 7000)
    {
        try
        {
            var url = $"http://{deviceIp}:{port}/pair-pin-verify";
            
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            // Add required headers
            request.Headers.Add("X-Apple-HKP", "3");
            
            // The PIN is sent as plain text in the body
            request.Content = new StringContent(pin, Encoding.UTF8, "application/octet-stream");
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("PIN verified successfully");
                return true;
            }
            else
            {
                Console.WriteLine($"Failed to verify PIN: {response.StatusCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error verifying PIN: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Complete pairing flow: request PIN and then verify it
    /// </summary>
    public async Task<bool> PairWithDevice(string deviceIp, int port = 7000)
    {
        Console.WriteLine($"Starting pairing with {deviceIp}:{port}");
        
        // Step 1: Request PIN display
        bool pinRequested = await RequestPinDisplay(deviceIp, port);
        if (!pinRequested)
        {
            Console.WriteLine("Failed to request PIN display");
            return false;
        }

        // Step 2: Wait for user to see and enter PIN
        Console.WriteLine("Please check the device screen for the PIN code.");
        Console.Write("Enter PIN: ");
        string pin = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(pin))
        {
            Console.WriteLine("No PIN entered");
            return false;
        }

        // Step 3: Submit PIN
        bool pinVerified = await SubmitPin(deviceIp, pin, port);
        if (!pinVerified)
        {
            Console.WriteLine("PIN verification failed");
            return false;
        }

        Console.WriteLine("Pairing completed successfully!");
        return true;
    }
    
    public async Task<bool> RequestPinDisplay(string deviceIp, int port = 7000)
    {
        try
        {
            var url = $"http://{deviceIp}:{port}/pair-pin-start";
            
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            // Add required headers
            request.Headers.Add("X-Apple-HKP", "3");
            request.Content = new StringContent("", Encoding.UTF8, "application/octet-stream");
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("PIN display requested successfully");
                return true;
            }
            else
            {
                Console.WriteLine($"Failed to request PIN: {response.StatusCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error requesting PIN: {ex.Message}");
            return false;
        }
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

    private async Task<bool> PairWithPinAsync(AirPlayDevice device)
    {
        // Step 1: Get authentication challenge from device
        var challenge = await GetAuthenticationChallengeAsync(device);
        if (challenge == null)
        {
            Console.WriteLine("Failed to get authentication challenge");
            return false;
        }
        
        // Step 2: Ask for end user input for PIN
        Console.WriteLine($"Enter PIN for {device.DisplayName}:");
        var pin = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(pin))
        {
            Console.WriteLine("PIN cannot be empty.");
            return false;
        }
        
        var authResponse = GenerateAuthResponse(challenge, pin);
        
        // Step 3: Send the authentication response
        return await SendAuthenticationResponseAsync(device, authResponse);
    }
    
    private async Task<bool> SendAuthenticationResponseAsync(AirPlayDevice device, byte[] authResponseBytes)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://{device.Ip}:{device.Port}/pair-pin-validate");
            
            // The body must be a proper binary plist format
            request.Content = new ByteArrayContent(authResponseBytes);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-apple-binary-plist");
            
            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Authentication failed with status {response.StatusCode}: {errorContent}");
            }
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending authentication response: {ex.Message}");
            return false;
        }
    }
    
    private byte[] GenerateAuthResponse(byte[] challengeBytes, string pin)
    {
        // AirPlay PIN authentication uses SHA512 hash
        // The response should be: SHA512(challenge + PIN)
        using var sha512 = SHA512.Create();

        // Convert PIN to bytes
        byte[] pinBytes = Encoding.UTF8.GetBytes(pin);
        
        // Combine challenge and PIN
        byte[] combinedBytes = new byte[challengeBytes.Length + pinBytes.Length];
        Buffer.BlockCopy(challengeBytes, 0, combinedBytes, 0, challengeBytes.Length);
        Buffer.BlockCopy(pinBytes, 0, combinedBytes, challengeBytes.Length, pinBytes.Length);
        
        // Compute SHA512 hash
        byte[] hashBytes = sha512.ComputeHash(combinedBytes);
        
        // Create a binary plist dictionary with the response
        // Format: { "response": <hash_bytes> }
        return CreateBinaryPlistWithResponse(hashBytes);
    }
    
    private byte[] CreateBinaryPlistWithResponse(byte[] responseHash)
    {
        // Binary plist format (simplified structure)
        // This creates a minimal binary plist with a dictionary containing the response
        // Note: This is a simplified implementation. A full binary plist library would be better.
        // For a proper implementation, you'd need a full binary plist writer library.
        // The actual format should be: { "response": <hash_bytes> }
        
        // Simplified approach: create a basic binary plist structure
        // In a full implementation, you would properly construct:
        // - Object table with dictionary containing "response" key and data value
        // - Offset table pointing to objects
        // - Trailer with metadata
        
        // For now, return the hash directly wrapped in a minimal plist structure
        // This may need adjustment based on actual device requirements
        var header = Encoding.ASCII.GetBytes("bplist00");
        var result = new byte[header.Length + responseHash.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(responseHash, 0, result, header.Length, responseHash.Length);
        
        return result;
    }
    
    private async Task<byte[]?> GetAuthenticationChallengeAsync(AirPlayDevice device)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://{device.Ip}:{device.Port}/pair-pin-start");
            
            // The body should be an empty binary plist or minimal binary plist
            // Empty binary plist: "bplist00" + minimal structure
            var emptyPlist = CreateEmptyBinaryPlist();
            request.Content = new ByteArrayContent(emptyPlist);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-apple-binary-plist");
            
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to get challenge: {response.StatusCode}");
                return null;
            }
            
            // The response is a binary plist containing the challenge
            // Extract the challenge bytes from the binary plist response
            var responseContent = await response.Content.ReadAsByteArrayAsync();
            
            // In a full implementation, you would parse the binary plist to extract
            // the actual challenge value. For now, we'll use the response content
            // and try to extract the challenge from it.
            // The challenge is typically in a "challenge" or "salt" field in the plist
            
            // Skip the "bplist00" header if present
            if (responseContent.Length > 8 && Encoding.ASCII.GetString(responseContent, 0, 8) == "bplist00")
            {
                // Return the full response for now - proper parsing would extract just the challenge
                return responseContent;
            }
            
            return responseContent;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting authentication challenge: {ex.Message}");
            return null;
        }
    }
    
    private byte[] CreateEmptyBinaryPlist()
    {
        // Create a minimal empty binary plist
        // Binary plist format: "bplist00" + object table + offset table + trailer
        // Minimal structure: header + empty dict + offsets + trailer
        var plist = new List<byte>();
        
        // Header
        plist.AddRange(Encoding.ASCII.GetBytes("bplist00"));
        
        // Minimal empty dictionary structure
        // Object 0: dictionary (0x0D = dict, 0x00 = empty)
        plist.Add(0x0D);
        plist.Add(0x00);
        
        // Offset table (1 byte offset, pointing to object 0)
        plist.Add(0x00);
        
        // Trailer (8 bytes: offset size, ref size, object count, root object, offset table offset)
        plist.Add(0x01); // offset size
        plist.Add(0x01); // ref size  
        plist.Add(0x00); plist.Add(0x00); plist.Add(0x00); plist.Add(0x00); // object count (0)
        plist.Add(0x00); plist.Add(0x00); plist.Add(0x00); plist.Add(0x00); // root object (0)
        plist.Add(0x00); plist.Add(0x00); plist.Add(0x00); plist.Add(0x00); plist.Add(0x00); plist.Add(0x00); plist.Add(0x00); plist.Add(0x0A); // offset table offset
        
        return plist.ToArray();
    }
    
    private async Task<bool> IsDeviceReachable(AirPlayDevice device)
    {
        try
        {
            // Try to ping the device with a simple request
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://{device.Ip}:{device.Port}/info");
            var response = await _httpClient.SendAsync(request);
                
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}