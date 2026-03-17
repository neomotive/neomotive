using ICs.IOExpanders.PCanBasic;
using Meadow.Hardware;
using Neomotive.ControlModule;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var expander = new PCanUsb();

        var bus = expander.CreateCanBus(CanBitrate.Can_500kbps);

        PrimaryControlModule pcm = new PrimaryControlModule(bus);

        Console.WriteLine("PCM simulator running. Type 'exit' to quit.");

        while (Console.ReadLine() != "exit") { }
    }
}