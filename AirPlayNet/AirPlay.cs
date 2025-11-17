using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace AirPlayNet;

public class AirPlay
{
    public const string DnsSdType = "_airplay._tcp.local.";
    
    public const string None = "None";
    public const string SlideLeft = "SlideLeft";
    public const string SlideRight = "SlideRight";
    public const string Dissolve = "Dissolve";
    public const string Username = "Airplay";
    public const int Port = 7000;
    public const int AppleTvWidth = 1280;
    public const int AppleTvHeight = 720;
    public const float AppleTvAspect = (float)AppleTvWidth / AppleTvHeight;
    
    protected string? hostname;
    protected string? name;
    protected int port;
    protected PhotoThread? photothread;
    protected string? password;
    protected Dictionary<string, string>? params_;
    protected string? authorization;
    protected IAuth? auth;
    protected int appletv_width = AppleTvWidth;
    protected int appletv_height = AppleTvHeight;
    protected float appletv_aspect = AppleTvAspect;
    
    private static readonly Configuration ImageConfig = Configuration.Default;
    
    private readonly HttpClient httpClient;
    
    // AirPlay class constructors
    public AirPlay(Service service)
        : this(service.Hostname, service.Port, service.Name)
    {
    }
    
    public AirPlay(string hostname)
        : this(hostname, Port)
    {
    }
    
    public AirPlay(string hostname, int port)
        : this(hostname, port, hostname)
    {
    }
    
