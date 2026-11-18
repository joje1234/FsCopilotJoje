global using Serilog;
using System.Globalization;
using System.Text.Json;
using FsCopilot.Simulation;

try {
var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    ++checks;
}
IEnumerable<(string File, Definition Link)> Links(DefinitionNode node)
{
    foreach (var child in node.Include)
        foreach (var link in Links(child)) yield return link;
    foreach (var link in node.Master.Concat(node.Shared)) yield return (node.Path, link);
}

var original = args.SingleOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "Original", "baron.yaml");
// Copy only the reviewed source fixture into this runner's own build output.
// Definitions.cs resolves included modules beside the executable.
var fixture = Path.Combine(AppContext.BaseDirectory, "Definitions", "original-baron.yaml");
File.Copy(original, fixture, true);
Check(Definitions.TryLoadTree("original-baron.yaml", out var originalTree), "Original profile must parse with vanilla's loader.");
Check(Definitions.TryLoadTree("bksq-aircraft-baronpropress.yaml", out var candidateTree), "Candidate profile must parse with the same loader.");
var oldLinks = Links(originalTree).ToArray();
var newLinks = Links(candidateTree).ToArray();
Check(originalTree.Include.Length == 5 && candidateTree.Include.Length == 6, "All five original includes plus the standard vanilla surface route remain present.");

var oldAltimeters = oldLinks.Where(x => x.Link.Get.StartsWith("L:INSTRUMENT_Altimeter_", StringComparison.Ordinal)).ToArray();
Check(oldAltimeters.Length == 2, "Two original cockpit-altimeter mappings.");
foreach (var item in oldAltimeters)
{
    // These assertions reproduce a defect in the actual original expressions,
    // not a claim that a simulated loop proves the user's complete live failure.
    Check(item.Link.Set(1013.25, 1013.25).Contains("KOHLSMAN_DEC", StringComparison.Ordinal), "Original equal values incorrectly decrement.");
    Check(item.Link.Set(1013.3125, 1013.25) == item.Link.Set(1020.0, 1013.25), "Original setter discards the size of the pressure difference.");
    Check(item.Link.Set(1013.3125, 1013.3125).Contains("KOHLSMAN_DEC", StringComparison.Ordinal), "A repeated now-aligned target sends a reverse command.");
}
Check(oldLinks.Any(x => x.Link.Get == "A:KOHLSMAN SETTING MB:0" && x.File.Contains("PMS50", StringComparison.Ordinal)), "Included vanilla GTN module adds another barometer writer.");
Check(!newLinks.Any(x => x.Link.Get.StartsWith("L:INSTRUMENT_Altimeter_", StringComparison.Ordinal) || x.Link.Get == "A:KOHLSMAN SETTING MB:0"), "Candidate removes both relative writers and the overlapping module writer.");

var barometers = newLinks.Where(x => x.Link.Get.StartsWith("A:KOHLSMAN SETTING MB EX1:", StringComparison.Ordinal)).ToArray();
Check(barometers.Length == 2, "Exactly two indexed absolute barometer mappings.");
foreach (var index in new[] {1, 2})
{
    var def = barometers.Single(x => x.Link.Get.EndsWith(":" + index, StringComparison.Ordinal)).Link;
    Check(def.Units == "Millibars" && def.Shared, "Legacy profile retains shared pressure values in explicit Millibars.");
    foreach (var pressure in new[] {500.0, 998.0, 1013.25, 1200.0})
    {
        var target = def.ParseSet(pressure, 1001.0, out _, out var values);
        var raw = (uint)Math.Floor(pressure * 16 + .5);
        Check(target == "K:KOHLSMAN_SET" && values.Length == 2, "Actual vanilla parser recognizes the absolute two-argument setter.");
        Check(Convert.ToUInt32(values[0], CultureInfo.InvariantCulture) == raw && Convert.ToUInt32(values[1], CultureInfo.InvariantCulture) == index, "Pressure is first SDK argument and the separate instrument index is second.");
        Check(def.Set(pressure, pressure) == string.Empty, "Aligned values produce no command.");
        def.ParseSet(pressure, pressure, out _, out var aligned);
        Check(aligned.Length == 0, "No-op must not fall back to writing the original SimVar.");
        // Replay the absolute event twice: each application has the same target,
        // independent of local pressure. It cannot reverse like the old setter.
        var first = def.ParseSet(pressure, 900.0, out _, out var repeated);
        Check(first == target && repeated.SequenceEqual(values), "Replayed absolute target is stable across different local values.");
    }
    Check(def.Set(1013.25001, 1013.25) == string.Empty, "Sub-event quantization noise produces no command.");
    foreach (var bad in new[] {double.NaN, double.PositiveInfinity, 499.0, 1201.0})
    {
        Check(def.Set(bad, 1013.25) == string.Empty, "Invalid or out-of-range target is refused.");
        def.ParseSet(bad, 1013.25, out _, out var rejected);
        Check(rejected.Length == 0, "Rejected expression must not fall back to a write.");
    }
}

foreach (var shared in new[] {false, true})
{
    var optional = new Definition(shared, "L:TestOptional, Number", "value === current ? '' : `${value} (>L:TestOptional, Number)`", null);
    optional.ParseSet(2.0, 2.0, out _, out var skipped);
    Check(skipped.Length == 0, "Vanilla master/shared no-op expressions emit no setter values.");
    var target = optional.ParseSet(3.0, 2.0, out var units, out var changed);
    Check(target == "L:TestOptional" && units == "Number" && changed.Length == 1 && Convert.ToInt32(changed[0]) == 3, "Changed generic LVar mapping retains vanilla's setter behavior.");
}

