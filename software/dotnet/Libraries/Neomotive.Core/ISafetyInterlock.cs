namespace Neomotive;

public interface ISafetyInterlock
{
    event EventHandler<bool>? Changed;
    bool IsSafe { get; }
}
