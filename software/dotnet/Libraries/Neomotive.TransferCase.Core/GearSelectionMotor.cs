using Meadow;
using Meadow.Foundation.Motors;
using Meadow.Hardware;
using System;
using System.Threading;
using static Meadow.Foundation.Motors.BidirectionalDcMotor;

namespace Neomotive.TransferCase;

public class GearSelectionMotor : IGearSelectionMotor
{
    /// <summary>
    /// Raised when the MotorState changes.
    /// </summary>
    public event EventHandler<MotorState>? StateChanged = default!;

    private readonly BidirectionalDcMotor _motor;

    private IDigitalOutputPort? _lockRelease;
    private TimeSpan _lockReleaseDelay;

    public GearSelectionMotor(IPin pinA, IPin pinB, IPin? lockRelease, TimeSpan? releaseDelay)
    {
        _motor = new BidirectionalDcMotor(
            pinA.CreateDigitalOutputPort(false),
            pinB.CreateDigitalOutputPort(false));

        if (lockRelease != null)
        {
            _lockRelease = lockRelease.CreateDigitalOutputPort(false);
        }
        _lockReleaseDelay = releaseDelay ?? TimeSpan.Zero;

        _motor.StateChanged += OnMotorStateChanged;
    }

    private void OnMotorStateChanged(object sender, MotorState e)
    {
        StateChanged?.Invoke(this, e);
    }

    /// <summary>
    /// Indicates whether the motor is currently moving, based on its state. The motor is considered moving if its state is not Stopped.
    /// </summary>
    public bool IsMoving => _motor.State != MotorState.Stopped;

    /// <summary>
    /// Begins shifting the motor upwards. If _lockRelease is not null, it will first be set to true and the thread will sleep for _lockReleaseDelay before starting the motor.
    /// </summary>
    /// <exception cref="Exception">Any exception that might occur during the execution of the method or when accessing _motor.</exception>
    public void BeginShiftUp()
    {
        if (_lockRelease != null)
        {
            _lockRelease.State = true;
            Thread.Sleep(_lockReleaseDelay);
        }
        _motor.StartCounterClockwise();
    }

    /// <summary>
    /// Begins a shift down process, sets lock release state to true and sleeps for _lockReleaseDelay milliseconds before starting the motor in clockwise direction.
    /// </summary>
    /// <remarks>This method assumes that _lockRelease and _motor are properly initialized.</remarks>
    /// <exception cref="System.NullReferenceException">Thrown if _lockRelease is null.</exception>
    public void BeginShiftDown()
    {
        if (_lockRelease != null)
        {
            _lockRelease.State = true;
            Thread.Sleep(_lockReleaseDelay);
        }
        _motor.StartClockwise();
    }

    /// <summary>
    /// Stops the shift by stopping the motor and releasing any locks if necessary.
    /// </summary>
    /// <remarks>
    /// The method first stops the motor and then releases the lock if _lockRelease is not null. A delay of _lockReleaseDelay is observed before releasing the lock.
    /// </remarks>
    /// <exception cref="Exception">Any exception that might occur while stopping the motor or releasing the lock.</exception>
    public void StopShift()
    {
        _motor.Stop();
        if (_lockRelease != null)
        {
            Thread.Sleep(_lockReleaseDelay);
            _lockRelease.State = false;
        }
    }
}
