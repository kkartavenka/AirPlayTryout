namespace AirPlayNet;

public interface IAuth
{
    string? GetPassword(string hostname, string name);
}

public class AuthConsole : IAuth
{
    public string? GetPassword(string hostname, string name)
    {
        string display = hostname == name ? hostname : name + " (" + hostname + ")";
        return AirPlay.WaitForUser("Please input password for " + display);
    }
}

// Note: AuthDialog would require a UI framework (WPF, WinForms, etc.)
// For a console application, we'll skip this implementation
public class AuthDialog : IAuth
{
    private object? parent;
    
    public AuthDialog(object? parent)
    {
        this.parent = parent;
    }
    
    public string? GetPassword(string hostname, string name)
    {
        // Console-based password input as fallback
        return new AuthConsole().GetPassword(hostname, name);
    }
}

