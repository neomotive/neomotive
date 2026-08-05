using Avalonia;
using Neomotive.ScanTool.UI;
using System;

// The Pi appliance has no X server — Avalonia renders straight to DRM/KMS
// (/dev/dri/card*). StartLinuxDrm uses a single-view lifetime, so App sets
// MainView rather than MainWindow. Override the card with SCANTOOL_DRM_CARD
// if the display is not on card0/card1 default probing.
Console.WriteLine("Starting Neomotive Scan Tool (Raspberry Pi / DRM)...");

var card = Environment.GetEnvironmentVariable("SCANTOOL_DRM_CARD");

var scaling = double.TryParse(
    Environment.GetEnvironmentVariable("SCANTOOL_DRM_SCALING"),
    out var s) ? s : 1.0;

// UseSkia registers rendering only. UsePlatformDetect (which the desktop head
// uses) is what normally also wires up text shaping, but it would drag in X11
// here — so HarfBuzz has to be requested explicitly or AppBuilder.Setup throws
// "No text shaping system configured".
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHarfBuzz()
    .WithInterFont()
    .LogToTrace()
    .StartLinuxDrm(args, card, scaling);
