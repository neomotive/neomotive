using Avalonia;
using Neomotive.ModuleSimulator.UI;
using System;

Console.WriteLine("Starting Neomotive Module Simulator for the Desktop...");

AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);