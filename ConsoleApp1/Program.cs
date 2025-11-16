using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class AirPlayAuthenticator
{
    private readonly HttpClient _httpClient;
    private string _sessionId;
    private string _realm;
    private string _nonce;
    private int _nonceCount = 0;

    public AirPlayAuthenticator()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        _httpClient = new HttpClient(handler);
        _sessionId = Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Test connection and check authentication requirements
    /// </summary>
    public async Task<AuthenticationInfo> CheckAuthentication(string deviceIp, int port = 7000)
    {
        try
        {
            // Try a simple play request to see what auth is needed
            var url = $"http://{deviceIp}:{port}/play";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("User-Agent", "MediaControl/1.0");
            request.Headers.Add("X-Apple-Session-ID", _sessionId);
            request.Content = new StringContent("", Encoding.UTF8, "text/parameters");

            var response = await _httpClient.SendAsync(request);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // HTTP Digest authentication required
                var authHeader = response.Headers.WwwAuthenticate.ToString();
                Console.WriteLine($"Authentication required: {authHeader}");
                
                ParseAuthenticationHeader(authHeader);
                
                return new AuthenticationInfo
                {
                    RequiresAuth = true,
                    AuthType = "Digest",
                    Realm = _realm,
                    Details = authHeader
                };
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // Device verification/pairing required
                Console.WriteLine("Device requires pairing/verification");
                return new AuthenticationInfo
                {
                    RequiresAuth = true,
                    AuthType = "Pairing",
                    Details = "Requires HomeKit pairing (pair-setup)"
                };
            }
            else if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("No authentication required!");
                return new AuthenticationInfo
                {
                    RequiresAuth = false,
                    AuthType = "None"
                };
            }
            else
            {
                Console.WriteLine($"Unexpected response: {response.StatusCode}");
                return new AuthenticationInfo
                {
                    RequiresAuth = true,
                    AuthType = "Unknown",
                    Details = $"Status: {response.StatusCode}"
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking authentication: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Play video with HTTP Digest authentication
    /// </summary>
    public async Task<bool> PlayVideoWithDigestAuth(string deviceIp, string videoUrl, 
        string username, string password, int port = 7000)
    {
        try
        {
            var playUrl = $"http://{deviceIp}:{port}/play";
            
            // First request to get authentication challenge
            var initialRequest = new HttpRequestMessage(HttpMethod.Post, playUrl);
            initialRequest.Headers.Add("User-Agent", "MediaControl/1.0");
            initialRequest.Headers.Add("X-Apple-Session-ID", _sessionId);
            
            var body = $@"Content-Location: {videoUrl}
Start-Position: 0

";
            initialRequest.Content = new StringContent(body, Encoding.UTF8, "text/parameters");
            
            var initialResponse = await _httpClient.SendAsync(initialRequest);
            
            Console.WriteLine($"Initial response: {initialResponse.StatusCode}");
            
            if (initialResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // Parse WWW-Authenticate header
                var authHeader = initialResponse.Headers.WwwAuthenticate.ToString();
                Console.WriteLine($"WWW-Authenticate: {authHeader}");
                ParseAuthenticationHeader(authHeader);
                
                // Create authenticated request
                var authRequest = new HttpRequestMessage(HttpMethod.Post, playUrl);
                authRequest.Headers.Add("User-Agent", "MediaControl/1.0");
                authRequest.Headers.Add("X-Apple-Session-ID", _sessionId);
                
                // Generate Authorization header
                var authValue = GenerateDigestAuthHeader("POST", "/play", username, password);
                Console.WriteLine($"Authorization header: {authValue}");
                authRequest.Headers.Add("Authorization", authValue);
                
                authRequest.Content = new StringContent(body, Encoding.UTF8, "text/parameters");
                
                var authResponse = await _httpClient.SendAsync(authRequest);
                
                if (authResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine("✓ Video started with authentication!");
                    return true;
                }
                else
                {
                    Console.WriteLine($"✗ Authentication failed: {authResponse.StatusCode}");
                    var content = await authResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"Response: {content}");
                    
                    // Debug: Show all response headers
                    Console.WriteLine("\nResponse Headers:");
                    foreach (var header in authResponse.Headers)
                    {
                        Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
                    }
                    
                    return false;
                }
            }
            else if (initialResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("✓ Video started (no auth needed)!");
                return true;
            }
            else
            {
                Console.WriteLine($"✗ Failed: {initialResponse.StatusCode}");
                var content = await initialResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"Response: {content}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    private void ParseAuthenticationHeader(string authHeader)
    {
        // Example: Digest realm="AirPlay", nonce="MTMzMTMwODI0MCDEJP5Jo7HFo81rbAcKNKw2"
        var realmMatch = Regex.Match(authHeader, @"realm=""([^""]+)""");
        var nonceMatch = Regex.Match(authHeader, @"nonce=""([^""]+)""");
        
        if (realmMatch.Success)
            _realm = realmMatch.Groups[1].Value;
        
        if (nonceMatch.Success)
            _nonce = nonceMatch.Groups[1].Value;
        
        Console.WriteLine($"Realm: {_realm}");
        Console.WriteLine($"Nonce: {_nonce}");
    }

    private string GenerateDigestAuthHeader(string method, string uri, string username, string password)
    {
        _nonceCount++;
        
        // For AirPlay, username is typically empty
        if (string.IsNullOrEmpty(username))
        {
            username = "";
        }
        
        // HA1 = MD5(username:realm:password)
        var ha1 = ComputeMD5Hash($"{username}:{_realm}:{password}");
        
        // HA2 = MD5(method:uri)
        var ha2 = ComputeMD5Hash($"{method}:{uri}");
        
        // Response = MD5(HA1:nonce:HA2) - simplified digest without qop
        var response = ComputeMD5Hash($"{ha1}:{_nonce}:{ha2}");
        
        // Build authorization header with algorithm parameter
        if (string.IsNullOrEmpty(username))
        {
            // For empty username, don't include it or use empty string
            return $"Digest realm=\"{_realm}\", nonce=\"{_nonce}\", uri=\"{uri}\", response=\"{response}\", algorithm=MD5";
        }
        else
        {
            return $"Digest username=\"{username}\", realm=\"{_realm}\", nonce=\"{_nonce}\", uri=\"{uri}\", response=\"{response}\", algorithm=MD5";
        }
    }

    private string ComputeMD5Hash(string input)
    {
        using (var md5 = MD5.Create())
        {
            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        }
    }

    /// <summary>
    /// Stop playback
    /// </summary>
    public async Task<bool> Stop(string deviceIp, int port = 7000)
    {
        try
        {
            var url = $"http://{deviceIp}:{port}/stop";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("User-Agent", "MediaControl/1.0");
            request.Headers.Add("X-Apple-Session-ID", _sessionId);
            
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public class AuthenticationInfo
{
    public bool RequiresAuth { get; set; }
    public string AuthType { get; set; }
    public string Realm { get; set; }
    public string Details { get; set; }
}

public class Program
{
    public static async Task Main(string[] args)
    {
        var streamer = new AirPlayAuthenticator();
        string deviceIp = "192.168.2.121"; // Your Apple TV IP
        int port = 7000;
        
        Console.WriteLine("=== AirPlay Authentication Test ===\n");
        
        // Step 1: Check what authentication is needed
        Console.WriteLine("Checking authentication requirements...");
        var authInfo = await streamer.CheckAuthentication(deviceIp, port);
        
        if (authInfo == null)
        {
            Console.WriteLine("Failed to check authentication");
            return;
        }
        
        Console.WriteLine($"\nAuthentication Type: {authInfo.AuthType}");
        Console.WriteLine($"Details: {authInfo.Details}\n");
        
        // Step 2: Handle based on auth type
        if (authInfo.AuthType == "Pairing")
        {
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine("Your Apple TV requires HomeKit pairing (403 Forbidden)");
            Console.WriteLine("═══════════════════════════════════════════════════════\n");
            
            Console.WriteLine("OPTIONS TO FIX:\n");
            
            Console.WriteLine("1. DISABLE AUTHENTICATION (Easiest):");
            Console.WriteLine("   On your Apple TV:");
            Console.WriteLine("   • Go to Settings > AirPlay and HomeKit");
            Console.WriteLine("   • Set 'Allow Access' to 'Everyone' or 'Anyone on the Same Network'");
            Console.WriteLine("   • Turn OFF 'Require Device Verification'");
            Console.WriteLine("   • This removes the 403 error\n");
            
            Console.WriteLine("2. USE DIGEST PASSWORD (Simple):");
            Console.WriteLine("   On your Apple TV:");
            Console.WriteLine("   • Go to Settings > AirPlay and HomeKit > Security");
            Console.WriteLine("   • Set 'Security Type' to 'Password'");
            Console.WriteLine("   • Set a password (e.g., '1234')");
            Console.WriteLine("   • Then use PlayVideoWithDigestAuth() with that password\n");
            
            Console.WriteLine("3. IMPLEMENT FULL PAIRING (Complex):");
            Console.WriteLine("   • Requires SRP-6a cryptography implementation");
            Console.WriteLine("   • Use /pair-pin-start + /pair-setup + /pair-verify");
            Console.WriteLine("   • Consider using pyatv library as reference");
            Console.WriteLine("   • See: https://github.com/postlund/pyatv\n");
        }
        else if (authInfo.AuthType == "Digest")
        {
            Console.WriteLine("Your Apple TV uses password authentication (401)");
            Console.Write("Enter AirPlay password: ");
            string password = Console.ReadLine();
            
            string videoUrl = "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4";
            
            Console.WriteLine("\nTrying with empty username (standard for AirPlay)...");
            bool success = await streamer.PlayVideoWithDigestAuth(
                deviceIp, videoUrl, "", password, port);
            
            if (!success)
            {
                Console.WriteLine("\nTrying with 'AirPlay' username...");
                success = await streamer.PlayVideoWithDigestAuth(
                    deviceIp, videoUrl, "AirPlay", password, port);
            }
            
            if (success)
            {
                Console.WriteLine("\n✓ Video is playing! Press any key to stop...");
                Console.ReadKey();
                await streamer.Stop(deviceIp, port);
            }
            else
            {
                Console.WriteLine("\n✗ Authentication failed with both username attempts");
                Console.WriteLine("Please verify the password is correct in Apple TV settings");
            }
        }
        else if (authInfo.AuthType == "None")
        {
            Console.WriteLine("✓ No authentication required - your original code should work!");
        }
        
        Console.WriteLine("\n=== Test Complete ===");
    }
}