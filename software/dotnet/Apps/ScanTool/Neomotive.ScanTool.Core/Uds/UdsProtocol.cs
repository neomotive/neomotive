using Meadow.Foundation.Telematics.J1979;
using Neomotive.Obd2;
using System;
using System.Collections.Generic;
using System.Text;

namespace Neomotive.ScanTool.Core;

/// <summary>
/// Protocol parsing and encoding helpers for ISO 14229-1 (UDS).
/// </summary>
public static class UdsProtocol
{
    /// <summary>
    /// Decodes a 2-byte SAE/ISO DTC code into standard letter+4-digit notation (e.g., P0100, U0100).
    /// </summary>
    public static string DecodeBaseDtc(byte hi, byte lo)
    {
        char prefix = (hi >> 6) switch
        {
            0 => 'P',
            1 => 'C',
            2 => 'B',
            3 => 'U',
            _ => 'P'
        };

        int d1 = (hi >> 4) & 0x03;
        int d2 = hi & 0x0F;
        int d3 = (lo >> 4) & 0x0F;
        int d4 = lo & 0x0F;

        return $"{prefix}{d1:X1}{d2:X1}{d3:X1}{d4:X1}";
    }

    /// <summary>
    /// Parses a UDS Service $19 SubFunction $02 response (0x59 0x02 [AvailabilityMask] [DTC records...])
    /// into a list of <see cref="UdsDtc"/>.
    /// </summary>
    public static IReadOnlyList<UdsDtc> ParseDtcResponse(byte[]? responseData)
    {
        if (responseData == null || responseData.Length < 3)
            return [];

        // Check for Service $59 and subfunction $02 (or $0A/$15)
        if (responseData[0] != ((byte)UdsService.ReadDtcInformation + Obd2Addresses.ResponseOffset))
            return [];

        var list = new List<UdsDtc>();

        // Byte 0: 0x59, Byte 1: SubFunction (0x02), Byte 2: DTCStatusAvailabilityMask
        // Remaining bytes are 4-byte records: [DTC_HI, DTC_MID, DTC_LO (FTB), Status]
        int index = 3;
        while (index + 4 <= responseData.Length)
        {
            byte hi = responseData[index];
            byte mid = responseData[index + 1];
            byte ftb = responseData[index + 2];
            byte statusByte = responseData[index + 3];

            // Ignore blank records (0x00 0x00 0x00)
            if (hi != 0 || mid != 0 || ftb != 0)
            {
                string baseCode = DecodeBaseDtc(hi, mid);
                string fullCode = $"{baseCode}-{ftb:X2}";
                string ftbDesc = GetFaultTypeDescription(ftb);
                string description = DtcDescriptions.Lookup(baseCode);
                var status = (UdsDtcStatusMask)statusByte;

                list.Add(new UdsDtc(baseCode, ftb, ftbDesc, fullCode, description, status));
            }

            index += 4;
        }

        return list;
    }

    /// <summary>
    /// Parses a UDS Service $22 ReadDataByIdentifier response (0x62 [DID_HI] [DID_LO] [Data...]).
    /// </summary>
    public static UdsDidValue? ParseDidResponse(ushort did, byte[]? responseData)
    {
        if (responseData == null || responseData.Length < 3)
            return null;

        if (responseData[0] != ((byte)UdsService.ReadDataByIdentifier + Obd2Addresses.ResponseOffset))
            return null;

        ushort respDid = (ushort)((responseData[1] << 8) | responseData[2]);
        if (respDid != did)
            return null;

        int dataLen = responseData.Length - 3;
        var dataBytes = new byte[dataLen];
        Array.Copy(responseData, 3, dataBytes, 0, dataLen);

        string name = GetStandardDidName(did);
        string displayValue = FormatDidData(did, dataBytes);

        return new UdsDidValue(did, name, dataBytes, displayValue);
    }

    /// <summary>
    /// Returns the standard DID name for common ISO 14229 / SAE J1979-2 DIDs.
    /// </summary>
    public static string GetStandardDidName(ushort did) => did switch
    {
        0xF180 => "Boot Software Identification",
        0xF181 => "Application Software Identification",
        0xF182 => "Application Data Identification",
        0xF183 => "Boot Software Fingerprint",
        0xF184 => "Application Software Fingerprint",
        0xF185 => "Application Data Fingerprint",
        0xF186 => "Active Diagnostic Session",
        0xF187 => "ECU Spare Part Number",
        0xF188 => "ECU Software Number",
        0xF189 => "ECU Software Version Number",
        0xF18A => "System Supplier Identifier",
        0xF18B => "ECU Manufacturing Date",
        0xF18C => "ECU Serial Number",
        0xF190 => "Vehicle Identification Number (VIN)",
        0xF191 => "ECU Hardware Number",
        0xF197 => "System Name / Engine Type",
        0xF1A0 => "ECU Diagnostic Identifier",
        _ => $"DID 0x{did:X4}"
    };

