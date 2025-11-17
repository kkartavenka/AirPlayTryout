using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Collections;
using System.Linq;

namespace AirPlayVideoStreaming;

public class AirPlayVideoStreamer
{
    private readonly string _deviceIp;
    private readonly int _port;
    private readonly HttpClient _httpClient;
    private AirPlayDeviceInfo? _deviceInfo;
    private AirPlayPairing? _pairing;
    private bool _isPaired = false;
    
    public AirPlayVideoStreamer(string deviceIp, int port = 7000)
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
        
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "MediaControl/1.0");
        _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
        
        _pairing = new AirPlayPairing(deviceIp, port);
    }
    
    /// <summary>
    /// Pair with the device if required
    /// </summary>
    public async Task<bool> PairIfRequiredAsync()
    {
        if (_isPaired)
        {
            Console.WriteLine("✓ Device is already paired");
            return true;
        }
        
        if (_deviceInfo != null && (_deviceInfo.RequiresPassword || _deviceInfo.RequiresAuth))
        {
            Console.WriteLine("\n⚠ Device requires pairing/authentication");
            Console.Write("Would you like to pair with the device now? (y/n): ");
            var response = Console.ReadLine();
            
            if (response?.ToLower() == "y" || response?.ToLower() == "yes")
            {
                if (_pairing != null)
                {
                    _isPaired = await _pairing.PairWithDeviceAsync();
                    if (!_isPaired)
                    {
                        Console.WriteLine("\n⚠ Pairing failed, but some devices allow unauthenticated connections.");
                        Console.WriteLine("   We'll proceed with video playback - it may work without pairing.");
                        Console.WriteLine("   If playback fails, the device may require proper authentication.");
                    }
                    return _isPaired;
                }
            }
            else
            {
                Console.WriteLine("Skipping pairing - will try video playback anyway");
                Console.WriteLine("Note: Some devices allow unauthenticated connections for certain operations");
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Test connection to device by attempting a simple operation
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            Console.WriteLine("\n=== Testing Connection to Device ===");
            
            // Test 1: Basic info endpoint
            Console.WriteLine("1. Testing /info endpoint...");
            var infoRequest = new HttpRequestMessage(HttpMethod.Get, $"http://{_deviceIp}:{_port}/info");
            var infoResponse = await _httpClient.SendAsync(infoRequest);
            Console.WriteLine($"   /info: {infoResponse.StatusCode}");
            
            // Test 2: Server info endpoint
            Console.WriteLine("2. Testing /server-info endpoint...");
            var serverInfoRequest = new HttpRequestMessage(HttpMethod.Get, $"http://{_deviceIp}:{_port}/server-info");
            var serverInfoResponse = await _httpClient.SendAsync(serverInfoRequest);
            Console.WriteLine($"   /server-info: {serverInfoResponse.StatusCode}");
            
            // Test 3: Try OPTIONS on RTSP
            Console.WriteLine("3. Testing RTSP OPTIONS...");
            try
            {
                using var rtspClient = new RtspClient(_deviceIp, _port);
                if (_deviceInfo != null)
                {
                    rtspClient.SetDeviceInfo(_deviceInfo);
                    // Use discovered device ID
                    if (!string.IsNullOrEmpty(_deviceInfo.DeviceId))
                    {
                        var deviceId = _deviceInfo.DeviceId.Replace(":", "").Replace("-", "");
                        if (deviceId.Length > 16) deviceId = deviceId.Substring(0, 16);
                        rtspClient.SetDeviceId(deviceId, Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16));
                    }
                }
                
                if (await rtspClient.ConnectAsync())
                {
                    bool optionsSuccess = await rtspClient.OptionsAsync();
                    Console.WriteLine($"   RTSP OPTIONS: {(optionsSuccess ? "Success" : "Failed")}");
                    return optionsSuccess;
                }
                else
                {
                    Console.WriteLine("   RTSP connection failed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   RTSP test error: {ex.Message}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection test failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Get pairing status
    /// </summary>
    public bool IsPaired()
    {
        return _isPaired;
    }
    
    /// <summary>
    /// Check if device is reachable
    /// </summary>
    public async Task<bool> IsDeviceReachableAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://{_deviceIp}:{_port}/info");
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Discover device via mDNS and extract capabilities
    /// </summary>
    public async Task<AirPlayDeviceInfo?> DiscoverDeviceAsync()
    {
        _deviceInfo = await AirPlayDeviceDiscoverer.DiscoverDeviceAsync(_deviceIp, _port);
        return _deviceInfo;
    }
    
    /// <summary>
    /// Get discovered device info
    /// </summary>
    public AirPlayDeviceInfo? GetDeviceInfo()
    {
        return _deviceInfo;
    }
    
    /// <summary>
    /// Check if device requires pairing/authentication
    /// </summary>
    public async Task<bool> CheckPairingRequiredAsync()
    {
        // First check discovered device info
        if (_deviceInfo != null)
        {
            if (_deviceInfo.RequiresAuth || _deviceInfo.RequiresPassword)
            {
                return true;
            }
        }
        
        // Also check via HTTP
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://{_deviceIp}:{_port}/server-info");
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                // Check if response indicates pairing is required
                if (response.StatusCode == HttpStatusCode.Unauthorized || 
                    content.Contains("pair") || 
                    content.Contains("auth"))
                {
                    return true;
                }
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Try to play video using RTSP reverse connection (some AirPlay devices use this)
    /// </summary>
    public async Task<bool> TryRtspReverseAsync(string videoUrl)
    {
        try
        {
            Console.WriteLine("\nTrying RTSP reverse connection approach...");
            var url = $"http://{_deviceIp}:{_port}/reverse";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            request.Headers.Add("User-Agent", "MediaControl/1.0");
            request.Headers.Add("X-Apple-Device-ID", Guid.NewGuid().ToString().Replace("-", ""));
            request.Headers.Add("X-Apple-Session-ID", Guid.NewGuid().ToString().Replace("-", ""));
            request.Headers.Add("Upgrade", "PTTH/1.0");
            request.Headers.Add("Connection", "Upgrade");
            
            // RTSP reverse connection - send binary plist with Content-Location
            var plistDict = new Dictionary<string, object>
            {
                ["Content-Location"] = videoUrl,
                ["Start-Position"] = 0.0
            };
            
            byte[] plistBytes = BinaryPlistHelper.Write(plistDict);
            request.Content = new ByteArrayContent(plistBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-apple-binary-plist");
            request.Content.Headers.ContentLength = plistBytes.Length;
            
            var response = await _httpClient.SendAsync(request);
            Console.WriteLine($"RTSP reverse response: {response.StatusCode}");
            
            // 101 Switching Protocols means it's switching to RTSP - this is success!
            if (response.StatusCode == HttpStatusCode.SwitchingProtocols)
            {
                Console.WriteLine("✓ RTSP reverse connection established! Device is ready for RTSP streaming.");
                Console.WriteLine("Note: Full RTSP implementation would require RTSP client library.");
                return true;
            }
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RTSP reverse failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Get device information
    /// </summary>
    public async Task<Dictionary<string, string>?> GetDeviceInfoAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://{_deviceIp}:{_port}/server-info");
            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var contentBytes = await response.Content.ReadAsByteArrayAsync();
            
            Console.WriteLine($"Device Info Response Type: {contentType}");
            Console.WriteLine($"Response Length: {contentBytes.Length} bytes");
            
            var info = new Dictionary<string, string>
            {
                ["Status"] = response.IsSuccessStatusCode ? "Connected" : "Failed",
                ["StatusCode"] = response.StatusCode.ToString(),
                ["ContentType"] = contentType
            };
            
            // Try to parse binary plist if that's what we got
            if (contentType.Contains("binary-plist") || contentType.Contains("plist"))
            {
                try
                {
                    var plist = BinaryPlistHelper.Read(contentBytes);
                    if (plist is Dictionary<string, object> dict)
                    {
                        foreach (var kvp in dict)
                        {
                            info[kvp.Key] = kvp.Value?.ToString() ?? "";
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not parse plist: {ex.Message}");
                }
            }
            else
            {
                var content = Encoding.UTF8.GetString(contentBytes);
                Console.WriteLine($"Device Info Response: {content}");
            }
            
            return info;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting device info: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Check available endpoints on the device
    /// </summary>
    public async Task<List<string>> CheckAvailableEndpointsAsync()
    {
        var endpoints = new List<string>();
        // Common AirPlay endpoints
        var commonEndpoints = new[] 
        { 
            "/play", "/playback-info", "/playback", "/stream", "/video", "/media",
            "/photo", "/stop", "/reverse", "/scrub", "/rate", "/info", "/server-info",
            "/pair-setup", "/pair-verify", "/pair-add", "/pair-remove"
        };
        
        Console.WriteLine("Scanning for available endpoints...");
        foreach (var endpoint in commonEndpoints)
        {
            try
            {
                // Try OPTIONS first
                var request = new HttpRequestMessage(HttpMethod.Options, $"http://{_deviceIp}:{_port}{endpoint}");
                request.Headers.Add("User-Agent", "MediaControl/1.0");
                var response = await _httpClient.SendAsync(request);
                
                if (response.StatusCode != HttpStatusCode.NotFound)
                {
                    endpoints.Add(endpoint);
                    Console.WriteLine($"✓ Found endpoint: {endpoint} (Status: {response.StatusCode})");
                }
            }
            catch { }
        }
        
        // Also try GET requests for info endpoints
        var infoEndpoints = new[] { "/info", "/server-info" };
        foreach (var endpoint in infoEndpoints)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"http://{_deviceIp}:{_port}{endpoint}");
                request.Headers.Add("User-Agent", "MediaControl/1.0");
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode && !endpoints.Contains(endpoint))
                {
                    endpoints.Add(endpoint);
                    Console.WriteLine($"✓ Found endpoint: {endpoint} (Status: {response.StatusCode})");
                }
            }
            catch { }
        }
        
        return endpoints;
    }
    
    /// <summary>
    /// Start video playback by sending video URL or data
    /// </summary>
    public async Task<bool> PlayVideoAsync(string videoUrl, double? startPosition = null, double? duration = null)
    {
        try
        {
            // Try different endpoints and methods - AirPlay devices vary
            var endpointsToTry = new[]
            {
                // Standard AirPlay endpoints
                ("/play", HttpMethod.Post),
                ("/play", HttpMethod.Put),
                ("/reverse", HttpMethod.Post), // RTSP reverse connection
                ("/playback-info", HttpMethod.Post),
                ("/playback", HttpMethod.Post),
                ("/stream", HttpMethod.Post),
                ("/video", HttpMethod.Post),
                // Some devices use these
                ("/media", HttpMethod.Post),
                ("/content", HttpMethod.Post),
                // Try photo endpoint with video (some devices accept it)
                ("/photo", HttpMethod.Put)
            };
            
            HttpResponseMessage? response = null;
            string? successfulEndpoint = null;
            
            Console.WriteLine($"Attempting to play video: {videoUrl}");
            Console.WriteLine($"Start Position: {startPosition ?? 0.0}");
            Console.WriteLine();
            
            // Build binary plist request body
            var plistDict = new Dictionary<string, object>
            {
                ["Content-Location"] = videoUrl,
                ["Start-Position"] = startPosition ?? 0.0
            };
            
            if (duration.HasValue)
            {
                plistDict["Duration"] = duration.Value;
            }
            
            byte[] plistBytes = BinaryPlistHelper.Write(plistDict);
            
            foreach (var (endpoint, method) in endpointsToTry)
            {
                try
                {
                    var url = $"http://{_deviceIp}:{_port}{endpoint}";
                    var request = new HttpRequestMessage(method, url);
            
                    // AirPlay video playback headers
                    request.Headers.Add("User-Agent", "MediaControl/1.0");
                    request.Headers.Add("X-Apple-Device-ID", Guid.NewGuid().ToString().Replace("-", ""));
                    request.Headers.Add("X-Apple-Session-ID", Guid.NewGuid().ToString().Replace("-", ""));
                    
                    // Create binary plist
                    request.Content = new ByteArrayContent(plistBytes);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-apple-binary-plist");
                    request.Content.Headers.ContentLength = plistBytes.Length;
                    
                    Console.WriteLine($"Trying {method} {endpoint}...");
                    
                    response = await _httpClient.SendAsync(request);
                    
                    Console.WriteLine($"  Response: {response.StatusCode}");
                    
                    // 101 Switching Protocols means RTSP upgrade - this is success for /reverse!
                    if (response.StatusCode == HttpStatusCode.SwitchingProtocols && endpoint == "/reverse")
                    {
                        Console.WriteLine($"✓ {endpoint} returned SwitchingProtocols - RTSP connection ready!");
                        successfulEndpoint = endpoint;
                        
                        // Store response for RTSP stream access
                        // We'll handle RTSP on the upgraded connection
                        break; // This is success, stop trying other endpoints
                    }
                    else if (response.IsSuccessStatusCode)
                    {
                        successfulEndpoint = endpoint;
                        Console.WriteLine($"✓ Success with {method} {endpoint}!");
                        break;
                    }
                    else if (response.StatusCode != HttpStatusCode.NotFound)
                    {
                        // Got a response but not success - might be the right endpoint with wrong format
                        Console.WriteLine($"  {endpoint} returned {response.StatusCode} (might work with different format)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Error trying {endpoint}: {ex.Message}");
                }
            }
            
            // If binary plist failed and we didn't get SwitchingProtocols, try XML plist
            if (response == null || (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.SwitchingProtocols))
            {
                Console.WriteLine("\nTrying XML plist format on /play endpoint...");
                try
                {
                    var url = $"http://{_deviceIp}:{_port}/play";
                    var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Headers.Add("User-Agent", "MediaControl/1.0");
                    request.Headers.Add("X-Apple-Device-ID", Guid.NewGuid().ToString().Replace("-", ""));
                    request.Headers.Add("X-Apple-Session-ID", Guid.NewGuid().ToString().Replace("-", ""));
                    
                    var xmlPlist = BuildXmlPlist(videoUrl, startPosition, duration);
                    request.Content = new StringContent(xmlPlist, Encoding.UTF8, "application/x-apple-plist+xml");
                    
                    response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("✓ XML plist format worked!");
                    }
                    else
                    {
                        Console.WriteLine($"  XML plist returned: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"XML plist attempt failed: {ex.Message}");
                }
            }
            
            // If we got SwitchingProtocols from /reverse, establish RTSP connection
            if (response != null && response.StatusCode == HttpStatusCode.SwitchingProtocols && successfulEndpoint == "/reverse")
            {
                Console.WriteLine("\n=== Establishing RTSP Connection ===");
                Console.WriteLine("HTTP connection upgraded to RTSP protocol.");
                
                // For AirPlay reverse RTSP, we need to use the upgraded HTTP connection's stream
                // HttpClient doesn't easily expose this, so we'll try using HttpWebRequest for better control
                // or use the response stream directly
                
                try
                {
                    // Try to use HttpWebRequest for better control over the upgraded connection
                    var rtspSuccess = await PlayVideoViaUpgradedConnectionAsync(videoUrl, startPosition);
                    if (rtspSuccess)
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Upgraded connection approach failed: {ex.Message}");
                }
                
            // Fallback: Try new TCP connection (some devices accept this)
            Console.WriteLine("Trying new TCP connection for RTSP...");
            using var rtspClient = new RtspClient(_deviceIp, _port);
            
            // Set device identification - use discovered device ID if available
            string deviceId;
            if (_deviceInfo != null && !string.IsNullOrEmpty(_deviceInfo.DeviceId))
            {
                // Use actual device ID from discovery (MAC address without colons)
                deviceId = _deviceInfo.DeviceId.Replace(":", "").Replace("-", "");
                if (deviceId.Length > 16)
                {
                    deviceId = deviceId.Substring(0, 16);
                }
                Console.WriteLine($"Using discovered Device ID: {_deviceInfo.DeviceId}");
            }
            else
            {
                deviceId = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16);
            }
            
            var sessionId = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16);
            rtspClient.SetDeviceId(deviceId, sessionId);
            
            // Use discovered device info if available
            if (_deviceInfo != null)
            {
                rtspClient.SetDeviceInfo(_deviceInfo);
            }
            
            if (await rtspClient.ConnectAsync())
                {
                    // Play video via RTSP
                    bool rtspSuccess = await rtspClient.PlayVideoAsync(videoUrl, startPosition);
                    if (rtspSuccess)
                    {
                        Console.WriteLine("\n✓ Video playback initiated via RTSP!");
                        Console.WriteLine("Press Enter to stop playback...");
                        Console.ReadLine();
                        
                        // Teardown
                        await rtspClient.TeardownAsync("*");
                        return true;
                    }
                }
            }
            
            // Try RTSP reverse if all HTTP endpoints failed (but we already tried /reverse above, so skip if we got SwitchingProtocols)
            if (response == null || (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.SwitchingProtocols))
            {
                // Only try RTSP reverse if we haven't already gotten SwitchingProtocols from /reverse
                if (successfulEndpoint != "/reverse")
                {
                    var rtspSuccess = await TryRtspReverseAsync(videoUrl);
                    if (rtspSuccess)
                    {
                        Console.WriteLine("Note: RTSP reverse connection initiated. Video playback may require RTSP protocol.");
                        return true;
                    }
                }
            }
            
            // Last resort: Try sending video URL in Content-Location header (proper way)
            if (response == null || (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.SwitchingProtocols))
            {
                Console.WriteLine("\nTrying Content-Location header approach...");
                try
                {
                    var url = $"http://{_deviceIp}:{_port}/play";
                    var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Headers.Add("User-Agent", "MediaControl/1.0");
                    request.Headers.Add("X-Apple-Device-ID", Guid.NewGuid().ToString().Replace("-", ""));
                    request.Headers.Add("X-Apple-Session-ID", Guid.NewGuid().ToString().Replace("-", ""));
                    
                    // Content-Location goes in content headers, not request headers
                    request.Content = new StringContent("", Encoding.UTF8, "application/octet-stream");
                    request.Content.Headers.Add("Content-Location", videoUrl);
                    
                    response = await _httpClient.SendAsync(request);
                    Console.WriteLine($"Content-Location header approach: {response.StatusCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Content-Location header approach failed: {ex.Message}");
                }
            }
            
            if (response == null)
            {
                Console.WriteLine("\nNo response received from any endpoint");
                return false;
            }
            
            var responseContentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var responseBytes = await response.Content.ReadAsByteArrayAsync();
            
            Console.WriteLine($"\nFinal Response Status: {response.StatusCode}");
            Console.WriteLine($"Response Content-Type: {responseContentType}");
            Console.WriteLine($"Response Length: {responseBytes.Length} bytes");
            
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.SwitchingProtocols)
            {
                if (response.StatusCode == HttpStatusCode.SwitchingProtocols)
                {
                    // RTSP connection handling is done above, so if we reach here it means RTSP failed
                    Console.WriteLine("\n✓ RTSP reverse connection established!");
                    Console.WriteLine("RTSP protocol upgrade successful, but video playback setup failed.");
                    return false; // Return false since RTSP playback wasn't successful
                }
                
                // Try to parse binary plist response
                if (responseContentType.Contains("binary-plist") || responseContentType.Contains("plist"))
                {
                    try
                    {
                        var responsePlist = BinaryPlistHelper.Read(responseBytes);
                        Console.WriteLine($"Play response (parsed): {responsePlist}");
                    }
                    catch
                    {
                        if (responseBytes.Length > 0)
                        {
                            Console.WriteLine($"Play response (raw): {BitConverter.ToString(responseBytes.Take(Math.Min(100, responseBytes.Length)).ToArray())}...");
                        }
                    }
                }
                else if (responseBytes.Length > 0)
                {
                    var responseText = Encoding.UTF8.GetString(responseBytes);
                    Console.WriteLine($"Play response: {responseText}");
                }
                return true;
            }
            else
            {
                if (responseBytes.Length > 0)
                {
                    var errorText = Encoding.UTF8.GetString(responseBytes);
                    Console.WriteLine($"Play failed: {response.StatusCode} - {errorText}");
                }
                else
                {
                    Console.WriteLine($"Play failed: {response.StatusCode} (no content)");
                }
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error playing video: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }
    
    /// <summary>
    /// Stop current playback
    /// </summary>
    public async Task<bool> StopAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://{_deviceIp}:{_port}/stop");
            request.Headers.Add("User-Agent", "MediaControl/1.0");
            
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping playback: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Send video file data directly (for local files)
    /// </summary>
    public async Task<bool> PlayVideoFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"File not found: {filePath}");
            return false;
        }
        
        try
        {
            // Read file as bytes
            var videoData = await File.ReadAllBytesAsync(filePath);
            
            var url = $"http://{_deviceIp}:{_port}/play";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            request.Headers.Add("User-Agent", "MediaControl/1.0");
            request.Headers.Add("X-Apple-Device-ID", Guid.NewGuid().ToString().Replace("-", ""));
            request.Headers.Add("X-Apple-Session-ID", Guid.NewGuid().ToString().Replace("-", ""));
            request.Headers.Add("Content-Type", "video/mp4");
            request.Headers.Add("Content-Length", videoData.Length.ToString());
            
            request.Content = new ByteArrayContent(videoData);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            request.Content.Headers.ContentLength = videoData.Length;
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Play file response: {responseContent}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Play file failed: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error playing video file: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Stream video data in chunks (for real-time streaming)
    /// </summary>
    public async Task<bool> StreamVideoAsync(Stream videoStream, string contentType = "video/mp4")
    {
        try
        {
            var url = $"http://{_deviceIp}:{_port}/play";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            request.Headers.Add("User-Agent", "MediaControl/1.0");
            request.Headers.Add("X-Apple-Device-ID", Guid.NewGuid().ToString().Replace("-", ""));
            request.Headers.Add("X-Apple-Session-ID", Guid.NewGuid().ToString().Replace("-", ""));
            
            request.Content = new StreamContent(videoStream);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            
            if (videoStream.CanSeek)
            {
                request.Content.Headers.ContentLength = videoStream.Length;
            }
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Stream response: {responseContent}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Stream failed: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error streaming video: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Set playback position
    /// </summary>
    public async Task<bool> SetPositionAsync(double position)
    {
        try
        {
            var url = $"http://{_deviceIp}:{_port}/scrub?position={position}";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("User-Agent", "MediaControl/1.0");
            
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting position: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Get current playback position
    /// </summary>
    public async Task<double?> GetPositionAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://{_deviceIp}:{_port}/scrub");
            request.Headers.Add("User-Agent", "MediaControl/1.0");
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                // Parse position from response (typically in format "position=X.XX")
                if (double.TryParse(content, out double position))
                {
                    return position;
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting position: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Start screen mirroring by sending screen frames
    /// </summary>
    public async Task<bool> StartScreenMirroringAsync(Stream frameStream, string contentType = "image/jpeg")
    {
        try
        {
            // AirPlay screen mirroring endpoint
            var url = $"http://{_deviceIp}:{_port}/mirror";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            request.Headers.Add("User-Agent", "MediaControl/1.0");
            request.Headers.Add("X-Apple-Device-ID", Guid.NewGuid().ToString().Replace("-", ""));
            request.Headers.Add("X-Apple-Session-ID", Guid.NewGuid().ToString().Replace("-", ""));
            
            request.Content = new StreamContent(frameStream);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            
            if (frameStream.CanSeek)
            {
                request.Content.Headers.ContentLength = frameStream.Length;
            }
            
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting screen mirroring: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Send a single frame/image to AirPlay device
    /// </summary>
    public async Task<bool> SendFrameAsync(byte[] frameData, string contentType = "image/jpeg")
    {
        try
        {
            // Try /photo endpoint for individual frames
            var url = $"http://{_deviceIp}:{_port}/photo";
            var request = new HttpRequestMessage(HttpMethod.Put, url);
            
            request.Headers.Add("User-Agent", "MediaControl/1.0");
            request.Headers.Add("X-Apple-Transition", "None");
            
            request.Content = new ByteArrayContent(frameData);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            request.Content.Headers.ContentLength = frameData.Length;
            
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending frame: {ex.Message}");
            return false;
        }
    }
    
    
    /// <summary>
    /// Play video using the upgraded HTTP connection stream (after 101 SwitchingProtocols)
    /// Uses manual TCP connection to handle the upgrade properly
    /// </summary>
    private async Task<bool> PlayVideoViaUpgradedConnectionAsync(string videoUrl, double? startPosition)
    {
        try
        {
            Console.WriteLine("Establishing TCP connection for RTSP reverse...");
            
            // Create TCP connection manually to handle upgrade
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(_deviceIp, _port);
            var stream = tcpClient.GetStream();
            
            // Send HTTP POST request for /reverse
            var plistDict = new Dictionary<string, object>
            {
                ["Content-Location"] = videoUrl,
                ["Start-Position"] = startPosition ?? 0.0
            };
            
            byte[] plistBytes = BinaryPlistHelper.Write(plistDict);
            
            // Generate device ID and session ID for this connection
            // Use discovered device ID if available
            string deviceId;
            if (_deviceInfo != null && !string.IsNullOrEmpty(_deviceInfo.DeviceId))
            {
                // Use actual device ID from discovery (MAC address without colons)
                deviceId = _deviceInfo.DeviceId.Replace(":", "").Replace("-", "");
                if (deviceId.Length > 16)
                {
                    deviceId = deviceId.Substring(0, 16);
                }
            }
            else
            {
                deviceId = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16);
            }
            
            var sessionId = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16);
            
            var httpRequest = new StringBuilder();
            httpRequest.Append($"POST /reverse HTTP/1.1\r\n");
            httpRequest.Append($"Host: {_deviceIp}:{_port}\r\n");
            httpRequest.Append($"User-Agent: AirPlay/375.3\r\n");
            httpRequest.Append($"X-Apple-Device-ID: {deviceId}\r\n");
            httpRequest.Append($"X-Apple-Session-ID: {sessionId}\r\n");
            httpRequest.Append($"Upgrade: PTTH/1.0\r\n");
            httpRequest.Append($"Connection: Upgrade\r\n");
            httpRequest.Append($"Content-Type: application/x-apple-binary-plist\r\n");
            httpRequest.Append($"Content-Length: {plistBytes.Length}\r\n");
            httpRequest.Append("\r\n"); // Empty line to end headers
            
            var httpRequestBytes = Encoding.UTF8.GetBytes(httpRequest.ToString());
            await stream.WriteAsync(httpRequestBytes, 0, httpRequestBytes.Length);
            await stream.WriteAsync(plistBytes, 0, plistBytes.Length);
            await stream.FlushAsync();
            
            Console.WriteLine("Sent HTTP POST /reverse request");
            Console.WriteLine($"Request size: {httpRequestBytes.Length} bytes headers + {plistBytes.Length} bytes plist");
            
            // Read HTTP response - need to read until we get complete response
            var responseBuffer = new List<byte>();
            var tempBuffer = new byte[4096];
            var timeout = DateTime.Now.AddSeconds(5);
            var foundEnd = false;
            
            // Give device a moment to respond
            await Task.Delay(100);
            
            while (DateTime.Now < timeout && !foundEnd)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var bytesRead = await stream.ReadAsync(tempBuffer, 0, tempBuffer.Length, cts.Token);
                    
                    if (bytesRead == 0)
                    {
                        // No data - might mean connection is ready for RTSP
                        if (responseBuffer.Count == 0)
                        {
                            Console.WriteLine("No HTTP response received - connection may be upgraded immediately");
                            // Proceed with RTSP anyway
                            break;
                        }
                        break;
                    }
                    
                    responseBuffer.AddRange(tempBuffer.Take(bytesRead));
                    
                    // Check if we have complete HTTP response (ends with \r\n\r\n)
                    var responseSoFar = Encoding.UTF8.GetString(responseBuffer.ToArray());
                    if (responseSoFar.Contains("\r\n\r\n"))
                    {
                        foundEnd = true;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (responseBuffer.Count > 0)
                    {
                        break;
                    }
                    else
                    {
                        // Timeout with no data - might be upgraded already
                        Console.WriteLine("Timeout waiting for HTTP response - proceeding with RTSP");
                        break;
                    }
                }
            }
            
            var httpResponse = responseBuffer.Count > 0 ? Encoding.UTF8.GetString(responseBuffer.ToArray()) : "";
            
            if (!string.IsNullOrEmpty(httpResponse))
            {
                Console.WriteLine($"HTTP Response ({responseBuffer.Count} bytes):");
                var responseLines = httpResponse.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Take(15);
                foreach (var line in responseLines)
                {
                    Console.WriteLine($"  {line}");
                }
            }
            else
            {
                Console.WriteLine("No HTTP response received - assuming connection upgraded");
            }
            
            // Check if we got 101 Switching Protocols OR if we got no response (device might upgrade silently)
            bool isUpgraded = httpResponse.Contains("101") || 
                             httpResponse.Contains("Switching Protocols") || 
                             httpResponse.Contains("HTTP/1.1 101") ||
                             httpResponse.Contains("HTTP/1.0 101") ||
                             responseBuffer.Count == 0; // No response might mean immediate upgrade
            
            if (isUpgraded)
            {
                Console.WriteLine("✓ Connection upgraded to RTSP!");
                
            // Now use this stream for RTSP commands
            using var rtspClient = new RtspClient(_deviceIp, _port);
            
            // Use the same device ID and session ID from the HTTP request
            // This maintains session continuity with the device
            rtspClient.SetDeviceId(deviceId, sessionId);
            
            // Use discovered device info if available
            if (_deviceInfo != null)
            {
                rtspClient.SetDeviceInfo(_deviceInfo);
                // Use actual device ID from discovery if available
                if (!string.IsNullOrEmpty(_deviceInfo.DeviceId))
                {
                    // Use MAC address format (remove colons) or keep as is
                    var discoveredDeviceId = _deviceInfo.DeviceId.Replace(":", "").Replace("-", "");
                    if (discoveredDeviceId.Length >= 16)
                    {
                        rtspClient.SetDeviceId(discoveredDeviceId.Substring(0, 16), sessionId);
                    }
                }
            }
            
            rtspClient.SetStream(stream);
                
                // Play video via RTSP
                bool rtspSuccess = await rtspClient.PlayVideoAsync(videoUrl, startPosition);
                if (rtspSuccess)
                {
                    Console.WriteLine("\n✓ Video playback initiated via RTSP!");
                    Console.WriteLine("Press Enter to stop playback...");
                    Console.ReadLine();
                    
                    // Teardown
                    await rtspClient.TeardownAsync("*");
                    return true;
                }
            }
            else
            {
                Console.WriteLine("Did not receive 101 Switching Protocols");
                if (!string.IsNullOrEmpty(httpResponse))
                {
                    Console.WriteLine($"Response was: {httpResponse.Substring(0, Math.Min(200, httpResponse.Length))}");
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Upgraded connection RTSP failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }
    
    private string BuildXmlPlist(string videoUrl, double? startPosition, double? duration)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        sb.AppendLine("<plist version=\"1.0\">");
        sb.AppendLine("<dict>");
        sb.AppendLine($"  <key>Content-Location</key>");
        sb.AppendLine($"  <string>{videoUrl}</string>");
        
        if (startPosition.HasValue)
        {
            sb.AppendLine($"  <key>Start-Position</key>");
            sb.AppendLine($"  <real>{startPosition.Value}</real>");
        }
        
        if (duration.HasValue)
        {
            sb.AppendLine($"  <key>Duration</key>");
            sb.AppendLine($"  <real>{duration.Value}</real>");
        }
        
        sb.AppendLine("</dict>");
        sb.AppendLine("</plist>");
        
        return sb.ToString();
    }
    
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

