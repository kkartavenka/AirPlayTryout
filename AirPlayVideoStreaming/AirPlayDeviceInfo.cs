using System.Globalization;

namespace AirPlayVideoStreaming;

/// <summary>
/// Information about an AirPlay device discovered via mDNS
/// </summary>
public class AirPlayDeviceInfo
{
    public string DisplayName { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public int Port { get; set; } = 7000;
    public string? DeviceId { get; set; }
    public string? Model { get; set; }
    public string? SourceVersion { get; set; }
    public string? FeaturesHex { get; set; }
    public string? FlagsHex { get; set; }
    public string? PublicKey { get; set; }
    public string? PairingId { get; set; }
    
    // Parsed features flags
    public long Features { get; set; }
    public long Flags { get; set; }
    
    // Feature flags (common AirPlay feature bits)
    public bool SupportsVideo { get; set; }
    public bool SupportsPhoto { get; set; }
    public bool SupportsVideoFairPlay { get; set; }
    public bool SupportsVideoVolumeControl { get; set; }
    public bool SupportsVideoHTTPLiveStreams { get; set; }
    public bool SupportsSlideshow { get; set; }
    public bool SupportsScreen { get; set; }
    public bool SupportsScreenRotate { get; set; }
    public bool SupportsAudio { get; set; }
    public bool SupportsAudioRedundant { get; set; }
    public bool RequiresPassword { get; set; }
    public bool RequiresAuth { get; set; }
    public bool SupportsLegacyPairing { get; set; }
    public bool SupportsTransientPairing { get; set; }
    public bool SupportsHasUnifiedAdvertiserInfo { get; set; }
    public bool SupportsSupportsLegacyPairing { get; set; }
    public bool SupportsHasRequestedPairing { get; set; }
    
    /// <summary>
    /// Parse features hex string into feature flags
    /// </summary>
    public void ParseFeatures()
    {
        if (string.IsNullOrEmpty(FeaturesHex))
        {
            return;
        }
        
        try
        {
            // Features can be comma-separated hex values
            var featuresHex = FeaturesHex.Replace("0x", "").Replace("0X", "");
            var parts = featuresHex.Split(',');
            
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (long.TryParse(trimmed, NumberStyles.HexNumber, null, out var featureValue))
                {
                    Features |= featureValue;
                }
            }
            
            // Parse individual feature bits
            // Common AirPlay feature flags (from openairplay.github.io)
            SupportsVideo = (Features & 0x00000001) != 0; // Video
            SupportsPhoto = (Features & 0x00000002) != 0; // Photo
            SupportsVideoFairPlay = (Features & 0x00000008) != 0; // Video FairPlay
            SupportsVideoVolumeControl = (Features & 0x00000020) != 0; // Video Volume Control
            SupportsVideoHTTPLiveStreams = (Features & 0x00000080) != 0; // Video HTTP Live Streams
            SupportsSlideshow = (Features & 0x00000100) != 0; // Slideshow
            SupportsScreen = (Features & 0x00000800) != 0; // Screen
            SupportsScreenRotate = (Features & 0x00001000) != 0; // Screen Rotate
            SupportsAudio = (Features & 0x00020000) != 0; // Audio
            SupportsAudioRedundant = (Features & 0x00080000) != 0; // Audio Redundant
            RequiresPassword = (Features & 0x02000000) != 0; // Password Required (bit 25)
            RequiresAuth = (Features & 0x80000000) != 0; // Auth Required (bit 31)
            SupportsLegacyPairing = (Features & 0x00000400) != 0; // Legacy Pairing
            SupportsTransientPairing = (Features & 0x00008000) != 0; // Transient Pairing
            SupportsHasUnifiedAdvertiserInfo = (Features & 0x00010000) != 0; // Has Unified Advertiser Info
            SupportsSupportsLegacyPairing = (Features & 0x00000400) != 0; // Supports Legacy Pairing
            SupportsHasRequestedPairing = (Features & 0x00020000) != 0; // Has Requested Pairing
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing features: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Parse flags hex string
    /// </summary>
    public void ParseFlags()
    {
        if (string.IsNullOrEmpty(FlagsHex))
        {
            return;
        }
        
        try
        {
            var flagsHex = FlagsHex.Replace("0x", "").Replace("0X", "");
            if (long.TryParse(flagsHex, NumberStyles.HexNumber, null, out var flagsValue))
            {
                Flags = flagsValue;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing flags: {ex.Message}");
        }
    }
    
    public void PrintInfo()
    {
        Console.WriteLine("\n=== AirPlay Device Discovery Info ===");
        Console.WriteLine($"Display Name: {DisplayName}");
        Console.WriteLine($"IP Address: {IpAddress}");
        Console.WriteLine($"Port: {Port}");
        Console.WriteLine($"Device ID: {DeviceId ?? "N/A"}");
        Console.WriteLine($"Model: {Model ?? "N/A"}");
        Console.WriteLine($"Source Version: {SourceVersion ?? "N/A"}");
        Console.WriteLine($"Features (hex): {FeaturesHex ?? "N/A"}");
        Console.WriteLine($"Flags (hex): {FlagsHex ?? "N/A"}");
        Console.WriteLine($"Features (dec): {Features}");
        Console.WriteLine($"Flags (dec): {Flags}");
        
        // Group capabilities
        var capabilities = new List<string>();
        if (SupportsVideo) capabilities.Add("Video");
        if (SupportsPhoto) capabilities.Add("Photo");
        if (SupportsScreen) capabilities.Add("Screen");
        if (SupportsAudio) capabilities.Add("Audio");
        if (SupportsSlideshow) capabilities.Add("Slideshow");
        
        var videoFeatures = new List<string>();
        if (SupportsVideoFairPlay) videoFeatures.Add("FairPlay");
        if (SupportsVideoVolumeControl) videoFeatures.Add("Volume Control");
        if (SupportsVideoHTTPLiveStreams) videoFeatures.Add("HTTP Live Streams");
        
        var screenFeatures = new List<string>();
        if (SupportsScreenRotate) screenFeatures.Add("Screen Rotate");
        
        var audioFeatures = new List<string>();
        if (SupportsAudioRedundant) audioFeatures.Add("Audio Redundant");
        
        // Security features
        var securityFeatures = new List<string>();
        if (RequiresPassword) securityFeatures.Add("Password Required");
        if (RequiresAuth) securityFeatures.Add("Authentication Required");
        
        // Pairing features
        var pairingFeatures = new List<string>();
        if (SupportsLegacyPairing) pairingFeatures.Add("Legacy Pairing");
        if (SupportsTransientPairing) pairingFeatures.Add("Transient Pairing");
        if (SupportsHasUnifiedAdvertiserInfo) pairingFeatures.Add("Unified Advertiser Info");
        if (SupportsHasRequestedPairing) pairingFeatures.Add("Requested Pairing");
        
        // Display grouped information
        Console.WriteLine("\n--- Capabilities ---");
        if (capabilities.Count > 0)
        {
            Console.WriteLine($"  {string.Join(", ", capabilities)}");
        }
        else
        {
            Console.WriteLine("  None");
        }
        
        if (videoFeatures.Count > 0)
        {
            Console.WriteLine("\n--- Video Features ---");
            Console.WriteLine($"  {string.Join(", ", videoFeatures)}");
        }
        
        if (screenFeatures.Count > 0)
        {
            Console.WriteLine("\n--- Screen Features ---");
            Console.WriteLine($"  {string.Join(", ", screenFeatures)}");
        }
        
        if (audioFeatures.Count > 0)
        {
            Console.WriteLine("\n--- Audio Features ---");
            Console.WriteLine($"  {string.Join(", ", audioFeatures)}");
        }
        
        Console.WriteLine("\n--- Security ---");
        if (securityFeatures.Count > 0)
        {
            Console.WriteLine($"  {string.Join(", ", securityFeatures)}");
        }
        else
        {
            Console.WriteLine("  No security requirements");
        }
        
        if (!string.IsNullOrEmpty(PairingId))
        {
            Console.WriteLine($"  Pairing ID: {PairingId}");
        }
        
        if (pairingFeatures.Count > 0)
        {
            Console.WriteLine("\n--- Pairing Support ---");
            Console.WriteLine($"  {string.Join(", ", pairingFeatures)}");
        }
        
        if (!string.IsNullOrEmpty(PublicKey))
        {
            Console.WriteLine("\n--- Additional Info ---");
            Console.WriteLine($"  Public Key: {PublicKey.Substring(0, Math.Min(40, PublicKey.Length))}...");
        }
        
        Console.WriteLine("=====================================\n");
    }
}