var baseGtn = oldLinks.Where(x => x.File == "modules/PMS50_GTN_650-750.yaml" && x.Link.Get != "A:KOHLSMAN SETTING MB:0").Select(x => x.Link).ToArray();
var scopedGtn = newLinks.Where(x => x.File == "modules/PMS50_GTN_650-750_no_baro.yaml").Select(x => x.Link).ToArray();
Check(baseGtn.Length == scopedGtn.Length, "Scoped GTN module retains every non-barometer mapping.");
for (var i = 0; i < baseGtn.Length; ++i)
    Check(baseGtn[i].Get == scopedGtn[i].Get && baseGtn[i].Units == scopedGtn[i].Units && baseGtn[i].Set(1.0, 0.0) == scopedGtn[i].Set(1.0, 0.0), "GTN mapping order, units and setters are unchanged.");
foreach (var index in new[] {1, 2})
{
    var name = "L:BKSQ_MixtureLeverPosition_" + index;
    var oldDef = oldLinks.Single(x => x.Link.Get == name).Link;
    var newDef = newLinks.Single(x => x.Link.Get == name).Link;
    Check(oldDef.Units == "Percent" && newDef.Set(50.0, 0.0) == oldDef.Set(50.0, 0.0), "Existing vanilla cockpit-mixture route is retained exactly.");
}

bool Trim(string name)=>name is "A:AILERON TRIM PCT" or "A:ELEVATOR TRIM PCT" or "A:RUDDER TRIM PCT";
var oldUnchanged = oldLinks.Select(x => x.Link).Where(x => !x.Get.StartsWith("L:INSTRUMENT_Altimeter_", StringComparison.Ordinal) && x.Get != "A:KOHLSMAN SETTING MB:0" && !x.Get.StartsWith("A:AUTOPILOT ",StringComparison.Ordinal) && !Trim(x.Get)).ToArray();
var newUnchanged = newLinks.Select(x => x.Link).Where(x => !x.Get.StartsWith("A:KOHLSMAN SETTING MB EX1:", StringComparison.Ordinal) && x.Get != "A:RUDDER POSITION" && x.Get != "A:STEER INPUT CONTROL" && !x.Get.StartsWith("A:AUTOPILOT ",StringComparison.Ordinal) && !Trim(x.Get)).ToArray();
Check(newLinks.Count(x => x.Link.Get == "A:RUDDER POSITION") == 1, "Exactly one standard rudder route was added from vanilla surface behavior.");
Check(oldUnchanged.Length == newUnchanged.Length, "Every mapping outside the corrected barometer/AP routes is preserved.");
for (var i = 0; i < oldUnchanged.Length; ++i)
{
    var before = oldUnchanged[i]; var after = newUnchanged[i];
    Check(before.Get == after.Get && before.Units == after.Units && before.Shared == after.Shared && before.Skip == after.Skip,
        "Non-barometer mapping identity, order, units, direction and skip rule remain unchanged.");
    Check(before.Get is "A:BRAKE RIGHT POSITION EX1" or "A:FLAPS HANDLE INDEX" or "A:AILERON TRIM PCT" || before.Set(1.0, 0.0) == after.Set(1.0, 0.0) && before.Set(0.0, 1.0) == after.Set(0.0, 1.0),
        "Non-barometer forward and reverse setter expressions remain unchanged.");
}

var oldFlaps=oldLinks.Single(x=>x.Link.Get=="A:FLAPS HANDLE INDEX").Link;
var flaps=newLinks.Single(x=>x.Link.Get=="A:FLAPS HANDLE INDEX").Link;
Check(oldFlaps.Set(2.0,0.0)==oldFlaps.Set(1.0,0.0),"Original flap setter loses the requested detent difference.");
foreach(var current in new[]{0.0,1.0,2.0})foreach(var target in new[]{0.0,1.0,2.0})
    Check(flaps.Set(target,current)==(target==current?"":new[]{"0 (>K:FLAPS_UP)","0 (>K:FLAPS_1)","0 (>K:FLAPS_DOWN)"}[(int)target]),"Flaps select the absolute detent from any initial position, without equality commands.");
Check(flaps.Set(3.0,0.0)==""&&flaps.Set(-1.0,0.0)==""&&flaps.Set(.5,0.0)=="","Unknown flap detents are refused.");

// Importing all legacy switches blindly would also import non-idempotent AP
// toggles. Keep this evidence visible for the ownership-aware adapter work.
var apMaster = oldLinks.First(x => x.Link.Get == "A:AUTOPILOT MASTER").Link;
Check(apMaster.Set(false, false).Contains("K:AP_MASTER", StringComparison.Ordinal), "Legacy AP-master setter is a toggle even for equal OFF state.");
var duplicateGetters = oldLinks.GroupBy(x => x.Link.Get).Where(x => x.Count() > 1)
    .Select(x => new {name = x.Key, sources = x.Select(y => y.File).ToArray()}).ToArray();
ControlDefinitionChecks.Run(Check);
CockpitReplicationChecks.Run(Check);
SharedAutopilotChecks.Run(Check);
Console.WriteLine(JsonSerializer.Serialize(new {checks, originalDefinitionCount = oldLinks.Length, candidateDefinitionCount = newLinks.Length,
    originalAltimeterEqualityBug = true, overlappingGtnBarometer = true, candidateAbsoluteIndexedSetters = true,
    preservedMixtureDefinitions = true, duplicateGetters, simulatorContacted = false}, new JsonSerializerOptions {WriteIndented = true}));
Console.WriteLine($"PASS {checks} definition checks with vanilla's actual YAML loader and Jint expression engine. No simulator/network access.");
} catch (Exception error) {
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1; // Report failures without an interactive Windows crash dialog.
}
