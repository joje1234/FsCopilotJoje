namespace FsCopilot.Simulation;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public sealed record CockpitDefinitionPolicy(int Version, string[] ExcludeModules, string[] Exclude, string Reason, string[]? Required = null,
    string[]? RetrySafe = null, Dictionary<string,string>? Labels = null, bool SharedAutopilot = false, Dictionary<string,double>? Tolerances = null);
public sealed record CockpitDefinitionEntry(int Index, string Module, Definition Definition);

/// <summary>Ordinary state uses vanilla setters. AP sharing is an explicit aircraft-policy choice; physical controls and navigation stay separate.</summary>
public sealed class CockpitDefinitionProfile
{
    public const int Maximum = 512;
    public string Aircraft { get; }
    public string Identity { get; }
    public IReadOnlyList<CockpitDefinitionEntry> Entries { get; }
    public IReadOnlyList<string> Excluded { get; }
    public IReadOnlyList<int> Required { get; }
    public IReadOnlySet<int> RetrySafe { get; }
    public IReadOnlyList<double> Tolerances { get; }
    private readonly Dictionary<string,string> _labels;
    public string Label(int index) => _labels.GetValueOrDefault(Entries[index].Definition.Get,Entries[index].Definition.Get);
    private CockpitDefinitionProfile(string aircraft, CockpitDefinitionEntry[] entries, string[] excluded, string[] required, string[] retrySafe, Dictionary<string,string> labels, Dictionary<string,double> tolerances) {
        Aircraft = aircraft; Entries = Array.AsReadOnly(entries); Excluded = Array.AsReadOnly(excluded);
        Required=Array.AsReadOnly(required.Select(name=>entries.Single(e=>e.Definition.Get==name).Index).Order().ToArray());
        RetrySafe=retrySafe.Select(name=>entries.Single(e=>e.Definition.Get==name).Index).ToHashSet();_labels=new(labels);
        Tolerances=Array.AsReadOnly(entries.Select(e=>tolerances.GetValueOrDefault(e.Definition.Get,.000001)).ToArray());
        var canonical = JsonSerializer.Serialize(new {replicationVersion=2,entries=entries.Select(e => new {e.Module, e.Definition.Get, e.Definition.Units, e.Definition.Shared, e.Definition.Setter, e.Definition.Skip}),required=Required,retrySafe=RetrySafe.Order(),tolerances=Tolerances});
        Identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..32];
    }
    public static CockpitDefinitionProfile Load(string aircraft) {
        var policy = JsonSerializer.Deserialize<CockpitDefinitionPolicy>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Definitions", "cockpit", aircraft + ".json")),
            new JsonSerializerOptions {PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow})
            ?? throw new InvalidDataException("Cockpit policy is empty.");
        if (!Definitions.TryLoadTree(aircraft + ".yaml", out var tree)) throw new InvalidDataException("Cockpit definitions unavailable.");
        var controls = ControlDefinitionProfile.Load("simobjects/airplanes/" + aircraft + "/aircraft.cfg");
        return Create(aircraft, tree, controls, policy);
    }
    public static CockpitDefinitionProfile Create(string aircraft, DefinitionNode tree, ControlDefinitionProfile controls, CockpitDefinitionPolicy policy) {
        if (policy.Version != 1 || policy.ExcludeModules is null || policy.Exclude is null) throw new InvalidDataException("Unsupported cockpit policy.");
        var entries = new List<CockpitDefinitionEntry>(); var excluded = new List<string>();
        var physical = controls.Axes.Select(a => a.Read).ToHashSet(StringComparer.Ordinal);
        // Generic boundaries match the native physical/AP engines. Aircraft-specific choices stay in policy data.
        bool Trim(Definition d) => Regex.IsMatch(d.Get,@"^A:(?:AILERON|ELEVATOR|RUDDER) TRIM ",RegexOptions.CultureInvariant);
        bool Separate(Definition d) => physical.Contains(d.Get) || (!Trim(d) && (Regex.IsMatch(d.Get,
            @"^A:(?:GPS |AILERON |ELEVATOR |RUDDER |STEER INPUT|BRAKE (?:LEFT|RIGHT) POSITION|GENERAL ENG (?:THROTTLE|PROPELLER|MIXTURE) LEVER POSITION)", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(d.Setter ?? "", @">K:(?:2:)?(?:.*(?:THROTTLE|PROPELLER|MIXTURE|BRAKE|AILERON|ELEVATOR|RUDDER)_.*SET)", RegexOptions.CultureInvariant))) ||
            (!policy.SharedAutopilot && (d.Get.StartsWith("A:AUTOPILOT ", StringComparison.Ordinal) ||
            Regex.IsMatch(d.Setter ?? "", @">K:(?:2:)?(?:AP_|HEADING_BUG_|AUTOPILOT_|TOGGLE_FLIGHT_DIRECTOR|FLIGHT_LEVEL_CHANGE|YAW_DAMPER)", RegexOptions.CultureInvariant)));
        void Visit(DefinitionNode node, bool local) {
            local |= policy.ExcludeModules.Contains(node.Path, StringComparer.Ordinal);
            foreach (var child in node.Include) Visit(child, local);
            foreach (var d in node.Master.Concat(node.Shared)) {
                if (local || policy.Exclude.Contains(d.Get, StringComparer.Ordinal) || Separate(d) ||
                    !(d.Get.StartsWith("A:", StringComparison.Ordinal) || d.Get.StartsWith("L:", StringComparison.Ordinal) || d.Get.StartsWith("B:", StringComparison.Ordinal)) ||
                    d.Units.Equals("String", StringComparison.OrdinalIgnoreCase)) { excluded.Add(d.Get); continue; }
                entries.Add(new(0, node.Path, d));
            }
        }
        Visit(tree, false);
        var selected = entries.GroupBy(e => e.Definition.Get, StringComparer.Ordinal).Select(group => {
            var items = group.ToArray(); var first = items[0].Definition;
            if (items.Any(e => e.Definition.Units != first.Units || e.Definition.Shared != first.Shared || e.Definition.Setter != first.Setter))
                throw new InvalidDataException("Ambiguous ordinary cockpit definition: " + first.Get);
            return items[0];
        }).OrderBy(e => e.Definition.Get, StringComparer.Ordinal).Select((e, index) => e with {Index = index}).ToArray();
        if (selected.Length is < 1 or > Maximum) throw new InvalidDataException("Cockpit catalog exceeds the supported size.");
        if ((policy.Required??[]).Any(name=>!selected.Any(e=>e.Definition.Get==name))) throw new InvalidDataException("Required cockpit definition is missing or excluded.");
        if((policy.RetrySafe??[]).Any(name=>!selected.Any(e=>e.Definition.Get==name && e.Definition.Shared)) ||
            (policy.Labels??[]).Any(pair=>!selected.Any(e=>e.Definition.Get==pair.Key) || pair.Value is null || pair.Value.Length is <1 or >80 || pair.Value.Any(c=>c is <' ' or >'~')))
            throw new InvalidDataException("Invalid cockpit retry/label policy.");
        if((policy.Tolerances??[]).Any(pair=>!selected.Any(e=>e.Definition.Get==pair.Key) || !double.IsFinite(pair.Value) || pair.Value<=0 || pair.Value>1))
            throw new InvalidDataException("Invalid cockpit readback tolerance policy.");
        return new(aircraft, selected, excluded.Distinct(StringComparer.Ordinal).ToArray(),policy.Required??[],policy.RetrySafe??[],policy.Labels??[],policy.Tolerances??[]);
    }
    public string Plan(int index, double desired, double current) {
        if (index < 0 || index >= Entries.Count || !double.IsFinite(desired) || !double.IsFinite(current)) throw new InvalidDataException("Invalid cockpit update.");
        if (Equivalent(desired, current,Tolerances[index])) return ""; // Suppress equality toggles and declared SDK output quantization.
        var expression = Entries[index].Definition.Set(desired, current);
        if (expression.Length >= 2048 || expression.Any(c => c is < ' ' or > '~')) throw new InvalidDataException("Cockpit setter is too large or contains invalid text.");
        return expression;
    }
    public static bool Equivalent(double a, double b, double tolerance=.000001) => double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) <= tolerance;
}
