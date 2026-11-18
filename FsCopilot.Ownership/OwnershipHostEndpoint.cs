namespace FsCopilot.Ownership;

/// <summary>
/// Wire-to-policy adapter. The caller broadcasts a returned snapshot and routes
/// an accepted frame to the physics simulator through a separate simulator
/// adapter. This class never transmits simulator events or changes MasterSwitch.
/// </summary>
public sealed class OwnershipHostEndpoint(OwnershipSession session)
{
    public OwnershipSession Session { get; } = session;
    private readonly object _sync = new();

    public bool Receive(string transportSender, ReadOnlySpan<byte> bytes,
        out OwnershipSnapshot? changed, out ControlFrame? accepted)
    {
        lock (_sync)
        {
            changed = null; accepted = null;
            if (!OwnershipWire.TryDecode(bytes, out var message)) return false;
            var before = Session.Snapshot.Revision;
            bool applied;
            switch (message)
            {
                case RequestMessage request: applied = Session.RequestTransfer(transportSender, request.Request); break;
                case PreparationMessage preparation: applied = Session.AcknowledgePreparation(transportSender, preparation.Ack); break;
                case AttachmentMessage attachment: applied = Session.AcknowledgeAttachment(transportSender, attachment.Ack); break;
                case CancellationMessage cancellation: applied = Session.CancelTransfer(transportSender, cancellation.Cancellation); break;
                case InputMessage input:
                    applied = Session.AcceptInput(transportSender, input.Frame);
                    if (applied) accepted = input.Frame;
                    break;
                default: return false; // A peer's claimed snapshot cannot overwrite host authority.
            }
            var after = Session.Snapshot;
            if (after.Revision != before) changed = after;
            return applied;
        }
    }
}
