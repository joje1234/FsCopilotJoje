namespace FsCopilot.Ownership;

[Flags]
public enum ControlCapabilities { None = 0, HardwareObservation = 1, ConfirmedMasks = 2, IsolatedRemoteInput = 4, IndependentEnginePairs = 8 }
public enum SessionRole { PhysicsHost, Copilot }
public enum CockpitSeat { PilotLeft = 1, CopilotRight = 2 }
public sealed record CockpitSeats(string PilotPeer, string CopilotPeer);
public enum NegotiationPhase { Created, WaitingForPeer, WaitingForOffer, WaitingForAcceptance, WaitingForCommit, Established, Rejected, TimedOut, Closed, PeerLost }
public sealed record SessionConfiguration(string Aircraft, int Engines, string Adapter, int NativeProtocol, ControlCapabilities Capabilities,
    uint SteeringAxes = AtomicProtocol.DefaultSteeringAxes);
public sealed record NegotiationState(NegotiationPhase Phase, string Error, Guid? Session);

/// <summary>
/// Explicitly binds two transport identities and matching verified simulator
/// capabilities to a new session. The caller serializes calls. Establishment
/// agrees metadata only: every control group still requires gate/attachment acks.
/// </summary>
public sealed class SessionNegotiation
{
    public const ControlCapabilities RequiredCapabilities = ControlCapabilities.HardwareObservation |
        ControlCapabilities.ConfirmedMasks | ControlCapabilities.IsolatedRemoteInput;
    public const ControlCapabilities AdvancedCapabilities = RequiredCapabilities | ControlCapabilities.IndependentEnginePairs;
    private readonly string _local;
    private readonly string _remote;
    private readonly SessionRole _role;
    private readonly SessionConfiguration _configuration;
    private readonly CockpitSeat _seat;
    private readonly Func<TimeSpan> _clock;
    private readonly TimeSpan _timeout;
    private readonly Guid _attempt = Guid.NewGuid();
    private TimeSpan _deadline;
    private SessionHandshake? _retry;
    private SessionOffer? _offer;
    private AtomicSessionOffer? _atomicOffer;
    private OwnershipSession? _host;
    private OwnershipReplica? _replica;
    private ControlAuthoritySession? _authority;
    private ControlAuthorityReplica? _authorityReplica;
    public NegotiationState State { get; private set; } = new(NegotiationPhase.Created, "", null);
    private bool HostAvailable => State.Phase == NegotiationPhase.Established || State.Phase == NegotiationPhase.PeerLost && _role == SessionRole.PhysicsHost;
    public OwnershipSession? HostSession => HostAvailable ? _host : null;
    public OwnershipReplica? Replica => State.Phase == NegotiationPhase.Established ? _replica : null;
    public ControlAuthoritySession? AuthoritySession => HostAvailable ? _authority : null;
    public ControlAuthorityReplica? AuthorityReplica => State.Phase == NegotiationPhase.Established ? _authorityReplica : null;
    public CockpitSeats? Seats => HostAvailable ? _offer?.Seats ?? _atomicOffer?.Seats : null;
    private bool Advanced => _configuration.Capabilities == AdvancedCapabilities;

