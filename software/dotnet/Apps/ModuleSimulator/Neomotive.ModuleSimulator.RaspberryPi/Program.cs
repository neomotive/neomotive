using Avalonia;
using Neomotive.ModuleSimulator.RaspberryPi;
using System;

Console.WriteLine("Starting Neomotive Module Simulator for Raspberry Pi...");

AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
