using System.Net;
using FsCopilot.Network;
using FsCopilot.Ownership;
using FsCopilot.Transport;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using P2PDiscovery;

internal sealed record Probe(string Text)
{
    public sealed class Codec : IPacketCodec<Probe>
    {
        public void Encode(Probe value, BinaryWriter writer) => writer.Write(value.Text);
        public Probe Decode(BinaryReader reader) => new(reader.ReadString());
    }
}

internal static class Program
{
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Wait(Func<bool> ready, string reason)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(12)) await Task.Delay(20);
        Require(ready(), "Timed out: " + reason);
    }

    private static async Task Main(string[] args)
    {
        try { await Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    private static async Task Run(string[] args)
    {
        if (args.Contains("--controls-only")) {
            using var controls = new Relay(NullLogger<Relay>.Instance, new ServerStats(), port: 0, loopbackOnly: true);
            await controls.StartAsync(CancellationToken.None);
            try { await NativeCoordinatorNetworkTests.Run(controls.LocalPort); }
            finally { await controls.StopAsync(CancellationToken.None); }
            return;
        }
        if(args.Contains("--cockpit-only")) {
            using var relay=new Relay(NullLogger<Relay>.Instance,new ServerStats(),port:0,loopbackOnly:true);
            await relay.StartAsync(CancellationToken.None);
            try{await CockpitDefinitionNetworkTests.Run(relay.LocalPort);}finally{await relay.StopAsync(CancellationToken.None);}
            return;
        }
        if (args.Contains("--native-guidance-only")) {
            using var ap = new Relay(NullLogger<Relay>.Instance, new ServerStats(), port: 0, loopbackOnly: true);
            await ap.StartAsync(CancellationToken.None);
            try { await NativeGuidanceNetworkTests.Run(ap.LocalPort); await NativeGuidanceNetworkTests.Run(ap.LocalPort, peerSupported: false); }
            finally { await ap.StopAsync(CancellationToken.None); }
            return;
        }
        if (args.Contains("--guidance-only")) {
            using var guidance = new Relay(NullLogger<Relay>.Instance, new ServerStats(), port: 0, loopbackOnly: true);
            await guidance.StartAsync(CancellationToken.None);
            try { await GuidanceAuthorityNetworkTests.Run(guidance.LocalPort); }
            finally { await guidance.StopAsync(CancellationToken.None); }
            return;
        }
        if (args.Contains("--instruments-only")) {
            using var instruments = new Relay(NullLogger<Relay>.Instance, new ServerStats(), port: 0, loopbackOnly: true);
            await instruments.StartAsync(CancellationToken.None);
            try { await NativeAltimeterNetworkTests.Run(instruments.LocalPort); await NativeAltimeterNetworkTests.Run(instruments.LocalPort, peerSupported: false); await NativeAltimeterNetworkTests.Run(instruments.LocalPort, hostIsCopilot: true); }
            finally { await instruments.StopAsync(CancellationToken.None); }
            return;
        }
        if (args.Contains("--native-only") || args.Contains("--connection-only")) {
            using var isolated = new Relay(NullLogger<Relay>.Instance, new ServerStats(), port: 0, loopbackOnly: true);
            await isolated.StartAsync(CancellationToken.None);
            try {
                if (!args.Contains("--connection-only")) { await NativeCoordinatorNetworkTests.Run(isolated.LocalPort); await NativeCoordinatorNetworkTests.HeartbeatExpiry(isolated.LocalPort); await NativeCoordinatorNetworkTests.HeartbeatExpiry(isolated.LocalPort, worldOnly: true); SharedStartupTests.Options(); await SharedStartupTests.Run(isolated.LocalPort); }
                await EfbConnectionTests.Run(isolated.LocalPort);
                if (!args.Contains("--connection-only")) { await AltimeterAuthorityNetworkTests.Run(isolated.LocalPort); await NativeAltimeterNetworkTests.Run(isolated.LocalPort); await NativeAltimeterNetworkTests.Run(isolated.LocalPort, peerSupported: false); await NativeAltimeterNetworkTests.Run(isolated.LocalPort, hostIsCopilot: true); }
            }
            finally { await isolated.StopAsync(CancellationToken.None); }
            return;
        }
        var payload = new byte[] { 1, 2, 3 };
        var frame = RelayOriginFrame.Encode("actual-peer", payload);
        Require(RelayOriginFrame.TryDecode(frame, out var origin, out var decoded) && origin == "actual-peer" && decoded.SequenceEqual(payload), "Envelope round trip");
        foreach (var bad in new[] { Array.Empty<byte>(), frame[..4], new byte[] { 70, 83, 82, 49, 65, 0, 1 }, new byte[] { 70, 83, 82, 49, 1, 255, 1 } })
            Require(!RelayOriginFrame.TryDecode(bad, out _, out _), "Invalid envelope accepted");
        var unknownVersion = frame.ToArray(); unknownVersion[3] = (byte)'9';
        Require(!RelayOriginFrame.TryDecode(unknownVersion, out _, out _), "Unknown envelope version accepted");
        Console.WriteLine("PASS bounded relay origin envelope validation");

        using var inbox = new PacketInbox();
        var legacy = new ConcurrentQueue<Probe>();
        var sourced = new ConcurrentQueue<ReceivedPacket<Probe>>();
        using var legacySubscription = inbox.Stream<Probe>().Subscribe(legacy.Enqueue);
        using var sourceSubscription = inbox.StreamWithSource<Probe>().Subscribe(sourced.Enqueue);
        inbox.Publish(null, Peer.TransportKind.Relay, new Probe("legacy"));
        inbox.Publish("direct-peer", Peer.TransportKind.Direct, new Probe("direct"));
        await Wait(() => legacy.Count == 2 && sourced.Count == 1, "legacy/direct inbox delivery");
        Require(sourced.Single().Sender == "direct-peer", "Legacy relay invented a sender");
        Console.WriteLine("PASS legacy relay payloads remain legacy-only; direct metadata retains its connection sender");

        var codecs = new Codecs().Add<Probe, Probe.Codec>();
        var encodedProbe = codecs.Encode(new Probe("hello"));
        Require(codecs.Decode(encodedProbe, 0, encodedProbe.Length) is Probe { Text: "hello" }, "Packet decode");
        Require(codecs.Decode([], 0, 0) is null, "Empty packet decode");
        Require(codecs.Decode(encodedProbe, 0, encodedProbe.Length - 1) is null, "Truncated packet decode");
        var trailing = encodedProbe.Concat(new byte[] { 99 }).ToArray();
        Require(codecs.Decode(trailing, 0, trailing.Length) is null, "Trailing packet data accepted");
        Console.WriteLine("PASS actual application codec rejects empty/truncated/trailing payloads");

        using var server = new Relay(NullLogger<Relay>.Instance, new ServerStats(), port: 0, loopbackOnly: true);
        await server.StartAsync(CancellationToken.None);
        try { await RelayTest(server.LocalPort); await CockpitDefinitionNetworkTests.Run(server.LocalPort); await NegotiationNetworkTests.Run(server.LocalPort); await AdvancedAuthorityNetworkTests.Run(server.LocalPort); await NativeCoordinatorNetworkTests.Run(server.LocalPort); await NativeCoordinatorNetworkTests.HeartbeatExpiry(server.LocalPort); await NativeCoordinatorNetworkTests.HeartbeatExpiry(server.LocalPort, worldOnly: true); SharedStartupTests.Options(); await SharedStartupTests.Run(server.LocalPort); await EfbConnectionTests.Run(server.LocalPort); await AltimeterAuthorityNetworkTests.Run(server.LocalPort); await NativeAltimeterNetworkTests.Run(server.LocalPort); await NativeAltimeterNetworkTests.Run(server.LocalPort, peerSupported: false); await NativeAltimeterNetworkTests.Run(server.LocalPort, hostIsCopilot: true); await GuidanceAuthorityNetworkTests.Run(server.LocalPort); await NativeGuidanceNetworkTests.Run(server.LocalPort); await NativeGuidanceNetworkTests.Run(server.LocalPort, peerSupported: false); }
        finally { await server.StopAsync(CancellationToken.None); }
        await OldRelayFallback();
        Console.WriteLine("Network origin checks passed using the real relay and clients on loopback. No simulator or public relay was contacted.");
    }

    private static async Task RelayTest(int port)
    {
        using var a = new RelayNetwork("127.0.0.1", "peer-A", "A", false, port);
        using var b = new RelayNetwork("127.0.0.1", "peer-B", "B", false, port);
        using var c = new RelayNetwork("127.0.0.1", "peer-C", "C", false, port);
        foreach (var client in new[] { a, b, c })
        {
            client.RegisterPacket<Probe, Probe.Codec>();
            OwnershipNetworkHost.RegisterProtocol(client);
        }
        var received = new ConcurrentQueue<ReceivedPacket<Probe>>();
        var plain = new ConcurrentQueue<Probe>();
        using var source = b.StreamWithSource<Probe>().Subscribe(received.Enqueue);
        using var legacy = b.Stream<Probe>().Subscribe(plain.Enqueue);
        var capable = 0;
        using var capabilities = new CompositeDisposable();
        foreach (var client in new[] { a, b, c })
            capabilities.Add(client.OriginAvailable.Where(value => value).Take(1).Subscribe(_ => Interlocked.Increment(ref capable)));
        await Wait(() => Volatile.Read(ref capable) == 3, "all relay clients registered and origin capability negotiated");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Require(await a.Connect("peer-B", timeout.Token) == ConnectionResult.Success, "A/B relay link");
        Require(await c.Connect("peer-B", timeout.Token) == ConnectionResult.Success, "C/B relay link");
        a.SendAll(new Probe("from-A")); c.SendAll(new Probe("from-C"));
        await Wait(() => received.Any(p => p.Packet.Text == "from-A") && received.Any(p => p.Packet.Text == "from-C"), "origin-preserving relay delivery");
        Require(received.Single(p => p.Packet.Text == "from-A").Sender == "peer-A", "Wrong A origin");
        Require(received.Single(p => p.Packet.Text == "from-C").Sender == "peer-C", "Wrong C origin");
        Require(received.All(p => p.Transport == Peer.TransportKind.Relay), "Wrong relay transport metadata");
        await Wait(() => plain.Count >= 2, "legacy API delivery on a capable relay");
        Console.WriteLine("PASS real relay attributes concurrent linked participants independently; old Stream API still works");

        var sessionClock = Stopwatch.StartNew();
        var session = new OwnershipSession("peer-A", "peer-B", 2, () => sessionClock.Elapsed);
        using var host = new OwnershipNetworkHost(a, session);
        var acceptedInputs = new ConcurrentQueue<ControlFrame>();
        using var acceptedSubscription = host.AcceptedInputs.Subscribe(acceptedInputs.Enqueue);
        var snapshots = new ConcurrentQueue<OwnershipSnapshot>();
        using var stateSubscription = b.StreamWithSource<OwnershipPacket>().Subscribe(packet =>
        {
            if (packet.Sender == "peer-A" && OwnershipWire.TryDecode(packet.Packet.Payload, out var message) && message is SnapshotMessage snapshot)
                snapshots.Enqueue(snapshot.State);
        });
        Require(await host.ApplyLocal(new AttachmentMessage(new(session.Session, ControlGroup.Steering, 1, true))), "Local steering attach");
        Require(await host.ApplyLocal(new AttachmentMessage(new(session.Session, ControlGroup.Quadrant, 1, true))), "Local quadrant attach");
        b.SendAll(new OwnershipPacket(OwnershipWire.Encode(new RequestMessage(new(session.Session, 1, ControlGroup.Quadrant, 1, "peer-B")))));
        await Wait(() => session.Snapshot.Quadrant.Pending is not null, "remote ownership request");
        var preparation = session.Snapshot.Quadrant.Pending!;
        Require(await host.ApplyLocal(new PreparationMessage(new(session.Session, ControlGroup.Quadrant, preparation.Transfer, true, true))), "Host gate preparation");
        b.SendAll(new OwnershipPacket(OwnershipWire.Encode(new PreparationMessage(new(session.Session, ControlGroup.Quadrant, preparation.Transfer, true, true)))));
        await Wait(() => session.Snapshot.Quadrant.Owner == "peer-B", "remote gate acknowledgement and ownership commit");
        Require(!session.Snapshot.Quadrant.Attached, "Transfer auto-attached hardware");
        b.SendAll(new OwnershipPacket(OwnershipWire.Encode(new AttachmentMessage(new(session.Session, ControlGroup.Quadrant, 2, true)))));
        await Wait(() => session.Snapshot.Quadrant.Attached, "remote simulator attachment acknowledgement");
        var input = new InputMessage(new(session.Session, ControlGroup.Quadrant, 2, 1, [new(ControlAxis.Throttle1, 0.75)]));
        var inputPacket = new OwnershipPacket(OwnershipWire.Encode(input));
        b.SendAll(inputPacket); b.SendAll(inputPacket);
        await Wait(() => acceptedInputs.Count == 1, "single accepted remote frame");
        Require(await c.Connect("peer-A", timeout.Token) == ConnectionResult.Success, "Third participant test link");
        var thirdReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var thirdSubscription = a.StreamWithSource<OwnershipPacket>().Where(p => p.Sender == "peer-C").Take(1).Subscribe(_ => thirdReceived.TrySetResult());
        c.SendAll(new OwnershipPacket(OwnershipWire.Encode(input with { Frame = input.Frame with { Sequence = 2 } })));
        await thirdReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Require(!await host.ApplyLocal(input), "Previous owner supplied a remote-owned group");
        Require(await host.ApplyLocal(new InputMessage(new(session.Session, ControlGroup.Steering, 1, 1, [new(ControlAxis.Roll, 0.5)]))), "Steering stopped during quadrant transfer");
        await Wait(() => snapshots.Any(s => s.Quadrant.Owner == "peer-B" && s.Quadrant.Attached), "host state returned over relay");
        Require(acceptedInputs.Count(frame => frame.Group == ControlGroup.Quadrant) == 1 && session.Snapshot.PhysicsHost == "peer-A", "Duplicate input or physics host changed");
        Console.WriteLine("PASS ownership requests, gate acknowledgements, commit, accepted remote input and snapshots cross the actual relay");

        // A legacy client without origin=1 must receive the old unframed channel,
        // and a client may not inject a server-origin frame on channel 2.
        var listener = new EventBasedNetListener();
        var old = new NetManager(listener) { ChannelsCount = 3 };
        try
        {
            NetPeer? oldPeer = null;
            var oldReady = false;
            var legacyPacket = false;
            // The raw test peer only needs the same negotiated schema, then sends
            // bytes encoded exactly as the real client's Probe registration.
            var schemaField = typeof(RelayNetwork).GetField("_codecs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var actualCodecs = (Codecs)schemaField.GetValue(a)!;
            listener.PeerConnectedEvent += peer => oldPeer = peer;
            listener.NetworkReceiveEvent += (_, reader, channel, _) =>
            {
                try
                {
                    if (channel == 0 && reader.GetByte() == 10) oldReady = true;
                    else if (channel == 1 && actualCodecs.Decode(reader) is Probe { Text: "for-old" }) legacyPacket = true;
                }
                finally { if (channel != 1) reader.Recycle(); }
            };
            Require(old.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0), "Legacy test socket start");
            old.Connect(new IPEndPoint(IPAddress.Loopback, port), $"pid=old-peer;schema={actualCodecs.Schema}");
            var clock = Stopwatch.StartNew();
            while (oldPeer is null && clock.Elapsed < TimeSpan.FromSeconds(5)) { old.PollEvents(); await Task.Delay(10); }
            Require(oldPeer is not null, "Legacy peer connection");
            var link = new NetDataWriter(); link.Put((byte)1); link.Put("peer-B");
            oldPeer!.Send(link, 0, DeliveryMethod.ReliableOrdered);
            clock.Restart();
            while (!oldReady && clock.Elapsed < TimeSpan.FromSeconds(5)) { old.PollEvents(); await Task.Delay(10); }
            Require(oldReady, "Legacy peer link");
            await Task.Delay(100);
            b.SendAll(new Probe("for-old"));
            clock.Restart();
            while (!legacyPacket && clock.Elapsed < TimeSpan.FromSeconds(5)) { old.PollEvents(); await Task.Delay(10); }
            Require(legacyPacket, "Legacy recipient was sent an incompatible origin frame");
            Console.WriteLine("PASS new relay keeps an unmodified legacy recipient compatible");

            var fake = RelayOriginFrame.Encode("peer-A", actualCodecs.Encode(new Probe("forged-origin")));
            oldPeer.Send(fake, RelayOriginFrame.Channel, DeliveryMethod.ReliableOrdered);
            var wrapped = RelayOriginFrame.Encode("peer-A", actualCodecs.Encode(new Probe("nested-forgery")));
            oldPeer.Send(wrapped, 1, DeliveryMethod.ReliableOrdered);
            clock.Restart();
            while (clock.Elapsed < TimeSpan.FromMilliseconds(350)) { old.PollEvents(); await Task.Delay(10); }
            Require(!received.Any(p => p.Packet.Text is "forged-origin" or "nested-forgery"), "Client supplied a relay origin");
        Require(acceptedInputs.Count(frame => frame.Group == ControlGroup.Quadrant) == 1, "Wrong-sender frame reached the ownership output");
            Console.WriteLine("PASS clients cannot inject the reserved origin channel or a nested claimed sender");
            b.Disconnect();
            await Wait(() => !session.Snapshot.CopilotConnected && session.Snapshot.Quadrant.Owner is null, "transport disconnect revokes remote ownership");
            Require(!session.Snapshot.Quadrant.Attached && session.Snapshot.Steering.Owner == "peer-A", "Disconnect silently attached or took away local steering");
            Console.WriteLine("PASS real peer loss revokes remote authority without changing physics host or silently attaching hardware");
        }
        finally { old.Stop(); }
    }

    private static async Task OldRelayFallback()
    {
        // Minimal v1 server speaks the unchanged legacy control/data format.
        // It deliberately ignores the new origin=1 capability advertisement.
        var listener = new EventBasedNetListener();
        var oldServer = new NetManager(listener) { ChannelsCount = 2 };
        NetPeer? remote = null;
        listener.ConnectionRequestEvent += request => request.Accept();
        listener.PeerConnectedEvent += peer => remote = peer;
        listener.NetworkReceiveEvent += (peer, reader, channel, _) =>
        {
            try
            {
                if (channel == 0 && reader.GetByte() == 1)
                {
                    var writer = new NetDataWriter(); writer.Put((byte)10); writer.Put(reader.GetString());
                    peer.Send(writer, 0, DeliveryMethod.ReliableOrdered);
                }
            }
            finally { reader.Recycle(); }
        };
        Require(oldServer.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0), "v1 server start");
        using var stop = new CancellationTokenSource();
        var poll = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested) { oldServer.PollEvents(); await Task.Delay(10); }
        });
        try
        {
            using var client = new RelayNetwork("127.0.0.1", "v2-client", "V2", false, oldServer.LocalPort);
            client.RegisterPacket<Probe, Probe.Codec>();
            var legacy = new ConcurrentQueue<Probe>();
            var sourced = new ConcurrentQueue<ReceivedPacket<Probe>>();
            var capable = false;
            using var a = client.Stream<Probe>().Subscribe(legacy.Enqueue);
            using var b = client.StreamWithSource<Probe>().Subscribe(sourced.Enqueue);
            using var c = client.OriginAvailable.Subscribe(value => capable = value);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Require(await client.Connect("old-source", timeout.Token) == ConnectionResult.Success, "v2 client / v1 relay link");
            var codecsField = typeof(RelayNetwork).GetField("_codecs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var codecs = (Codecs)codecsField.GetValue(client)!;
            remote!.Send(codecs.Encode(new Probe("from-v1")), 1, DeliveryMethod.ReliableOrdered);
            await Wait(() => legacy.Any(p => p.Text == "from-v1"), "legacy packet through v1 server");
            Require(!capable && sourced.IsEmpty, "v1 relay traffic invented an attributable sender");
            Console.WriteLine("PASS new client uses an old relay for legacy sync without enabling sender-aware ownership traffic");
        }
        finally { stop.Cancel(); await poll; oldServer.Stop(); }
    }
}
