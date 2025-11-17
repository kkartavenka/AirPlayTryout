using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace AirPlayVideoStreaming;

/// <summary>
/// Streams screen capture to AirPlay device via RTSP with H.264 encoding
/// </summary>
public class ScreenStreamer : IDisposable
{
    private readonly string _deviceIp;
    private readonly int _port;
    private readonly AirPlayDeviceInfo? _deviceInfo;
    private readonly ScreenCapture _screenCapture;
    private RtspClient? _rtspClient;
    private HttpClient? _httpClient;
    private HttpResponseMessage? _reverseResponse; // Keep response alive to prevent stream closure
    private bool _isStreaming = false;
    private CancellationTokenSource? _streamingCts;
    private Task? _streamingTask;
    private int _frameCount = 0;
    private DateTime _streamStartTime;
    private int _targetFps = 30;
    private int _targetWidth = 1920;
    private int _targetHeight = 1080;
    
    // Statistics
    private long _totalFramesSent = 0;
    private long _totalBytesSent = 0;
    private DateTime _lastStatsTime;
    
    public ScreenStreamer(string deviceIp, int port, AirPlayDeviceInfo? deviceInfo = null)
    {
        _deviceIp = deviceIp;
        _port = port;
        _deviceInfo = deviceInfo;
        _screenCapture = new ScreenCapture();
        _screenCapture.CaptureIntervalMs = 1000 / _targetFps; // ~30 FPS
        _lastStatsTime = DateTime.Now;
        
        // Initialize HTTP client for /reverse endpoint
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "MediaControl/1.0");
    }
    
    /// <summary>
    /// Start streaming screen to device
    /// </summary>
    public async Task<bool> StartStreamingAsync()
    {
        if (_isStreaming)
        {
            Console.WriteLine("Screen streaming is already active");
            return true;
        }
        
        Console.WriteLine("\n=== Starting Screen Streaming ===");
        Console.WriteLine($"Target: {_deviceIp}:{_port}");
        Console.WriteLine($"Resolution: {_targetWidth}x{_targetHeight}");
        Console.WriteLine($"Target FPS: {_targetFps}");
        
        // Check if device requires authentication
        if (_deviceInfo != null && (_deviceInfo.RequiresPassword || _deviceInfo.RequiresAuth))
        {
            Console.WriteLine("\n⚠ WARNING: This device requires authentication/pairing for screen mirroring");
            Console.WriteLine("   The connection will likely fail without pairing.");
            Console.WriteLine("   Consider pairing first (option 10) for best results.");
            Console.WriteLine("   Continuing anyway...\n");
        }
        
        try
        {
            // Step 1: Establish RTSP connection via /reverse endpoint
            Console.WriteLine("\n1. Establishing RTSP connection via /reverse endpoint...");
            Console.WriteLine("   This upgrades HTTP to RTSP protocol...");
            
            // First, try to establish connection via /reverse endpoint (like video playback)
            bool reverseSuccess = await EstablishReverseConnectionAsync();
            
            if (!reverseSuccess)
            {
                Console.WriteLine("⚠ /reverse endpoint failed, trying direct RTSP connection...");
                // Fallback to direct RTSP connection
                _rtspClient = new RtspClient(_deviceIp, _port);
                
                if (_deviceInfo != null)
                {
                    _rtspClient.SetDeviceInfo(_deviceInfo);
                    if (!string.IsNullOrEmpty(_deviceInfo.DeviceId))
                    {
                        var deviceId = _deviceInfo.DeviceId.Replace(":", "").Replace("-", "");
                        if (deviceId.Length > 16) deviceId = deviceId.Substring(0, 16);
                        _rtspClient.SetDeviceId(deviceId, Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16));
                    }
                }
                
                if (!await _rtspClient.ConnectAsync())
                {
                    Console.WriteLine("✗ Failed to connect RTSP");
                    return false;
                }
            }
            
            Console.WriteLine("✓ RTSP connection established");
            
            // Step 2: Send OPTIONS
            /*Console.WriteLine("\n2. Sending RTSP OPTIONS...");
            if (_rtspClient != null && await _rtspClient.OptionsAsync())
            {
                Console.WriteLine("✓ OPTIONS successful");
            }
            else
            {
                Console.WriteLine("⚠ OPTIONS failed, continuing anyway...");
            } */
            
            // Step 3: Check stream status before proceeding
            Console.WriteLine("\n3. Checking stream status...");
            var streamCheck = _rtspClient?.GetStream();
            if (streamCheck == null || !streamCheck.CanRead || !streamCheck.CanWrite)
            {
                Console.WriteLine("⚠ Stream is not valid - connection may have been closed");
                Console.WriteLine("   Attempting to reconnect...");
                // Try direct connection
                _rtspClient?.Dispose();
                _reverseResponse?.Dispose();
                _reverseResponse = null;
                
                _rtspClient = new RtspClient(_deviceIp, _port);
                if (_deviceInfo != null)
                {
                    _rtspClient.SetDeviceInfo(_deviceInfo);
                }
                if (!await _rtspClient.ConnectAsync())
                {
                    Console.WriteLine("✗ Failed to reconnect");
                    return false;
                }
                Console.WriteLine("✓ Reconnected successfully");
            }
            else
            {
                Console.WriteLine($"✓ Stream is valid (readable: {streamCheck.CanRead}, writable: {streamCheck.CanWrite})");
            }
            
            // Step 4: Send OPTIONS on the /reverse connection
            Console.WriteLine("\n4. Sending RTSP OPTIONS on /reverse connection...");
            if (_rtspClient != null)
            {
                bool optionsSuccess = await _rtspClient.OptionsAsync();
                if (optionsSuccess)
                {
                    Console.WriteLine("✓ OPTIONS successful");
                }
                else
                {
                    Console.WriteLine("⚠ OPTIONS failed, but continuing...");
                }
                
                // Check stream after OPTIONS
                streamCheck = _rtspClient?.GetStream();
                if (streamCheck == null || !streamCheck.CanRead || !streamCheck.CanWrite)
                {
                    Console.WriteLine("⚠ Stream closed after OPTIONS - reconnecting...");
                    _rtspClient?.Dispose();
                    _reverseResponse?.Dispose();
                    _reverseResponse = null;
                    
                    _rtspClient = new RtspClient(_deviceIp, _port);
                    if (_deviceInfo != null)
                    {
                        _rtspClient.SetDeviceInfo(_deviceInfo);
                    }
                    await _rtspClient.ConnectAsync();
                    await _rtspClient.OptionsAsync();
                }
            }
            
            // Step 5: Try SETUP, but if it fails, try sending frames directly
            Console.WriteLine("\n5. Attempting RTSP SETUP...");
            Console.WriteLine("   Note: Some devices allow screen mirroring without explicit SETUP");
            bool setupSuccess = false;
            int setupRetries = 3;
            
            for (int i = 0; i < setupRetries; i++)
            {
                if (i > 0)
                {
                    Console.WriteLine($"   Retry {i}/{setupRetries - 1}...");
                    await Task.Delay(500); // Brief delay before retry
                }
                
                // Check stream before each SETUP attempt
                streamCheck = _rtspClient?.GetStream();
                if (streamCheck == null || !streamCheck.CanRead || !streamCheck.CanWrite)
                {
                    Console.WriteLine($"   Stream invalid before SETUP attempt {i + 1} - reconnecting...");
                    _rtspClient?.Dispose();
                    _reverseResponse?.Dispose();
                    _reverseResponse = null;
                    
                    // Try /reverse again for fresh connection
                    bool reconnectSuccess = await EstablishReverseConnectionAsync();
                    if (!reconnectSuccess)
                    {
                        // Fallback to direct connection
                        _rtspClient = new RtspClient(_deviceIp, _port);
                        if (_deviceInfo != null)
                        {
                            _rtspClient.SetDeviceInfo(_deviceInfo);
                        }
                        await _rtspClient.ConnectAsync();
                    }
                }
                
                setupSuccess = await SetupScreenStreamAsync();
                
                if (setupSuccess)
                {
                    Console.WriteLine("✓ SETUP successful");
                    break;
                }
                else
                {
                    Console.WriteLine($"   SETUP attempt {i + 1} failed");
                    
                    // If connection was reset, try to reconnect
                    if (i < setupRetries - 1)
                    {
                        Console.WriteLine("   Connection may have been reset - reconnecting...");
                        try
                        {
                            // Check if stream is still valid
                            var stream = _rtspClient?.GetStream();
                            if (stream != null && stream.CanRead && stream.CanWrite)
                            {
                                Console.WriteLine("   Stream appears to still be valid - retrying SETUP...");
                                // Stream is still good, just retry
                                continue;
                            }
                            
                            // Stream is closed, need to reconnect
                            Console.WriteLine("   Stream is closed - establishing new connection...");
                            
                            // Dispose old client
                            if (_rtspClient != null)
                            {
                                _rtspClient.Dispose();
                                _rtspClient = null;
                            }
                            
                            // Dispose old reverse response if exists
                            if (_reverseResponse != null)
                            {
                                _reverseResponse.Dispose();
                                _reverseResponse = null;
                            }
                            
                            // Don't try /reverse again if we already had a session (it will return 454)
                            // Use direct RTSP connection instead
                            Console.WriteLine("   Using direct RTSP connection (avoiding /reverse to prevent 454)...");
                            _rtspClient = new RtspClient(_deviceIp, _port);
                            
                            if (_deviceInfo != null)
                            {
                                _rtspClient.SetDeviceInfo(_deviceInfo);
                                if (!string.IsNullOrEmpty(_deviceInfo.DeviceId))
                                {
                                    var deviceId = _deviceInfo.DeviceId.Replace(":", "").Replace("-", "");
                                    if (deviceId.Length > 16) deviceId = deviceId.Substring(0, 16);
                                    _rtspClient.SetDeviceId(deviceId, Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16));
                                }
                            }
                            
                            if (await _rtspClient.ConnectAsync())
                            {
                                await _rtspClient.OptionsAsync();
                                Console.WriteLine("   Reconnected successfully");
                            }
                            else
                            {
                                Console.WriteLine("   Reconnection failed");
                            }
                        }
                        catch (Exception reconnectEx)
                        {
                            Console.WriteLine($"   Reconnection error: {reconnectEx.Message}");
                            if (reconnectEx.InnerException != null)
                            {
                                Console.WriteLine($"   Inner exception: {reconnectEx.InnerException.Message}");
                            }
                        }
                    }
                }
            }
            
            if (!setupSuccess)
            {
                Console.WriteLine("⚠ SETUP failed after all retries");
                
                // Check if stream is still valid
                streamCheck = _rtspClient?.GetStream();
                if (streamCheck == null || !streamCheck.CanRead || !streamCheck.CanWrite)
                {
                    Console.WriteLine("\n✗ Stream is closed - cannot proceed with screen mirroring");
                    Console.WriteLine("   The device is closing connections, which strongly suggests:");
                    Console.WriteLine("   1. Screen mirroring requires authentication/pairing");
                    Console.WriteLine("   2. The device may not support unauthenticated screen mirroring");
                    Console.WriteLine("\n   Recommendation: Try pairing with the device first (option 10)");
                    Console.WriteLine("   Then attempt screen mirroring again.");
                    return false;
                }
                
                // Stream is still valid, but SETUP failed - try direct frame streaming
                Console.WriteLine("   Stream is still valid - trying alternative approach:");
                Console.WriteLine("   Sending frames directly without SETUP/PLAY");
                Console.WriteLine("   Note: This may still fail if authentication is required");
                
                // Verify stream one more time before proceeding
                await Task.Delay(100); // Brief delay
                streamCheck = _rtspClient?.GetStream();
                if (streamCheck == null || !streamCheck.CanRead || !streamCheck.CanWrite)
                {
                    Console.WriteLine("\n✗ Stream closed before starting frame streaming");
                    Console.WriteLine("   This device requires authentication for screen mirroring");
                    Console.WriteLine("   Please pair with the device first (option 10)");
                    return false;
                }
            }
            else
            {
                // Step 6: PLAY (only if SETUP succeeded)
                Console.WriteLine("\n6. Sending RTSP PLAY...");
                if (await _rtspClient!.PlayAsync("*"))
                {
                    Console.WriteLine("✓ PLAY successful");
                }
                else
                {
                    Console.WriteLine("⚠ PLAY failed, but starting stream anyway...");
                }
            }
            
            // Step 7: Start screen capture and streaming
            Console.WriteLine("\n7. Starting screen capture and streaming...");
            _isStreaming = true;
            _streamStartTime = DateTime.Now;
            _streamingCts = new CancellationTokenSource();
            _screenCapture.StartCapture();
            
            // Start streaming task
            _streamingTask = Task.Run(() => StreamingLoop(_streamingCts.Token));
            
            Console.WriteLine("\n✓ Screen streaming started!");
            Console.WriteLine("Press Enter to stop streaming...");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to start streaming: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }
    
    /// <summary>
    /// Stop streaming
    /// </summary>
    public async Task StopStreamingAsync()
    {
        if (!_isStreaming)
        {
            return;
        }
        
        Console.WriteLine("\n=== Stopping Screen Streaming ===");
        
        _isStreaming = false;
        _screenCapture.StopCapture();
        _streamingCts?.Cancel();
        
        if (_streamingTask != null)
        {
            try
            {
                await _streamingTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping streaming task: {ex.Message}");
            }
        }
        
        // Send TEARDOWN
        if (_rtspClient != null)
        {
            try
            {
                Console.WriteLine("Sending RTSP TEARDOWN...");
                await _rtspClient.TeardownAsync("*");
                Console.WriteLine("✓ TEARDOWN sent");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TEARDOWN error: {ex.Message}");
            }
        }
        
        PrintStatistics();
        Console.WriteLine("✓ Screen streaming stopped");
    }
    
    /// <summary>
    /// Establish RTSP connection via /reverse endpoint (HTTP upgrade to RTSP)
    /// </summary>
    private async Task<bool> EstablishReverseConnectionAsync()
    {
        try
        {
            Console.WriteLine("   Sending HTTP POST to /reverse endpoint...");
            
            // Create binary plist for /reverse request
            var plistDict = new Dictionary<string, object>
            {
                ["Content-Location"] = "screenmirror"
            };
            byte[] plistBytes = BinaryPlistHelper.Write(plistDict);
            
            var url = $"http://{_deviceIp}:{_port}/reverse";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            // AirPlay headers
            if (_deviceInfo != null && !string.IsNullOrEmpty(_deviceInfo.DeviceId))
            {
                var deviceId = _deviceInfo.DeviceId.Replace(":", "").Replace("-", "");
                if (deviceId.Length > 16) deviceId = deviceId.Substring(0, 16);
                request.Headers.Add("X-Apple-Device-ID", deviceId);
            }
            else
            {
                request.Headers.Add("X-Apple-Device-ID", Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16));
            }
            
            request.Headers.Add("X-Apple-Session-ID", Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16));
            request.Headers.Add("Upgrade", "PTTH/1.0");
            request.Headers.Add("Connection", "Upgrade");
            
            request.Content = new ByteArrayContent(plistBytes);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-apple-binary-plist");
            request.Content.Headers.ContentLength = plistBytes.Length;
            
            Console.WriteLine($"   Request size: {plistBytes.Length} bytes");
            
            var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            Console.WriteLine($"   Response status: {response.StatusCode}");
            
            if (response.StatusCode == HttpStatusCode.SwitchingProtocols)
            {
                Console.WriteLine("✓ Received 101 Switching Protocols - RTSP upgrade successful!");
                
                // IMPORTANT: Keep the response alive to prevent stream closure
                // The stream will be closed if the HttpResponseMessage is disposed
                _reverseResponse = response;
                
                // Get the underlying stream from the response
                // Note: We must keep the response object alive for the stream to remain open
                var responseStream = await response.Content.ReadAsStreamAsync();
                
                // Verify stream is readable
                if (!responseStream.CanRead)
                {
                    Console.WriteLine("⚠ Stream is not readable");
                    return false;
                }
                
                Console.WriteLine($"   Stream is readable: {responseStream.CanRead}, writable: {responseStream.CanWrite}");
                
                // Create RTSP client and set the upgraded stream
                _rtspClient = new RtspClient(_deviceIp, _port);
                
                if (_deviceInfo != null)
                {
                    _rtspClient.SetDeviceInfo(_deviceInfo);
                    if (!string.IsNullOrEmpty(_deviceInfo.DeviceId))
                    {
                        var deviceId = _deviceInfo.DeviceId.Replace(":", "").Replace("-", "");
                        if (deviceId.Length > 16) deviceId = deviceId.Substring(0, 16);
                        _rtspClient.SetDeviceId(deviceId, Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16));
                    }
                }
                
                // Use the upgraded stream
                _rtspClient.SetStream(responseStream);
                
                return true;
            }
            else
            {
                Console.WriteLine($"⚠ /reverse returned {response.StatusCode} (expected 101 Switching Protocols)");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Error establishing reverse connection: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
            return false;
        }
    }
    
    /// <summary>
    /// ANNOUNCE with SDP for screen mirroring
    /// </summary>
    private async Task<bool> AnnounceScreenMirroringAsync()
    {
        if (_rtspClient == null) return false;
        
        // Check if stream is still valid
        var stream = _rtspClient.GetStream();
        if (stream == null || !stream.CanRead || !stream.CanWrite)
        {
            Console.WriteLine("   Stream is not available for ANNOUNCE (closed or invalid)");
            return false;
        }
        
        try
        {
            // Create SDP for screen mirroring with H.264
            var sdp = CreateScreenMirroringSDP();
            
            var headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/sdp"
            };
            
            Console.WriteLine("   Sending ANNOUNCE request...");
            var response = await _rtspClient.SendRtspRequestPublicAsync("ANNOUNCE", "*", headers, sdp);
            
            // Check stream status immediately after sending
            stream = _rtspClient.GetStream();
            if (stream == null || !stream.CanRead || !stream.CanWrite)
            {
                Console.WriteLine("   ⚠ Stream was closed immediately after ANNOUNCE");
                return false;
            }
            
            if (!string.IsNullOrEmpty(response))
            {
                if (_rtspClient.ParseResponseStatusPublic(response, out int statusCode, out var responseHeaders))
                {
                    Console.WriteLine($"   ANNOUNCE response: {statusCode}");
                    if (statusCode == 200)
                    {
                        Console.WriteLine("✓ ANNOUNCE successful");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"   ANNOUNCE returned status {statusCode}");
                        // Check if stream is still valid after error response
                        stream = _rtspClient.GetStream();
                        if (stream == null || !stream.CanRead || !stream.CanWrite)
                        {
                            Console.WriteLine("   ⚠ Stream was closed after error response");
                        }
                        return false;
                    }
                }
            }
            else
            {
                Console.WriteLine("   No response received for ANNOUNCE");
                // Check stream status
                stream = _rtspClient.GetStream();
                if (stream == null || !stream.CanRead || !stream.CanWrite)
                {
                    Console.WriteLine("   ⚠ Stream was closed (no response received)");
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ANNOUNCE error: {ex.Message}");
            // Check stream status after exception
            var streamAfterError = _rtspClient.GetStream();
            if (streamAfterError == null || !streamAfterError.CanRead || !streamAfterError.CanWrite)
            {
                Console.WriteLine("   ⚠ Stream was closed due to error");
            }
            return false;
        }
    }
    
    /// <summary>
    /// Create SDP for screen mirroring
    /// </summary>
    private string CreateScreenMirroringSDP()
    {
        var sdp = new StringBuilder();
        sdp.AppendLine("v=0");
        sdp.AppendLine($"o=- 0 0 IN IP4 {_deviceIp}");
        sdp.AppendLine("s=AirPlay Screen Mirroring");
        sdp.AppendLine($"c=IN IP4 {_deviceIp}");
        sdp.AppendLine("t=0 0");
        sdp.AppendLine("a=control:*");
        sdp.AppendLine("a=type:broadcast");
        sdp.AppendLine($"m=video 0 RTP/AVP 96");
        sdp.AppendLine("a=rtpmap:96 H264/90000");
        // H.264 profile: baseline profile level 3.0
        // sprop-parameter-sets: SPS and PPS in base64
        sdp.AppendLine("a=fmtp:96 packetization-mode=1;profile-level-id=42001e;sprop-parameter-sets=Z0IAHpWoKA9puAgICBA=,aM48gA==;");
        sdp.AppendLine("a=control:trackID=0");
        
        var sdpString = sdp.ToString();
        Console.WriteLine("   SDP Content:");
        foreach (var line in sdpString.Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                Console.WriteLine($"     {line.Trim()}");
            }
        }
        
        return sdpString;
    }
    
    /// <summary>
    /// SETUP for screen streaming - tries multiple URL and transport options
    /// </summary>
    private async Task<bool> SetupScreenStreamAsync()
    {
        if (_rtspClient == null) return false;
        
        // Check if stream is still valid
        var stream = _rtspClient.GetStream();
        if (stream == null || !stream.CanRead || !stream.CanWrite)
        {
            Console.WriteLine("   Stream is not available for SETUP (closed or invalid)");
            return false;
        }
        
        // Try different SETUP URLs and transport options
        var setupUrls = new[] { "/stream", "trackID=0", "*", "/video" };
        var transportOptions = new[]
        {
            "RTP/AVP/TCP;unicast;interleaved=0-1",
            "RTP/AVP/TCP;unicast;interleaved=0",
            "RTP/AVP/UDP;unicast;client_port=5000-5001",
            "RTP/AVP/TCP;unicast"
        };
        
        foreach (var setupUrl in setupUrls)
        {
            foreach (var transport in transportOptions)
            {
                try
                {
                    Console.WriteLine($"   Trying SETUP {setupUrl} with Transport: {transport}...");
                    
                    var headers = new Dictionary<string, string>
                    {
                        ["Transport"] = transport
                    };
                    
                    var response = await _rtspClient.SendRtspRequestPublicAsync("SETUP", setupUrl, headers);
                    
                    // Check stream immediately after SETUP
                    stream = _rtspClient.GetStream();
                    if (stream == null || !stream.CanRead || !stream.CanWrite)
                    {
                        Console.WriteLine($"   ⚠ Stream closed after SETUP {setupUrl}");
                        continue; // Try next option
                    }
                    
                    if (_rtspClient.ParseResponseStatusPublic(response, out int statusCode, out var responseHeaders))
                    {
                        Console.WriteLine($"   SETUP {setupUrl} response: {statusCode}");
                        
                        // Extract session ID
                        if (responseHeaders.TryGetValue("Session", out var session))
                        {
                            Console.WriteLine($"   Session ID: {session}");
                        }
                        
                        if (statusCode == 200)
                        {
                            Console.WriteLine($"✓ SETUP successful with {setupUrl} and {transport}");
                            return true;
                        }
                        else if (statusCode != 404 && statusCode != 454)
                        {
                            // Got a response but not success - might be close, try next
                            Console.WriteLine($"   Status {statusCode} - trying next option...");
                        }
                    }
                    else if (!string.IsNullOrEmpty(response))
                    {
                        Console.WriteLine($"   Got response but couldn't parse status");
                    }
                }
                catch (Exception ex)
                {
                    // Check if stream is still valid
                    stream = _rtspClient.GetStream();
                    if (stream == null || !stream.CanRead || !stream.CanWrite)
                    {
                        Console.WriteLine($"   ⚠ Stream closed: {ex.Message}");
                        return false; // Stream closed, can't continue
                    }
                    Console.WriteLine($"   SETUP {setupUrl} error: {ex.Message}");
                    // Continue to next option
                }
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Main streaming loop - captures and sends frames
    /// </summary>
    private async Task StreamingLoop(CancellationToken cancellationToken)
    {
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / _targetFps);
        var nextFrameTime = DateTime.Now;
        
        Console.WriteLine($"\n=== Streaming Loop Started ===");
        Console.WriteLine($"Frame interval: {frameInterval.TotalMilliseconds}ms ({_targetFps} FPS)");
        
        _screenCapture.FrameCaptured += async (sender, frame) =>
        {
            if (!_isStreaming || cancellationToken.IsCancellationRequested)
            {
                return;
            }
            
            try
            {
                var frameStartTime = DateTime.Now;
                _frameCount++;
                
                // Resize frame if needed
                var processedFrame = ProcessFrame(frame);
                
                // Encode frame as JPEG (for now - H.264 encoding would be better)
                var frameData = await EncodeFrameAsync(processedFrame);
                
                // Send frame via RTSP interleaved data
                await SendFrameDataAsync(frameData);
                
                _totalFramesSent++;
                _totalBytesSent += frameData.Length;
                
                // Log every 30 frames (~1 second at 30 FPS)
                if (_frameCount % 30 == 0)
                {
                    var elapsed = DateTime.Now - _streamStartTime;
                    var fps = elapsed.TotalSeconds > 0 ? _totalFramesSent / elapsed.TotalSeconds : 0;
                    var bps = elapsed.TotalSeconds > 0 ? _totalBytesSent / elapsed.TotalSeconds : 0;
                    
                    Console.WriteLine($"[Frame {_frameCount}] Processed - Original: {frame.Width}x{frame.Height}, "
                        + $"Processed: {processedFrame.Width}x{processedFrame.Height}, "
                        + $"Encoded: {frameData.Length} bytes, "
                        + $"FPS: {fps:F1}, Bitrate: {bps / 1024 / 1024 * 8:F1} Mbps");
                }
                
                // Dispose processed frame
                processedFrame?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing frame {_frameCount}: {ex.Message}");
            }
        };
        
        // Keep loop running
        while (_isStreaming && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(100, cancellationToken);
        }
        
        Console.WriteLine("\n=== Streaming Loop Stopped ===");
    }
    
    /// <summary>
    /// Process frame (resize, format conversion)
    /// </summary>
    private Image<Rgba32> ProcessFrame(Image<Rgba32> frame)
    {
        // Resize to target resolution if needed
        if (frame.Width != _targetWidth || frame.Height != _targetHeight)
        {
            var resized = frame.Clone();
            resized.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(_targetWidth, _targetHeight),
                Mode = ResizeMode.Max
            }));
            return resized;
        }
        
        return frame.Clone();
    }
    
    /// <summary>
    /// Encode frame as JPEG (temporary - should be H.264)
    /// </summary>
    private async Task<byte[]> EncodeFrameAsync(Image<Rgba32> frame)
    {
        using var ms = new MemoryStream();
        var encoder = new JpegEncoder { Quality = 85 };
        await frame.SaveAsync(ms, encoder);
        return ms.ToArray();
    }
    
    /// <summary>
    /// Send frame data via RTSP interleaved binary data
    /// RTSP interleaved binary format: $<channel><length_high><length_low><data>
    /// Channel 0 = RTP, Channel 1 = RTCP
    /// </summary>
    private async Task SendFrameDataAsync(byte[] frameData)
    {
        if (_rtspClient == null || _rtspClient.GetStream() == null)
        {
            // Stop streaming if stream is not available
            if (_frameCount > 0 && _frameCount % 10 == 0)
            {
                Console.WriteLine($"⚠ RTSP stream not available - stopping streaming after {_frameCount} frames");
                _isStreaming = false;
            }
            return;
        }
        
        try
        {
            var stream = _rtspClient.GetStream();
            if (stream == null)
            {
                if (_frameCount > 0 && _frameCount % 10 == 0)
                {
                    Console.WriteLine($"⚠ RTSP stream is null - stopping streaming after {_frameCount} frames");
                    _isStreaming = false;
                }
                return;
            }
            
            // Check if stream is still valid before sending
            if (!stream.CanWrite)
            {
                if (_frameCount > 0 && _frameCount % 10 == 0)
                {
                    Console.WriteLine($"⚠ Stream is not writable - connection may be closed");
                    Console.WriteLine($"   Stopping streaming after {_frameCount} frames");
                    _isStreaming = false;
                }
                return;
            }
            
            // RTSP interleaved binary data format:
            // $<channel><length_high><length_low><data>
            // Channel 0 = RTP video data, Channel 1 = RTCP control
            
            // For screen mirroring, we send JPEG frames as RTP payload
            // In a full implementation, this would be H.264 NAL units in RTP packets
            
            // Format: $ + channel (1 byte) + length (2 bytes, big-endian) + data
            var packet = new List<byte>();
            packet.Add((byte)'$');  // Magic byte
            packet.Add(0);          // Channel 0 (RTP)
            
            // Length as 2-byte big-endian
            ushort length = (ushort)frameData.Length;
            packet.Add((byte)((length >> 8) & 0xFF));  // High byte
            packet.Add((byte)(length & 0xFF));          // Low byte
            
            // Frame data
            packet.AddRange(frameData);
            
            var packetBytes = packet.ToArray();
            await stream.WriteAsync(packetBytes, 0, packetBytes.Length);
            await stream.FlushAsync();
            
            // Log every 30 frames
            if (_frameCount % 30 == 0)
            {
                Console.WriteLine($"[Frame {_frameCount}] Sent {frameData.Length} bytes via RTSP interleaved (packet size: {packetBytes.Length} bytes)");
            }
        }
        catch (Exception ex)
        {
            // If we get broken pipe errors repeatedly, stop streaming
            if (ex.Message.Contains("Broken pipe") || ex.Message.Contains("closed"))
            {
                if (_frameCount <= 5)
                {
                    // First few errors - just log
                    Console.WriteLine($"[Frame {_frameCount}] Connection closed: {ex.Message}");
                }
                else
                {
                    // Multiple errors - stop streaming
                    Console.WriteLine($"\n⚠ Connection closed repeatedly - stopping screen streaming");
                    Console.WriteLine($"   Total frames attempted: {_frameCount}");
                    Console.WriteLine($"   This device likely requires authentication for screen mirroring");
                    Console.WriteLine($"   Try pairing first (option 10), then attempt screen mirroring again");
                    _isStreaming = false;
                    return;
                }
            }
            else
            {
                Console.WriteLine($"[Frame {_frameCount}] Error sending frame data: {ex.Message}");
            }
            
            // Don't throw - let the streaming loop handle it
        }
    }
    
    /// <summary>
    /// Print streaming statistics
    /// </summary>
    private void PrintStatistics()
    {
        var elapsed = DateTime.Now - _streamStartTime;
        var fps = elapsed.TotalSeconds > 0 ? _totalFramesSent / elapsed.TotalSeconds : 0;
        var bps = elapsed.TotalSeconds > 0 ? _totalBytesSent / elapsed.TotalSeconds : 0;
        
        Console.WriteLine("\n=== Streaming Statistics ===");
        Console.WriteLine($"Duration: {elapsed.TotalSeconds:F1} seconds");
        Console.WriteLine($"Total frames sent: {_totalFramesSent}");
        Console.WriteLine($"Total bytes sent: {_totalBytesSent / 1024 / 1024:F2} MB");
        Console.WriteLine($"Average FPS: {fps:F1}");
        Console.WriteLine($"Average bitrate: {bps / 1024 / 1024 * 8:F1} Mbps");
        Console.WriteLine("===========================");
    }
    
    public void Dispose()
    {
        _screenCapture?.Dispose();
        _rtspClient?.Dispose();
        _reverseResponse?.Dispose(); // Dispose response to clean up stream
        _httpClient?.Dispose();
        _streamingCts?.Dispose();
    }
}

