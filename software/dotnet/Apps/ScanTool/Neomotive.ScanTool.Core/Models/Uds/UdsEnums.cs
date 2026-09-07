using System;

namespace Neomotive.ScanTool.Core;

/// <summary>
/// Unified Diagnostic Services (ISO 14229-1) service identifiers.
/// </summary>
public enum UdsService : byte
{
    DiagnosticSessionControl       = 0x10,
    EcuReset                       = 0x11,
    ClearDiagnosticInformation     = 0x14,
    ReadDtcInformation             = 0x19,
    ReadDataByIdentifier           = 0x22,
    ReadMemoryByAddress            = 0x23,
    ReadScalingDataByIdentifier    = 0x24,
    SecurityAccess                 = 0x27,
    CommunicationControl           = 0x28,
    ReadDataByPeriodicIdentifier   = 0x2A,
    DynamicallyDefineDataIdentifier= 0x2C,
    WriteDataByIdentifier          = 0x2E,
    InputOutputControlByIdentifier = 0x2F,
    RoutineControl                 = 0x31,
    RequestDownload                = 0x34,
    RequestUpload                  = 0x35,
    TransferData                   = 0x36,
    RequestTransferExit            = 0x37,
    WriteMemoryByAddress           = 0x3D,
    TesterPresent                  = 0x3E,
    ControlDtcSetting              = 0x85,
    NegativeResponse               = 0x7F
}

/// <summary>
/// Sub-functions for UDS Service $19 (ReadDtcInformation).
/// </summary>
public enum UdsDtcSubFunction : byte
{
    ReportNumberOfDtcByStatusMask          = 0x01,
    ReportDtcByStatusMask                  = 0x02,
    ReportDtcSnapshotIdentification        = 0x03,
    ReportDtcSnapshotRecordByDtcNumber     = 0x04,
    ReportDtcExtendedDataRecordByDtcNumber = 0x06,
    ReportNumberOfDtcBySeverityMaskRecord  = 0x07,
    ReportDtcBySeverityInformationRecord   = 0x08,
    ReportSupportedDtcs                    = 0x0A,
    ReportFirstTestFailedDtc               = 0x0B,
    ReportFirstConfirmedDtc                = 0x0C,
    ReportMostRecentTestFailedDtc          = 0x0D,
    ReportMostRecentConfirmedDtc           = 0x0E,
    ReportDtcFaultDetectionCounter         = 0x14,
    ReportDtcWithPermanentStatus           = 0x15
}

/// <summary>
/// ISO 14229-1 DTC Status Mask bits.
/// </summary>
[Flags]
public enum UdsDtcStatusMask : byte
{
    None                               = 0x00,
    TestFailed                         = 0x01,
    TestFailedThisOperationCycle       = 0x02,
    PendingDtc                         = 0x04,
    ConfirmedDtc                       = 0x08,
    TestNotCompletedSinceLastClear     = 0x10,
    TestFailedSinceLastClear           = 0x20,
    TestNotCompletedThisOperationCycle = 0x40,
    WarningIndicatorRequested          = 0x80
}

/// <summary>
/// Diagnostic session types for Service $10.
/// </summary>
public enum UdsSessionType : byte
{
    DefaultSession                     = 0x01,
    ProgrammingSession                 = 0x02,
    ExtendedDiagnosticSession          = 0x03,
    SafetySystemDiagnosticSession      = 0x04
}

/// <summary>
/// Standard UDS Negative Response Codes (NRC) defined in ISO 14229-1.
/// </summary>
public enum UdsNrc : byte
{
    GeneralReject                               = 0x10,
    ServiceNotSupported                         = 0x11,
    SubFunctionNotSupported                     = 0x12,
    IncorrectMessageLengthOrInvalidFormat       = 0x13,
    ResponseTooLong                             = 0x14,
    BusyRepeatRequest                           = 0x21,
    ConditionsNotCorrect                        = 0x22,
    RequestSequenceError                        = 0x24,
    NoResponseFromSubnetComponent               = 0x25,
    FailurePreventsExecutionOfRequestedAction   = 0x26,
    RequestOutOfRange                           = 0x31,
    SecurityAccessDenied                        = 0x33,
    InvalidKey                                  = 0x35,
    ExceededNumberOfAttempts                    = 0x36,
    RequiredTimeDelayNotExpired                 = 0x37,
    UploadDownloadNotAccepted                   = 0x70,
    TransferDataSuspended                       = 0x71,
    GeneralProgrammingFailure                   = 0x72,
    WrongBlockSequenceCounter                   = 0x73,
    RequestCorrectlyReceivedResponsePending     = 0x78,
    SubFunctionNotSupportedInActiveSession      = 0x7E,
    ServiceNotSupportedInActiveSession          = 0x7F
}
