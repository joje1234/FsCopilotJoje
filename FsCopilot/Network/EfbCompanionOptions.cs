namespace FsCopilot.Network;

/// <summary>The ordinary EFB helper gets its connection settings from the tablet.</summary>
public sealed record EfbCompanionOptions(string Name, int? RunSeconds, bool DryRun)
{
    public static EfbCompanionOptions? Parse(string[] args)
    {
        if (!args.Contains("--efb-backend", StringComparer.Ordinal)) return null;
        if (args.Any(arg => new[] { "--relay-host=", "--relay-port=", "--local=", "--peer=", "--role=", "--seat=" }.Any(arg.StartsWith))) return null;
        var name = Environment.UserName; int? seconds = null; var dry = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arg in args) {
            var key = arg.Split('=', 2)[0];
            if (!seen.Add(key)) throw new ArgumentException("Duplicate EFB helper option: " + key);
            if (arg is "--efb-backend" or "--debug") continue;
            if (arg == "--efb-dry-run") dry = true;
            else if (arg.StartsWith("--name=", StringComparison.Ordinal)) name = arg[7..];
            else if (arg.StartsWith("--run-seconds=", StringComparison.Ordinal) && int.TryParse(arg[14..], out var duration) && duration is >= 1 and <= 86400) seconds = duration;
            else throw new ArgumentException("Invalid EFB helper option: " + arg);
        }
        if (name.Length is < 1 or > 64 || name.Any(char.IsControl)) throw new ArgumentException("Pilot name must be 1..64 visible characters.");
        return new(name, seconds, dry);
    }
}
