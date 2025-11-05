using OpenCvSharp;
using System.Diagnostics;

namespace Neomotive.Video;

public class UsbCamera : ICamera, IDisposable
{
    private readonly VideoCapture _capture;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _captureTask;

    /// <summary>
    /// Gets a value indicating whether this object has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }
    /// <summary>
    /// Gets a value indicating whether the object is currently capturing. This property is read-only.
    /// </summary>
    public bool IsCapturing { get; private set; }

    /// <summary>
    /// Raised when a frame is captured.
    /// </summary>
    public event EventHandler<Frame>? FrameCaptured;

    public UsbCamera(int cameraIndex = 3)
    {
        _capture = new VideoCapture(cameraIndex);
        if (!_capture.IsOpened())
        {
            throw new Exception($"Failed to open camera with index {cameraIndex}");
        }

        // Optional: Set resolution (depends on camera support)
        // capture.Set(VideoCaptureProperties.FrameWidth, 1280);
        // capture.Set(VideoCaptureProperties.FrameHeight, 720);

    }

    /// <summary>
    /// Initiates the capture operation. If capture is already running, throws an exception.
    /// </summary>
    /// <param name="">No parameters for this method.</param>
    /// <exception cref="InvalidOperationException">Thrown if capture is already running.</exception>
    public async Task StartCapture()
    {
        if (IsCapturing)
        {
            throw new InvalidOperationException("Capture is already running.");
        }

        _cancellationTokenSource = new CancellationTokenSource();
        IsCapturing = true;

        _captureTask = Task.Run(() => CaptureLoop(_cancellationTokenSource.Token));

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops the capture if currently capturing.
    /// </summary>
    /// <remarks>
    /// If not currently capturing, does nothing.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when _cancellationTokenSource is already disposed.</exception>
    public async Task StopCapture()
    {
        if (!IsCapturing)
        {
            return;
        }

        _cancellationTokenSource?.Cancel();

        if (_captureTask != null)
        {
            await _captureTask;
        }

        IsCapturing = false;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            CaptureFrame();

            // Small delay to prevent maxing out CPU
            Thread.Sleep(100);
        }
    }

    private void CaptureFrame()
    {
        var mat = new Mat();

        // Read a single frame
        if (!_capture.Read(mat) || mat.Empty())
        {
            mat.Dispose();
            Debug.WriteLine("Error: Could not read a frame from the camera.");
            return;
        }

        // Wrap Mat in Frame
        var frame = CreateFrameFromMat(mat);

        // Raise event with the frame
        FrameCaptured?.Invoke(this, frame);

        Debug.WriteLine($"Frame captured at {DateTime.Now:HH:mm:ss.fff}");
    }

    private static Frame CreateFrameFromMat(Mat mat)
    {
        if (mat == null || mat.Empty())
        {
            throw new ArgumentException("Mat is null or empty", nameof(mat));
        }

        int width = mat.Width;
        int height = mat.Height;
        int channels = mat.Channels();

        // Determine pixel format
        PixelFormat format = channels switch
        {
            1 => PixelFormat.Gray8,
            3 => PixelFormat.BGR24,
            4 => PixelFormat.BGRA32,
            _ => throw new NotSupportedException($"Unsupported number of channels: {channels}")
        };

        // Wrap the Mat in a Frame with a dispose action
        return new Frame(
            nativeFrame: mat,
            width: width,
            height: height,
            channels: channels,
            format: format,
            disposeAction: obj => ((Mat)obj).Dispose()
        );
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            if (disposing)
            {
                // Stop capture if running
                if (IsCapturing)
                {
                    StopCapture().GetAwaiter().GetResult();
                }

                _capture.Dispose();
                _cancellationTokenSource?.Dispose();
            }

            IsDisposed = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~UsbCamera()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    /// <remarks>
    /// This method calls the 'Dispose(bool disposing)' method with a value of true for disposing.
    /// It then suppresses finalization by calling GC.SuppressFinalize on the current object.
    /// </remarks>
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
