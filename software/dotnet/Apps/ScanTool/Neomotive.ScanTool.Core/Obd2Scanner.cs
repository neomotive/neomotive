using Meadow.Hardware;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Neomotive.ScanTool.Core;

public class Obd2Scanner : IObd2Scanner
{
    private const short RequestId = 0x7DF;
    private const short EcuResponseIdMin = 0x7E8;
    private const short EcuResponseIdMax = 0x7EF;
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(3);

    private readonly ICanBus _bus;

    public Obd2Scanner(ICanBus bus)
    {
        _bus = bus;
    }

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            var vin = await ReadVinAsync(ct);
            return vin != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> ReadVinAsync(CancellationToken ct = default)
    {
        var data = await SendAndReceive([0x09, 0x02], 0x49, ct);
        return data != null ? Obd2Protocol.ParseVin(data) : null;
    }

    public async Task<IReadOnlyList<ReadinessMonitor>> ReadReadinessAsync(CancellationToken ct = default)
    {
        var data = await SendAndReceive([0x01, 0x01], 0x41, ct);
        if (data == null || data.Length < 6) return [];
        return Obd2Protocol.ParseReadiness(data[2], data[3], data[4], data[5]);
    }

    public async Task<IReadOnlyList<DiagnosticTroubleCode>> ReadStoredDtcsAsync(CancellationToken ct = default)
    {
        var data = await SendAndReceive([0x03], 0x43, ct);
        return data != null ? Obd2Protocol.ParseDtcs(data, DtcStatus.Stored) : [];
    }

    public async Task<IReadOnlyList<DiagnosticTroubleCode>> ReadPendingDtcsAsync(CancellationToken ct = default)
    {
        var data = await SendAndReceive([0x07], 0x47, ct);
        return data != null ? Obd2Protocol.ParseDtcs(data, DtcStatus.Pending) : [];
    }

    public Task ClearDtcsAsync(CancellationToken ct = default)
    {
        SendRequest([0x04]);
        return Task.Delay(500, ct);
    }

    private void SendRequest(byte[] obd2Data)
    {
        var payload = new byte[8];
        payload[0] = (byte)obd2Data.Length;
        Array.Copy(obd2Data, 0, payload, 1, obd2Data.Length);
        _bus.WriteFrame(new StandardDataFrame { ID = RequestId, Payload = payload });
    }

    private void SendFlowControl(short ecuPhysicalId)
    {
        // CTS (continue to send), block size = 0 (all frames), ST = 0 ms
        var payload = new byte[] { 0x30, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
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
            if (frame is not StandardDataFrame sdf) return;
            if (sdf.ID < EcuResponseIdMin || sdf.ID > EcuResponseIdMax) return;

            var p = sdf.Payload;
            if (p == null || p.Length == 0) return;

            byte frameTypeByte = p[0];
            byte frameType = (byte)(frameTypeByte >> 4);

            if (frameType == 0) // single frame
            {
                int len = frameTypeByte & 0x0F;
                if (len == 0 || len > p.Length - 1) return;
                var data = new byte[len];
                Array.Copy(p, 1, data, 0, len);
                if (data.Length > 0 && data[0] == expectedResponseService)
                {
                    _bus.FrameReceived -= handler;
                    tcs.TrySetResult(data);
                }
            }
            else if (frameType == 1) // first frame
            {
                int totalLen = ((frameTypeByte & 0x0F) << 8) | p[1];
                int firstBytes = Math.Min(6, p.Length - 2);
                var initial = new byte[firstBytes];
                Array.Copy(p, 2, initial, 0, firstBytes);
                assembler.Start(totalLen, initial);

                // physical address is response ID minus 8 (0x7E8 → 0x7E0)
                SendFlowControl((short)(sdf.ID - 8));
            }
            else if (frameType == 2) // consecutive frame
            {
                int available = p.Length - 1;
                if (available <= 0) return;
                var chunk = new byte[available];
                Array.Copy(p, 1, chunk, 0, available);
                assembler.Append(chunk);

                if (assembler.IsComplete)
                {
                    var data = assembler.GetData();
                    if (data.Length > 0 && data[0] == expectedResponseService)
                    {
                        _bus.FrameReceived -= handler;
                        tcs.TrySetResult(data);
                    }
                }
            }
        };

        cts.Token.Register(() =>
        {
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
