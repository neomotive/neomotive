using System;
using System.Collections.Generic;
using System.Text;

namespace Neomotive.ScanTool.Core;

/// <summary>
/// Pure static decoding helpers — no bus dependency, fully unit-testable.
/// </summary>
public static class Obd2Protocol
{
    /// <summary>
    /// Decodes a two-byte OBD2 DTC into its human-readable code string (e.g. "P0300").
    /// Returns null if both bytes are zero.
    /// </summary>
    public static string? DecodeDtcCode(byte hi, byte lo)
    {
        if (hi == 0 && lo == 0) return null;

        char prefix = ((hi >> 6) & 0x03) switch
        {
            0 => 'P',
            1 => 'C',
            2 => 'B',
            3 => 'U',
            _ => '?'
        };
        int d1 = (hi >> 4) & 0x03;
        int d2 = hi & 0x0F;
        int d3 = (lo >> 4) & 0x0F;
        int d4 = lo & 0x0F;
        return $"{prefix}{d1}{d2:X}{d3:X}{d4:X}";
    }

    /// <summary>
    /// Parses Mode $01 PID $01 readiness bytes (A, B, C, D) into a monitor list.
    /// Bytes come from the response payload after the service and PID echo bytes.
    /// </summary>
    public static IReadOnlyList<ReadinessMonitor> ParseReadiness(byte a, byte b, byte c, byte d)
    {
        return
        [
            new ReadinessMonitor("Misfire",           (b & 0x01) != 0, (b & 0x10) == 0),
            new ReadinessMonitor("Fuel System",       (b & 0x02) != 0, (b & 0x20) == 0),
            new ReadinessMonitor("Comprehensive",     (b & 0x04) != 0, (b & 0x40) == 0),
            new ReadinessMonitor("Catalyst",          (c & 0x01) != 0, (d & 0x01) == 0),
            new ReadinessMonitor("Heated Catalyst",   (c & 0x02) != 0, (d & 0x02) == 0),
            new ReadinessMonitor("Evap System",       (c & 0x04) != 0, (d & 0x04) == 0),
            new ReadinessMonitor("Secondary Air",     (c & 0x08) != 0, (d & 0x08) == 0),
            new ReadinessMonitor("A/C Refrigerant",   (c & 0x10) != 0, (d & 0x10) == 0),
            new ReadinessMonitor("O2 Sensor",         (c & 0x20) != 0, (d & 0x20) == 0),
            new ReadinessMonitor("O2 Sensor Heater",  (c & 0x40) != 0, (d & 0x40) == 0),
            new ReadinessMonitor("EGR System",        (c & 0x80) != 0, (d & 0x80) == 0),
        ];
    }

    /// <summary>
    /// Extracts the VIN string from a Mode $09 PID $02 assembled response payload.
    /// Expected layout: [0x49, 0x02, 0x01, VIN[17]]
    /// </summary>
    public static string? ParseVin(byte[] responseData)
    {
        // minimum: service(1) + pid(1) + count(1) + at least 1 vin byte
        if (responseData == null || responseData.Length < 4) return null;
        if (responseData[0] != 0x49 || responseData[1] != 0x02) return null;

        int vinStart = 3;
        int vinLen = Math.Min(17, responseData.Length - vinStart);
        if (vinLen <= 0) return null;

        var vin = Encoding.ASCII.GetString(responseData, vinStart, vinLen).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(vin) ? null : vin;
    }

    /// <summary>
    /// Parses a DTC list from an assembled Mode $03 or $07 response payload.
    /// Expected layout: [service_echo, count, hi0, lo0, hi1, lo1, ...]
    /// </summary>
    public static IReadOnlyList<DiagnosticTroubleCode> ParseDtcs(byte[] responseData, DtcStatus status)
    {
        if (responseData == null || responseData.Length < 2) return [];

        int count = responseData[1];
        var result = new List<DiagnosticTroubleCode>(count);

        for (int i = 0; i < count; i++)
        {
            int offset = 2 + i * 2;
            if (offset + 1 >= responseData.Length) break;

            byte hi = responseData[offset];
            byte lo = responseData[offset + 1];
            var code = DecodeDtcCode(hi, lo);
            if (code == null) continue;

            var type = (hi & 0x30) == 0 ? DtcType.Generic : DtcType.Manufacturer;
            result.Add(new DiagnosticTroubleCode(code, "", status, type));
        }

        return result;
    }
}
