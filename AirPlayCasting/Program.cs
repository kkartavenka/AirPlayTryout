// See https://aka.ms/new-console-template for more information

using AirPlayCasting;

var locator = new AirPlayDeviceLocator(TimeSpan.FromSeconds(10));
locator.OnDeviceDiscovered += (sender, eventArgs) =>
{
    Console.WriteLine(eventArgs.Device);
};
locator.StartAsync();

while (true)
{
    Console.ReadLine();
}