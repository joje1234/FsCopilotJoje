using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.FlightSimulator.SimConnect;

namespace FsCopilot.Native;

/// <summary>
/// Actual Windows SimConnect transport for the EFB module's discovery and
/// observations, with an opt-in consent-bound session command queue. It never
/// transmits aircraft events directly or creates ClientData areas.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NativeControlClient : INativeBackendClient, INativeGuidanceClient, INativeCockpitClient, IDisposable
{
    private enum Area { Status = 0x7100, Command, Reply, SessionStatus = 0x7110, SessionCommand }
    private enum Request { StatusStream = 0x7200, ReplyStream, StatusOnce, ReplyOnce, SessionStream, SessionOnce }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct PacketBytes { [MarshalAs(UnmanagedType.ByValArray, SizeConst = NativeControlWire.Size)] public byte[] Data; }
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _reader;
    private readonly NativeGuidanceClient? _guidance;
    private readonly NativeCockpitClient? _cockpit;
    public NativeCockpitState CockpitState => _cockpit?.State ?? new(false, false, null, "Cockpit transport is disabled.");
    public event Action<NativeCockpitState>? CockpitChanged {
        add { if (_cockpit is not null) _cockpit.Changed += value; }
        remove { if (_cockpit is not null) _cockpit.Changed -= value; }
    }
    public bool TrySendCockpit(NativeCockpitPacket packet) => _cockpit?.TrySend(packet, null) == true;
    public void SetCockpitFence(NativeCockpitFence? fence) => _cockpit?.SetFence(fence);
    public NativeGuidanceBridgeState GuidanceState => _guidance?.State ?? new(false, false, null, "AP transport is disabled.");
    public event Action<NativeGuidanceBridgeState>? GuidanceChanged {
        add { if (_guidance is not null) _guidance.Changed += value; }
        remove { if (_guidance is not null) _guidance.Changed -= value; }
    }
    public event Action<ulong>? GuidanceCommandDropped {
        add { if (_guidance is not null) _guidance.Dropped += value; }
        remove { if (_guidance is not null) _guidance.Dropped -= value; }
    }
    public void SetGuidanceFence(NativeGuidanceFence? fence) => _guidance?.SetFence(fence);
    public bool TrySendGuidance(NativeGuidancePacket command, Func<Func<bool>, bool>? execute = null) => _guidance?.TrySend(command, execute) == true;
    private readonly string _prefix;
    private readonly bool _sessions;
    private sealed record Queued(NativeSessionPacket Packet, byte[] Bytes, TimeSpan Expires);
    private readonly ConcurrentQueue<Queued> _outgoing = new();
    private int _queued;
    private readonly long _started = Stopwatch.GetTimestamp();
    private NativeBridgeState _state = new(false, false, null, "Waiting for simulator/native bridge.");
    private int _disposed;
    private NativeBackendNotice? _notice;
    public bool Report(NativeBackendNotice notice) {
        if (Volatile.Read(ref _disposed) != 0 || !Enum.IsDefined(notice.Phase) || !Enum.IsDefined(notice.Code) || notice.Configuration > uint.MaxValue) return false;
        Volatile.Write(ref _notice, notice); return true;
    }
    public event Action<NativeBridgeState>? StateChanged;
    public event Action<ulong>? SessionCommandDropped;
    public NativeBridgeState State { get { var state = Volatile.Read(ref _state); return state with { Snapshot = state.Snapshot?.Copy(), Session = state.Session?.Copy() }; } }
    public NativeControlClient(string testPrefix = "", bool enableSessions = false) {
        if (testPrefix.Length > 32 || testPrefix.Length != 0 && (!testPrefix.StartsWith("FSC_TEST_", StringComparison.Ordinal) ||
            !testPrefix.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))) throw new ArgumentException("Invalid isolated test prefix.", nameof(testPrefix));
        if (Marshal.SizeOf<PacketBytes>() != NativeControlWire.Size) throw new InvalidOperationException("Native bridge marshalling size mismatch.");
        _prefix = testPrefix;
        _sessions = enableSessions;
        _reader = Task.Factory.StartNew(Run, _stop.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        if (enableSessions) _guidance = new(() => State, testPrefix);
        if (enableSessions) _cockpit = new(() => State, testPrefix);
    }
    private TimeSpan Now() => Stopwatch.GetElapsedTime(_started);
    public bool TrySendSession(NativeSessionPacket command) {
        var state = State;
        if (!_sessions || Volatile.Read(ref _disposed) != 0 || !NativeSessionWire.Valid(command) || !state.Live || state.Snapshot is not { } observation || state.Session is not { } session) return false;
        var copy = command.Copy();
        if (!state.Fresh && copy.Kind is not (NativeSessionKind.Heartbeat or NativeSessionKind.End)) return false;
        if (!NativeSessionSendGate.Allows(copy, state.Backend, observation, session)) return false;
        var bytes = NativeSessionWire.Encode(copy);
        if (Interlocked.Increment(ref _queued) > 64) { Interlocked.Decrement(ref _queued); return false; }
        _outgoing.Enqueue(new(copy, bytes, Now() + TimeSpan.FromMilliseconds(copy.Kind is NativeSessionKind.Input or NativeSessionKind.World ? 250 : 1000)));
        return true;
    }
    private void Publish(bool connected, NativeBridgeMonitor? monitor, string error = "", NativeSessionPacket? session = null, bool sessionFresh = true) {
        var state = new NativeBridgeState(connected, connected && monitor is { Live: true }, monitor?.Snapshot, error) {
            Backend = monitor?.Backend ?? "", Session = connected && monitor is { Live: true } ? session?.Copy() : null,
            Fresh = connected && monitor is { ObservationsFresh: true } && sessionFresh
        };
        Volatile.Write(ref _state, state); StateChanged?.Invoke(State);
    }
    private void Run() {
        while (!_stop.IsCancellationRequested) {
            try { ReadConnection(); }
            catch (COMException error) { Publish(false, null, $"Native bridge disconnected (0x{error.HResult:X8}): {error.Message}"); }
            catch (Exception error) when (error is not OperationCanceledException) {
                Publish(false, null, $"Native bridge transport failed: {error.GetType().Name} (0x{error.HResult:X8}) {error.Message}");
            }
            if (_stop.Token.WaitHandle.WaitOne(2000)) break;
        }
    }
    private void ReadConnection() {
        using var signal = new AutoResetEvent(false);
        using var sim = new SimConnect("FS Copilot native observations", IntPtr.Zero, 0, signal, 0);
        var monitor = new NativeBridgeMonitor(Now, _sessions ? NativeSessionTiming.Recovery : null);
        NativeSessionPacket? session = null; var sessionAt = TimeSpan.MinValue; var sessionNative = ""; ulong sessionSerial = 0;
        var opened = false; var reconnect = false; var live = false; var sessionMapped = false; var delayed = false; var connectedAt = Now(); var lastHello = TimeSpan.MinValue;
        var lastReport = TimeSpan.MinValue; ulong reportSequence = 0;
        void MapSessionTransport() {
            if (sessionMapped || !_sessions) return;
            sessionMapped = true;
            sim.MapClientDataNameToID(_prefix + NativeSessionWire.StatusName, Area.SessionStatus);
            sim.MapClientDataNameToID(_prefix + NativeSessionWire.CommandName, Area.SessionCommand);
            foreach (var area in new[] { Area.SessionStatus, Area.SessionCommand }) {
                sim.AddToClientDataDefinition(area, 0, NativeControlWire.Size, 0.0f, uint.MaxValue);
                sim.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PacketBytes>(area);
            }
            sim.RequestClientData(Area.SessionStatus, Request.SessionStream, Area.SessionStatus, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            sim.RequestClientData(Area.SessionStatus, Request.SessionOnce, Area.SessionStatus, SIMCONNECT_CLIENT_DATA_PERIOD.ONCE, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        sim.OnRecvOpen += (_, _) => {
            opened = true;
            sim.MapClientDataNameToID(_prefix + NativeControlWire.StatusName, Area.Status);
            sim.MapClientDataNameToID(_prefix + NativeControlWire.CommandName, Area.Command);
            sim.MapClientDataNameToID(_prefix + NativeControlWire.ReplyName, Area.Reply);
            foreach (var area in new[] { Area.Status, Area.Command, Area.Reply }) {
                sim.AddToClientDataDefinition(area, 0, NativeControlWire.Size, 0.0f, uint.MaxValue);
                sim.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, PacketBytes>(area);
            }
            sim.RequestClientData(Area.Status, Request.StatusStream, Area.Status, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            sim.RequestClientData(Area.Reply, Request.ReplyStream, Area.Reply, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            sim.RequestClientData(Area.Status, Request.StatusOnce, Area.Status, SIMCONNECT_CLIENT_DATA_PERIOD.ONCE, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            sim.RequestClientData(Area.Reply, Request.ReplyOnce, Area.Reply, SIMCONNECT_CLIENT_DATA_PERIOD.ONCE, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            if (_prefix.Length != 0) MapSessionTransport(); // Known isolated SDK fixture areas.
            Publish(true, monitor, "Waiting for a fresh native discovery reply.");
        };
        sim.OnRecvClientData += (_, data) => {
            if (_sessions && data.dwDefineID == (uint)Area.SessionStatus && data.dwData is { Length: > 0 } && data.dwData[0] is PacketBytes shared &&
                NativeSessionWire.TryDecode(shared.Data, out var update) && update!.Kind == NativeSessionKind.Status && monitor.Live &&
                update.Native == monitor.Snapshot?.Native && (sessionNative != update.Native || update.Serial > sessionSerial)) {
                session = update.Copy(); sessionAt = Now(); sessionNative = update.Native; sessionSerial = update.Serial;
                Publish(true, monitor, session: session); return;
            }
            if (data.dwDefineID != (uint)Area.Status && data.dwDefineID != (uint)Area.Reply || data.dwData is not { Length: > 0 } ||
                data.dwData[0] is not PacketBytes bytes || bytes.Data is null || !NativeControlWire.TryDecode(bytes.Data, out var packet)) return;
            if (monitor.Receive(packet!)) {
                // Installed observation-only packages do not have session areas.
                // Probe those areas only after fresh discovery advertises the
                // remote-output capability; never turn their absence into a
                // reconnect loop against the accepted solo package.
                // Connection API 1 exposes the session areas even while actual
                // remote output remains gated. Mapping them grants no capability.
                if (monitor.Live && monitor.Snapshot is { } observed && ((observed.Capabilities & 4) != 0 || observed.ConnectionApi == 1)) MapSessionTransport();
                if (session?.Native != monitor.Snapshot?.Native) session = null;
                Publish(true, monitor, session: session is not null && Now() - sessionAt < NativeSessionTiming.Recovery ? session : null,
                    sessionFresh: session is null || Now() - sessionAt <= NativeSessionTiming.Fresh);
            }
            if (monitor.ConflictingInstances) { Publish(true, monitor, "Multiple native modules answered discovery. Remove duplicate FS Copilot control packages."); reconnect = true; }
        };
        sim.OnRecvException += (_, error) => {
            Publish(false, null, $"Native bridge SDK error {error.dwException} (send {error.dwSendID}, index {error.dwIndex}). The matching EFB module must be loaded.");
            reconnect = true;
        };
        sim.OnRecvQuit += (_, _) => reconnect = true;
        while (!_stop.IsCancellationRequested && !reconnect) {
            if (signal.WaitOne(100)) sim.ReceiveMessage();
            if (reconnect || _stop.IsCancellationRequested) break;
            if (session is not null && Now() - sessionAt >= NativeSessionTiming.Recovery) {
                session = null; Publish(true, monitor, "Native session recovery deadline exceeded (8 seconds).");
            }
            var waiting = session is not null && (!monitor.ObservationsFresh || Now() - sessionAt > NativeSessionTiming.Fresh);
            if (delayed != waiting) {
                delayed = waiting;
                Publish(true, monitor, delayed ? "Simulator updates delayed; retaining the current shared session." : "", session, !delayed);
            }
            for (var index = 0; index < 32 && _outgoing.TryDequeue(out var command); ++index) {
                Interlocked.Decrement(ref _queued);
                if (!monitor.Live || session is null || Now() > command.Expires || monitor.Snapshot is not { } observation ||
                    ((!monitor.ObservationsFresh || Now() - sessionAt > NativeSessionTiming.Fresh) && command.Packet.Kind is not (NativeSessionKind.Heartbeat or NativeSessionKind.End)) ||
                    !NativeSessionSendGate.Allows(command.Packet, monitor.Backend, observation, session)) {
                    SessionCommandDropped?.Invoke(command.Packet.Command); continue;
                }
                sim.SetClientData(Area.SessionCommand, Area.SessionCommand, SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0, new PacketBytes { Data = command.Bytes });
            }
            if (!opened && Now() - connectedAt > TimeSpan.FromSeconds(15)) { Publish(false, null, "No simulator connection-open reply."); break; }
            if (opened && (lastHello == TimeSpan.MinValue || Now() - lastHello >= TimeSpan.FromSeconds(1))) {
                var hello = monitor.Hello();
                // Discovery remains separate from optional session commands.
                sim.SetClientData(Area.Command, Area.Command, SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0,
                    new PacketBytes { Data = NativeControlWire.Encode(hello) });
                lastHello = Now();
            }
            if (opened && monitor.Live && monitor.Snapshot is { ConnectionApi: 1 } current && Volatile.Read(ref _notice) is { } notice &&
                notice.Configuration == (current.Connection?.Revision ?? 0) && (lastReport == TimeSpan.MinValue || Now() - lastReport >= TimeSpan.FromMilliseconds(500))) {
                var report = new NativeControlPacket {
                    Kind = NativePacketKind.Backend, Native = current.Native, Backend = monitor.Backend, Request = checked(++reportSequence),
                    ConnectionApi = 1, ReportConfiguration = notice.Configuration, BackendPhase = notice.Phase, BackendCode = notice.Code
                };
                sim.SetClientData(Area.Command, Area.Command, SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0, new PacketBytes { Data = NativeControlWire.Encode(report) });
                lastReport = Now();
            }
            if (live != monitor.Live) { live = monitor.Live; Publish(true, monitor, live ? "" : "Native observations timed out; control authority is unavailable.", session); }
        }
        if (!_stop.IsCancellationRequested) {
            var last = State.Error;
            Publish(false, null, last.Length == 0 ? "Native bridge connection closed." : last);
        }
    }
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _guidance?.Dispose();
        _cockpit?.Dispose();
        if (Task.CurrentId != _reader.Id) try { _reader.Wait(TimeSpan.FromSeconds(3)); } catch (AggregateException) { }
        Volatile.Write(ref _state, new(false, false, null, "Native bridge closed."));
        if (_reader.IsCompleted) _stop.Dispose();
        else _ = _reader.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
