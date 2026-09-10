using Avalonia;
using System;

// The Pi appliance has no X server — Avalonia renders straight to DRM/KMS
// (/dev/dri/card*), exactly as the ScanTool does. StartLinuxDrm uses a
// single-view lifetime, so App sets MainView rather than MainWindow. Override
// the card with SIMULATOR_DRM_CARD if the display is not on the default probe.
//
// This used to be UseX11 + StartWithClassicDesktopLifetime, which cost an X
// server, an xinit chain and an xinitrc supervise loop on an image that ships no
// display stack — and needed a PrivateTmp drop-in on top, because app.service
// runs with ProtectSystem=strict and X cannot create /tmp/.X11-unix/X0 on a
// read-only /tmp. None of that applies to DRM. Both appliances now stand the UI
// up the same way.
Console.WriteLine("Starting Neomotive Module Simulator (Raspberry Pi / DRM)...");

var card = Environment.GetEnvironmentVariable("SIMULATOR_DRM_CARD");

var scaling = double.TryParse(
    Environment.GetEnvironmentVariable("SIMULATOR_DRM_SCALING"),
    out var s) ? s : 1.0;

// UseSkia registers rendering only. UsePlatformDetect (which the desktop head
// uses) is what normally also wires up text shaping, but it would drag in X11
// here — so HarfBuzz has to be requested explicitly or AppBuilder.Setup throws
// "No text shaping system configured".
AppBuilder.Configure<Neomotive.ModuleSimulator.App>()
    .UseSkia()
    .UseHarfBuzz()
    .WithInterFont()
    .LogToTrace()
    .StartLinuxDrm(args, card, scaling);
