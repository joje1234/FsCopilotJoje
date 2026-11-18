namespace FsCopilot.Simulation;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

public sealed record ControlRoleBinding(string Axis, string Definition, double Minimum, double Maximum, bool CockpitReference = false, double[]? OutputBreakpoints = null, double[]? AxisInputResponse = null);
public sealed record ControlRoleMap(int Version, string Aircraft, int Engines, ControlRoleBinding[] Axes);
public enum DefinitionOutputKind { SimVar = 1, LocalVar = 2, KeyEvent = 3 }
public sealed record ControlDefinitionAxis(int Axis, string Role, string Read, string ReadUnits, double Minimum, double Maximum,
    bool CockpitReference, DefinitionOutputKind OutputKind, string Write, string WriteUnits, double OutputMinimum, double OutputMaximum,
    double[]? OutputBreakpoints = null, double[]? OutputValues = null, double[]? AxisInputResponse = null);

/// <summary>Physical roles select existing vanilla mappings; no independent setters are specified by the role map.</summary>
public sealed class ControlDefinitionProfile
{
    private static readonly string[] SteeringRoles = ["roll", "pitch", "rudder", "steering", "leftBrake", "rightBrake", "elevatorTrim", "aileronTrim", "rudderTrim"];
    private readonly Dictionary<int, Definition> _definitions;
    public string Aircraft { get; }
    public int Engines { get; }
    public string Adapter { get; }
    public IReadOnlyList<ControlDefinitionAxis> Axes { get; }
    public uint Mask => Axes.Aggregate(0u, (value, axis) => value | 1u << axis.Axis);
    private ControlDefinitionProfile(ControlRoleMap map, ControlDefinitionAxis[] axes, Dictionary<int, Definition> definitions)
    {
        Aircraft = map.Aircraft; Engines = map.Engines; Axes = Array.AsReadOnly(axes); _definitions = definitions;
        var canonical = JsonSerializer.Serialize(new { version = 1, aircraft = Aircraft, engines = Engines, axes });
        Adapter = "definitions-v1-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..24];
    }
    public static bool TryAircraftName(string? path, out string name)
    {
        name = "";
        if (string.IsNullOrWhiteSpace(path)) return false;
        // Same aircraft-directory convention used by vanilla SimConnectConsumer,
        // accepting native observations' normalized forward slashes as well.
        var match = Regex.Match(path.Replace('/', '\\'), @"(?:^|\\)SimObjects\\Airplanes\\([^\\]+)(?:\\|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !Regex.IsMatch(match.Groups[1].Value, @"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)) return false;
        name = match.Groups[1].Value.ToLowerInvariant(); return true;
    }
    public static int AxisIndex(string role)
    {
        var steering = Array.IndexOf(SteeringRoles, role);
        if (steering >= 0) return steering;
        for (var engine = 1; engine <= 4; ++engine)
            for (var type = 0; type < 3; ++type)
                if (role == new[] {"throttle", "propeller", "mixture"}[type] + engine) return 9 + (engine - 1) * 3 + type;
        return -1;
    }
    public static ControlDefinitionProfile Load(string aircraftPath)
    {
        if (!TryAircraftName(aircraftPath, out var aircraft)) throw new InvalidDataException("The aircraft directory is unavailable.");
        var path = Path.Combine(AppContext.BaseDirectory, "Definitions", "controls", aircraft + ".json");
        var map = JsonSerializer.Deserialize<ControlRoleMap>(File.ReadAllText(path), new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("Control roles are empty.");
        if (map.Aircraft != aircraft) throw new InvalidDataException("Control roles do not match this aircraft.");
        return Create(Definitions.Load(aircraft), map);
    }
    public static ControlDefinitionProfile Create(Definitions definitions, ControlRoleMap map) => Create(definitions.ToArray(), map);
    public static ControlDefinitionProfile Create(IEnumerable<Definition> definitions, ControlRoleMap map)
    {
        if (map.Version != 1 || map.Engines is < 1 or > 4 || map.Axes is not {Length: > 0 and <= 21} ||
            !Regex.IsMatch(map.Aircraft ?? "", @"^[a-z0-9_-]+$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Unsupported control-role schema.");
        var source = definitions.ToArray(); var selected = new Dictionary<int, Definition>(); var result = new List<ControlDefinitionAxis>();
        foreach (var role in map.Axes)
        {
            if (role is null) throw new InvalidDataException("A control role is empty.");
            var axis = AxisIndex(role.Axis);
            if (axis < 2 || axis is >= 6 and <= 8 || axis >= 9 + map.Engines * 3 || selected.ContainsKey(axis) ||
                !double.IsFinite(role.Minimum) || !double.IsFinite(role.Maximum) || role.Minimum >= role.Maximum)
                throw new InvalidDataException("Unknown, duplicate or invalid control role: " + role.Axis);
            var matches = source.Where(d => d.Get == role.Definition).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Control definition is missing or ambiguous: " + role.Definition);
            var def = matches[0];
            var unit = def.Units.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
            var fullScale = unit == "percent" ? 100d : unit is "position16k" or "percentscaler16k" ? 16384d : unit == "position32k" ? 32768d : unit is "percentover100" or "number" ? 1d : double.NaN;
            if (role.Minimum != (axis < 9 && axis is not (4 or 5) ? -fullScale : 0) || role.Maximum != fullScale)
                throw new InvalidDataException("Physical control range must match its supported scalar unit: " + def.Get);
            string[] steeringVariables = ["AILERON POSITION", "ELEVATOR POSITION", "RUDDER POSITION", "STEER INPUT CONTROL", "BRAKE LEFT POSITION", "BRAKE RIGHT POSITION", "ELEVATOR TRIM PCT", "AILERON TRIM PCT", "RUDDER TRIM PCT"];
            var standard = axis < 9 ? "A:" + steeringVariables[axis] :
                "A:GENERAL ENG " + new[] {"THROTTLE", "PROPELLER", "MIXTURE"}[(axis - 9) % 3] + " LEVER POSITION:" + ((axis - 9) / 3 + 1);
            if (!role.CockpitReference && def.Get != standard) throw new InvalidDataException("A custom cockpit reference must be selected explicitly: " + def.Get);
            // Physical output currently supports direct variable setters and
            // single absolute K setters. Arbitrary expressions stay in vanilla's
            // engine until their native execution semantics can be verified.
            var knots = role.OutputBreakpoints;
            if (knots is not null && (knots.Length is < 3 or > 9 || knots[0] != 0 || knots[^1] != 1 ||
                knots.Any(k => !double.IsFinite(k)) || knots.Zip(knots.Skip(1)).Any(k => k.First >= k.Second)))
                throw new InvalidDataException("Output breakpoints must increase from zero to one.");
            const string numeric = @"\d+(?:\.\d+)?";
            const string segment = @"\(value - " + numeric + @"\) / " + numeric + @" \+ -?" + numeric;
            var curvePattern = @"^`\$\{(?:value <= " + numeric + @" \? " + segment + @" : ){1,7}" + segment + @"\} \(>K:[A-Z0-9_]+\)`$";
            if (def.Setter is { } setter && !(knots is null
                ? Regex.IsMatch(setter, @"^\(>K:[A-Z0-9_]+\)$|^`\$\{(?:-value|-?value\s*[+*-]\s*\d+(?:\.\d+)?)\} \(>K:[A-Z0-9_]+\)`$", RegexOptions.CultureInvariant)
                : Regex.IsMatch(setter, curvePattern, RegexOptions.CultureInvariant)))
                throw new InvalidDataException("This physical setter needs an explicit executor: " + def.Get);
            if (knots is not null) {
                var thresholds = Regex.Matches(def.Setter ?? "", "value <= (" + numeric + ")").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
                if (thresholds.Length != knots.Length - 2 || thresholds.Where((v, i) => Math.Abs(v - (role.Minimum + knots[i + 1] * (role.Maximum - role.Minimum))) > 1e-8).Any())
                    throw new InvalidDataException("Output breakpoints must match the setter's piecewise affine branches.");
            }
            var target = def.ParseSet(role.Minimum, role.Minimum, out var units, out var values);
            var kind = target.StartsWith("A:", StringComparison.Ordinal) ? DefinitionOutputKind.SimVar :
                target.StartsWith("L:", StringComparison.Ordinal) ? DefinitionOutputKind.LocalVar :
                target.StartsWith("K:", StringComparison.Ordinal) ? DefinitionOutputKind.KeyEvent : 0;
            if (kind == 0 || values.Length != 1 || !Text(def.Get, 96) || !Text(def.Units, 32) || !Text(target, 96) || !Text(units, 32, true) ||
                (role.CockpitReference && !(def.Get.StartsWith("L:", StringComparison.Ordinal) && kind == DefinitionOutputKind.LocalVar ||
                    def.Get.StartsWith("A:", StringComparison.Ordinal) && axis >= 2 && axis < 9 && kind is DefinitionOutputKind.KeyEvent or DefinitionOutputKind.SimVar)))
                throw new InvalidDataException("Unsupported typed control mapping: " + def.Get);
            if (kind == DefinitionOutputKind.KeyEvent && !(target.EndsWith("_SET", StringComparison.Ordinal) || target.EndsWith("_SET_EX1", StringComparison.Ordinal)))
                throw new InvalidDataException("Physical output requires an absolute setter: " + target);
            double Argument(double value, double current) {
                var parsed = def.ParseSet(value, current, out var parsedUnits, out var parsedValues);
                if (parsed != target || parsedUnits != units || parsedValues.Length != 1) throw new InvalidDataException("Inconsistent physical setter: " + def.Get);
                var argument = Convert.ToDouble(parsedValues[0], CultureInfo.InvariantCulture);
                if (!double.IsFinite(argument)) throw new InvalidDataException("Nonfinite physical setter: " + def.Get);
                return argument;
            }
            var outputMinimum = Argument(role.Minimum, role.Maximum); var outputMaximum = Argument(role.Maximum, role.Minimum);
            var positions = knots ?? [0d, 1d];
            var outputs = positions.Select(p => Argument(role.Minimum + p * (role.Maximum - role.Minimum), role.Minimum)).ToArray();
            for (var part = 0; part < positions.Length - 1; ++part) {
                if (knots is not null && (outputs[part + 1] - outputs[part]) * (outputMaximum - outputMinimum) <= 0)
                    throw new InvalidDataException("Output curve must be strictly monotonic.");
                for (var step = 0; step <= 16; ++step) foreach (var current in new[] {role.Minimum, role.Maximum}) {
                    var fraction = step / 16d;
                    var position = positions[part] + fraction * (positions[part + 1] - positions[part]);
                    var actual = Argument(role.Minimum + position * (role.Maximum - role.Minimum), current);
                    if (Math.Abs(actual - (outputs[part] + fraction * (outputs[part + 1] - outputs[part]))) > 1e-8)
                        throw new InvalidDataException("Physical setters must be absolute piecewise affine mappings: " + def.Get);
                }
            }
            if (kind == DefinitionOutputKind.KeyEvent && (outputMinimum != Math.Round(outputMinimum) || outputMaximum != Math.Round(outputMaximum) ||
                Math.Min(outputMinimum, outputMaximum) < int.MinValue || Math.Max(outputMinimum, outputMaximum) > int.MaxValue))
                throw new InvalidDataException("Key-event range must fit an integer.");
            if (role.AxisInputResponse is {} response && (kind != DefinitionOutputKind.KeyEvent || knots is not null || response.Length is <2 or >9 ||
                response[0] != 0 || response[^1] != 1 || response.Any(v=>!double.IsFinite(v)) || response.Zip(response.Skip(1)).Any(pair=>pair.First>=pair.Second)))
                throw new InvalidDataException("Axis input response needs an affine key setter and 2..9 strictly increasing normalized samples.");
            selected.Add(axis, def);
            result.Add(new(axis, role.Axis, def.Get, def.Units, role.Minimum, role.Maximum, role.CockpitReference, kind, target, units, outputMinimum, outputMaximum,
                knots?.ToArray(), knots is null ? null : outputs, role.AxisInputResponse?.ToArray()));
        }
        for (var axis = 9; axis < 9 + map.Engines * 3; ++axis)
            if (!selected.ContainsKey(axis)) throw new InvalidDataException("Every active throttle, propeller and mixture needs a role.");
        return new(map, result.OrderBy(a => a.Axis).ToArray(), selected);
    }
    public (string Target, string Units, object[] Values) Plan(int axis, double normalized, bool axisSource = false)
    {
        var mapping = Axes.SingleOrDefault(a => a.Axis == axis) ?? throw new InvalidDataException("Control is not mapped.");
        if (!double.IsFinite(normalized) || normalized < 0 || normalized > 1) throw new InvalidDataException("Invalid normalized control position.");
        if (axisSource && mapping.AxisInputResponse is {} response) {
            var part=0;while(part+2<response.Length&&normalized>response[part+1])++part;
            normalized=(part+(normalized-response[part])/(response[part+1]-response[part]))/(response.Length-1);
        }
        var value = mapping.Minimum + normalized * (mapping.Maximum - mapping.Minimum);
        var target = _definitions[axis].ParseSet(value, value, out var units, out var values);
        // Apply the definition's unit conversion before rounding the SDK payload.
        // Rounding a -1..1 getter first would collapse scaled axis setters to detents.
        if (mapping.OutputKind == DefinitionOutputKind.KeyEvent && mapping.OutputBreakpoints is null)
            values[0] = Math.Round(Convert.ToDouble(values[0]), MidpointRounding.AwayFromZero);
        return (target, units, values);
    }
    private static bool Text(string value, int capacity, bool empty = false) => (empty || value.Length > 0) && value.Length < capacity && value.All(c => c is >= ' ' and <= '~' && c is not '"' and not '\\');
}
