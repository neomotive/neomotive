using Meadow;

internal partial class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        await MeadowOS.Start<MeadowApp>();
    }
}