using OpenCvSharp;
using System.Diagnostics;

namespace Neomotive.Video;

public class VideoFileCamera : ICamera, IDisposable
{
    private readonly VideoCapture _capture;
    private readonly string _filePath;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _captureTask;

    /// <summary>
    /// Indicates if an object has been disposed. Once an object is disposed, it should not be used anymore.
    /// </summary>
    public bool IsDisposed { get; private set; }
    /// <summary>
    /// A read-only boolean property indicating whether the object is currently capturing.
    /// </summary>
    public bool IsCapturing { get; private set; }

    /// <summary>
    /// Raised when a frame is captured.
    /// </summary>
    public event EventHandler<Frame>? FrameCaptured;

    public VideoFileCamera(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Video file not found: {filePath}", filePath);
        }

        _filePath = filePath;
        _capture = new VideoCapture(filePath);

        if (!_capture.IsOpened())
        {
            throw new Exception($"Failed to open video file: {filePath}");
        }

        Debug.WriteLine($"Video file opened: {filePath}");
        Debug.WriteLine($"Frame count: {_capture.FrameCount}, FPS: {_capture.Fps}");
    }

    /// <summary>
    /// Initiates the capture process. If capture is already running, throws an exception.
    /// </summary>
    /// <exception cref="InvalidOperationException">Capture is already running.</exception>
    /// <remarks>This method starts a capture loop and sets the IsCapturing flag to true.</remarks>
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
    /// Stops the capture if it is currently running.
    /// </summary>
    /// <remarks>
    /// If capture is not currently active, this method does nothing. After stopping the capture task, the _cancellationTokenSource is disposed and set to null.
    /// </remarks>
    /// <exception cref="System.Exception">Any exception that might be thrown by the capture task.</exception>
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
            // Reached end of video, loop back to start
            Debug.WriteLine("End of video reached, looping back to start");
            _capture.Set(VideoCaptureProperties.PosFrames, 0);
            mat.Dispose();

            // Try to read the first frame again
            mat = new Mat();
            if (!_capture.Read(mat) || mat.Empty())
            {
                mat.Dispose();
                Debug.WriteLine("Error: Could not read a frame from the video file.");
                return;
            }
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

    /// <summary>
    /// This method performs cleanup operations.
    /// </summary>
    /// <exception cref="Exception">Any exception that might occur during the cleanup process.</exception>
    /// <remarks>The Dispose(bool disposing) method contains the actual cleanup code.</remarks>
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
