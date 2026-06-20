using Meadow;
using Meadow.Foundation.Telematics.J1979;
using Meadow.Hardware;

namespace Neomotive.ScanTool.Core;

public class Obd2Scanner : IObd2Scanner
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CollectTimeout = TimeSpan.FromSeconds(1);

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

    public async Task ClearModuleDtcsAsync(ushort moduleResponseAddress, CancellationToken ct = default)
    {
        short physicalAddress = (short)(moduleResponseAddress - Obd2Addresses.EcuPhysicalOffset);
        SendRequestTo(physicalAddress, [(byte)Service.ClearDtcs]);
        await Task.Delay(500, ct);
    }

    public async Task<IReadOnlyList<ModuleDtcGroup>> ReadDtcsByModuleAsync(CancellationToken ct = default)
    {
        var storedByModule = await SendAndCollectAll(
            [(byte)Service.StoredDtcs], ResponseServiceId(Service.StoredDtcs), ct);
        var pendingByModule = await SendAndCollectAll(
            [(byte)Service.PendingDtcs], ResponseServiceId(Service.PendingDtcs), ct);

        var allIds = new HashSet<ushort>(storedByModule.Keys);
        foreach (var id in pendingByModule.Keys) allIds.Add(id);

        return allIds.OrderBy(id => id).Select(id =>
        {
            var stored = storedByModule.TryGetValue(id, out var sd) ? Obd2Protocol.ParseDtcs(sd, DtcStatus.Stored) : (IReadOnlyList<DiagnosticTroubleCode>)[];
            var pending = pendingByModule.TryGetValue(id, out var pd) ? Obd2Protocol.ParseDtcs(pd, DtcStatus.Pending) : (IReadOnlyList<DiagnosticTroubleCode>)[];
            var module = new VehicleModule(id, ModuleName(id), stored.Count, pending.Count);
            return new ModuleDtcGroup(module, stored, pending);
        }).ToList();
    }

    public async Task<IReadOnlyList<VehicleModule>> ScanModulesAsync(CancellationToken ct = default)
    {
        // Step 1: discover all modules via Mode 01 PID 01 (Monitor Status) ping
        var discovered = (await SendAndCollectAll(
            [(byte)Service.Current, (byte)Pid.MonitorStatus],
            ResponseServiceId(Service.Current), ct)).Keys;

        // Step 2: stored DTC counts per module
        var storedByModule = await SendAndCollectAll(
            [(byte)Service.StoredDtcs],
            ResponseServiceId(Service.StoredDtcs), ct);

        // Step 3: pending DTC counts per module
        var pendingByModule = await SendAndCollectAll(
            [(byte)Service.PendingDtcs],
            ResponseServiceId(Service.PendingDtcs), ct);

        // Union of all responding module IDs
        var allIds = new HashSet<ushort>(discovered);
        foreach (var id in storedByModule.Keys) allIds.Add(id);
        foreach (var id in pendingByModule.Keys) allIds.Add(id);

        return allIds
            .OrderBy(id => id)
            .Select(id =>
            {
                int stored = storedByModule.TryGetValue(id, out var sd) && sd.Length > 1 ? sd[1] : 0;
                int pending = pendingByModule.TryGetValue(id, out var pd) && pd.Length > 1 ? pd[1] : 0;
                return new VehicleModule(id, ModuleName(id), stored, pending);
            })
            .ToList();
    }

    private static string ModuleName(ushort id) => id switch
    {
        0x7E8 => "PCM",
        0x7E9 => "TCU",
        0x7EA => "BCM",
        0x7EB => "HVAC",
        0x7EC => "ABS",
        0x7ED => "SRS",
        0x7EE => "IC",
        0x7EF => "GW",
        _ => "ECU"
    };

    private static byte ResponseServiceId(Service svc) =>
        (byte)((byte)svc + Obd2Addresses.ResponseOffset);

    private void SendRequestTo(short targetId, byte[] obd2Data)
    {
        var payload = new byte[8];
        payload[0] = (byte)obd2Data.Length;
        Array.Copy(obd2Data, 0, payload, 1, obd2Data.Length);
        Resolver.Log.Info($"TX 0x{targetId:X3}: [{string.Join(" ", payload.Select(b => $"{b:X2}"))}]");
        _bus.WriteFrame(new StandardDataFrame { ID = targetId, Payload = payload });
    }

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

    /// <summary>
    /// Sends a broadcast request and collects responses from all responding modules
    /// until <see cref="CollectTimeout"/> elapses. Returns a map of module CAN ID → response payload.
    /// </summary>
    private async Task<Dictionary<ushort, byte[]>> SendAndCollectAll(
        byte[] obd2Data, byte expectedResponseService, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CollectTimeout);

        var results = new Dictionary<ushort, byte[]>();
        var assemblers = new Dictionary<ushort, MultiFrameAssembler>();
        var tcs = new TaskCompletionSource<Dictionary<ushort, byte[]>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<ICanFrame>? handler = null;
        handler = (_, frame) =>
        {
            if (frame is not StandardDataFrame sdf) return;
            if (sdf.ID < Obd2Addresses.EcuResponseBase || sdf.ID > Obd2Addresses.EcuResponseMax) return;

            var p = sdf.Payload;
            if (p == null || p.Length == 0) return;

            byte frameTypeByte = p[0];
            var frameType = (IsoTpFrameType)(frameTypeByte >> 4);

            if (frameType == IsoTpFrameType.Single)
            {
                int len = frameTypeByte & 0x0F;
                if (len == 0 || len > p.Length - 1) return;
                var data = new byte[len];
                Array.Copy(p, 1, data, 0, len);
                if (data.Length > 0 && data[0] == expectedResponseService)
                    results[(ushort)sdf.ID] = data;
            }
            else if (frameType == IsoTpFrameType.First)
            {
                int totalLen = ((frameTypeByte & 0x0F) << 8) | p[1];
                int firstBytes = Math.Min(6, p.Length - 2);
                var initial = new byte[firstBytes];
                Array.Copy(p, 2, initial, 0, firstBytes);
                var asm = new MultiFrameAssembler();
                assemblers[(ushort)sdf.ID] = asm;
                asm.Start(totalLen, initial);
                SendFlowControl((short)(sdf.ID - Obd2Addresses.EcuPhysicalOffset));
            }
            else if (frameType == IsoTpFrameType.Consecutive)
            {
                if (!assemblers.TryGetValue((ushort)sdf.ID, out var asm)) return;
                int available = p.Length - 1;
                if (available <= 0) return;
                var chunk = new byte[available];
                Array.Copy(p, 1, chunk, 0, available);
                asm.Append(chunk);
                if (asm.IsComplete)
                {
                    var data = asm.GetData();
                    if (data.Length > 0 && data[0] == expectedResponseService)
                        results[(ushort)sdf.ID] = data;
                }
            }
        };

        cts.Token.Register(() =>
        {
            _bus.FrameReceived -= handler;
            tcs.TrySetResult(results);
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
