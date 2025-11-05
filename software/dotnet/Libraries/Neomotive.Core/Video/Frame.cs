namespace Neomotive.Video;

/// <summary>
/// Represents a single camera frame that wraps platform-specific frame data.
/// <para>
/// This design provides platform abstraction while maintaining zero-copy performance.
/// The Frame wraps the native frame object (e.g., OpenCV Mat) and provides common
/// metadata (Width, Height, Channels, Format, Timestamp) without requiring platform-specific queries.
/// </para>
/// <para>
/// For OpenCV consumers, use the ToMat() extension method to access the underlying Mat.
/// For ML consumers that need raw bytes, use the GetRawBytes() extension method.
/// </para>
/// <para>
/// IMPORTANT: The Frame owns the native frame object. When you dispose the Frame,
/// the native object is also disposed. Do not manually dispose the native frame object.
/// </para>
/// </summary>
public class Frame : IDisposable
{
    /// <summary>
    /// The platform-specific frame object (e.g., OpenCV Mat, Android Bitmap).
    /// <para>
    /// Use GetNativeFrame&lt;T&gt;() or TryGetNativeFrame&lt;T&gt;() for type-safe access.
    /// For OpenCV frames, use the ToMat() extension method instead.
    /// </para>
    /// <para>
    /// WARNING: Do not dispose the native frame object directly. The Frame owns it
    /// and will dispose it when Frame.Dispose() is called.
    /// </para>
    /// </summary>
    public object NativeFrame { get; }

    /// <summary>
    /// Width of the frame in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Height of the frame in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Number of color channels (typically 3 for BGR, 1 for grayscale).
    /// </summary>
    public int Channels { get; }

    /// <summary>
    /// Pixel format of the frame.
    /// </summary>
    public PixelFormat Format { get; }

    /// <summary>
    /// Timestamp when the frame was captured.
    /// </summary>
    public DateTime Timestamp { get; }

    public bool IsDisposed { get; private set; }

    private readonly Action<object>? _disposeAction;

    /// <summary>
    /// Creates a new Frame wrapping a platform-specific frame object.
    /// </summary>
    /// <param name="nativeFrame">The platform-specific frame object.</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="channels">Number of color channels.</param>
    /// <param name="format">Pixel format.</param>
    /// <param name="disposeAction">Optional action to dispose the native frame object.</param>
    public Frame(object nativeFrame, int width, int height, int channels, PixelFormat format, Action<object>? disposeAction = null)
    {
        NativeFrame = nativeFrame ?? throw new ArgumentNullException(nameof(nativeFrame));
        Width = width;
        Height = height;
        Channels = channels;
        Format = format;
        Timestamp = DateTime.UtcNow;
        _disposeAction = disposeAction;
    }

    /// <summary>
    /// Gets the native frame as a specific type.
    /// </summary>
    /// <typeparam name="T">The type to cast to (e.g., OpenCvSharp.Mat).</typeparam>
    /// <returns>The native frame cast to the specified type.</returns>
    /// <exception cref="InvalidCastException">If the native frame is not of type T.</exception>
    public T GetNativeFrame<T>() where T : class
    {
        if (NativeFrame is T typedFrame)
        {
            return typedFrame;
        }
        throw new InvalidCastException($"Native frame is {NativeFrame.GetType().Name}, not {typeof(T).Name}");
    }

    /// <summary>
    /// Tries to get the native frame as a specific type.
    /// </summary>
    /// <typeparam name="T">The type to cast to.</typeparam>
    /// <param name="nativeFrame">The native frame if successful, null otherwise.</param>
    /// <returns>True if the cast was successful, false otherwise.</returns>
    public bool TryGetNativeFrame<T>(out T? nativeFrame) where T : class
    {
        if (NativeFrame is T typedFrame)
        {
            nativeFrame = typedFrame;
            return true;
        }
        nativeFrame = null;
        return false;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            if (disposing)
            {
                // Call the dispose action if provided
                _disposeAction?.Invoke(NativeFrame);
            }

            IsDisposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Supported pixel formats for frames.
/// </summary>
public enum PixelFormat
{
    /// <summary>
    /// 8-bit grayscale (1 channel).
    /// </summary>
    Gray8,

    /// <summary>
    /// 24-bit BGR color (3 channels: Blue, Green, Red).
    /// </summary>
    BGR24,

    /// <summary>
    /// 32-bit BGRA color (4 channels: Blue, Green, Red, Alpha).
    /// </summary>
    BGRA32,

    /// <summary>
    /// 24-bit RGB color (3 channels: Red, Green, Blue).
    /// </summary>
    RGB24,

    /// <summary>
    /// 32-bit RGBA color (4 channels: Red, Green, Blue, Alpha).
    /// </summary>
    RGBA32
}
