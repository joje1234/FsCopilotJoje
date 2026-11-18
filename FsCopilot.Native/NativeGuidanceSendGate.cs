using FsCopilot.Ownership;

namespace FsCopilot.Native;

public static class NativeGuidanceSendGate
{
    public static bool Allows(NativeGuidancePacket command, NativeBridgeState primary, NativeGuidanceBridgeState guidance)
    {
        if (!primary.Live || !primary.Fresh || primary.Snapshot is not { } observation || primary.Session is not { Phase: NativeSessionPhase.Active } main ||
            !guidance.Live || guidance.Snapshot is not { } ap || !NativeGuidanceWire.Valid(command) || command.Kind != NativeGuidanceKind.Command ||
            !NativeGuidanceWire.Valid(ap) || ap.Kind != NativeGuidanceKind.Status || command.Command <= ap.Command || (observation.Capabilities & 16) == 0 ||
            command.Native != observation.Native || command.Native != main.Native || command.Native != ap.Native || command.Backend != primary.Backend ||
            main.Backend != primary.Backend || command.Backend != ap.Backend || command.Session != main.Session || command.Session != ap.Session ||
            command.Consent != main.Consent || command.Consent != ap.Consent || command.Epoch != main.Epoch || command.Epoch != ap.Epoch ||
            command.ControlRevision != main.Revision || command.ControlRevision != ap.ControlRevision ||
            command.Host != main.Flags.HasFlag(NativeSessionFlags.PhysicsHost) || command.Host != ap.Host || command.ReadSerial > ap.ReadSerial ||
            command.SteeringGeneration != 0 && (command.SteeringGeneration != main.Generation[0] || command.SteeringOwner != main.Owner[0])) return false;
        if (!command.Host && !main.Flags.HasFlag(NativeSessionFlags.World | NativeSessionFlags.WorldFrozen | NativeSessionFlags.WorldFresh)) return false;
        if (command.Action == NativeGuidanceAction.Poll) return true;
        var exact = command.CommandEpoch == ap.CommandEpoch && command.NavigationEpoch == ap.NavigationEpoch && command.Preparation == ap.Preparation;
        if (command.Action == NativeGuidanceAction.IntentAck) return exact && command.Sequence == ap.Sequence && ap.Sequence != 0;
        if (command.Action == NativeGuidanceAction.Stop) return exact;
        if (command.Action == NativeGuidanceAction.Reference) return exact && ap.Flags.HasFlag(NativeGuidanceFlags.Fresh | NativeGuidanceFlags.Profile) &&
            (ap.Flags & (NativeGuidanceFlags.Held | NativeGuidanceFlags.LocalOnly)) == 0;
        if ((ap.Flags & (NativeGuidanceFlags.Held | NativeGuidanceFlags.LocalOnly)) != 0 ||
            !ap.Flags.HasFlag(NativeGuidanceFlags.Fresh | NativeGuidanceFlags.Profile) || command.ReadSerial == 0 ||
            command.CommandEpoch < ap.CommandEpoch || command.NavigationEpoch < ap.NavigationEpoch || command.Preparation < ap.Preparation || command.Revision < ap.Revision) return false;
        if (command.Action == NativeGuidanceAction.Gate) return command.Preparation == ap.Preparation || !command.Capture;
        if (!exact || ap.Blocked != 8191 || !ap.Flags.HasFlag(NativeGuidanceFlags.SingleHostEngine)) return false;
        var disconnect = command.Action == NativeGuidanceAction.Apply && command.Setting == GuidanceSetting.Master && command.Value == 0;
        if (!disconnect && !ap.Flags.HasFlag(NativeGuidanceFlags.GateReady)) return false;
        if (command.Action == NativeGuidanceAction.Apply && !GuidanceProtocol.Supported(ap.Supported, command.Setting)) return false;
        var navigation = command.Value != 0 && (command.Setting is GuidanceSetting.NavHold or GuidanceSetting.ApproachHold ||
            command.Setting == GuidanceSetting.Master && (ap.Switches & (16 | 32 | 64 | 128 | 256)) != 0);
        return command.Action != NativeGuidanceAction.Apply || !navigation || ap.Flags.HasFlag(NativeGuidanceFlags.NavigationReady);
    }
}
