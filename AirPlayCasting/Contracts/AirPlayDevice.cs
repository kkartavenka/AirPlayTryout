namespace AirPlayCasting.Contracts;

public record AirPlayDevice
{
    public string DisplayName { get; init; }
    public int Port { get; init; }
    public string Ip { get; init; }
    public bool PinRequired { get; init; }
}