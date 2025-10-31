using OpenCvSharp;
using System.Diagnostics;

namespace Neomotive.Video;

public class VideoFileCamera : ICamera, IDisposable
{
    private readonly VideoCapture _capture;
    private readonly string _filePath;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _captureTask;

    public bool IsDisposed { get; private set; }
    public bool IsCapturing { get; private set; }

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

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