    /// <summary>
    /// Formats raw DID bytes into readable strings (ASCII for text DIDs, Hex for binary).
    /// </summary>
    public static string FormatDidData(ushort did, byte[] data)
    {
        if (data.Length == 0) return "";

        // Standard text DIDs
        if (did is 0xF190 or 0xF187 or 0xF188 or 0xF189 or 0xF18A or 0xF18C or 0xF191 or 0xF197)
        {
            string text = Encoding.ASCII.GetString(data).Trim('\0', ' ');
            if (IsPrintableAscii(text))
                return text;
        }

        // Active diagnostic session
        if (did == 0xF186 && data.Length >= 1)
        {
            return (UdsSessionType)data[0] switch
            {
                UdsSessionType.DefaultSession => "Default Session (0x01)",
                UdsSessionType.ProgrammingSession => "Programming Session (0x02)",
                UdsSessionType.ExtendedDiagnosticSession => "Extended Diagnostic Session (0x03)",
                UdsSessionType.SafetySystemDiagnosticSession => "Safety System Diagnostic Session (0x04)",
                var s => $"Session 0x{(byte)s:X2}"
            };
        }

        // Default hex representation
        return string.Join(" ", Array.ConvertAll(data, b => $"{b:X2}"));
    }

    private static bool IsPrintableAscii(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
        {
            if (c < 32 || c > 126) return false;
        }
        return true;
    }

    /// <summary>
    /// Checks if a payload is a UDS Negative Response (0x7F [ServiceId] [NRC])
    /// and returns the decoded NRC description if true.
    /// </summary>
    public static bool TryParseNegativeResponse(byte[]? data, out byte requestService, out UdsNrc nrc, out string errorDescription)
    {
        requestService = 0;
        nrc = 0;
        errorDescription = "";

        if (data == null || data.Length < 3 || data[0] != (byte)UdsService.NegativeResponse)
            return false;

        requestService = data[1];
        nrc = (UdsNrc)data[2];
        errorDescription = $"Negative Response (0x7F): Service 0x{requestService:X2} rejected with NRC 0x{(byte)nrc:X2} ({GetNrcDescription(data[2])})";
        return true;
    }

    /// <summary>
    /// Returns human-readable description for standard UDS Negative Response Codes.
    /// </summary>
    public static string GetNrcDescription(byte nrc) => ((UdsNrc)nrc) switch
    {
        UdsNrc.GeneralReject => "General Reject",
        UdsNrc.ServiceNotSupported => "Service Not Supported",
        UdsNrc.SubFunctionNotSupported => "Sub-Function Not Supported",
        UdsNrc.IncorrectMessageLengthOrInvalidFormat => "Incorrect Message Length Or Invalid Format",
        UdsNrc.ResponseTooLong => "Response Too Long",
        UdsNrc.BusyRepeatRequest => "Busy - Repeat Request",
        UdsNrc.ConditionsNotCorrect => "Conditions Not Correct",
        UdsNrc.RequestSequenceError => "Request Sequence Error",
        UdsNrc.NoResponseFromSubnetComponent => "No Response From Subnet Component",
        UdsNrc.FailurePreventsExecutionOfRequestedAction => "Failure Prevents Execution Of Requested Action",
        UdsNrc.RequestOutOfRange => "Request Out Of Range",
        UdsNrc.SecurityAccessDenied => "Security Access Denied",
        UdsNrc.InvalidKey => "Invalid Key",
        UdsNrc.ExceededNumberOfAttempts => "Exceeded Number Of Attempts",
        UdsNrc.RequiredTimeDelayNotExpired => "Required Time Delay Not Expired",
        UdsNrc.UploadDownloadNotAccepted => "Upload/Download Not Accepted",
        UdsNrc.TransferDataSuspended => "Transfer Data Suspended",
        UdsNrc.GeneralProgrammingFailure => "General Programming Failure",
        UdsNrc.WrongBlockSequenceCounter => "Wrong Block Sequence Counter",
        UdsNrc.RequestCorrectlyReceivedResponsePending => "Request Correctly Received - Response Pending",
        UdsNrc.SubFunctionNotSupportedInActiveSession => "Sub-Function Not Supported In Active Session",
        UdsNrc.ServiceNotSupportedInActiveSession => "Service Not Supported In Active Session",
        _ => $"Unknown NRC (0x{nrc:X2})"
    };