    public AirPlay(string hostname, int port, string name)
    {
        this.hostname = hostname;
        this.port = port;
        this.name = name;
        
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
    
    public void SetScreenSize(int width, int height)
    {
        appletv_width = width;
        appletv_height = height;
        appletv_aspect = (float)width / height;
    }
    
    public void SetPassword(string password)
    {
        this.password = password;
    }
    
    public void SetAuth(IAuth auth)
    {
        this.auth = auth;
    }
    
    protected string Md5Digest(string input)
    {
        byte[] source;
        try
        {
            source = Encoding.UTF8.GetBytes(input);
        }
        catch
        {
            source = Encoding.Default.GetBytes(input);
        }
        
        string result = string.Empty;
        char[] hexDigits = { '0', '1', '2', '3', '4', '5', '6', '7',
            '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };
        
        try
        {
            using var md5 = MD5.Create();
            byte[] temp = md5.ComputeHash(source);
            char[] str = new char[16 * 2];
            int k = 0;
            for (int i = 0; i < 16; i++)
            {
                byte byte0 = temp[i];
                str[k++] = hexDigits[byte0 >> 4 & 0xf];
                str[k++] = hexDigits[byte0 & 0xf];
            }
            result = new string(str);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
        }
        
        return result;
    }
    
    protected string MakeAuthorization(Dictionary<string, string> params_, string password, string method, string uri)
    {
        string realm = params_["realm"];
        string nonce = params_["nonce"];
        string ha1 = Md5Digest(Username + ":" + realm + ":" + password);
        string ha2 = Md5Digest(method + ":" + uri);
        string response = Md5Digest(ha1 + ":" + nonce + ":" + ha2);
        authorization = "Digest username=\"" + Username + "\", "
            + "realm=\"" + realm + "\", "
            + "nonce=\"" + nonce + "\", "
            + "uri=\"" + uri + "\", "
            + "response=\"" + response + "\"";
        return authorization;
    }
    
    protected Dictionary<string, string> GetAuthParams(string authString)
    {
        Dictionary<string, string> params_ = new Dictionary<string, string>();
        int firstSpace = authString.IndexOf(' ');
        string digest = authString.Substring(0, firstSpace);
        string rest = authString.Substring(firstSpace + 1).Replace("\r\n", " ");
        string[] lines = rest.Split("\", ");
        
        for (int i = 0; i < lines.Length; i++)
        {
            int split = lines[i].IndexOf("=\"");
            if (split < 0) continue;
            
            string key = lines[i].Substring(0, split);
            string value = lines[i].Substring(split + 2);
            if (value.Length > 0 && value[value.Length - 1] == '"')
            {
                value = value.Substring(0, value.Length - 1);
            }
            params_[key] = value;
        }
        
        return params_;
    }
    
    protected string SetPassword()
    {
        if (password != null)
        {
            return password;
        }
        else
        {
            if (auth != null)
            {
                password = auth.GetPassword(hostname!, name!);
                return password ?? throw new IOException("Authorization required");
            }
            else
            {
                throw new IOException("Authorization required");
            }
        }
    }
    
    protected async Task<string?> DoHttpAsync(string method, string uri)
    {
        return await DoHttpAsync(method, uri, null);
    }
    
    protected async Task<string?> DoHttpAsync(string method, string uri, byte[]? data)
    {
        return await DoHttpAsync(method, uri, data, new Dictionary<string, string>(), true);
    }
    
    protected async Task<string?> DoHttpAsync(string method, string uri, byte[]? data, Dictionary<string, string> headers, bool repeat)
    {
        try
        {
            var url = $"http://{hostname}:{port}{uri}";
            var request = new HttpRequestMessage(new HttpMethod(method), url);

            if (params_ != null)
            {
                headers["Authorization"] = MakeAuthorization(params_, password!, method, uri);
            }
            
            if (headers.Count > 0)
            {
                request.Headers.Add("User-Agent", "MediaControl/1.0");
                foreach (var header in headers)
                {
                    if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Headers.Authorization = AuthenticationHeaderValue.Parse(header.Value);
                    }
                    else
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }
                }
            }
            
            if (data != null)
            {
                request.Content = new ByteArrayContent(data);
                request.Content.Headers.ContentLength = data.Length;
            }
            
            var response = await httpClient.SendAsync(request);
            
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (repeat)
                {
                    var authHeader = response.Headers.WwwAuthenticate.FirstOrDefault();
                    if (authHeader != null)
                    {
                        string authstring = authHeader.ToString();
                        if (SetPassword() != null)
                        {
                            params_ = GetAuthParams(authstring);
                            return await DoHttpAsync(method, uri, data, headers, false);
                        }
                        else
                        {
                            return null;
                        }
                    }
                }
                else
                {
                    throw new IOException("Incorrect password");
                }
            }
            
            var content = await response.Content.ReadAsStringAsync();
            return content;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"HTTP request failed: {ex.Message}");
            return null;
        }
    }
    
    public void Stop()
    {
        try
        {
            StopPhotoThread();
            DoHttpAsync("POST", "/stop").Wait();
            params_ = null;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Stop failed: {e.Message}");
        }
    }
    
    public static Image<Rgba32> ScaleImage(Image<Rgba32> image, int targetWidth, int targetHeight, float targetAspect)
    {
        int width = image.Width;
        int height = image.Height;
        
        if (width <= targetWidth && height <= targetHeight)
        {
            return image.Clone();
        }
        else
        {
            int scaledheight;
            int scaledwidth;
            float image_aspect = (float)width / height;
            
            if (image_aspect > targetAspect)
            {
                scaledheight = (int)(targetWidth / image_aspect);
                scaledwidth = targetWidth;
            }
            else
            {
                scaledheight = targetHeight;
                scaledwidth = (int)(targetHeight * image_aspect);
            }
            
            Image<Rgba32> scaledimage = image.Clone();
            scaledimage.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(scaledwidth, scaledheight),
                Mode = ResizeMode.Stretch
            }));
            return scaledimage;
        }
    }
    
    protected Image<Rgba32> ScaleImage(Image<Rgba32> image)
    {
        return ScaleImage(image, appletv_width, appletv_height, appletv_aspect);
    }
    
    public void Photo(string filename)
    {
        Photo(filename, None);
    }
    
    public void Photo(string filename, string transition)
    {
        using var image = Image.Load<Rgba32>(filename);
        Photo(image, transition);
    }
    
    public void Photo(Image<Rgba32> image)
    {
        Photo(image, None);
    }
    
    public void Photo(Image<Rgba32> image, string transition)
    {
        StopPhotoThread();
        Image<Rgba32> scaledimage = ScaleImage(image);
        PhotoRawAsync(scaledimage, transition).Wait();
        photothread = new PhotoThread(this, scaledimage, 5000);
        photothread.Start();
    }
    
    public void PhotoRawCompress(Image<Rgba32> image, string transition)
    {
        Image<Rgba32> scaledimage = ScaleImage(image);
        PhotoRawAsync(scaledimage, transition).Wait();
    }
    
    protected async Task PhotoRawAsync(Image<Rgba32> image, string transition)
    {
        Dictionary<string, string> headers = new Dictionary<string, string>();
        headers["X-Apple-Transition"] = transition;
        
        byte[] imageData;
        using (MemoryStream ms = new MemoryStream())
        {
            await image.SaveAsJpegAsync(ms);
            imageData = ms.ToArray();
        }
        
        await DoHttpAsync("PUT", "/photo", imageData, headers, true);
    }
    
    // Synchronous wrapper for PhotoThread compatibility
    protected void PhotoRaw(Image<Rgba32> image, string transition)
    {
        PhotoRawAsync(image, transition).Wait();
    }
    
    public static Image<Rgba32> CaptureScreen()
    {
        // Note: Screen capture requires platform-specific implementation
        // This would need to use platform-specific APIs (e.g., CoreGraphics on macOS, GDI+ on Windows)
        throw new NotImplementedException("Screen capture requires additional platform-specific implementation");
    }
    
    public void StopPhotoThread()
    {
        if (photothread != null)
        {
            photothread.Interrupt();
            while (photothread.IsAlive)
            {
                Thread.Sleep(10);
            }
            photothread = null;
        }
    }
    
    public void Desktop()
    {
        StopPhotoThread();
        photothread = new PhotoThread(this);
        photothread.Start();
    }
    
    // Static helper methods
    public static string? WaitForUser()
    {
        return WaitForUser("Press return to quit");
    }
    
    public static string? WaitForUser(string message)
    {
        Console.WriteLine(message);
        string? s = Console.ReadLine();
        return s;
    }
    
    public static void Usage()
    {
        Console.WriteLine("commands: -s {stop} | -p file {photo} | -d {desktop} | -?");
        Console.WriteLine("AirPlayNet -h hostname[:port] [-a password] command");
    }
    
    public static async Task<AirPlay?> SearchDialogAsync(object? parent)
    {
        return await SearchDialogAsync(parent, 6000);
    }
    
    public static async Task<AirPlay?> SearchDialogAsync(object? parent, int timeout)
    {
        Console.WriteLine("Searching for AirPlay devices...");
        List<Service> services = await ServiceDiscovery.SearchAsync(timeout);
        
        if (services.Count > 0)
        {
            Console.WriteLine("Available AirPlay devices:");
            for (int i = 0; i < services.Count; i++)
            {
                Service service = services[i];
                Console.WriteLine($"{i}: {service.Name} ({service.Hostname})");
            }
            
            Console.Write("Select device number: ");
            string? input = Console.ReadLine();
            if (int.TryParse(input, out int index) && index >= 0 && index < services.Count)
            {
                AirPlay airplay = new AirPlay(services[index]);
                airplay.SetAuth(new AuthConsole());
                return airplay;
            }
            return null;
        }
        else
        {
            Console.WriteLine("No AirPlay devices found");
            return null;
        }
    }
    
    public static void SelectResolutionDialog(object? parent, AirPlay airplay)
    {
        string[] choices = new string[]
        {
            "Full HD  - 1080p - 1920x1080",
            "HD Ready - 720p - 1280x720"
        };
        
        Console.WriteLine("Select AppleTV Resolution:");
        for (int i = 0; i < choices.Length; i++)
        {
            Console.WriteLine($"{i}: {choices[i]}");
        }
        
        Console.Write("Select resolution number: ");
        string? input = Console.ReadLine();
        if (int.TryParse(input, out int index) && index >= 0 && index < choices.Length)
        {
            if (index == 0)
            {
                airplay.SetScreenSize(1920, 1080);
            }
            else if (index == 1)
            {
                airplay.SetScreenSize(1280, 720);
            }
        }
    }
}

