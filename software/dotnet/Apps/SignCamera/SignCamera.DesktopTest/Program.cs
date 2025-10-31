using Neomotive.Video;
using OpenCvSharp;

Console.WriteLine("Camera Test Application");
Console.WriteLine("=======================");
Console.WriteLine();

// Parse command line arguments
string cameraMode = "usb"; // default
int cameraIndex = 2;
string? videoFilePath = null;

if (args.Length > 0)
{
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i].ToLower())
        {
            case "--usb":
                cameraMode = "usb";
                // Check if next arg is a camera index
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int index))
                {
                    cameraIndex = index;
                    i++; // Skip next arg
                }
                break;
            case "--file":
                cameraMode = "file";
                if (i + 1 < args.Length)
                {
                    videoFilePath = args[i + 1];
                    i++; // Skip next arg
                }
                else
                {
                    Console.WriteLine("Error: --file requires a file path argument");
                    Console.WriteLine();
                    PrintUsage();
                    return 1;
                }
                break;
            case "--help":
            case "-h":
                PrintUsage();
                return 0;
            default:
                Console.WriteLine($"Unknown argument: {args[i]}");
                Console.WriteLine();
                PrintUsage();
                return 1;
        }
    }
}

// Display current configuration
if (cameraMode == "usb")
{
    Console.WriteLine($"Camera Mode: USB (Index: {cameraIndex})");
}
else
{
    Console.WriteLine($"Camera Mode: Video File");
    Console.WriteLine($"File Path: {videoFilePath}");
}
Console.WriteLine("Press ESC in the camera window to exit");
Console.WriteLine("Starting camera...");
Console.WriteLine();

Frame? latestFrame = null;
object frameLock = new object();
int frameCount = 0;

ICamera? camera = null;
try
{
    // Create camera based on mode
    camera = cameraMode == "usb"
        ? new UsbCamera(cameraIndex: cameraIndex)
        : new VideoFileCamera(videoFilePath!);

    // Subscribe to frame captured event
    camera.FrameCaptured += (sender, frame) =>
    {
        frameCount++;

        // Log frame info for debugging
        if (frameCount % 30 == 0) // Log every 30 frames
        {
            Console.WriteLine($"Frame {frameCount}: Size={frame.Width}x{frame.Height}, Channels={frame.Channels}, Format={frame.Format}");
        }

        // Store the latest frame (thread-safe)
        lock (frameLock)
        {
            // Dispose old frame if exists
            latestFrame?.Dispose();
            // Store new frame
            latestFrame = frame;
        }
    };

    // Start capturing frames
    await camera.StartCapture();

    Console.WriteLine("Camera started. Displaying video feed...");

    // Keep the application running and process window events on main thread
    // Press ESC key to exit
    while (true)
    {
        Frame? frameToDisplay = null;

        // Get the latest frame (thread-safe)
        lock (frameLock)
        {
            if (latestFrame != null)
            {
                // Keep reference to frame for display
                frameToDisplay = latestFrame;
            }
        }

        // Display frame on main thread
        if (frameToDisplay != null)
        {
            // Get the native Mat from the Frame (no copy, no conversion)
            var mat = frameToDisplay.ToMat();
            string windowTitle = cameraMode == "usb" ? $"USB Camera Feed (Index: {cameraIndex})" : "Video File Camera Feed";
            Cv2.ImShow(windowTitle, mat);
        }

        var key = Cv2.WaitKey(1);
        if (key == 27) // ESC key
        {
            Console.WriteLine("ESC pressed. Stopping camera...");
            break;
        }
    }

    // Stop capture
    await camera.StopCapture();

    // Clean up latest frame
    lock (frameLock)
    {
        latestFrame?.Dispose();
        latestFrame = null;
    }

    // Close all OpenCV windows
    Cv2.DestroyAllWindows();

    Console.WriteLine($"Camera stopped. Total frames captured: {frameCount}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    return 1;
}
finally
{
    if (camera is IDisposable d)
    {
        d.Dispose();
    }
}

return 0;

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  SignCamera.DesktopTest [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --usb [index]         Use USB camera (default). Optional camera index (default: 2)");
    Console.WriteLine("  --file <path>         Use video file as camera feed (loops continuously)");
    Console.WriteLine("  --help, -h            Show this help message");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  SignCamera.DesktopTest                                    # Use USB camera index 2");
    Console.WriteLine("  SignCamera.DesktopTest --usb 0                            # Use USB camera index 0");
    Console.WriteLine("  SignCamera.DesktopTest --file video.mp4                   # Use video file");
    Console.WriteLine("  SignCamera.DesktopTest --file \"C:\\Videos\\test.mp4\"       # Use video file with full path");
}
