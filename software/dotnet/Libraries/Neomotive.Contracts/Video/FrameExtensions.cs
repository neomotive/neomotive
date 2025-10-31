using OpenCvSharp;

namespace Neomotive.Video;

/// <summary>
/// Extension methods for Frame class to work with OpenCV.
/// <para>
/// These extensions provide convenient access to OpenCV-specific operations on frames
/// captured by cameras using OpenCV (e.g., UsbCamera).
/// </para>
/// </summary>
public static class FrameExtensions
{
    /// <summary>
    /// Gets the OpenCV Mat from a Frame (zero-copy).
    /// <para>
    /// Use this method when you need to display the frame with OpenCV (Cv2.ImShow)
    /// or process it with OpenCV functions (filtering, transformations, etc.).
    /// </para>
    /// <para>
    /// IMPORTANT: The returned Mat is owned by the Frame. Do NOT dispose it manually.
    /// The Mat will be disposed when the Frame is disposed.
    /// </para>
    /// </summary>
    /// <param name="frame">The frame to get the Mat from.</param>
    /// <returns>The Mat wrapped by the frame (zero-copy, no data duplication).</returns>
    /// <exception cref="ArgumentNullException">If frame is null.</exception>
    /// <exception cref="InvalidCastException">If the frame doesn't contain a Mat.</exception>
    /// <example>
    /// <code>
    /// camera.FrameCaptured += (sender, frame) =>
    /// {
    ///     var mat = frame.ToMat();
    ///     Cv2.ImShow("Camera", mat);  // Display the frame
    ///     // Do NOT dispose mat - Frame owns it
    /// };
    /// </code>
    /// </example>
    public static Mat ToMat(this Frame frame)
    {
        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        return frame.GetNativeFrame<Mat>();
    }

    /// <summary>
    /// Creates an independent copy of the frame as a new Mat.
    /// <para>
    /// Use this when you need to keep the frame data after the original Frame is disposed,
    /// or when you need to modify the frame without affecting the original.
    /// </para>
    /// <para>
    /// The caller is responsible for disposing the returned Mat.
    /// </para>
    /// </summary>
    /// <param name="frame">The frame to clone.</param>
    /// <returns>A new Mat containing a deep copy of the frame data.</returns>
    /// <exception cref="ArgumentNullException">If frame is null.</exception>
    /// <exception cref="InvalidCastException">If the frame doesn't contain a Mat.</exception>
    /// <example>
    /// <code>
    /// Mat savedFrame = null;
    /// camera.FrameCaptured += (sender, frame) =>
    /// {
    ///     savedFrame?.Dispose();  // Dispose previous frame
    ///     savedFrame = frame.CloneMat();  // Keep a copy
    /// };
    /// </code>
    /// </example>
    public static Mat CloneMat(this Frame frame)
    {
        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        var mat = frame.GetNativeFrame<Mat>();
        return mat.Clone();
    }

    /// <summary>
    /// Gets the raw image data as a byte array (requires memory copy).
    /// <para>
    /// Use this method when you need to:
    /// - Pass frame data to ML frameworks that require byte[] input
    /// - Serialize/save frame data
    /// - Send frame data over network
    /// </para>
    /// <para>
    /// NOTE: This method creates a copy of the frame data. For zero-copy access,
    /// use ToMat() instead if you're working with OpenCV.
    /// </para>
    /// </summary>
    /// <param name="frame">The frame to get bytes from.</param>
    /// <returns>A new byte array containing the frame's raw pixel data.</returns>
    /// <exception cref="ArgumentNullException">If frame is null.</exception>
    /// <exception cref="InvalidCastException">If the frame doesn't contain a Mat.</exception>
    /// <example>
    /// <code>
    /// camera.FrameCaptured += (sender, frame) =>
    /// {
    ///     byte[] imageData = frame.GetRawBytes();
    ///     // Pass to ML model, save to file, etc.
    ///     var tensor = ConvertToTensor(imageData, frame.Width, frame.Height);
    /// };
    /// </code>
    /// </example>
    public static byte[] GetRawBytes(this Frame frame)
    {
        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        var mat = frame.GetNativeFrame<Mat>();

        // Calculate total data size
        int stride = (int)mat.Step();
        int dataSize = stride * mat.Height;

        // Copy Mat data to byte array
        byte[] data = new byte[dataSize];
        System.Runtime.InteropServices.Marshal.Copy(mat.Data, data, 0, dataSize);

        return data;
    }
}
