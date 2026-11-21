// SDK boundary only. The test compiles the unmodified production Windows
// clients and uses their actual workers, queues, gates, callbacks and fences.
using System.Collections.Concurrent;
using System.Reflection;
namespace Microsoft.FlightSimulator.SimConnect;

public enum SIMCONNECT_CLIENT_DATA_PERIOD { ON_SET, ONCE }
public enum SIMCONNECT_CLIENT_DATA_REQUEST_FLAG { DEFAULT }
public enum SIMCONNECT_CLIENT_DATA_SET_FLAG { DEFAULT }
public sealed class SIMCONNECT_RECV_OPEN { }
public sealed class SIMCONNECT_RECV_QUIT { }
public sealed class SIMCONNECT_RECV_EXCEPTION { public uint dwException, dwSendID, dwIndex; }
public sealed class SIMCONNECT_RECV_CLIENT_DATA { public uint dwDefineID; public object[] dwData = []; }

public sealed class SimConnect : IDisposable
{
    public static readonly ConcurrentQueue<SimConnect> Connections = new();
    private readonly EventWaitHandle _signal;
    private readonly ConcurrentQueue<Action> _callbacks = new();
    private Type? _bytesType;
    private uint _status;
    private int _disposed;
    public string Name { get; }
    public bool Disposed => Volatile.Read(ref _disposed) != 0;
    public readonly ConcurrentQueue<byte[]> Writes = new();
    public readonly TaskCompletionSource Registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event Action<SimConnect, SIMCONNECT_RECV_OPEN>? OnRecvOpen;
    public event Action<SimConnect, SIMCONNECT_RECV_CLIENT_DATA>? OnRecvClientData;
    public event Action<SimConnect, SIMCONNECT_RECV_EXCEPTION>? OnRecvException;
    public event Action<SimConnect, SIMCONNECT_RECV_QUIT>? OnRecvQuit;

    public SimConnect(string name, IntPtr window, uint message, EventWaitHandle signal, uint config)
    {
        Name = name; _signal = signal; Connections.Enqueue(this);
        Queue(() => OnRecvOpen?.Invoke(this, new()));
    }
    private void Queue(Action action)
    {
        if (Disposed) return;
        _callbacks.Enqueue(action);
        try { _signal.Set(); } catch (ObjectDisposedException) { }
    }
    public void MapClientDataNameToID(string name, Enum id) { if (name.Contains("STATUS")) _status = Convert.ToUInt32(id); }
    public void AddToClientDataDefinition(Enum id, uint offset, uint size, float epsilon, uint datum) { }
    public void RegisterStruct<TReceive, TBytes>(Enum id) => _bytesType = typeof(TBytes);
    public void RequestClientData(Enum area, Enum request, Enum definition, SIMCONNECT_CLIENT_DATA_PERIOD period,
        SIMCONNECT_CLIENT_DATA_REQUEST_FLAG flags, uint origin, uint interval, uint limit) => Registered.TrySetResult();
    public void ReceiveMessage() { while (_callbacks.TryDequeue(out var action)) action(); }
    public void Status(byte[] bytes) => Queue(() => {
        var value = Activator.CreateInstance(_bytesType!)!;
        _bytesType!.GetField("Data", BindingFlags.Public | BindingFlags.Instance)!.SetValue(value, bytes.ToArray());
        OnRecvClientData?.Invoke(this, new() { dwDefineID = _status, dwData = [value] });
    });
    public void SetClientData(Enum area, Enum definition, SIMCONNECT_CLIENT_DATA_SET_FLAG flags, uint reserved, object value)
    {
        if (Disposed) throw new ObjectDisposedException(nameof(SimConnect));
        var bytes = (byte[])value.GetType().GetField("Data")!.GetValue(value)!;
        Writes.Enqueue(bytes.ToArray());
    }
    public void Exception(uint error) => Queue(() => OnRecvException?.Invoke(this, new() { dwException = error }));
    public void Quit() => Queue(() => OnRecvQuit?.Invoke(this, new()));
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
