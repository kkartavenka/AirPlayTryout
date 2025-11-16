using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AirPlayNet;

public class PhotoThread
{
    private readonly AirPlay airplay;
    private Image<Rgba32>? image;
    private int timeout = 5000;
    private Thread? thread;
    private volatile bool interrupted = false;
    
    public bool IsAlive => thread?.IsAlive ?? false;
    
    public PhotoThread(AirPlay airplay)
        : this(airplay, null, 1000)
    {
    }
    
    public PhotoThread(AirPlay airplay, Image<Rgba32>? image, int timeout)
    {
        this.airplay = airplay;
        this.image = image;
        this.timeout = timeout;
    }
    
    public void Start()
    {
        interrupted = false;
        thread = new Thread(Run)
        {
            IsBackground = true
        };
        thread.Start();
    }
    
    public void Interrupt()
    {
        interrupted = true;
    }
    
    private void Run()
    {
        while (!interrupted)
        {
            try
            {
                if (image == null)
                {
                    // Screen capture - requires platform-specific implementation
                    // For now, we'll skip screen capture functionality
                    // Image<Rgba32> frame = AirPlay.ScaleImage(AirPlay.CaptureScreen());
                    // airplay.PhotoRawCompress(frame, AirPlay.None);
                    Thread.Sleep(100);
                }
                else
                {
                    // Use PhotoRawCompress which handles scaling
                    airplay.PhotoRawCompress(image, AirPlay.None);
                    Thread.Sleep((int)(0.9 * timeout));
                }
            }
            catch (ThreadInterruptedException)
            {
                break;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"PhotoThread error: {e.Message}");
                break;
            }
        }
    }
}

