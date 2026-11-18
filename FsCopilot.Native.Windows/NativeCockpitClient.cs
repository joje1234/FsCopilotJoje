using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.FlightSimulator.SimConnect;

namespace FsCopilot.Native;

// Separate SDK connection: optional Cockpit mapping/dispatch failures cannot tear
// down the primary steering/world transport. It creates no ClientData areas
// and transmits no simulator events directly.
[SupportedOSPlatform("windows")]
internal sealed class NativeCockpitClient : IDisposable
{
    private enum Area { Status = 0x7130, Command }
    private enum Request { Stream = 0x7230, Once }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Bytes { [MarshalAs(UnmanagedType.ByValArray, SizeConst = NativeCockpitWire.Size)] public byte[] Data; }
    private sealed record Queued(NativeCockpitPacket Packet, byte[] Bytes, TimeSpan Expires, Func<Func<bool>, bool>? Execute);
    private readonly Func<NativeBridgeState> _primary;
    private readonly string _prefix;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _reader;
    private readonly ConcurrentQueue<Queued> _queue = new();
    private readonly object _fenceLock = new();
    private readonly long _started = Stopwatch.GetTimestamp();
    private NativeCockpitFence? _fence;
    private NativeCockpitState _state = new(false, false, null, "Cockpit transport has not been advertised.");
    private int _queued, _disposed;
    public event Action<NativeCockpitState>? Changed;
    public event Action<ulong>? Dropped;
    public NativeCockpitState State => Volatile.Read(ref _state);
    public NativeCockpitClient(Func<NativeBridgeState> primary, string prefix)
    {
        if (Marshal.SizeOf<Bytes>() != NativeCockpitWire.Size) throw new InvalidOperationException("Cockpit marshalling size mismatch.");
        _primary = primary; _prefix = prefix;
        _reader = Task.Factory.StartNew(Run, _stop.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    private TimeSpan Now() => Stopwatch.GetElapsedTime(_started);
    public void SetFence(NativeCockpitFence? fence) { lock (_fenceLock) _fence = fence; }
    public bool TrySend(NativeCockpitPacket packet, Func<Func<bool>, bool>? execute)
    {
        if (Volatile.Read(ref _disposed) != 0 || !NativeCockpitSendGate.Allows(packet, _primary(), State) ||
            Volatile.Read(ref _fence)?.Allows(packet) != true) return false;
        var bytes = NativeCockpitWire.Encode(packet);
        if (Interlocked.Increment(ref _queued) > 32) { Interlocked.Decrement(ref _queued); return false; }
        _queue.Enqueue(new(packet, bytes, Now() + TimeSpan.FromMilliseconds(250), execute));
        return true;
    }
    private void Publish(bool connected, bool live, NativeCockpitPacket? packet, string error)
    {
        var next = new NativeCockpitState(connected, live, packet, error);
        if (next == State) return;
        Volatile.Write(ref _state, next); Changed?.Invoke(next);
    }
    private void Run()
    {
        while (!_stop.IsCancellationRequested) {
            var main = _primary();
            if (!main.Live || main.Snapshot is not { } observed || (observed.Capabilities & 32) == 0) {
                Publish(false, false, null, "Cockpit transport has not been advertised.");
                while (_queue.TryDequeue(out var retired)) { Interlocked.Decrement(ref _queued); Dropped?.Invoke(retired.Packet.Command); }
                _stop.Token.WaitHandle.WaitOne(200); continue;
            }
            try { Read(observed.Native); }
            catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException) { Publish(false, false, null, "Cockpit SDK transport: " + error.Message); }
            if (!_stop.IsCancellationRequested) _stop.Token.WaitHandle.WaitOne(500);
        }
    }
    private void Read(string native)
    {
        using var signal = new AutoResetEvent(false);
        using var sim = new SimConnect("FS Copilot Cockpit bridge", IntPtr.Zero, 0, signal, 0);
        var opened = false; var reconnect = false; var connectedAt = Now(); var receivedAt = TimeSpan.MinValue;
        ulong serial = 0, sentCommand = 0; NativeCockpitPacket? last = null;
        sim.OnRecvOpen += (_, _) => {
            opened = true;
            sim.MapClientDataNameToID(_prefix + NativeCockpitWire.StatusName, Area.Status);
            sim.MapClientDataNameToID(_prefix + NativeCockpitWire.CommandName, Area.Command);
            foreach (var area in new[] { Area.Status, Area.Command }) {
                sim.AddToClientDataDefinition(area, 0, NativeCockpitWire.Size, 0, uint.MaxValue);
                sim.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, Bytes>(area);
            }
            sim.RequestClientData(Area.Status, Request.Stream, Area.Status, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            sim.RequestClientData(Area.Status, Request.Once, Area.Status, SIMCONNECT_CLIENT_DATA_PERIOD.ONCE, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            Publish(true, false, null, "Waiting for fresh Cockpit observations.");
        };
        sim.OnRecvClientData += (_, data) => {
            if (data.dwDefineID != (uint)Area.Status || data.dwData is not { Length: > 0 } || data.dwData[0] is not Bytes bytes ||
                !NativeCockpitWire.TryDecode(bytes.Data, out var packet) || packet!.Kind != NativeCockpitKind.Status || packet.Native != native || packet.Serial <= serial) return;
            last = packet; serial = packet.Serial; receivedAt = Now(); Publish(true, true, packet, "");
        };
        sim.OnRecvException += (_, error) => {
            Publish(false, false, null, $"Cockpit SDK error {error.dwException} (send {error.dwSendID}, index {error.dwIndex})."); reconnect = true;
        };
        sim.OnRecvQuit += (_, _) => reconnect = true;
        while (!_stop.IsCancellationRequested && !reconnect) {
            var main = _primary();
            if (!main.Live || main.Snapshot is not { } current || current.Native != native || (current.Capabilities & 32) == 0) break;
            if (signal.WaitOne(20)) sim.ReceiveMessage();
            if (reconnect || _stop.IsCancellationRequested) break;
            if (!opened && Now() - connectedAt > TimeSpan.FromSeconds(10)) { Publish(false, false, null, "No Cockpit SDK connection-open reply."); break; }
            if (last is not null && Now() - receivedAt > TimeSpan.FromSeconds(1)) Publish(true, false, last, "Cockpit observations timed out.");
            for (var index = 0; index < 16 && _queue.TryDequeue(out var queued); ++index) {
                Interlocked.Decrement(ref _queued);
                bool Write() {
                    lock (_fenceLock) {
                        if (_stop.IsCancellationRequested || _fence?.Allows(queued.Packet) != true || Now() > queued.Expires || queued.Packet.Command <= sentCommand ||
                            !NativeCockpitSendGate.Allows(queued.Packet, _primary(), State)) return false;
                        sim.SetClientData(Area.Command, Area.Command, SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0, new Bytes { Data = queued.Bytes });
                        sentCommand = queued.Packet.Command;
                        return true;
                    }
                }
                // Execute may acquire the host model lock. Never hold the
                // transport lock while entering that model fence.
                var written = queued.Execute is null ? Write() : queued.Execute(Write);
                if (!written) Dropped?.Invoke(queued.Packet.Command);
            }
        }
        if (!_stop.IsCancellationRequested) Publish(false, false, null, State.Error.Length == 0 ? "Cockpit connection closed." : State.Error);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        SetFence(null); _stop.Cancel();
        if (Task.CurrentId != _reader.Id) try { _reader.Wait(TimeSpan.FromSeconds(3)); } catch (AggregateException) { }
        Volatile.Write(ref _state, new(false, false, null, "Cockpit bridge closed."));
        if (_reader.IsCompleted) _stop.Dispose(); else _ = _reader.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
