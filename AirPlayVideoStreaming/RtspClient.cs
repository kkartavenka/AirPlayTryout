using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace AirPlayVideoStreaming;

public class RtspClient : IDisposable
{
    private TcpClient? _tcpClient;
    private Stream? _stream;
    private string _deviceIp;
    private int _port;
    private string _sessionId = "";
    private int _cseq = 1;
    private bool _disposed = false;
    private bool _ownsStream = true;
    private string _deviceId;
    private string _sessionIdHeader;
    private AirPlayDeviceInfo? _deviceInfo;
    private string? _videoUrl;
    
    public RtspClient(string deviceIp, int port = 7000)
    {
        _deviceIp = deviceIp;
        _port = port;
        // Generate device ID and session ID for AirPlay identification
        _deviceId = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16);
        _sessionIdHeader = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16);
    }
    
    /// <summary>
    /// Set device identification (can be called before connecting)
    /// </summary>
    public void SetDeviceId(string deviceId, string sessionId)
    {
        _deviceId = deviceId;
        _sessionIdHeader = sessionId;
    }
    
    /// <summary>
    /// Set device info to inform connection strategy
    /// </summary>
    public void SetDeviceInfo(AirPlayDeviceInfo deviceInfo)
    {
        _deviceInfo = deviceInfo;
    }
    
    /// <summary>
    /// Set an existing stream (e.g., from upgraded HTTP connection)
    /// </summary>
    public void SetStream(Stream stream)
    {
        _stream = stream;
        _ownsStream = false; // Don't dispose it, it's managed elsewhere
    }
    
    /// <summary>
    /// Get the underlying stream for sending binary data
    /// </summary>
    public Stream? GetStream()
    {
        return _stream;
    }
    
    /// <summary>
    /// Send RTSP request and return raw response (public wrapper)
    /// </summary>
    public async Task<string> SendRtspRequestPublicAsync(string method, string uri, Dictionary<string, string>? headers = null, string? body = null)
    {
        return await SendRtspRequestAsync(method, uri, headers, body);
    }
    
    /// <summary>
    /// Parse RTSP response status (public wrapper)
    /// </summary>
    public bool ParseResponseStatusPublic(string response, out int statusCode, out Dictionary<string, string> responseHeaders)
    {
        return ParseResponseStatus(response, out statusCode, out responseHeaders);
    }
    
    /// <summary>
    /// Connect to RTSP server after HTTP upgrade
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        try
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(_deviceIp, _port);
            _stream = _tcpClient.GetStream();
            
            Console.WriteLine($"RTSP TCP connection established to {_deviceIp}:{_port}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect RTSP: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Send RTSP request and get response
    /// </summary>
    private async Task<string> SendRtspRequestAsync(string method, string uri, Dictionary<string, string>? headers = null, string? body = null)
    {
        if (_stream == null)
        {
            throw new InvalidOperationException("RTSP stream not connected");
        }
        
        var request = new StringBuilder();
        request.Append($"{method} {uri} RTSP/1.0\r\n");
        request.Append($"CSeq: {_cseq++}\r\n");
        request.Append($"User-Agent: AirPlay/375.3\r\n");
        
        // Add AirPlay device identification headers
        request.Append($"X-Apple-Device-ID: {_deviceId}\r\n");
        request.Append($"X-Apple-Session-ID: {_sessionIdHeader}\r\n");
        
        // Additional AirPlay headers that may be required
        // DACP-ID: Used for remote control sessions
        // Client-Instance: Unique client instance identifier
        request.Append($"DACP-ID: {_deviceId}\r\n");
        request.Append($"Client-Instance: {_sessionIdHeader}\r\n");
        
        if (headers != null)
        {
            foreach (var header in headers)
            {
                request.Append($"{header.Key}: {header.Value}\r\n");
            }
        }
        
        if (!string.IsNullOrEmpty(_sessionId))
        {
            request.Append($"Session: {_sessionId}\r\n");
        }
        
        if (!string.IsNullOrEmpty(body))
        {
            request.Append($"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n");
        }
        
        request.Append("\r\n"); // Empty line before body
        
        if (!string.IsNullOrEmpty(body))
        {
            request.Append(body);
        }
        
        var requestBytes = Encoding.UTF8.GetBytes(request.ToString());
        await _stream.WriteAsync(requestBytes, 0, requestBytes.Length);
        await _stream.FlushAsync();
        
        Console.WriteLine($"Sent RTSP {method} {uri}");
        
        // Read response - RTSP responses end with \r\n\r\n
        var responseBuffer = new List<byte>();
        var tempBuffer = new byte[4096];
        var totalRead = 0;
        var timeout = DateTime.Now.AddSeconds(10); // Longer timeout for RTSP
        var foundEnd = false;
        
        // Try to read response - RTSP might not respond immediately
        while (DateTime.Now < timeout && !foundEnd)
        {
            try
            {
                // Check if data is available (for NetworkStream)
                if (_stream is NetworkStream networkStream)
                {
                    // NetworkStream doesn't have DataAvailable in .NET 6, so we'll just try reading
                }
                
                // Use ReadAsync with timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var bytesRead = await _stream.ReadAsync(tempBuffer, 0, tempBuffer.Length, cts.Token);
                
                if (bytesRead == 0)
                {
                    // No data available - might be waiting for more data or connection closed
                    await Task.Delay(100); // Small delay
                    continue;
                }
                
                responseBuffer.AddRange(tempBuffer.Take(bytesRead));
                totalRead += bytesRead;
                
                // Check if we have a complete RTSP response (ends with \r\n\r\n)
                var responseSoFar = Encoding.UTF8.GetString(responseBuffer.ToArray());
                if (responseSoFar.Contains("\r\n\r\n") || responseSoFar.Contains("\n\n"))
                {
                    foundEnd = true;
                    break; // Complete response received
                }
                
                // Also check if we got a status line (RTSP/1.0 XXX)
                if (responseSoFar.Contains("RTSP/1.0") && responseSoFar.Length > 20)
                {
                    // Might have complete response even without \r\n\r\n
                    foundEnd = true;
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout - check if we have partial response
                if (responseBuffer.Count > 0)
                {
                    Console.WriteLine("Timeout waiting for complete RTSP response, using partial response");
                    break;
                }
                else
                {
                    // No response at all - device might not be responding
                    Console.WriteLine("No RTSP response received (timeout)");
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading RTSP response: {ex.Message}");
                break;
            }
        }
        
        var response = Encoding.UTF8.GetString(responseBuffer.ToArray());
        
        if (!string.IsNullOrEmpty(response))
        {
            Console.WriteLine($"RTSP Response ({totalRead} bytes):");
            // Show first few lines of response
            var responseLines = response.Split('\n').Take(10);
            foreach (var line in responseLines)
            {
                Console.WriteLine($"  {line.Trim()}");
            }
        }
        else
        {
            Console.WriteLine("No RTSP response received");
        }
        
        return response;
    }
    
    /// <summary>
    /// Parse RTSP response status
    /// </summary>
    private bool ParseResponseStatus(string response, out int statusCode, out Dictionary<string, string> responseHeaders)
    {
        statusCode = 0;
        responseHeaders = new Dictionary<string, string>();
        
        var lines = response.Split('\n');
        if (lines.Length == 0)
        {
            return false;
        }
        
        // Parse status line: RTSP/1.0 200 OK
        var statusMatch = Regex.Match(lines[0], @"RTSP/1\.0\s+(\d+)");
        if (statusMatch.Success)
        {
            statusCode = int.Parse(statusMatch.Groups[1].Value);
        }
        
        // Parse headers
        bool inHeaders = true;
        foreach (var line in lines.Skip(1))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                inHeaders = false;
                continue;
            }
            
            if (inHeaders)
            {
                var colonIndex = trimmed.IndexOf(':');
                if (colonIndex > 0)
                {
                    var key = trimmed.Substring(0, colonIndex).Trim();
                    var value = trimmed.Substring(colonIndex + 1).Trim();
                    responseHeaders[key] = value;
                }
            }
        }
        
        return statusCode > 0;
    }
    
    /// <summary>
    /// Send OPTIONS request
    /// </summary>
    public async Task<bool> OptionsAsync()
    {
        try
        {
            var response = await SendRtspRequestAsync("OPTIONS", "*");
            if (ParseResponseStatus(response, out int statusCode, out var headers))
            {
                return statusCode == 200;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OPTIONS failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Send ANNOUNCE request with video URL
    /// For URL-based playback, AirPlay might not need SDP - try without first
    /// </summary>
    public async Task<bool> AnnounceAsync(string videoUrl)
    {
        try
        {
            // For devices that support Video HTTP Live Streams, we might not need ANNOUNCE
            // But let's try it anyway
            
            // Try ANNOUNCE without SDP first (some AirPlay implementations accept this for URLs)
            var headers = new Dictionary<string, string>();
            
            // Try with Content-Location header instead of SDP body
            headers["Content-Location"] = videoUrl;
            
            var response = await SendRtspRequestAsync("ANNOUNCE", "*", headers);
            if (ParseResponseStatus(response, out int statusCode, out var responseHeaders))
            {
                if (statusCode == 200)
                {
                    Console.WriteLine("ANNOUNCE succeeded with Content-Location header");
                    return true;
                }
            }
            
            // If that didn't work, try with SDP
            // For devices supporting HTTP Live Streams, use appropriate SDP
            headers.Clear();
            headers["Content-Type"] = "application/sdp";
            
            // SDP for URL-based playback (HTTP Live Streams)
            var sdpBody = $"v=0\r\n" +
                         $"o=- 0 0 IN IP4 0.0.0.0\r\n" +
                         $"s=AirPlay\r\n" +
                         $"c=IN IP4 0.0.0.0\r\n" +
                         $"t=0 0\r\n" +
                         $"a=control:*\r\n" +
                         $"a=x-apple-streaming:url={videoUrl}\r\n";
            
            // Add video media description if device supports video
            if (_deviceInfo == null || _deviceInfo.SupportsVideo)
            {
                sdpBody += $"m=video 0 RTP/AVP 96\r\n" +
                          $"a=rtpmap:96 H264/90000\r\n";
            }
            
            response = await SendRtspRequestAsync("ANNOUNCE", "*", headers, sdpBody);
            if (ParseResponseStatus(response, out statusCode, out responseHeaders))
            {
                return statusCode == 200;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ANNOUNCE failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Send SETUP request to set up transport (HTTP Live Streams mode)
    /// </summary>
    public async Task<bool> SetupAsync(string controlUrl)
    {
        try
        {
            // HTTP Live Streams approach: Try SETUP with Content-Location header
            var setupOptions = new List<object>();
            
            // Only use HTTP Live Streams approach
            if (_deviceInfo != null && _deviceInfo.SupportsVideoHTTPLiveStreams && !string.IsNullOrEmpty(_videoUrl))
            {
                // Try SETUP with Content-Location header for HTTP Live Streams
                // This is the primary approach for HTTP Live Streams
                setupOptions.Add(new { Url = "*", Transport = (string?)null, ContentLocation = _videoUrl });
                setupOptions.Add(new { Url = controlUrl, Transport = (string?)null, ContentLocation = _videoUrl });
                
                // Also try with transport options but still include Content-Location
                setupOptions.Add(new { Url = "*", Transport = "RTP/AVP/TCP;unicast;interleaved=0-1", ContentLocation = _videoUrl });
                setupOptions.Add(new { Url = controlUrl, Transport = "RTP/AVP/TCP;unicast;interleaved=0-1", ContentLocation = _videoUrl });
            }
            else
            {
                // Fallback if device info not available
                setupOptions.Add(new { Url = controlUrl, Transport = (string?)null, ContentLocation = (string?)null });
                setupOptions.Add(new { Url = "*", Transport = (string?)null, ContentLocation = (string?)null });
            }
            
            foreach (var option in setupOptions)
            {
                try
                {
                    // Use reflection to get properties (since we have different option types)
                    var url = option.GetType().GetProperty("Url")?.GetValue(option)?.ToString() ?? "*";
                    var transport = option.GetType().GetProperty("Transport")?.GetValue(option)?.ToString();
                    var contentLocation = option.GetType().GetProperty("ContentLocation")?.GetValue(option)?.ToString();
                    
                    var headers = new Dictionary<string, string>();
                    if (transport != null)
                    {
                        headers["Transport"] = transport;
                    }
                    
                    // For HTTP Live Streams, try Content-Location header
                    if (contentLocation != null && !string.IsNullOrEmpty(contentLocation) && _deviceInfo != null && _deviceInfo.SupportsVideoHTTPLiveStreams)
                    {
                        // Try with Content-Location in SETUP (some devices accept this)
                        headers["Content-Location"] = contentLocation;
                    }
                    
                    var response = await SendRtspRequestAsync("SETUP", url, headers);
                    if (ParseResponseStatus(response, out int statusCode, out var responseHeaders))
                    {
                        // Extract session ID from response
                        if (responseHeaders.TryGetValue("Session", out var session))
                        {
                            var sessionMatch = Regex.Match(session, @"(\w+)");
                            if (sessionMatch.Success)
                            {
                                _sessionId = sessionMatch.Groups[1].Value;
                                Console.WriteLine($"RTSP Session ID: {_sessionId}");
                            }
                        }
                        
                        if (statusCode == 200)
                        {
                            Console.WriteLine($"SETUP succeeded with URL: {url}, Transport: {transport ?? "none"}");
                            return true;
                        }
                        else
                        {
                            Console.WriteLine($"SETUP returned status {statusCode} for URL: {url}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    var url = option.GetType().GetProperty("Url")?.GetValue(option)?.ToString() ?? "*";
                    Console.WriteLine($"SETUP attempt failed for {url}: {ex.Message}");
                    // Continue to next option
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SETUP failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Send PLAY request to start playback
    /// </summary>
    public async Task<bool> PlayAsync(string controlUrl, double? startPosition = null)
    {
        try
        {
            var headers = new Dictionary<string, string>();
            
            if (startPosition.HasValue)
            {
                headers["Range"] = $"npt={startPosition.Value}-";
            }
            
            var response = await SendRtspRequestAsync("PLAY", controlUrl, headers);
            if (ParseResponseStatus(response, out int statusCode, out var responseHeaders))
            {
                return statusCode == 200;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PLAY failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Send TEARDOWN request to stop playback
    /// </summary>
    public async Task<bool> TeardownAsync(string controlUrl)
    {
        try
        {
            var response = await SendRtspRequestAsync("TEARDOWN", controlUrl);
            if (ParseResponseStatus(response, out int statusCode, out var responseHeaders))
            {
                return statusCode == 200;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TEARDOWN failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Complete RTSP setup and play flow for video URL
    /// </summary>
    public async Task<bool> PlayVideoAsync(string videoUrl, double? startPosition = null)
    {
        // Store video URL for use in SETUP
        _videoUrl = videoUrl;
        
        // Only proceed if device supports HTTP Live Streams
        if (_deviceInfo == null || !_deviceInfo.SupportsVideoHTTPLiveStreams)
        {
            Console.WriteLine("⚠ Device does not support HTTP Live Streams - this method requires it");
            return false;
        }
        
        try
        {
            Console.WriteLine("\n=== Starting RTSP Video Playback (HTTP Live Streams Mode) ===");
            
            // Step 1: OPTIONS
            Console.WriteLine("\n1. Sending OPTIONS...");
            if (!await OptionsAsync())
            {
                Console.WriteLine("OPTIONS failed - continuing anyway...");
            }
            
            // Step 2: Skip ANNOUNCE for HTTP Live Streams (not required)
            Console.WriteLine("\n2. Skipping ANNOUNCE (not required for HTTP Live Streams)");
            
            // Step 3: SETUP with Content-Location header for HTTP Live Streams
            Console.WriteLine("\n3. Sending SETUP with Content-Location header (HTTP Live Streams)...");
            bool setupSuccess = false;
            
            // For HTTP Live Streams, try SETUP with video URL and Content-Location header
            var setupUrls = new[] { videoUrl, "*", "/stream" };
            
            foreach (var setupUrl in setupUrls)
            {
                try
                {
                    if (await SetupAsync(setupUrl))
                    {
                        setupSuccess = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SETUP failed for {setupUrl}: {ex.Message}");
                    // If connection was reset, try to reconnect
                    if (ex.Message.Contains("reset") || ex.Message.Contains("Broken pipe"))
                    {
                        Console.WriteLine("Connection was reset during SETUP - reconnecting...");
                        try
                        {
                            if (_tcpClient != null)
                            {
                                try { _tcpClient.Close(); } catch { }
                                _tcpClient = null;
                                _stream = null;
                            }
                            
                            _tcpClient = new TcpClient();
                            await _tcpClient.ConnectAsync(_deviceIp, _port);
                            _stream = _tcpClient.GetStream();
                            Console.WriteLine("Reconnected - retrying SETUP...");
                            // Reset CSeq for new connection
                            _cseq = 1;
                            // Retry OPTIONS to re-establish
                            await OptionsAsync();
                        }
                        catch (Exception reconnectEx)
                        {
                            Console.WriteLine($"Reconnection failed: {reconnectEx.Message}");
                            return false;
                        }
                    }
                }
            }
            
            if (!setupSuccess)
            {
                Console.WriteLine("SETUP failed - device may require different approach");
                // Try playing anyway - some devices work without explicit SETUP
            }
            
            // Step 4: PLAY
            Console.WriteLine("\n4. Sending PLAY...");
            bool playSuccess = false;
            
            // Try different control URLs for PLAY
            if (await PlayAsync(videoUrl, startPosition))
            {
                playSuccess = true;
            }
            else if (await PlayAsync("*", startPosition))
            {
                playSuccess = true;
            }
            else if (await PlayAsync("/stream", startPosition))
            {
                playSuccess = true;
            }
            
            if (playSuccess)
            {
                Console.WriteLine("\n✓ RTSP video playback started successfully!");
                Console.WriteLine("Video should now be playing on the device.");
                return true;
            }
            else
            {
                Console.WriteLine("\nPLAY command failed - device may have different RTSP requirements");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RTSP playback error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsStream)
            {
                _stream?.Close();
            }
            _tcpClient?.Close();
            _disposed = true;
        }
    }
}

