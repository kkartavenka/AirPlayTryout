namespace AirPlayNet;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("-?") || args.Contains("--help"))
            {
                AirPlay.Usage();
                return;
            }
            
            string? hostname = null;
            string? password = null;
            bool stop = false;
            string? photo = null;
            bool desktop = false;
            int width = 0;
            int height = 0;
            
            // Parse command line arguments
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-h":
                    case "--hostname":
                        if (i + 1 < args.Length)
                        {
                            hostname = args[++i];
                        }
                        break;
                    case "-a":
                    case "--password":
                        if (i + 1 < args.Length)
                        {
                            password = args[++i];
                        }
                        break;
                    case "-s":
                    case "--stop":
                        stop = true;
                        break;
                    case "-p":
                    case "--photo":
                        if (i + 1 < args.Length)
                        {
                            photo = args[++i];
                        }
                        break;
                    case "-d":
                    case "--desktop":
                        desktop = true;
                        break;
                    case "-x":
                    case "--width":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int w))
                        {
                            width = w;
                        }
                        break;
                    case "-y":
                    case "--height":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int h))
                        {
                            height = h;
                        }
                        break;
                }
            }
            
            AirPlay? airplay;
            
            if (hostname == null)
            {
                // Show search dialog if no host address is given
                airplay = await AirPlay.SearchDialogAsync(null);
                if (airplay != null)
                {
                    AirPlay.SelectResolutionDialog(null, airplay);
                    airplay.Desktop();
                    Console.WriteLine("Press Ctrl+C to quit");
                    // Keep running
                    await Task.Delay(-1);
                }
            }
            else
            {
                string[] hostport = hostname.Split(':', 2);
                if (hostport.Length > 1)
                {
                    airplay = new AirPlay(hostport[0], int.Parse(hostport[1]));
                }
                else
                {
                    airplay = new AirPlay(hostport[0]);
                }
                
                if (width != 0 && height != 0)
                {
                    airplay.SetScreenSize(width, height);
                }
                
                airplay.SetAuth(new AuthConsole());
                if (password != null)
                {
                    airplay.SetPassword(password);
                }
                
                if (stop)
                {
                    airplay.Stop();
                }
                else if (photo != null)
                {
                    Console.WriteLine("Press Ctrl+C to quit");
                    try
                    {
                        airplay.Photo(photo);
                        // Keep running
                        await Task.Delay(-1);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error displaying photo: {ex.Message}");
                    }
                }
                else if (desktop)
                {
                    Console.WriteLine("Press Ctrl+C to quit");
                    try
                    {
                        airplay.Desktop();
                        // Keep running
                        await Task.Delay(-1);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error displaying desktop: {ex.Message}");
                    }
                }
                else
                {
                    AirPlay.Usage();
                }
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error: {e.Message}");
            Console.Error.WriteLine(e.StackTrace);
        }
    }
}
