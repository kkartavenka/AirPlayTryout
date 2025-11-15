namespace AirPlayCasting.Contracts;

public class AirPlayDeviceArg : EventArgs
{
    public AirPlayDeviceArg(AirPlayDevice device)
    {
        Device = device;
    }

    public AirPlayDevice Device { get; }
}