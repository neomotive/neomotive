using Avalonia;
using System;

Console.WriteLine("Starting Neomotive Module Simulator for Raspberry Pi...");

AppBuilder.Configure<Neomotive.ModuleSimulator.App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
