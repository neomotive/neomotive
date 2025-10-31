using Neomotive.Video;
using NeoMotive.Services;
using OpenCvSharp;

Console.WriteLine("Camera Test Application with Speed Limit Detection");
Console.WriteLine("===================================================");
Console.WriteLine();

// Parse command line arguments
string cameraMode = "usb"; // default
int cameraIndex = 2;
string? videoFilePath = null;
bool enableDetection = false;
string? modelPath = null;

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
            case "--detect":
                enableDetection = true;
                break;
            case "--model":
                if (i + 1 < args.Length)
                {
                    modelPath = args[i + 1];
                    i++; // Skip next arg
                }
                else
                {
                    Console.WriteLine("Error: --model requires a file path argument");
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
Console.WriteLine($"Detection: {(enableDetection ? "Enabled" : "Disabled")}");
if (enableDetection && modelPath != null)
{
    Console.WriteLine($"Model Path: {modelPath}");
}
Console.WriteLine("Press ESC in the camera window to exit");
Console.WriteLine("Starting camera...");
Console.WriteLine();

Frame? latestFrame = null;
object frameLock = new object();
int frameCount = 0;
int detectionCount = 0;

ICamera? camera = null;
SpeedLimitService? detector = null;
try
{
    // Create camera based on mode
    camera = cameraMode == "usb"
        ? new UsbCamera(cameraIndex: cameraIndex)
        : new VideoFileCamera(videoFilePath!);

    // Create detector if enabled
    if (enableDetection)
    {
        Console.WriteLine("Initializing speed limit detector...");
        detector = modelPath != null ? new SpeedLimitService(modelPath) : new SpeedLimitService();
        Console.WriteLine("Detector initialized.");
    }

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
        Mat? matToDisplay = null;

        // Get and clone the latest frame (thread-safe)
        lock (frameLock)
        {
            if (latestFrame != null)
            {
                // Clone the Mat so we own a copy that won't be disposed by the camera thread
                var mat = latestFrame.ToMat();
                matToDisplay = mat.Clone();
            }
        }

        // Display frame on main thread
        if (matToDisplay != null)
        {
            try
            {
                // Run detection if enabled
                if (detector != null)
                {
                    var detections = detector.CheckForSpeedLimit(matToDisplay, saveToFile: false, drawDetections: true);
                    if (detections.Count > 0)
                    {
                        detectionCount++;
                        // Detections are already drawn on the mat
                    }
                }

                string windowTitle = cameraMode == "usb" ? $"USB Camera Feed (Index: {cameraIndex})" : "Video File Camera Feed";
                if (enableDetection)
                {
                    windowTitle += " - Detection Enabled";
                }
                Cv2.ImShow(windowTitle, matToDisplay);
            }
            finally
            {
                // Dispose our cloned mat
                matToDisplay.Dispose();
            }
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
    if (enableDetection)
    {
        Console.WriteLine($"Frames with detections: {detectionCount}");
    }
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
    detector?.Dispose();
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
    Console.WriteLine("  --detect              Enable speed limit sign detection");
    Console.WriteLine("  --model <path>        Path to custom ONNX model (optional, uses default if not specified)");
    Console.WriteLine("  --help, -h            Show this help message");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  SignCamera.DesktopTest                                    # Use USB camera index 2");
    Console.WriteLine("  SignCamera.DesktopTest --usb 0                            # Use USB camera index 0");
    Console.WriteLine("  SignCamera.DesktopTest --file video.mp4                   # Use video file");
    Console.WriteLine("  SignCamera.DesktopTest --file video.mp4 --detect          # Use video file with detection");
    Console.WriteLine("  SignCamera.DesktopTest --usb 0 --detect --model model.onnx  # USB camera with custom model");
}
