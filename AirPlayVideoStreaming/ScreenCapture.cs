using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AirPlayVideoStreaming;

public class ScreenCapture : IDisposable
{
    private bool _isCapturing = false;
    private CancellationTokenSource? _cancellationTokenSource;
    
    public event EventHandler<Image<Rgba32>>? FrameCaptured;
    
    public int CaptureIntervalMs { get; set; } = 100; // 10 FPS default
    
    public void StartCapture()
    {
        if (_isCapturing)
        {
            return;
        }
        
        _isCapturing = true;
        _cancellationTokenSource = new CancellationTokenSource();
        
        Task.Run(() => CaptureLoop(_cancellationTokenSource.Token));
    }
    
    public void StopCapture()
    {
        _isCapturing = false;
        _cancellationTokenSource?.Cancel();
    }
    
    private async Task CaptureLoop(CancellationToken cancellationToken)
    {
        while (_isCapturing && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var frame = CaptureScreen();
                if (frame != null)
                {
                    FrameCaptured?.Invoke(this, frame);
                }
                
                await Task.Delay(CaptureIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error capturing frame: {ex.Message}");
                await Task.Delay(CaptureIntervalMs, cancellationToken);
            }
        }
    }
    
    public Image<Rgba32>? CaptureScreen()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return CaptureScreenMacOS();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return CaptureScreenWindows();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return CaptureScreenLinux();
        }
        else
        {
            throw new PlatformNotSupportedException("Screen capture not supported on this platform");
        }
    }
    
    private Image<Rgba32>? CaptureScreenMacOS()
    {
        try
        {
            // Use screencapture command on macOS (simpler than CoreGraphics P/Invoke)
            var tempFile = Path.GetTempFileName() + ".png";
            
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/sbin/screencapture",
                    Arguments = $"-x -t png \"{tempFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            
            process.Start();
            process.WaitForExit(1000);
            
            if (File.Exists(tempFile))
            {
                var image = Image.Load<Rgba32>(tempFile);
                File.Delete(tempFile);
                return image;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"macOS screen capture error: {ex.Message}");
        }
        
        return null;
    }
    
    private Image<Rgba32>? CaptureScreenWindows()
    {
        // Windows implementation would use System.Drawing or GDI+
        // For now, return null - can be implemented later
        Console.WriteLine("Windows screen capture not yet implemented");
        return null;
    }
    
    private Image<Rgba32>? CaptureScreenLinux()
    {
        // Linux implementation would use X11 or similar
        // For now, return null - can be implemented later
        Console.WriteLine("Linux screen capture not yet implemented");
        return null;
    }
    
    public void Dispose()
    {
        StopCapture();
        _cancellationTokenSource?.Dispose();
    }
}

