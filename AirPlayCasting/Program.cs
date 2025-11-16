// See https://aka.ms/new-console-template for more information

using AirPlayCasting;
using AirPlayCasting.Contracts;

var locator = new AirPlayDeviceLocator();
var devices = new Dictionary<string, AirPlayDevice>();
var idx = 0;
locator.OnDeviceDiscovered += (sender, eventArgs) =>
{
    devices.Add(idx.ToString(), eventArgs.Device);
    Console.WriteLine($"{idx++} {eventArgs.Device}");
};
await locator.StartAsync();
Console.Write("Selected device by ID: ");
var selectedDevice = Console.ReadLine();

var connector = new AirPlayConnector();
await connector.PairWithDevice(devices[selectedDevice].Ip);
//await connector.ConnectAsync(devices[selectedDevice]);
while (true)
{
    Console.ReadLine();
}