using Meadow.Foundation.Telematics.J1979;
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

        var category = (DtcCategory)(hi & Obd2Addresses.DtcCategoryMask);
        char prefix = category switch
        {
            DtcCategory.P => 'P',
            DtcCategory.C => 'C',
            DtcCategory.B => 'B',
            DtcCategory.U => 'U',
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
            new ReadinessMonitor("Misfire",
                (b & ReadinessMonitorBits.MisfireSupported) != 0,
                (b & ReadinessMonitorBits.MisfireIncomplete) == 0),
            new ReadinessMonitor("Fuel System",
                (b & ReadinessMonitorBits.FuelSystemSupported) != 0,
                (b & ReadinessMonitorBits.FuelSystemIncomplete) == 0),
            new ReadinessMonitor("Comprehensive",
                (b & ReadinessMonitorBits.ComprehensiveSupported) != 0,
                (b & ReadinessMonitorBits.ComprehensiveIncomplete) == 0),
            new ReadinessMonitor("Catalyst",
                (c & ReadinessMonitorBits.CatalystBit) != 0,
                (d & ReadinessMonitorBits.CatalystBit) == 0),
            new ReadinessMonitor("Heated Catalyst",
                (c & ReadinessMonitorBits.HeatedCatalystBit) != 0,
                (d & ReadinessMonitorBits.HeatedCatalystBit) == 0),
            new ReadinessMonitor("Evap System",
                (c & ReadinessMonitorBits.EvapSystemBit) != 0,
                (d & ReadinessMonitorBits.EvapSystemBit) == 0),
            new ReadinessMonitor("Secondary Air",
                (c & ReadinessMonitorBits.SecondaryAirBit) != 0,
                (d & ReadinessMonitorBits.SecondaryAirBit) == 0),
            new ReadinessMonitor("A/C Refrigerant",
                (c & ReadinessMonitorBits.AcRefrigerantBit) != 0,
                (d & ReadinessMonitorBits.AcRefrigerantBit) == 0),
            new ReadinessMonitor("O2 Sensor",
                (c & ReadinessMonitorBits.OxygenSensorBit) != 0,
                (d & ReadinessMonitorBits.OxygenSensorBit) == 0),
            new ReadinessMonitor("O2 Sensor Heater",
                (c & ReadinessMonitorBits.OxygenSensorHeaterBit) != 0,
                (d & ReadinessMonitorBits.OxygenSensorHeaterBit) == 0),
            new ReadinessMonitor("EGR System",
                (c & ReadinessMonitorBits.EgrSystemBit) != 0,
                (d & ReadinessMonitorBits.EgrSystemBit) == 0),
        ];
    }

    /// <summary>
    /// Extracts the VIN string from a Mode $09 PID $02 assembled response payload.
    /// Expected layout: [0x49, 0x02, 0x01, VIN[17]]
    /// </summary>
    public static string? ParseVin(byte[] responseData)
    {
        if (responseData == null || responseData.Length < 4) return null;

        byte expectedService = (byte)((byte)Service.VehicleInfo + Obd2Addresses.ResponseOffset);
        if (responseData[0] != expectedService || responseData[1] != (byte)VehicleInfoPid.Vin) return null;

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

            var type = (hi & Obd2Addresses.DtcManufacturerMask) == 0 ? DtcType.Generic : DtcType.Manufacturer;
            result.Add(new DiagnosticTroubleCode(code, DtcDescriptions.Lookup(code), status, type));
        }

        return result;
    }
}
