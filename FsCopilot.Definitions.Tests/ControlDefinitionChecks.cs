using FsCopilot.Simulation;

static class ControlDefinitionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string path = "simobjects/airplanes/bksq-aircraft-baronpropress/aircraft.cfg";
        var profile = ControlDefinitionProfile.Load(path);
        check(profile.Engines == 2 && profile.Axes.Count == 10 && profile.Mask == 0x7e3c, "Fixed rudder/tiller/brake and engine roles exclude normally shared trims.");
        check(profile.Adapter.StartsWith("definitions-v1-", StringComparison.Ordinal) && profile.Adapter.Length < 64, "Versioned profile identity fits native transport.");
        foreach (var axis in profile.Axes)
        {
            var action = profile.Plan(axis.Axis, .5);
            check(action.Target == axis.Write && action.Units == axis.WriteUnits, "Actual vanilla planner matches native descriptor.");
            check(action.Values.Length == 1 && (axis.OutputBreakpoints is not null || Convert.ToDouble(action.Values[0]) == (axis.OutputMinimum + axis.OutputMaximum) / 2), "Normalized position executes the definition's setter.");
        }
        check(Convert.ToDouble(profile.Plan(4, 0).Values[0]) == -16384 && Convert.ToDouble(profile.Plan(4, 1).Values[0]) == 16384, "Vanilla brake offset is compiled from its definition.");
        check(Convert.ToDouble(profile.Plan(2, 0).Values[0]) == 16384 && Convert.ToDouble(profile.Plan(2, 1).Values[0]) == -16384, "Standard rudder sign matches vanilla surface output.");
        foreach(var position in new[]{0.0,.25,.49,.5,.51,.75,1.0}) {
            var action=profile.Plan(3,position);
            var raw=Convert.ToDouble(action.Values[0]);
            check(action.Target=="K:AXIS_STEERING_SET" && Math.Abs(.5-raw/32768-position)<.00002,
                "Tiller setter round-trips the opposite-sign SDK readback, including the observed 51% wizard probe.");
        }
        var source = Definitions.Load(profile.Aircraft).ToArray();
        var cockpit = CockpitDefinitionProfile.Load(profile.Aircraft);
        foreach(var trimName in new[]{"A:AILERON TRIM PCT","A:ELEVATOR TRIM PCT","A:RUDDER TRIM PCT"}) {
            var entry=cockpit.Entries.Single(e=>e.Definition.Get==trimName);
            check(entry.Definition.Shared && !profile.Axes.Any(a=>a.Read==trimName),"Each trim is shared as an ordinary instrument and excluded from physical ownership.");
            check(cockpit.Plan(entry.Index,0,0)=="" && cockpit.Plan(entry.Index,entry.Definition.Units=="Percent scaler 16k"?100:0.1,0).Contains("TRIM_SET",StringComparison.Ordinal),"Trim reuses its definition setter with equality suppression.");
        }
        check(cockpit.Entries.Count > 100 && cockpit.Entries.Count <= 512, "Ordinary cockpit catalog uses the full vanilla definition tree.");
        foreach (var bvarName in new[] {"B:ELECTRICAL_Battery_1", "B:ELECTRICAL_Alternator_1", "B:ELECTRICAL_Avionics_Bus_1", "B:LIGHTING_NAV_1", "B:LIGHTING_STROBE_1", "B:LIGHTING_TAXI_1", "B:LIGHTING_LANDING_1"}) {
            var entry = cockpit.Entries.Single(e => e.Definition.Get == bvarName);
            check(entry.Definition.Shared, "Electrical and lighting input events retain vanilla bidirectional sharing.");
            check(cockpit.Plan(entry.Index, 1, 0) == entry.Definition.Set(1d, 0d) && cockpit.Plan(entry.Index, 0, 1) == entry.Definition.Set(0d, 1d), "Input-event changes reuse the exact existing YAML setter in both directions.");
            check(cockpit.Plan(entry.Index, 1, 1) == "", "Already matching input events never replay a toggle.");
        }
        check(cockpit.Entries.All(e => !e.Definition.Get.StartsWith("H:") && !e.Module.Contains("GTN") && !e.Module.Contains("GNS")), "GTN/GNS interactions and screen presses remain outside ordinary instrument sync.");
        var pressure = cockpit.Entries.Single(e => e.Definition.Get == "A:KOHLSMAN SETTING MB EX1:1").Index;
        check(cockpit.Plan(pressure, 1013.25, 1013.25) == "" && cockpit.Plan(pressure, 1014, 1013.25) == "1 16224 (>K:2:KOHLSMAN_SET)", "Ordinary synchronization reuses the corrected absolute indexed barometer setter and suppresses equal writes.");
        var pressure2 = cockpit.Entries.Single(e => e.Definition.Get == "A:KOHLSMAN SETTING MB EX1:2").Index;
        check(cockpit.Plan(pressure2, 1014, 1013.25) == "2 16224 (>K:2:KOHLSMAN_SET)", "Both independent altimeters retain normal shared definition semantics.");
        foreach (var (position, raw) in new[] {(0d, -16384d), (.01, -15360d), (.08, -8192d), (.27, 0d), (.53, 8192d), (1d, 16384d)})
            check(Math.Abs(Convert.ToDouble(profile.Plan(5, position).Values[0]) - raw) < 1e-8, "Right brake definition inverts documented nonlinear knots and the actual 1% failure case.");
        // Both peers send normalized brake demand; each PC uses its own declared
        // event response. Button users retain the previously tested vanilla route.
        foreach (var demand in new[] { .011895, .014863, .013809, .08, .27, .53, 1 }) {
            var axisRaw = Convert.ToDouble(profile.Plan(4, demand, axisSource: true).Values[0]);
            double[] response = [0, .08, .27, .53, 1];
            var segment = (axisRaw + 16384) / 8192;
            var part = Math.Min(3, (int)segment);
            var observed = response[part] + (segment - part) * (response[part + 1] - response[part]);
            check(Math.Abs(observed - demand) < .00004, "Absolute left-brake output compensates the SDK curve, including all three recorded failures.");
            var buttonRaw = Convert.ToDouble(profile.Plan(4, demand).Values[0]);
            check(Math.Abs((buttonRaw + 16384) / 32768 - demand) < .00004, "Existing button-brake output remains unchanged.");
        }
        var roles = profile.Axes.Select(a => new ControlRoleBinding(a.Role, a.Read, a.Minimum, a.Maximum, a.CockpitReference, a.OutputBreakpoints, a.AxisInputResponse)).ToArray();
        var map = new ControlRoleMap(1, profile.Aircraft, 2, roles);
        check(ControlDefinitionProfile.Create(source.Reverse(), map with {Axes = roles.Reverse().ToArray()}).Adapter == profile.Adapter, "Equivalent definition/role ordering keeps stable identity.");
        check(ControlDefinitionProfile.Create(source, map with {Aircraft = "another-aircraft"}).Aircraft == "another-aircraft", "Compiler contains no Baron-specific aircraft rule.");
        void Refuse(Action action, string reason)
        {
            try { action(); } catch (InvalidDataException) { check(true, reason); return; }
            check(false, reason);
        }
        Refuse(() => profile.Plan(11, double.NaN), "NaN positions are refused.");
        Refuse(() => profile.Plan(11, 1.01), "Out-of-range positions are refused.");
        Refuse(() => profile.Plan(1, .5), "An unmapped role is refused.");
        Refuse(() => ControlDefinitionProfile.Create(source,map with {Axes=[..roles,new ControlRoleBinding("elevatorTrim","A:ELEVATOR TRIM PCT",-16384,16384,true)]}),"Trims cannot be reintroduced as owned physical roles.");
        foreach (var badResponse in new[] { new[] {0d}, new[] {0d, .2, .1, 1}, new[] {0d, double.NaN, 1}, new[] {.1, 1d} })
            Refuse(() => ControlDefinitionProfile.Create(source, map with {Axes = roles.Select(r => r.Axis == "leftBrake" ? r with {AxisInputResponse = badResponse} : r).ToArray()}), "Malformed event-response data is refused.");
        check(ControlDefinitionProfile.Create(source, map with {Axes = roles.Select(r => r with {AxisInputResponse = null}).ToArray()}).Adapter != profile.Adapter, "Event response changes require matching helper/native definition identities.");
        Refuse(() => ControlDefinitionProfile.Create(source, map with {Axes = roles.Select(r => r.Axis == "rightBrake" ? r with {OutputBreakpoints = [0, .1, .27, .53, 1]} : r).ToArray()}), "Curve breakpoints cannot disagree with the actual YAML expression.");
        Refuse(() => ControlDefinitionProfile.Create(source, map with {Axes = roles.Select(r => r.Axis == "rightBrake" ? r with {OutputBreakpoints = null} : r).ToArray()}), "A nonlinear setter cannot silently compile as affine output.");
        Refuse(() => ControlDefinitionProfile.Create(source, map with {Version = 2}), "Unknown control-role versions are refused.");
        Refuse(() => ControlDefinitionProfile.Create(source, map with {Axes = roles[..5]}), "Partial quadrant schemas are refused.");
        Refuse(() => ControlDefinitionProfile.Create(source, map with {Axes = [..roles, roles[0]]}), "Duplicate roles are refused.");
        Refuse(() => ControlDefinitionProfile.Create(source, map with {Axes = roles.Select((r, i) => i == 0 ? r with {Definition = "L:Missing"} : r).ToArray()}), "Missing definition is refused.");
        Refuse(() => ControlDefinitionProfile.Create(source.Concat([source.First(d => d.Get == roles[0].Definition)]), map), "Ambiguous definition is refused.");
        var toggle = source.Select(d => d.Get == roles[0].Definition ? new Definition(false, d.Get + ", " + d.Units, "(>K:AP_MASTER)", null) : d);
        Refuse(() => ControlDefinitionProfile.Create(toggle, map), "Toggle events cannot become physical absolute setters.");
        Refuse(() => ControlDefinitionProfile.Create(source, map with {Axes = roles.Select(r => r.CockpitReference ? r with {CockpitReference = false} : r).ToArray()}), "Custom cockpit reference cannot silently use standard engine observation.");
        Refuse(() => ControlDefinitionProfile.Create(source, map with {Axes = roles.Select((r, i) => i == 0 ? r with {Maximum = 100} : r).ToArray()}), "A same-state test cannot use an inconsistent standard-variable unit range.");
        Refuse(() => ControlDefinitionProfile.Create(source, map with {Axes = roles.Select((r, i) => i == 2 ? r with {Minimum = -100} : r).ToArray()}), "Unsupported signed cockpit domains are refused before output.");
        check(ControlDefinitionProfile.TryAircraftName("C:\\Packages\\SimObjects\\Airplanes\\BKSQ-AIRCRAFT-BARONPROPRESS\\aircraft.CFG", out var name) && name == profile.Aircraft, "Vanilla and normalized native paths select the same profile.");
        check(!ControlDefinitionProfile.TryAircraftName("simobjects/airplanes/../aircraft.cfg", out _), "Profile path traversal is refused.");
        var loads = 0;
        var selection = new ControlDefinitionSelection(_ => { ++loads; return profile; });
        check(selection.Validate("a1", path, profile.Adapter, 2) is null && loads == 1, "Aircraft definitions load before matching shared output.");
        check(selection.Validate("a1", path, profile.Adapter, 2) is null && loads == 1, "Fresh observations reuse the immutable selection.");
        check(selection.Validate("a1", path, "old-adapter", 2) is not null, "Mismatched native definitions cannot join.");
        check(selection.Validate("a1", path, profile.Adapter, 1) is not null, "Mismatched engine count cannot join.");
        check(selection.Validate("a2", path, profile.Adapter, 2) is null && loads == 2, "Native restart reselects the profile.");
        var missing = new ControlDefinitionSelection(_ => throw new FileNotFoundException("missing profile"));
        check(missing.Validate("a1", path, profile.Adapter, 2) is not null && missing.Profile is null, "Missing aircraft definition fails without any output action.");
    }
}
