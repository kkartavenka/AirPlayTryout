namespace AirPlayCasting.Contracts;

public class AirPlayDeviceComparer : IEqualityComparer<AirPlayDevice>
{
    public bool Equals(AirPlayDevice? x, AirPlayDevice? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null)
            return false;

        return x.DisplayName == y.DisplayName &&
               x.Port == y.Port &&
               x.Ip == y.Ip &&
               x.PinRequired == y.PinRequired;
    }

    public int GetHashCode(AirPlayDevice? obj)
    {
        if (obj is null)
            return 0;

        int hash = 17;
        hash = hash * 23 + (obj.DisplayName?.GetHashCode() ?? 0);
        hash = hash * 23 + obj.Port.GetHashCode();
        hash = hash * 23 + (obj.Ip?.GetHashCode() ?? 0);
        hash = hash * 23 + obj.PinRequired.GetHashCode();
        return hash;
    }
}