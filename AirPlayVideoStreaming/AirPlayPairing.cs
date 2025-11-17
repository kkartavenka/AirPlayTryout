using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Collections;

namespace AirPlayVideoStreaming;

/// <summary>
/// Handles AirPlay device pairing/authentication
/// </summary>
public class AirPlayPairing
{
    private readonly string _deviceIp;
    private readonly int _port;
    private readonly HttpClient _httpClient;
    
    public AirPlayPairing(string deviceIp, int port = 7000)
    {
        _deviceIp = deviceIp;
        _port = port;
        
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AirPlay/375.3");
    }
    
    /// <summary>
    /// Check if pairing is required by attempting to pair-setup
    /// </summary>
    public async Task<bool> IsPairingRequiredAsync()
    {
        try
        {
            var url = $"http://{_deviceIp}:{_port}/pair-setup";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            request.Headers.Add("X-Apple-HKP", "3");
            request.Content = new ByteArrayContent(Array.Empty<byte>());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            
            var response = await _httpClient.SendAsync(request);
            
            // If we get 200 OK, pairing might not be required
            // If we get other status codes, pairing might be required
            return !response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized;
        }
        catch
        {
            return true; // Assume pairing required if we can't check
        }
    }
    
    /// <summary>
    /// Request PIN display on device
    /// Try both pair-pin-start and pair-setup endpoints
    /// </summary>
    public async Task<bool> RequestPinDisplayAsync()
    {
        // Try pair-pin-start first (simpler PIN-based pairing)
        try
        {
            var url = $"http://{_deviceIp}:{_port}/pair-pin-start";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            request.Headers.Add("X-Apple-HKP", "3");
            request.Content = new ByteArrayContent(Array.Empty<byte>());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✓ PIN display requested successfully (pair-pin-start)");
                Console.WriteLine("Please check your device screen for the PIN code.");
                return true;
            }
            else
            {
                Console.WriteLine($"pair-pin-start returned: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"pair-pin-start error: {ex.Message}");
        }
        
        // Try pair-setup (HomeKit pairing protocol)
        try
        {
            var url = $"http://{_deviceIp}:{_port}/pair-setup";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            request.Headers.Add("X-Apple-HKP", "3");
            // HomeKit pairing M1 request (simplified - full implementation needs proper HomeKit protocol)
            var m1Request = new Dictionary<string, object>
            {
                ["method"] = 0, // Pair Setup Method
                ["state"] = 1    // M1 state
            };
            
            byte[] plistBytes = BinaryPlistHelper.Write(m1Request);
            request.Content = new ByteArrayContent(plistBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-apple-binary-plist");
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✓ Pairing initiated (pair-setup)");
                Console.WriteLine("Please check your device screen for the PIN code.");
                return true;
            }
            else
            {
                Console.WriteLine($"pair-setup returned: {response.StatusCode}");
                var content = await response.Content.ReadAsByteArrayAsync();
                if (content.Length > 0)
                {
                    Console.WriteLine($"Response length: {content.Length} bytes");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"pair-setup error: {ex.Message}");
        }
        
        Console.WriteLine("⚠ Could not request PIN display - device may use different pairing method");
        return false;
    }
    
    /// <summary>
    /// Verify PIN with device
    /// Try multiple approaches as different devices use different formats
    /// </summary>
    public async Task<bool> VerifyPinAsync(string pin)
    {
        // Try approach 1: Plain text PIN to pair-pin-verify
        Console.WriteLine("  Trying pair-pin-verify with plain text PIN...");
        try
        {
            var url = $"http://{_deviceIp}:{_port}/pair-pin-verify";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            request.Headers.Add("X-Apple-HKP", "3");
            request.Content = new StringContent(pin, Encoding.UTF8, "application/octet-stream");
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✓ PIN verified successfully!");
                return true;
            }
            else
            {
                Console.WriteLine($"  pair-pin-verify failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  pair-pin-verify error: {ex.Message}");
        }
        
        // Try approach 2: PIN as bytes to pair-pin-verify
        Console.WriteLine("  Trying pair-pin-verify with PIN as bytes...");
        try
        {
            var url = $"http://{_deviceIp}:{_port}/pair-pin-verify";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            request.Headers.Add("X-Apple-HKP", "3");
            var pinBytes = Encoding.UTF8.GetBytes(pin);
            request.Content = new ByteArrayContent(pinBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✓ PIN verified successfully!");
                return true;
            }
            else
            {
                Console.WriteLine($"  Bytes format failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Bytes format error: {ex.Message}");
        }
        
        // Try approach 3: pair-pin-validate with binary plist (more complex authentication)
        Console.WriteLine("  Trying pair-pin-validate with binary plist...");
        try
        {
            // First, try to get authentication challenge
            var challengeUrl = $"http://{_deviceIp}:{_port}/pair-pin-start";
            var challengeRequest = new HttpRequestMessage(HttpMethod.Post, challengeUrl);
            challengeRequest.Headers.Add("X-Apple-HKP", "3");
            challengeRequest.Content = new ByteArrayContent(Array.Empty<byte>());
            challengeRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            
            var challengeResponse = await _httpClient.SendAsync(challengeRequest);
            byte[]? challengeData = null;
            
            if (challengeResponse.IsSuccessStatusCode)
            {
                challengeData = await challengeResponse.Content.ReadAsByteArrayAsync();
                Console.WriteLine($"  Received challenge data: {challengeData.Length} bytes");
            }
            
            // Try pair-pin-validate with PIN in binary plist
            var validateUrl = $"http://{_deviceIp}:{_port}/pair-pin-validate";
            var validateRequest = new HttpRequestMessage(HttpMethod.Post, validateUrl);
            validateRequest.Headers.Add("X-Apple-HKP", "3");
            
            // Create binary plist with PIN
            var pinDict = new Dictionary<string, object>
            {
                ["pin"] = pin
            };
            
            // If we got challenge data, include it
            if (challengeData != null && challengeData.Length > 0)
            {
                // Generate SHA512 hash of challenge + PIN (AirPlay authentication)
                using var sha512 = SHA512.Create();
                var pinBytes = Encoding.UTF8.GetBytes(pin);
                var combined = new byte[challengeData.Length + pinBytes.Length];
                Buffer.BlockCopy(challengeData, 0, combined, 0, challengeData.Length);
                Buffer.BlockCopy(pinBytes, 0, combined, challengeData.Length, pinBytes.Length);
                var hash = sha512.ComputeHash(combined);
                
                pinDict["response"] = Convert.ToBase64String(hash);
            }
            
            byte[] plistBytes = BinaryPlistHelper.Write(pinDict);
            validateRequest.Content = new ByteArrayContent(plistBytes);
            validateRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-apple-binary-plist");
            
            var validateResponse = await _httpClient.SendAsync(validateRequest);
            
            if (validateResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("✓ PIN verified successfully!");
                return true;
            }
            else
            {
                Console.WriteLine($"  pair-pin-validate failed: {validateResponse.StatusCode}");
                var content = await validateResponse.Content.ReadAsByteArrayAsync();
                if (content.Length > 0)
                {
                    Console.WriteLine($"  Response length: {content.Length} bytes");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  pair-pin-validate error: {ex.Message}");
        }
        
        // Try approach 4: Simple PIN in binary plist format
        Console.WriteLine("  Trying pair-pin-validate with simple PIN plist...");
        try
        {
            var url = $"http://{_deviceIp}:{_port}/pair-pin-validate";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            request.Headers.Add("X-Apple-HKP", "3");
            
            var pinDict = new Dictionary<string, object>
            {
                ["pin"] = pin
            };
            
            byte[] plistBytes = BinaryPlistHelper.Write(pinDict);
            request.Content = new ByteArrayContent(plistBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-apple-binary-plist");
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✓ PIN verified successfully!");
                return true;
            }
            else
            {
                Console.WriteLine($"  Simple plist format failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Simple plist format error: {ex.Message}");
        }
        
        Console.WriteLine("✗ All PIN verification methods failed");
        Console.WriteLine("Note: This device may require HomeKit pairing protocol (pair-setup/pair-verify)");
        Console.WriteLine("      which requires more complex cryptographic handshake.");
        return false;
    }
    
    /// <summary>
    /// Complete pairing flow: request PIN and verify it
    /// </summary>
    public async Task<bool> PairWithDeviceAsync()
    {
        Console.WriteLine("\n=== Starting AirPlay Pairing ===");
        
        // Step 1: Request PIN display
        Console.WriteLine("\n1. Requesting PIN display on device...");
        bool pinRequested = await RequestPinDisplayAsync();
        if (!pinRequested)
        {
            Console.WriteLine("Failed to request PIN display");
            return false;
        }
        
        // Step 2: Get PIN from user
        Console.Write("\nEnter the PIN shown on your device: ");
        string? pin = Console.ReadLine();
        
        if (string.IsNullOrWhiteSpace(pin))
        {
            Console.WriteLine("No PIN entered");
            return false;
        }
        
        // Step 3: Verify PIN
        Console.WriteLine("\n2. Verifying PIN...");
        bool pinVerified = await VerifyPinAsync(pin);
        
        if (pinVerified)
        {
            Console.WriteLine("\n✓ Pairing completed successfully!");
            return true;
        }
        else
        {
            Console.WriteLine("\n✗ Pairing failed - please try again");
            return false;
        }
    }
    
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