    /// <summary>
    /// Resolves standard ISO 15031-6 / SAE J2019 Fault Type Byte (FTB) failure subtype descriptions.
    /// </summary>
    public static string GetFaultTypeDescription(byte ftb) => ftb switch
    {
        0x00 => "No Subtype Information",
        0x01 => "General Electrical Failure",
        0x02 => "General Signal Failure",
        0x04 => "System Internal Failure",
        0x05 => "System Programming Failure",
        0x07 => "Mechanical Failure",
        0x08 => "Bus Signal / Message Failure",
        0x09 => "Component Internal Failure",
        0x11 => "Circuit Short to Ground",
        0x12 => "Circuit Short to Battery",
        0x13 => "Circuit Open",
        0x14 => "Circuit Short to Ground or Open",
        0x15 => "Circuit Short to Battery or Open",
        0x16 => "Circuit Voltage Below Threshold",
        0x17 => "Circuit Voltage Above Threshold",
        0x18 => "Circuit Current Below Threshold",
        0x19 => "Circuit Current Above Threshold",
        0x1A => "Circuit Resistance Below Threshold",
        0x1B => "Circuit Resistance Above Threshold",
        0x1C => "Circuit Voltage Out of Range",
        0x1D => "Circuit Current Out of Range",
        0x1E => "Circuit Resistance Out of Range",
        0x1F => "Circuit Intermittent",
        0x21 => "Signal Amplitude < Min",
        0x22 => "Signal Amplitude > Max",
        0x23 => "Signal Stuck Low",
        0x24 => "Signal Stuck High",
        0x25 => "Signal Shape / Waveform Failure",
        0x26 => "Signal Rate of Change Below Threshold",
        0x27 => "Signal Rate of Change Above Threshold",
        0x28 => "Signal Bias Level Out of Range",
        0x29 => "Signal Invalid",
        0x2F => "Signal Erratic",
        0x31 => "No Signal",
        0x36 => "Signal Frequency Too Low",
        0x37 => "Signal Frequency Too High",
        0x38 => "Signal Frequency Incorrect",
        0x41 => "General Checksum Failure",
        0x42 => "General Memory Failure",
        0x43 => "Special Memory Failure",
        0x44 => "Data Memory Failure",
        0x45 => "Program Memory Failure",
        0x46 => "Calibration Memory Failure",
        0x47 => "Watchdog / Safety MCU Failure",
        0x48 => "Supervision Software Failure",
        0x49 => "Internal Electronic Failure",
        0x4A => "Incorrect Component Installed",
        0x51 => "Not Programmed",
        0x52 => "Not Activated",
        0x53 => "Deactivated",
        0x54 => "Missing Calibration",
        0x55 => "Not Configured",
        0x61 => "Signal Calculation Failure",
        0x62 => "Signal Compare Failure",
        0x63 => "Circuit Protection Timeout",
        0x64 => "Signal Plausibility Failure",
        0x65 => "Signal Has Too Few Transitions",
        0x66 => "Signal Has Too Many Transitions",
        0x67 => "Signal Incorrect Event",
        0x71 => "Actuator Stuck",
        0x72 => "Actuator Stuck Open",
        0x73 => "Actuator Stuck Closed",
        0x74 => "Actuator Slipping",
        0x75 => "Emergency Position Not Reachable",
        0x76 => "Wrong Mounting Position",
        0x77 => "Commanded Position Not Reachable",
        0x78 => "Alignment / Adjustment Incorrect",
        0x79 => "Mechanical Linkage Failure",
        0x7A => "Fluid Leak / Seal Failure",
        0x81 => "Invalid Serial Data Received",
        0x82 => "Alive Counter Incorrect",
        0x83 => "Value of Signal Protection Calculation Incorrect",
        0x86 => "Signal Invalid",
        0x87 => "Missing Message",
        0x88 => "Bus Off",
        0x91 => "Parametric Parameter at Limit",
        0x92 => "Performance or Incorrect Operation",
        0x93 => "No Operation",
        0x94 => "Unexpected Operation",
        0x95 => "Incorrect Assembly",
        0x96 => "Component Internal Failure",
        0x97 => "Component Function Obstructed",
        0x98 => "Component Overtemperature",
        _ => $"Failure Type 0x{ftb:X2}"
    };
}
