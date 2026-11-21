using System.Diagnostics;
using FsCopilot.Native;

// Runs the actual C++ module/controller/output adapter with a captured SDK.
// Binary packets cross a process boundary; this is not a second C# gate model.
internal sealed class CppNativeSessionClient : INativeBackendClient, INativeGuidanceClient, IDisposable
{
    private readonly Process _process;
    private readonly BinaryReader _reader;
    private readonly BinaryWriter _writer;
    private readonly object _io = new();
    private readonly string _backend = Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _poller;
    private NativeBridgeState _state = new(false, false, null, "Starting native fixture.");
    private int _disposed;
    private uint _transmissions;
    private long _reportSequence;
    private int _dropInstrumentValue, _droppedInstrumentValues, _duplicateCommands;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, NativeSessionKind> _commands = new();
    public int DroppedInstrumentValues => Volatile.Read(ref _droppedInstrumentValues);
    public bool DuplicateCommands => Volatile.Read(ref _duplicateCommands) != 0;
    public void DropNextInstrumentValue() => Interlocked.Exchange(ref _dropInstrumentValue, 1);
    private readonly bool _sharedCapabilities;
    public uint Transmissions => Volatile.Read(ref _transmissions);
    public NativeBridgeState State => Volatile.Read(ref _state).Copy();
    public event Action<NativeBridgeState>? StateChanged;
    public event Action<ulong>? SessionCommandDropped;
    private NativeGuidanceBridgeState _guidance = new(false, false, null, "Starting AP fixture.");
    private readonly object _guidanceLock = new();
    private NativeGuidanceFence? _guidanceFence;
    private readonly List<(NativeGuidancePacket Packet, Func<Func<bool>, bool>? Execute)> _pendingGuidance = new();
    private bool _pauseGuidance;
    private uint _guidanceOutputs;
    public uint GuidanceOutputs => Volatile.Read(ref _guidanceOutputs);
    public NativeGuidanceBridgeState GuidanceState => Volatile.Read(ref _guidance);
    public event Action<NativeGuidanceBridgeState>? GuidanceChanged;
    public event Action<ulong>? GuidanceCommandDropped;
    public void SetGuidanceFence(NativeGuidanceFence? fence) { lock (_guidanceLock) _guidanceFence = fence; }
    public CppNativeSessionClient(bool sharedCapabilities = false, bool armed = true, bool verifiedAltimeters = false, bool verifiedGuidance = false, bool definitionOutput = false, bool canonicalControls = false, bool relativeControls = false) {
        _sharedCapabilities = sharedCapabilities;
        var executable = Path.GetFullPath("artifacts/native-coordinator/tests/native_session_harness" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (!File.Exists(executable)) throw new InvalidOperationException("Build the native harness first with FsCopilot.Network.Tests/run.ps1.");
        _process = Process.Start(new ProcessStartInfo {
            FileName = executable,
            Arguments = (verifiedAltimeters ? "--verified-altimeter-fixture " : "") + (verifiedGuidance ? "--verified-guidance-fixture " : "") + (definitionOutput ? "--definition-fixture " : "") +
                (canonicalControls ? "--canonical-controls-fixture " : "") + (relativeControls ? "--relative-controls-fixture" : ""),
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("Cannot start native harness.");
        _reader = new(_process.StandardOutput.BaseStream); _writer = new(_process.StandardInput.BaseStream);
        Exchange(2);
        if (!armed) Exchange(9);
        _poller = Task.Run(async () => {
            try {
                while (!_stop.IsCancellationRequested) { await Task.Delay(33, _stop.Token); Exchange(2); }
            } catch (OperationCanceledException) { }
            catch (Exception error) { Lost(error.Message); }
        });
    }
    private void Lost(string error) {
        Volatile.Write(ref _state, new(false, false, null, error)); StateChanged?.Invoke(State);
    }
    private NativeBridgeState Exchange(uint kind, uint mask = 0, double[]? values = null, NativeSessionPacket? command = null, string? text = null, NativeControlPacket? report = null, NativeGuidancePacket? guidance = null) {
        NativeBridgeState state;
        lock (_io) {
            if (_process.HasExited) throw new IOException("Native fixture exited: " + _process.StandardError.ReadToEnd());
            _writer.Write(0x31544846u); _writer.Write(kind); _writer.Write(0d); _writer.Write(mask);
            for (var axis = 0; axis < 21; ++axis) _writer.Write(values?[axis] ?? 0);
            _writer.Write(guidance is not null ? NativeGuidanceWire.Encode(guidance).Concat(new byte[512]).ToArray() :
                report is not null ? NativeControlWire.Encode(report) : command is null ? new byte[1024] : NativeSessionWire.Encode(command));
            var message = new byte[512]; if (text is not null) { CheckText(text); System.Text.Encoding.ASCII.GetBytes(text, message); }
            _writer.Write(message); _writer.Flush();
            var managed = _reader.ReadBytes(1024); var observed = _reader.ReadBytes(1024);
            if (!NativeSessionWire.TryDecode(managed, out var session) || !NativeControlWire.TryDecode(observed, out var observation))
                throw new InvalidDataException("Native binary reply was truncated or invalid.");
            // Fixture declaration only: real MSFS still withholds bit 4. This
            // allows startup tests to represent a verified output-capable
            // package without enabling that capability in production.
            if (_sharedCapabilities) observation = observation! with { Capabilities = 15 | (observation.Capabilities & 16) };
            Volatile.Write(ref _transmissions, _reader.ReadUInt32()); Volatile.Write(ref _guidanceOutputs, _reader.ReadUInt32());
            if (!NativeGuidanceWire.TryDecode(_reader.ReadBytes(512), out var ap)) throw new InvalidDataException("Native AP reply was truncated or invalid.");
            Volatile.Write(ref _guidance, new(true, true, ap, ""));
            state = new(true, true, observation, "") { Backend = _backend, Session = session }; Volatile.Write(ref _state, state);
        }
        StateChanged?.Invoke(state.Copy()); GuidanceChanged?.Invoke(GuidanceState); return state;
    }
    public bool TrySendSession(NativeSessionPacket command) {
        var current = State; var copy = command.Copy();
        if (!current.Live || current.Snapshot is not { } observation || current.Session is not { } session ||
            !NativeSessionSendGate.Allows(copy, current.Backend, observation, session)) {
            SessionCommandDropped?.Invoke(command.Command); return false;
        }
        if (!_commands.TryAdd(copy.Command, copy.Kind)) Interlocked.Exchange(ref _duplicateCommands, 1);
        if (copy.Kind == NativeSessionKind.Barometer && copy.Barometer!.Action is NativeBarometerAction.Apply or NativeBarometerAction.Observe &&
            Interlocked.Exchange(ref _dropInstrumentValue, 0) != 0) {
            // Match the Windows client's accepted queue followed by its explicit
            // pre-execution drop notification. No SDK call saw this packet.
            Interlocked.Increment(ref _droppedInstrumentValues); SessionCommandDropped?.Invoke(copy.Command); return true;
        }
        try { Exchange(1, command: copy); return true; }
        catch (Exception error) { Lost(error.Message); return false; }
    }
    public void Hardware(uint axes, double position) {
        Exchange(3, axes, Enumerable.Repeat(position, 21).ToArray());
    }
    public void ControlReadback(uint axes, double position) => Exchange(19, axes, Enumerable.Repeat(position, 21).ToArray());
    public void ClearControlReadback(uint axes) => Exchange(20, axes);
    public bool TrySendGuidance(NativeGuidancePacket command, Func<Func<bool>, bool>? execute = null)
    {
        if (!NativeGuidanceSendGate.Allows(command, State, GuidanceState)) return false;
        lock (_guidanceLock) {
            if (_guidanceFence?.Allows(command) != true) return false;
            if (_pauseGuidance && command.Action == NativeGuidanceAction.Apply) { _pendingGuidance.Add((command, execute)); return true; }
        }
        return WriteGuidance(command, execute);
    }
    private bool WriteGuidance(NativeGuidancePacket command, Func<Func<bool>, bool>? execute)
    {
        bool Write() {
            lock (_guidanceLock) {
                if (_guidanceFence?.Allows(command) != true || !NativeGuidanceSendGate.Allows(command, State, GuidanceState)) return false;
                Exchange(16, guidance: command); return true;
            }
        }
        var written = execute is null ? Write() : execute(Write);
        if (!written) GuidanceCommandDropped?.Invoke(command.Command);
        return true; // Accepted queue; a pre-execution drop is reported separately.
    }
    public void PauseGuidanceValues(bool pause)
    {
        (NativeGuidancePacket Packet, Func<Func<bool>, bool>? Execute)[] queued;
        lock (_guidanceLock) { _pauseGuidance = pause; queued = pause ? [] : _pendingGuidance.ToArray(); if (!pause) _pendingGuidance.Clear(); }
        foreach (var item in queued) WriteGuidance(item.Packet, item.Execute);
    }
    public ulong QueuedGuidanceCommand(FsCopilot.Ownership.GuidanceSetting setting, double value) {
        lock (_guidanceLock) return _pendingGuidance.LastOrDefault(item => item.Packet.Setting == setting && item.Packet.Value == value).Packet?.Command ?? 0;
    }
    public void GuidanceInput(FsCopilot.Ownership.GuidanceSetting setting, double value) => Exchange(17, (uint)setting,
        new[] { value }.Concat(new double[20]).ToArray());
    public void GuidanceTap(FsCopilot.Ownership.GuidanceSetting setting) => GuidanceInput(setting, double.NaN);
    public void GuidanceAdapter(bool supported) => Exchange(18, supported ? 1u : 0u);
    private static void CheckText(string text) { if (text.Length > 511 || text.Any(c => c < 32 || c > 126)) throw new ArgumentException("Invalid native fixture command."); }
    public void Configure(NativeConnectionSettings settings) {
        if (!NativeConnectionSettings.Valid(settings)) throw new ArgumentException("Invalid fixture connection.");
        Exchange(12, text: $"5|harness|6|connect|{settings.Relay}|{settings.Port}|{settings.Local}|{settings.Peer}|{(settings.Role == 1 ? "host" : "guest")}|{(settings.Seat == 1 ? "pilot" : "copilot")}");
    }
    public bool Report(NativeBackendNotice notice) {
        var observation = State.Snapshot;
        if (observation is null || observation.ConnectionApi != 1 || notice.Configuration != (observation.Connection?.Revision ?? 0)) return false;
        Exchange(13, report: new NativeControlPacket {
            Kind = NativePacketKind.Backend, Native = observation.Native, Backend = _backend, ConnectionApi = 1,
            Request = (ulong)Interlocked.Increment(ref _reportSequence), ReportConfiguration = notice.Configuration, BackendPhase = notice.Phase, BackendCode = notice.Code
        }); return true;
    }
    public void Autopilot(bool enabled) => Exchange(7, enabled ? 1u : 0u);
    public void Altimeters(double first, bool std1, double second, bool std2) => Exchange(14,
        values: new[] { first, std1 ? 1d : 0d, second, std2 ? 1d : 0d }.Concat(new double[17]).ToArray());
    public void InstrumentAdapter(bool supported) => Exchange(15, supported ? 1u : 0u);
    public void UseLocalInstruments(string? expectedSession = null) => Exchange(12,
        text: $"5|instrument-ui|{Interlocked.Increment(ref _seatUiRequest)}|instruments|local|{expectedSession ?? State.Session!.Session}");
    public void Recover(uint selection) => Exchange(6, selection);
    public void Join() => Exchange(10);
    public void ResetSetup() => Exchange(12, text:"5|reset-network|1|reset-setup");
    public void Leave() => Exchange(9);
    public void World(double altitude) {
        var values = State.Session!.World.ToArray(); values[2] = altitude;
        Exchange(11, values: values.Concat(new double[9]).ToArray());
    }
    public void Efb(FsCopilot.Ownership.ControlSelection selection, NativeSessionIntent action) => Exchange(8, (uint)selection, Enumerable.Repeat((double)action, 21).ToArray());
    private int _seatUiRequest;
    public void EfbSeat(string action, ulong? revision = null, ulong? ticket = null) {
        if (action is not ("request" or "accept" or "decline" or "cancel")) throw new ArgumentException("Invalid seat fixture action.");
        var state = State.Session!;
        Exchange(12, text: $"5|seat-test|{Interlocked.Increment(ref _seatUiRequest)}|seat|{action}|{revision ?? state.SeatRevision}|{ticket ?? state.SeatRequest}");
    }
    public void Crash() {
        lock (_io) { if (!_process.HasExited) _process.Kill(); }
        Lost("Native fixture disconnected.");
    }
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel(); try { _poller.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        try { if (!_process.HasExited) Exchange(5); } catch (IOException) { }
        if (!_process.WaitForExit(1000)) _process.Kill();
        _reader.Dispose(); _writer.Dispose(); _process.Dispose(); _stop.Dispose();
    }
}