    public SessionNegotiation(string local, string remote, SessionRole role, SessionConfiguration configuration,
        Func<TimeSpan> clock, TimeSpan? timeout = null, CockpitSeat? seat = null)
    {
        if (!OwnershipProtocol.ValidPeer(local) || !OwnershipProtocol.ValidPeer(remote) || local == remote ||
            !Enum.IsDefined(role) || !ValidConfiguration(configuration)) throw new ArgumentException("Invalid session participants/configuration.");
        _local = local; _remote = remote; _role = role; _configuration = configuration;
        _seat = seat ?? (role == SessionRole.PhysicsHost ? CockpitSeat.PilotLeft : CockpitSeat.CopilotRight);
        if (!Enum.IsDefined(_seat)) throw new ArgumentException("Invalid cockpit seat.", nameof(seat));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public static bool ValidConfiguration(SessionConfiguration? configuration) => configuration is not null &&
        ValidToken(configuration.Aircraft, 128) && ValidToken(configuration.Adapter, 64) && configuration.Engines is >= 1 and <= 4 &&
        configuration.NativeProtocol is > 0 and <= 65535 && AtomicProtocol.ValidSteeringAxes(configuration.SteeringAxes) &&
        (configuration.Capabilities == RequiredCapabilities && configuration.SteeringAxes == AtomicProtocol.DefaultSteeringAxes ||
         configuration.Capabilities == AdvancedCapabilities && configuration.NativeProtocol >= 2);
    private static bool ValidToken(string? token, int maximum) => token is { Length: > 0 } && token.Length <= maximum &&
        token.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.');
    public static bool InitialSnapshot(OwnershipSnapshot? snapshot) => OwnershipProtocol.ValidSnapshot(snapshot) && snapshot is not null &&
        snapshot.Revision == 1 && snapshot.CopilotConnected &&
        snapshot.Steering is { Generation: 1, Attached: false, Pending: null } && snapshot.Steering.Owner == snapshot.PhysicsHost &&
        snapshot.Quadrant is { Generation: 1, Attached: false, Pending: null } && snapshot.Quadrant.Owner == snapshot.PhysicsHost;
    private bool InProgress => State.Phase is NegotiationPhase.WaitingForPeer or NegotiationPhase.WaitingForOffer or
        NegotiationPhase.WaitingForAcceptance or NegotiationPhase.WaitingForCommit;

    public SessionHandshake? Start()
    {
        if (State.Phase != NegotiationPhase.Created) throw new InvalidOperationException("A new negotiation object is required for each join.");
        _deadline = _clock() + _timeout;
        State = new(_role == SessionRole.PhysicsHost ? NegotiationPhase.WaitingForPeer : NegotiationPhase.WaitingForOffer, "", null);
        _retry = _role == SessionRole.Copilot ? new SessionHello(SessionWire.Version, _attempt, _configuration, _seat) : null;
        return _retry;
    }

    public SessionHandshake? Tick()
    {
        if (!InProgress) return null;
        if (_clock() >= _deadline) {
            var end = Ending(); Finish(NegotiationPhase.TimedOut, "Session negotiation timed out. Start a fresh session."); return end;
        }
        return _retry;
    }

    public SessionHandshake? Receive(string transportSender, SessionHandshake message)
    {
        if (transportSender != _remote || !SessionWire.Valid(message)) return null;
        if (InProgress && _clock() >= _deadline) return Tick();
        if (!InProgress && State.Phase != NegotiationPhase.Established) return null;
        switch (message)
        {
            case SessionEnd end:
                if (Matches(end.HostAttempt, end.ClientAttempt, end.Session)) Finish(NegotiationPhase.Closed, "The other participant ended this session.");
                return null;
            case SessionHello hello when _role == SessionRole.PhysicsHost:
                if (_offer is not null || _atomicOffer is not null) return hello.ClientAttempt == ClientAttempt && hello.Configuration == _configuration && hello.Seat != _seat ? (SessionHandshake?)_offer ?? _atomicOffer : null;
                if (State.Phase != NegotiationPhase.WaitingForPeer) return null;
                if (hello.Configuration != _configuration) {
                    Finish(NegotiationPhase.Rejected, "Aircraft, adapter or simulator bridge capabilities do not match.");
                    return new SessionRejection(SessionWire.Version, hello.ClientAttempt, "configuration-mismatch");
                }
                if (hello.Seat == _seat) {
                    Finish(NegotiationPhase.Rejected, "Both participants selected the same cockpit seat.");
                    return new SessionRejection(SessionWire.Version, hello.ClientAttempt, "seat-conflict");
                }
                var seats = _seat == CockpitSeat.PilotLeft ? new CockpitSeats(_local, _remote) : new CockpitSeats(_remote, _local);
                if (Advanced) {
                    _authority = new(_local, _remote, _configuration.Engines, _clock, _configuration.SteeringAxes, hostSeat: _seat);
                    _atomicOffer = new(SessionWire.Version, _attempt, hello.ClientAttempt, _configuration, _authority.Snapshot, seats);
                } else {
                    _host = new(_local, _remote, _configuration.Engines, _clock);
                    _offer = new(SessionWire.Version, _attempt, hello.ClientAttempt, _configuration, _host.Snapshot, seats);
                }
                State = new(NegotiationPhase.WaitingForAcceptance, "", null);
                return _retry = (SessionHandshake?)_offer ?? _atomicOffer;
            case SessionOffer offer when _role == SessionRole.Copilot:
                if (Advanced) return null;
                if (offer.ClientAttempt != _attempt || offer.Initial.PhysicsHost != _remote || offer.Initial.Copilot != _local) return null;
                if ((_seat == CockpitSeat.PilotLeft ? offer.Seats.PilotPeer : offer.Seats.CopilotPeer) != _local) return null;
                if (_offer is not null) return SameOffer(offer, _offer) ? new SessionAcceptance(SessionWire.Version, offer.HostAttempt, _attempt, offer.Initial.Session) : null;
                if (State.Phase != NegotiationPhase.WaitingForOffer) return null;
                if (offer.Configuration != _configuration) { Finish(NegotiationPhase.Rejected, "Aircraft, adapter or simulator bridge capabilities do not match."); return null; }
                _offer = offer;
                State = new(NegotiationPhase.WaitingForCommit, "", null);
                return _retry = new SessionAcceptance(SessionWire.Version, offer.HostAttempt, _attempt, offer.Initial.Session);
            case AtomicSessionOffer offer when _role == SessionRole.Copilot:
                if (!Advanced || offer.ClientAttempt != _attempt || offer.Initial.PhysicsHost != _remote || offer.Initial.Peer != _local ||
                    (_seat == CockpitSeat.PilotLeft ? offer.Seats.PilotPeer : offer.Seats.CopilotPeer) != _local) return null;
                if (_atomicOffer is not null) return offer.HostAttempt == _atomicOffer.HostAttempt && offer.Initial.Session == _atomicOffer.Initial.Session &&
                    offer.Configuration == _atomicOffer.Configuration && offer.Seats == _atomicOffer.Seats ?
                    new SessionAcceptance(SessionWire.Version, offer.HostAttempt, _attempt, offer.Initial.Session) : null;
                if (State.Phase != NegotiationPhase.WaitingForOffer) return null;
                if (offer.Configuration != _configuration) { Finish(NegotiationPhase.Rejected, "Aircraft or native control layout does not match."); return null; }
                _atomicOffer = offer; State = new(NegotiationPhase.WaitingForCommit, "", null);
                return _retry = new SessionAcceptance(SessionWire.Version, offer.HostAttempt, _attempt, offer.Initial.Session);
            case SessionAcceptance acceptance when _role == SessionRole.PhysicsHost && (_offer is not null || _atomicOffer is not null):
                if (!Matches(acceptance.HostAttempt, acceptance.ClientAttempt, acceptance.Session)) return null;
                State = new(NegotiationPhase.Established, "", SessionId);
                _retry = null;
                return new SessionCommit(SessionWire.Version, HostAttempt, ClientAttempt, SessionId);
            case SessionCommit commit when _role == SessionRole.Copilot && (_offer is not null || _atomicOffer is not null):
                if (!Matches(commit.HostAttempt, commit.ClientAttempt, commit.Session)) return null;
                if (State.Phase == NegotiationPhase.Established) return null;
                if (_atomicOffer is not null) _authorityReplica = new(_remote, _atomicOffer.Initial);
                else _replica = new(_remote, _offer!.Initial);
                State = new(NegotiationPhase.Established, "", commit.Session);
                _retry = null;
                return null;
            case SessionRejection rejection when _role == SessionRole.Copilot && State.Phase == NegotiationPhase.WaitingForOffer:
                if (rejection.ClientAttempt == _attempt) Finish(NegotiationPhase.Rejected,
                    rejection.Reason == "seat-conflict" ? "Both participants selected the same cockpit seat." : "The host rejected the session configuration.");
                return null;
            default: return null;
        }
    }

    private Guid HostAttempt => _offer?.HostAttempt ?? _atomicOffer?.HostAttempt ?? Guid.Empty;
    private Guid ClientAttempt => _offer?.ClientAttempt ?? _atomicOffer?.ClientAttempt ?? Guid.Empty;
    private Guid SessionId => _offer?.Initial.Session ?? _atomicOffer?.Initial.Session ?? Guid.Empty;
    private bool Matches(Guid host, Guid client, Guid session) => (_offer is not null || _atomicOffer is not null) &&
        host == HostAttempt && client == ClientAttempt && session == SessionId;
    private static bool SameOffer(SessionOffer left, SessionOffer right) => left.HostAttempt == right.HostAttempt &&
        left.ClientAttempt == right.ClientAttempt && left.Configuration == right.Configuration && left.Initial == right.Initial && left.Seats == right.Seats;
    public void Close(string reason = "Session closed. A fresh negotiation is required.") => Finish(NegotiationPhase.Closed, reason);
    public void PeerDisconnected()
    {
        if (State.Phase is NegotiationPhase.Closed or NegotiationPhase.Rejected or NegotiationPhase.TimedOut or NegotiationPhase.PeerLost) return;
        if (State.Phase != NegotiationPhase.Established) { Close("Peer disconnected during negotiation."); return; }
        _host?.CopilotDisconnected(); _authority?.PeerDisconnected(); _retry = null;
        State = new(NegotiationPhase.PeerLost, "Peer disconnected. Existing host controls remain active; recovery is explicit.", SessionId);
    }
    private SessionEnd? Ending() => SessionId == Guid.Empty ? null : new(SessionWire.Version, HostAttempt, ClientAttempt, SessionId);
    public SessionEnd? End() { var end = Ending(); Close(); return end; }
    private void Finish(NegotiationPhase phase, string error)
    {
        _host?.End();
        _authority?.End();
        _retry = null; State = new(phase, error, null);
    }
}
