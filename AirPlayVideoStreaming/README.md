# AirPlay Video Streaming

A .NET console application for streaming video to AirPlay devices without authentication. This project is configured to connect to device `192.168.2.39` on port `7000`.

## Features

- **Auth-Free Streaming**: Connects to AirPlay devices without requiring authentication
- **Video URL Playback**: Stream videos from HTTP/HTTPS URLs
- **Local File Playback**: Play video files from the local filesystem
- **Real-Time Streaming**: Stream video data in real-time
- **Screen Casting**: Stream your entire display screen to the AirPlay device in real-time
- **Playback Control**: Stop, seek, and get playback position
- **Device Discovery**: Check device connectivity and get device information

## Configuration

The target device is hardcoded in `Program.cs`:
- **IP Address**: `192.168.2.39`
- **Port**: `7000`

To change the device, modify the constants in `Program.cs`:
```csharp
private const string DeviceIp = "192.168.2.39";
private const int DevicePort = 7000;
```

## Usage

### Building

```bash
dotnet build
```

### Running

```bash
dotnet run
```

### Interactive Menu

The application provides an interactive menu with the following options:

1. **Play video from URL** - Stream a video from an HTTP/HTTPS URL
2. **Play video from local file** - Play a video file from your local filesystem
3. **Stream video from URL** - Real-time streaming from a URL
4. **Quick test with sample video URL** - Test with pre-configured sample videos
5. **Screen cast entire display** - Stream your screen to the AirPlay device in real-time
6. **Stop playback** - Stop current video playback
7. **Get playback position** - Get the current playback position
8. **Set playback position** - Seek to a specific position
9. **Exit** - Exit the application

## AirPlay Protocol

This implementation uses the AirPlay HTTP API:

- **`/info`** - Get device information
- **`/server-info`** - Get server information
- **`/play`** - Start video playback
- **`/stop`** - Stop playback
- **`/scrub`** - Get/set playback position

## Supported Video Formats

AirPlay typically supports:
- H.264 video codec
- MP4 container format
- HTTP Live Streaming (HLS)
- MPEG-2 Transport Stream

## Example Usage

### Play Video from URL

```
Select an option:
1. Play video from URL
Choice: 1
Enter video URL: https://example.com/video.mp4
Start position (seconds, optional): 0
Playing video from URL: https://example.com/video.mp4
Video playback started successfully!
```

### Play Local Video File

```
Select an option:
2. Play video from local file
Choice: 2
Enter video file path: /path/to/video.mp4
Playing video file: /path/to/video.mp4
Video playback started successfully!
```

## Requirements

- .NET 6.0 or later
- AirPlay-enabled device on the same network
- Device IP address: `192.168.2.39` (configurable)
- For screen casting: macOS (uses `screencapture` command), Windows/Linux support can be added

## Notes

- This implementation assumes the AirPlay device does not require authentication
- For devices that require authentication, you'll need to implement the authentication flow
- Video streaming performance depends on network conditions and device capabilities
- Some devices may require specific video formats or codecs

## Troubleshooting

### Device Not Reachable

- Ensure the device is on the same network
- Verify AirPlay is enabled on the device
- Check that the IP address is correct
- Ensure port 7000 is not blocked by firewall

### Playback Fails

- Verify the video format is supported by the device
- Check network connectivity
- Ensure the video URL is accessible
- Try a different video source

## Screen Casting

The screen casting feature captures your entire display and streams it to the AirPlay device in real-time:

- **Platform Support**: Currently supports macOS (uses native `screencapture` command)
- **Frame Rate**: Configurable (default: 10 FPS)
- **Image Quality**: JPEG compression at 85% quality
- **Usage**: Select option 5 from the menu, then press Enter to stop

### How It Works

1. Captures screen frames at regular intervals (default: every 100ms = 10 FPS)
2. Converts each frame to JPEG format
3. Sends frames to AirPlay device via `/photo` endpoint
4. Displays real-time statistics (frames sent, FPS)

**Note**: Screen casting sends individual frames, which may have some latency. For lower latency, consider reducing the capture interval, but this will increase network bandwidth usage.

## Project Structure

```
AirPlayVideoStreaming/
├── AirPlayVideoStreamer.cs    # Main streaming client class
├── ScreenCapture.cs           # Screen capture implementation
├── Program.cs                  # Console application entry point
├── AirPlayVideoStreaming.csproj # Project file
├── TestVideoUrls.txt          # List of test video URLs
└── README.md                   # This file
```

