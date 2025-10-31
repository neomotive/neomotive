using Neomotive.Video;
using OpenCvSharp;

Console.WriteLine("USB Camera Test Application");
Console.WriteLine("Press ESC in the camera window to exit");
Console.WriteLine("Starting camera...");

Frame? latestFrame = null;
object frameLock = new object();
int frameCount = 0;

try
{
    using var camera = new UsbCamera(cameraIndex: 2);

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
            Cv2.ImShow("USB Camera Feed", mat);
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

return 0;
