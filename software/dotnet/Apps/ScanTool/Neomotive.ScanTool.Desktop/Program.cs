using System;
using Avalonia;
using Neomotive.ScanTool.UI;

Console.WriteLine("Starting Neomotive Scan Tool...");

AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
