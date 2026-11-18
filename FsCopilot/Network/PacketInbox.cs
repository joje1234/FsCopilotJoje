namespace FsCopilot.Network;

using System.Reactive.Subjects;
using Transport;

public sealed record ReceivedPacket<T>(string Sender, Peer.TransportKind Transport, T Packet);

// One delivery path for old subscriptions and sender-aware subscriptions. An
// old relay may still publish legacy packets, but can never invent their origin.
internal sealed class PacketInbox : IDisposable
{
    private sealed record Delivery(string? Sender, Peer.TransportKind Transport, object Packet);
    private readonly ISubject<Delivery> _incoming = Subject.Synchronize(new Subject<Delivery>());

    public void Publish(string? sender, Peer.TransportKind transport, object packet) =>
        _incoming.OnNext(new(sender, transport, packet));

    public IObservable<T> Stream<T>() => _incoming
        .Where(item => item.Packet is T).Select(item => (T)item.Packet)
        .ObserveOn(TaskPoolScheduler.Default);

    public IObservable<ReceivedPacket<T>> StreamWithSource<T>() => _incoming
        .Where(item => RelayOriginFrame.ValidPeer(item.Sender) && item.Packet is T)
        .Select(item => new ReceivedPacket<T>(item.Sender!, item.Transport, (T)item.Packet))
        .ObserveOn(TaskPoolScheduler.Default);

    public void Dispose() => _incoming.OnCompleted();
}
