using Meadow;

namespace Neomotive.SignCamera;

internal class Program
{
    public static async Task Main(string[] args)
    {
        await MeadowOS.Start(args);
    }
}