namespace FsCopilot.Network;

using FsCopilot.Ownership;

/// <summary>Explicit opt-in to the EFB backend; ordinary launches keep the existing desktop flow.</summary>
public sealed record SharedBackendOptions(string RelayHost, int RelayPort, SharedSessionOptions Session, string Name, int? RunSeconds, bool DryRun)
{
    public static SharedBackendOptions? Parse(string[] args)
    {
        if (!args.Contains("--efb-backend", StringComparer.Ordinal)) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var dryRun = false;
        foreach (var argument in args) {
            if (argument is "--efb-backend" or "--debug") continue;
            if (argument == "--efb-dry-run") { if (dryRun) throw new ArgumentException("Duplicate dry-run flag."); dryRun = true; continue; }
            var separator = argument.IndexOf('=');
            if (separator < 3 || !values.TryAdd(argument[..separator], argument[(separator + 1)..])) throw new ArgumentException("Invalid or duplicate backend option: " + argument);
        }
        var names = new[] { "--relay-host", "--relay-port", "--local", "--peer", "--role", "--seat", "--name", "--run-seconds" };
        if (values.Keys.Any(key => !names.Contains(key))) throw new ArgumentException("Unknown EFB backend option.");
        string Required(string name) => values.TryGetValue(name, out var value) && value.Length > 0 ? value : throw new ArgumentException("Required EFB backend option: " + name);
        var host = Required("--relay-host");
        if (host.Length > 253 || Uri.CheckHostName(host) == UriHostNameType.Unknown) throw new ArgumentException("Use a relay hostname or IP address.");
        if (!int.TryParse(Required("--relay-port"), out var port) || port is < 1 or > 65535) throw new ArgumentException("Relay port must be 1..65535.");
        static bool Code(string value) => value.Length == 8 && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9');
        var local = Required("--local"); var remote = Required("--peer");
        if (!Code(local) || !Code(remote) || local == remote) throw new ArgumentException("Choose two different eight-character lowercase connection codes.");
        var role = Required("--role") switch { "host" => SessionRole.PhysicsHost, "guest" => SessionRole.Copilot, _ => throw new ArgumentException("Role must be host or guest.") };
        var seat = Required("--seat") switch { "pilot" => CockpitSeat.PilotLeft, "copilot" => CockpitSeat.CopilotRight, _ => throw new ArgumentException("Seat must be pilot or copilot.") };
        var name = values.GetValueOrDefault("--name", Environment.UserName);
        if (name.Length is < 1 or > 64 || name.Any(char.IsControl)) throw new ArgumentException("Pilot name must be 1..64 visible characters.");
        int? seconds = null;
        if (values.TryGetValue("--run-seconds", out var duration)) {
            if (!int.TryParse(duration, out var parsed) || parsed is < 1 or > 86400) throw new ArgumentException("Run duration must be 1..86400 seconds.");
            seconds = parsed;
        }
        return new(host, port, new(local, remote, role, seat), name, seconds, dryRun);
    }
}
