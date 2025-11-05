namespace Neomotive.Video;

public interface ICamera
{
    bool IsCapturing { get; }

    event EventHandler<Frame>? FrameCaptured;

    Task StartCapture();
    Task StopCapture();
}
