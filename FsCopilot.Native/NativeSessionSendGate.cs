namespace FsCopilot.Native;

/// <summary>Rechecked by the SDK reader at execution, after queueing copied command bytes.</summary>
public static class NativeSessionSendGate
{
    public static bool Allows(NativeSessionPacket command, string backend, NativeControlPacket observation, NativeSessionPacket current)
    {
        if (!NativeSessionWire.Valid(command) || command.Kind == NativeSessionKind.Status || !NativeSessionWire.Valid(current) ||
            current.Kind != NativeSessionKind.Status || !NativeControlWire.Valid(observation) || observation.Kind == NativePacketKind.Hello ||
            command.Backend != backend || command.Native != observation.Native || command.Native != current.Native ||
            command.Consent != current.Consent) return false;
        if (command.Kind == NativeSessionKind.Bind)
            return current.Phase == NativeSessionPhase.Armed && observation.Flags.HasFlag(NativeStatusFlags.Ready);
        if (current.Phase is not (NativeSessionPhase.Binding or NativeSessionPhase.Active) || current.Backend != backend ||
            current.Session != command.Session || current.Epoch != command.Epoch) return false;
        if (command.Kind == NativeSessionKind.End) return true;
        if (command.Kind == NativeSessionKind.IntentAck) return current.Phase == NativeSessionPhase.Active && current.IntentId == command.Sequence;
        if (command.Kind == NativeSessionKind.State) return current.Phase == NativeSessionPhase.Active && command.Revision >= current.Revision;
        if (command.Revision != current.Revision) return false;
        if (command.Kind == NativeSessionKind.Heartbeat) return true;
        if (current.Phase != NativeSessionPhase.Active) return false;
        if (command.Kind == NativeSessionKind.Barometer) {
            var body = command.Barometer!;
            var instruments = observation.Altimeters;
            if (instruments.LocalOnly || instruments.Held || instruments.Recovering) return false;
            if (!instruments.Fresh || !instruments.Transport || !instruments.Profile ||
                body.SeatRevision != current.SeatRevision || body.Seat != current.Seat || body.ReadSerial > instruments.Serial) return false;
            var otherMask = current.Seat == 1 ? 2u : 1u;
            return body.Action switch {
                NativeBarometerAction.Gate => body.Mask == 3 || body.Mask == otherMask && instruments.GateReady,
                NativeBarometerAction.Stop => true, // Requests both knobs held; it never unmasks an instrument.
                NativeBarometerAction.Seed => instruments.GateReady && instruments.Blocked == 3,
                NativeBarometerAction.Apply => instruments.GateReady && body.Instrument != current.Seat &&
                    (instruments.Blocked & (1u << ((int)body.Instrument - 1))) != 0,
                NativeBarometerAction.Observe => instruments.GateReady && (body.Instrument == current.Seat || instruments.Blocked == 3),
                _ => false
            };
        }
        if (command.Kind == NativeSessionKind.Feedback) return command.Automatic == current.Automatic;
        if (command.Kind == NativeSessionKind.InstrumentFeedback) return observation.Altimeters.Transport &&
            command.InstrumentFeedback!.SeatRevision == current.SeatRevision;
        if (command.Kind == NativeSessionKind.World) return current.Flags.HasFlag(NativeSessionFlags.World) &&
            !current.Flags.HasFlag(NativeSessionFlags.PhysicsHost) && command.Axes == current.Axes && command.Generation.SequenceEqual(current.Generation);
        // World positions may be unknown (-1), as validated by the wire layer.
        // The native guest skips only incomplete groups while applying fresh pose.
        if (command.Kind != NativeSessionKind.Input) return true; // Native preparation/attachment validation remains authoritative.
        if (!current.Flags.HasFlag(NativeSessionFlags.PhysicsHost) || command.Selection is not (1 or 2 or 4 or 8) || command.Sequence == 0) return false;
        var index = command.Selection == 1 ? 0 : command.Selection == 2 ? 1 : command.Selection == 4 ? 2 : 3;
        if (current.Owner[index] != NativeOwner.Remote || (current.Attached & command.Selection) == 0 ||
            (current.Pending & command.Selection) != 0 || command.Generation[index] != current.Generation[index]) return false;
        uint required = 0;
        for (var axis = 0; axis < 21; ++axis)
            if ((current.Axes & (1u << axis)) != 0 && (axis < 9 ? 1u : 1u << (1 + (axis - 9) % 3)) == command.Selection) {
                if (command.Positions[axis] < 0) return false;
                required |= 1u << axis;
            }
        return required != 0 && command.Axes == required;
    }
}
