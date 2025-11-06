using Meadow;
using Meadow.Hardware;
using Meadow.Units;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Neomotive.TransferCase;

public abstract class AnalogTransferCaseGearSelectorSwitch : ITransferCaseGearSelector, IDisposable
{
    /// <summary>
    /// Raised when the requested TransferCasePosition changes.
    /// </summary>
    public event EventHandler<TransferCasePosition>? RequestedPositionChanged;

    private readonly IAnalogInputPort _inputPort;
    private readonly TransferCaseSwitchSelectionBounds[] _switchPositions;
    private readonly Timer _switchPollTimer;

    private Voltage _lastReading;
    private TransferCasePosition _currentPosition = TransferCasePosition.Unknown;

    public static TimeSpan DefaultCheckPeriod = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Indicates whether this object has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }
    /// <summary>
    /// Gets the requested TransferCasePosition.
    /// </summary>
    public TransferCasePosition RequestedPosition { get; }
    /// <summary>
    /// Represents the time period for checks.
    /// </summary>
    public TimeSpan CheckPeriod { get; }

    public AnalogTransferCaseGearSelectorSwitch(IAnalogInputPort inputPort, TransferCaseSwitchSelectionBounds[] switchPositions)
        : this(inputPort, switchPositions, DefaultCheckPeriod)
    {
    }

    public AnalogTransferCaseGearSelectorSwitch(IAnalogInputPort inputPort, TransferCaseSwitchSelectionBounds[] switchPositions, TimeSpan checkPeriod)
    {
        _inputPort = inputPort;
        _switchPositions = switchPositions;
        CheckPeriod = checkPeriod;
        _switchPollTimer = new Timer(SwitchCheckTimerProc, null, CheckPeriod, TimeSpan.FromMilliseconds(-1));
    }

    /// <summary>
    /// Represents the current position of the transfer case switch.
    /// </summary>
    public TransferCasePosition CurrentSwitchPosition
    {
        get => _currentPosition;
        private set
        {
            if (value == _currentPosition) return;

            Resolver.Log.Info($"Setting switch position to {value} ({_lastReading.Volts:0.00} volts)");

            _currentPosition = value;
            RequestedPositionChanged?.Invoke(this, CurrentSwitchPosition);
        }
    }

    /// <summary>
    /// Logs the minimum and maximum voltages associated with each switch position.
    /// </summary>
    /// <remarks>This method logs the switch positions, their respective minimum voltages, and maximum voltages to the Resolver.Log.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when _switchPositions is null.</exception>
    public void ReportSettings()
    {
        foreach (var position in _switchPositions)
        {
            Resolver.Log.Info($"{position.Position}: {position.MinVoltage}..{position.MaxVoltage}");
        }
    }

    private void SwitchCheckTimerProc(object _)
    {
        if (IsDisposed) return;


        try
        {
            _lastReading = _inputPort.Read().Result;

            var detectedPosition = TransferCasePosition.Unknown;

            foreach (var position in _switchPositions)
            {
                if (position.IsActive(_lastReading))
                {
                    detectedPosition = position.Position;
                    break;
                }
            }

            CurrentSwitchPosition = detectedPosition;

            if (detectedPosition == TransferCasePosition.Unknown)
            {
                _switchPollTimer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(-1));
            }
            else
            {
                _switchPollTimer.Change(CheckPeriod, TimeSpan.FromMilliseconds(-1));
            }
        }
        catch (Exception ex)
        {
            Resolver.Log.Info($"Failed reading selector switch: {ex.Message}");
            _switchPollTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(-1));
        }
    }


    protected virtual void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            if (disposing)
            {
            }

            IsDisposed = true;
        }
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    /// <remarks>This method also calls GC.SuppressFinalize on this object.</remarks>
    /// <exception cref="Exception">Could be thrown in case of an error during the disposal process.</exception>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Returns an enumerator for the collection of TransferCaseSwitchSelectionBounds.
    /// </summary>
    /// <returns>An IEnumerator for the collection of TransferCaseSwitchSelectionBounds.</returns>
    public IEnumerator<TransferCaseSwitchSelectionBounds> GetEnumerator()
    {
        return _switchPositions.Cast<TransferCaseSwitchSelectionBounds>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
