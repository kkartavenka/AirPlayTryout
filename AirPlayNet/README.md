# AirPlayNet

A .NET implementation of AirPlay functionality, translated from Java. This project provides the ability to discover and connect to AirPlay devices, send photos, and stream desktop content.

## Project Structure

```
AirPlayNet/
├── AirPlay.cs          # Main AirPlay class with HTTP communication and photo handling
├── PhotoThread.cs      # Thread class for continuous photo streaming
├── Auth.cs             # Authentication interfaces and implementations
├── Service.cs          # Service discovery using mDNS/Bonjour
├── Program.cs          # Command-line interface and entry point
└── AirPlayNet.csproj   # Project file with dependencies
```

## Features

- **Device Discovery**: Automatically discover AirPlay devices on the local network using mDNS/Bonjour
- **Photo Display**: Send photos to AirPlay devices with transition effects
- **Authentication**: Support for password-protected AirPlay devices using Digest authentication
- **Image Scaling**: Automatic image scaling to match device resolution
- **Command-Line Interface**: Full CLI support for all operations

## Dependencies

- **Zeroconf** (v3.7.16): For mDNS/Bonjour service discovery
- **SixLabors.ImageSharp** (v3.1.5): For cross-platform image processing

## Usage

### Basic Commands

```bash
# Display help
dotnet run -- -?

# Search for devices and stream desktop
dotnet run

# Send a photo to a specific device
dotnet run -- -h 192.168.1.100 -p photo.jpg

# Send a photo with password
dotnet run -- -h 192.168.1.100:7000 -a password123 -p photo.jpg

# Stop current playback
dotnet run -- -h 192.168.1.100 -s

# Stream desktop
dotnet run -- -h 192.168.1.100 -d

# Set custom resolution
dotnet run -- -h 192.168.1.100 -x 1920 -y 1080 -p photo.jpg
```

### Command-Line Options

- `-h, --hostname`: Hostname or IP address (optionally with port, e.g., `192.168.1.100:7000`)
- `-a, --password`: Password for password-protected devices
- `-s, --stop`: Stop current playback
- `-p, --photo`: Path to photo file to display
- `-d, --desktop`: Stream desktop (requires additional implementation)
- `-x, --width`: Set custom width
- `-y, --height`: Set custom height
- `-?, --help`: Show usage information

## Architecture

### AirPlay Class
Main class that handles:
- HTTP communication with AirPlay devices
- Digest authentication
- Image scaling and JPEG encoding
- Photo transmission

### PhotoThread Class
Background thread for continuous photo streaming:
- Supports single image repetition
- Designed for desktop streaming (requires screen capture implementation)

### Service Discovery
Uses Zeroconf library to discover AirPlay devices via mDNS:
- Searches for `_airplay._tcp.local.` services
- Filters IPv4 addresses
- Returns list of available devices

### Authentication
Supports Digest authentication:
- Console-based password input
- MD5 hash generation for authentication
- Automatic retry on 401 Unauthorized responses

## Notes

- Screen capture functionality (`CaptureScreen()`) requires additional platform-specific implementation
- Desktop streaming is partially implemented and requires screen capture to be fully functional
- Image processing uses SixLabors.ImageSharp which is fully cross-platform
- The implementation follows the original Java code structure closely for easier comparison

## Building

```bash
dotnet restore
dotnet build
dotnet run
```

## License

This is a translation of the original Java AirPlay implementation.

