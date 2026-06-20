using Meadow;
using Meadow.Foundation.Telematics.J1979;
using Meadow.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Neomotive.ScanTool.Core;

public class Obd2Scanner : IObd2Scanner
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(3);

    private readonly ICanBus _bus;

    public Obd2Scanner(ICanBus bus)
    {
        _bus = bus;
    }

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        Resolver.Log.Info($"ConnectAsync: probing vehicle via VIN request on bus {_bus.GetType().Name}...");
        try
        {
            var vin = await ReadVinAsync(ct);
            if (vin != null)
            {
                Resolver.Log.Info($"ConnectAsync: vehicle responded — VIN={vin}");
                return true;
            }
            Resolver.Log.Warn("ConnectAsync: no VIN response — vehicle not detected.");
            return false;
        }
        catch (Exception ex)
        {
            Resolver.Log.Error($"ConnectAsync exception: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> ReadVinAsync(CancellationToken ct = default)
    {
        var data = await SendAndReceive(
            [(byte)Service.VehicleInfo, (byte)VehicleInfoPid.Vin],
            ResponseServiceId(Service.VehicleInfo), ct);
        return data != null ? Obd2Protocol.ParseVin(data) : null;
    }

    public async Task<IReadOnlyList<ReadinessMonitor>> ReadReadinessAsync(CancellationToken ct = default)
    {
        var data = await SendAndReceive(
            [(byte)Service.Current, (byte)Pid.MonitorStatus],
            ResponseServiceId(Service.Current), ct);
        if (data == null || data.Length < 6) return [];
        return Obd2Protocol.ParseReadiness(data[2], data[3], data[4], data[5]);
    }

    public async Task<IReadOnlyList<DiagnosticTroubleCode>> ReadStoredDtcsAsync(CancellationToken ct = default)
    {
        var data = await SendAndReceive(
            [(byte)Service.StoredDtcs],
            ResponseServiceId(Service.StoredDtcs), ct);
        return data != null ? Obd2Protocol.ParseDtcs(data, DtcStatus.Stored) : [];
    }

    public async Task<IReadOnlyList<DiagnosticTroubleCode>> ReadPendingDtcsAsync(CancellationToken ct = default)
    {
        var data = await SendAndReceive(
            [(byte)Service.PendingDtcs],
            ResponseServiceId(Service.PendingDtcs), ct);
        return data != null ? Obd2Protocol.ParseDtcs(data, DtcStatus.Pending) : [];
    }

    public Task ClearDtcsAsync(CancellationToken ct = default)
    {
        SendRequest([(byte)Service.ClearDtcs]);
        return Task.Delay(500, ct);
    }

    private static byte ResponseServiceId(Service svc) =>
        (byte)((byte)svc + Obd2Addresses.ResponseOffset);

    private void SendRequest(byte[] obd2Data)
    {
        var payload = new byte[8];
        payload[0] = (byte)obd2Data.Length;
        Array.Copy(obd2Data, 0, payload, 1, obd2Data.Length);
        Resolver.Log.Info($"TX 0x{Obd2Addresses.FunctionalRequest:X3}: [{string.Join(" ", payload.Select(b => $"{b:X2}"))}]");
        _bus.WriteFrame(new StandardDataFrame { ID = Obd2Addresses.FunctionalRequest, Payload = payload });
    }

    private void SendFlowControl(short ecuPhysicalId)
    {
        // CTS (continue to send), block size = 0 (all frames), ST = 0 ms
        byte fcByte = (byte)((byte)IsoTpFrameType.FlowControl << 4);
        var payload = new byte[] { fcByte, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        _bus.WriteFrame(new StandardDataFrame { ID = ecuPhysicalId, Payload = payload });
    }

    private async Task<byte[]?> SendAndReceive(byte[] obd2Data, byte expectedResponseService, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ResponseTimeout);

        var tcs = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var assembler = new MultiFrameAssembler();

        EventHandler<ICanFrame>? handler = null;
        handler = (_, frame) =>
        {
            if (frame is not StandardDataFrame sdf)
            {
                Resolver.Log.Info($"RX: non-standard frame ignored (type={frame.GetType().Name})");
                return;
            }

            Resolver.Log.Info($"RX 0x{sdf.ID:X3}: [{string.Join(" ", (sdf.Payload ?? []).Select(b => $"{b:X2}"))}]");

            if (sdf.ID < Obd2Addresses.EcuResponseBase || sdf.ID > Obd2Addresses.EcuResponseMax)
            {
                Resolver.Log.Info($"RX 0x{sdf.ID:X3}: ignored (not in ECU response range 0x{Obd2Addresses.EcuResponseBase:X3}–0x{Obd2Addresses.EcuResponseMax:X3})");
                return;
            }

            var p = sdf.Payload;
            if (p == null || p.Length == 0) return;

            byte frameTypeByte = p[0];
            var frameType = (IsoTpFrameType)(frameTypeByte >> 4);
            Resolver.Log.Info($"RX 0x{sdf.ID:X3}: ISO-TP frame type={frameType}");

            if (frameType == IsoTpFrameType.Single)
            {
                int len = frameTypeByte & 0x0F;
                if (len == 0 || len > p.Length - 1) return;
                var data = new byte[len];
                Array.Copy(p, 1, data, 0, len);
                if (data.Length > 0 && data[0] == expectedResponseService)
                {
                    Resolver.Log.Info($"RX 0x{sdf.ID:X3}: single frame match for service 0x{expectedResponseService:X2} — complete.");
                    _bus.FrameReceived -= handler;
                    tcs.TrySetResult(data);
                }
                else
                {
                    Resolver.Log.Info($"RX 0x{sdf.ID:X3}: single frame service=0x{(data.Length > 0 ? data[0] : 0):X2}, expected=0x{expectedResponseService:X2} — ignored.");
                }
            }
            else if (frameType == IsoTpFrameType.First)
            {
                int totalLen = ((frameTypeByte & 0x0F) << 8) | p[1];
                int firstBytes = Math.Min(6, p.Length - 2);
                var initial = new byte[firstBytes];
                Array.Copy(p, 2, initial, 0, firstBytes);
                assembler.Start(totalLen, initial);
                Resolver.Log.Info($"RX 0x{sdf.ID:X3}: first frame, totalLen={totalLen}, got {firstBytes} bytes — sending flow control.");

                // physical address = response ID - 8  (e.g. 0x7E8 → 0x7E0)
                SendFlowControl((short)(sdf.ID - Obd2Addresses.EcuPhysicalOffset));
            }
            else if (frameType == IsoTpFrameType.Consecutive)
            {
                int available = p.Length - 1;
                if (available <= 0) return;
                var chunk = new byte[available];
                Array.Copy(p, 1, chunk, 0, available);
                assembler.Append(chunk);
                Resolver.Log.Info($"RX 0x{sdf.ID:X3}: consecutive frame, {available} bytes appended, complete={assembler.IsComplete}");

                if (assembler.IsComplete)
                {
                    var data = assembler.GetData();
                    if (data.Length > 0 && data[0] == expectedResponseService)
                    {
                        Resolver.Log.Info($"RX 0x{sdf.ID:X3}: multi-frame complete, service=0x{data[0]:X2} — done.");
                        _bus.FrameReceived -= handler;
                        tcs.TrySetResult(data);
                    }
                    else
                    {
                        Resolver.Log.Warn($"RX 0x{sdf.ID:X3}: multi-frame complete but service=0x{(data.Length > 0 ? data[0] : 0):X2}, expected=0x{expectedResponseService:X2}.");
                    }
                }
            }
            else
            {
                Resolver.Log.Info($"RX 0x{sdf.ID:X3}: unhandled ISO-TP frame type={frameType}");
            }
        };

        cts.Token.Register(() =>
        {
            Resolver.Log.Warn($"SendAndReceive: timeout waiting for service 0x{expectedResponseService:X2} response.");
            _bus.FrameReceived -= handler;
            tcs.TrySetResult(null);
        });

        _bus.FrameReceived += handler;
        SendRequest(obd2Data);

        return await tcs.Task;
    }

    private sealed class MultiFrameAssembler
    {
        private int _totalLen;
        private readonly List<byte> _bytes = [];

        public bool IsComplete => _bytes.Count >= _totalLen;

        public void Start(int totalLen, byte[] initial)
        {
            _totalLen = totalLen;
            _bytes.Clear();
            _bytes.AddRange(initial);
        }

        public void Append(byte[] data)
        {
            int remaining = _totalLen - _bytes.Count;
            if (remaining <= 0) return;
            int take = Math.Min(remaining, data.Length);
            for (int i = 0; i < take; i++)
                _bytes.Add(data[i]);
        }

        public byte[] GetData() => [.. _bytes];
    }
}
