using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace AirPlayVideoStreaming;

class Program
{
    private const string DeviceIp = "192.168.2.121";
    private const int DevicePort = 7000;
    
    static async Task Main(string[] args)
    {
        Console.WriteLine("AirPlay Video Streaming Client");
        Console.WriteLine($"Target Device: {DeviceIp}:{DevicePort}");
        Console.WriteLine();
        
        var streamer = new AirPlayVideoStreamer(DeviceIp, DevicePort);
        
        try
        {
            // Check if device is reachable
            Console.WriteLine("Checking device connectivity...");
            bool isReachable = await streamer.IsDeviceReachableAsync();
            
            if (!isReachable)
            {
                Console.WriteLine($"Device {DeviceIp}:{DevicePort} is not reachable.");
                Console.WriteLine("Please ensure:");
                Console.WriteLine("1. The device is on the same network");
                Console.WriteLine("2. AirPlay is enabled on the device");
                Console.WriteLine("3. The IP address is correct");
                return;
            }
            
            Console.WriteLine("Device is reachable!");
            Console.WriteLine();
            
            // Discover device via mDNS to get features and capabilities
            Console.WriteLine("Discovering device via mDNS...");
            var discoveredDeviceInfo = await streamer.DiscoverDeviceAsync();
            if (discoveredDeviceInfo != null)
            {
                discoveredDeviceInfo.PrintInfo();
                
                // Use device info to inform connection strategy
                if (discoveredDeviceInfo.RequiresAuth || discoveredDeviceInfo.RequiresPassword)
                {
                    Console.WriteLine("⚠ Device requires authentication/pairing.");
                    
                    // Offer to pair now
                    bool paired = await streamer.PairIfRequiredAsync();
                    
                    if (!paired)
                    {
                        Console.WriteLine("\nProceeding without pairing - will test connection...");
                        await streamer.TestConnectionAsync();
                    }
                }
                else
                {
                    // Test connection even if pairing not required
                    Console.WriteLine("\nTesting connection to device...");
                    await streamer.TestConnectionAsync();
                }
                
                // Check if device supports video
                if (!discoveredDeviceInfo.SupportsVideo && !discoveredDeviceInfo.SupportsScreen)
                {
                    Console.WriteLine("⚠ Warning: Device may not support video streaming.");
                }
            }
            else
            {
                Console.WriteLine("Could not discover device via mDNS (device may not advertise, or discovery timed out)");
                Console.WriteLine("Will proceed with direct connection...");
            }
            Console.WriteLine();
            
            // Check if pairing is required (fallback check)
            Console.WriteLine("Checking if pairing is required...");
            bool pairingRequired = await streamer.CheckPairingRequiredAsync();
            if (pairingRequired && discoveredDeviceInfo == null)
            {
                Console.WriteLine("⚠ Warning: Device may require pairing/authentication.");
                Console.WriteLine("If playback fails, you may need to pair with the device first.");
            }
            else if (!pairingRequired && discoveredDeviceInfo == null)
            {
                Console.WriteLine("Device does not appear to require pairing.");
            }
            Console.WriteLine();
            
            // Get device info via HTTP
            Console.WriteLine("Getting device information...");
            var deviceInfo = await streamer.GetDeviceInfoAsync();
            if (deviceInfo != null)
            {
                foreach (var info in deviceInfo)
                {
                    Console.WriteLine($"  {info.Key}: {info.Value}");
                }
            }
            Console.WriteLine();
            
            // Check available endpoints
            Console.WriteLine("Checking available endpoints...");
            var endpoints = await streamer.CheckAvailableEndpointsAsync();
            if (endpoints.Count > 0)
            {
                Console.WriteLine($"Found {endpoints.Count} available endpoint(s)");
            }
            else
            {
                Console.WriteLine("No common endpoints found (will try all during playback)");
            }
            Console.WriteLine();
            
            // Interactive menu
            while (true)
            {
                Console.WriteLine("Select an option:");
                Console.WriteLine("1. Play video from URL");
                Console.WriteLine("2. Play video from local file");
                Console.WriteLine("3. Stream video from URL");
                Console.WriteLine("4. Quick test with sample video URL");
                Console.WriteLine("5. Screen cast entire display (RTSP video stream)");
                Console.WriteLine("6. Stop playback");
                Console.WriteLine("7. Get playback position");
                Console.WriteLine("8. Set playback position");
                Console.WriteLine("9. Exit");
                Console.WriteLine("10. Pair with device");
                Console.WriteLine("11. Test connection");
                Console.Write("Choice: ");
                
                var choice = Console.ReadLine();
                
                switch (choice)
                {
                    case "1":
                        await PlayVideoFromUrl(streamer);
                        break;
                    case "10":
                        await streamer.PairIfRequiredAsync();
                        break;
                    case "11":
                        await streamer.TestConnectionAsync();
                        break;
                    case "2":
                        await PlayVideoFromFile(streamer);
                        break;
                    case "3":
                        await StreamVideoFromUrl(streamer);
                        break;
                    case "4":
                        await QuickTestVideo(streamer);
                        break;
                    case "5":
                        await StartScreenStreaming(streamer, discoveredDeviceInfo);
                        break;
                    case "6":
                        await streamer.StopAsync();
                        Console.WriteLine("Playback stopped.");
                        break;
                    case "7":
                        var position = await streamer.GetPositionAsync();
                        if (position.HasValue)
                        {
                            Console.WriteLine($"Current position: {position.Value} seconds");
                        }
                        else
                        {
                            Console.WriteLine("Could not get position.");
                        }
                        break;
                    case "8":
                        await SetPlaybackPosition(streamer);
                        break;
                    case "9":
                        await streamer.StopAsync();
                        return;
                    default:
                        Console.WriteLine("Invalid choice.");
                        break;
                }
                
                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            streamer.Dispose();
        }
    }
    
    static async Task PlayVideoFromUrl(AirPlayVideoStreamer streamer)
    {
        Console.Write("Enter video URL: ");
        var url = Console.ReadLine();
        
        if (string.IsNullOrWhiteSpace(url))
        {
            Console.WriteLine("URL cannot be empty.");
            return;
        }
        
        Console.Write("Start position (seconds, optional): ");
        var startPosInput = Console.ReadLine();
        double? startPos = null;
        if (!string.IsNullOrWhiteSpace(startPosInput) && double.TryParse(startPosInput, out double pos))
        {
            startPos = pos;
        }
        
        Console.WriteLine($"Playing video from URL: {url}");
        bool success = await streamer.PlayVideoAsync(url, startPos);
        
        if (success)
        {
            Console.WriteLine("Video playback started successfully!");
        }
        else
        {
            Console.WriteLine("Failed to start video playback.");
        }
    }
    
    static async Task PlayVideoFromFile(AirPlayVideoStreamer streamer)
    {
        Console.Write("Enter video file path: ");
        var filePath = Console.ReadLine();
        
        if (string.IsNullOrWhiteSpace(filePath))
        {
            Console.WriteLine("File path cannot be empty.");
            return;
        }
        
        Console.WriteLine($"Playing video file: {filePath}");
        bool success = await streamer.PlayVideoFileAsync(filePath);
        
        if (success)
        {
            Console.WriteLine("Video playback started successfully!");
        }
        else
        {
            Console.WriteLine("Failed to start video playback.");
        }
    }
    
    static async Task StreamVideoFromUrl(AirPlayVideoStreamer streamer)
    {
        Console.Write("Enter video URL to stream: ");
        var url = Console.ReadLine();
        
        if (string.IsNullOrWhiteSpace(url))
        {
            Console.WriteLine("URL cannot be empty.");
            return;
        }
        
        Console.WriteLine($"Streaming video from URL: {url}");
        
        try
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            
            using var stream = await response.Content.ReadAsStreamAsync();
            bool success = await streamer.StreamVideoAsync(stream, response.Content.Headers.ContentType?.MediaType ?? "video/mp4");
            
            if (success)
            {
                Console.WriteLine("Video streaming started successfully!");
            }
            else
            {
                Console.WriteLine("Failed to start video streaming.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error streaming video: {ex.Message}");
        }
    }
    
    static async Task SetPlaybackPosition(AirPlayVideoStreamer streamer)
    {
        Console.Write("Enter position in seconds: ");
        var posInput = Console.ReadLine();
        
        if (string.IsNullOrWhiteSpace(posInput) || !double.TryParse(posInput, out double position))
        {
            Console.WriteLine("Invalid position.");
            return;
        }
        
        bool success = await streamer.SetPositionAsync(position);
        
        if (success)
        {
            Console.WriteLine($"Position set to {position} seconds.");
        }
        else
        {
            Console.WriteLine("Failed to set position.");
        }
    }
    
    static async Task QuickTestVideo(AirPlayVideoStreamer streamer)
    {
        // List of reliable test video URLs
        var testVideos = new[]
        {
            new { Name = "Big Buck Bunny (Short)", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4" },
            new { Name = "Elephant's Dream", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ElephantsDream.mp4" },
            new { Name = "For Bigger Blazes", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerBlazes.mp4" },
            new { Name = "For Bigger Escapes", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerEscapes.mp4" },
            new { Name = "For Bigger Fun", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerFun.mp4" },
            new { Name = "For Bigger Joyrides", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerJoyrides.mp4" },
            new { Name = "Sintel", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/Sintel.mp4" },
            new { Name = "Subaru Outback", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/SubaruOutbackOnStreetAndDirt.mp4" },
            new { Name = "Tears of Steel", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/TearsOfSteel.mp4" },
            new { Name = "Volkswagen GTI", Url = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/VolkswagenGTIReview.mp4" }
        };
        
        Console.WriteLine("\nAvailable test videos:");
        for (int i = 0; i < testVideos.Length; i++)
        {
            Console.WriteLine($"{i + 1}. {testVideos[i].Name}");
        }
        Console.Write("\nSelect video (1-10) or press Enter for default (Big Buck Bunny): ");
        
        var choice = Console.ReadLine();
        int index = 0;
        
        if (!string.IsNullOrWhiteSpace(choice) && int.TryParse(choice, out int selectedIndex) && 
            selectedIndex >= 1 && selectedIndex <= testVideos.Length)
        {
            index = selectedIndex - 1;
        }
        
        var selectedVideo = testVideos[index];
        Console.WriteLine($"\nPlaying: {selectedVideo.Name}");
        Console.WriteLine($"URL: {selectedVideo.Url}");
        Console.WriteLine();
        
        bool success = await streamer.PlayVideoAsync(selectedVideo.Url);
        
        if (success)
        {
            Console.WriteLine("Video playback started successfully!");
        }
        else
        {
            Console.WriteLine("Failed to start video playback.");
            Console.WriteLine("Note: Some devices may require specific video formats or codecs.");
        }
    }
    
    static async Task StartScreenStreaming(AirPlayVideoStreamer streamer, AirPlayDeviceInfo? deviceInfo)
    {
        Console.WriteLine("\n=== Starting Screen Streaming (RTSP Video Stream) ===");
        Console.WriteLine("This will stream your entire screen as a video stream using RTSP protocol.");
        Console.WriteLine("Press Enter to stop streaming.");
        Console.WriteLine();
        
        using var screenStreamer = new ScreenStreamer(DeviceIp, DevicePort, deviceInfo);
        
        try
        {
            // Start streaming
            bool started = await screenStreamer.StartStreamingAsync();
            
            if (!started)
            {
                Console.WriteLine("Failed to start screen streaming.");
                return;
            }
            
            // Wait for user to stop
            Console.ReadLine();
            
            // Stop streaming
            await screenStreamer.StopStreamingAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError during screen streaming: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}
