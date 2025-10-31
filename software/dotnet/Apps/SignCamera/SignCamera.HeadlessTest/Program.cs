using Neomotive.Video;
using OpenCvSharp;

Console.WriteLine("=== USB Camera Headless Test ===");
Console.WriteLine("Capturing 10 frames to disk for verification");
Console.WriteLine();

// Configuration
const int FramesToCapture = 10;
const int DelayBetweenFramesMs = 1000; // 1 second between frames
const string OutputDirectory = "captured_frames";

// Create output directory
if (!Directory.Exists(OutputDirectory))
{
    Directory.CreateDirectory(OutputDirectory);
    Console.WriteLine($"Created output directory: {OutputDirectory}");
}
else
{
    Console.WriteLine($"Using existing output directory: {OutputDirectory}");
}

int framesCaptured = 0;
DateTime lastSaveTime = DateTime.MinValue;
object lockObj = new object();

try
{
    Console.WriteLine("Initializing camera...");
    using var camera = new UsbCamera(cameraIndex: 0);
    Console.WriteLine("Camera initialized successfully");
    Console.WriteLine();

    // Subscribe to frame captured event
    camera.FrameCaptured += (sender, frame) =>
    {
        lock (lockObj)
        {
            // Check if we should save this frame
            var now = DateTime.Now;
            var timeSinceLastSave = (now - lastSaveTime).TotalMilliseconds;

            if (framesCaptured < FramesToCapture && timeSinceLastSave >= DelayBetweenFramesMs)
            {
                try
                {
                    // Get the Mat from the frame
                    var mat = frame.ToMat();

                    // Generate filename with timestamp
                    string timestamp = now.ToString("yyyyMMdd_HHmmss_fff");
                    string filename = $"frame_{framesCaptured + 1:D3}_{timestamp}.png";
                    string filepath = Path.Combine(OutputDirectory, filename);

                    // Save the frame to disk
                    Cv2.ImWrite(filepath, mat);

                    // Get file info
                    var fileInfo = new FileInfo(filepath);
                    long fileSizeKB = fileInfo.Length / 1024;

                    framesCaptured++;
                    lastSaveTime = now;

                    Console.WriteLine($"[{framesCaptured}/{FramesToCapture}] Saved: {filename}");
                    Console.WriteLine($"         Size: {mat.Width}x{mat.Height}, {mat.Channels()} channels, {fileSizeKB} KB");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving frame: {ex.Message}");
                }
            }
        }
    };

    // Start capturing frames
    Console.WriteLine("Starting capture...");
    await camera.StartCapture();
    Console.WriteLine("Capture started");
    Console.WriteLine();

    // Wait until we've captured all frames
    while (framesCaptured < FramesToCapture)
    {
        await Task.Delay(100);
    }

    Console.WriteLine();
    Console.WriteLine("Capture complete. Stopping camera...");

    // Stop capture
    await camera.StopCapture();

    Console.WriteLine("Camera stopped");
    Console.WriteLine();
    Console.WriteLine($"Successfully captured {framesCaptured} frames to {OutputDirectory}/");
    Console.WriteLine("Test completed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($"ERROR: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    return 1;
}

return 0;
