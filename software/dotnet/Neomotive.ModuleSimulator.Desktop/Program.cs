using Meadow.Hardware;
using Neomotive.ControlModule;

internal class Program
{
    private static void Main(string[] args)
    {
        var expander = new PCanUsb();

        var bus = expander.CreateCanBus(CanBitrate.Can_500kbps);

        PrimaryControlModule pcm = new PrimaryControlModule(bus);

        Console.WriteLine("PCM simulator running. Type 'exit' to quit.");

        while (Console.ReadLine() != "exit") { }
    }
}