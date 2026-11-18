using System.Globalization;
using System.Text;
using System.Text.Json;
using FsCopilot.Simulation;

try {
if (args.Length != 1) throw new ArgumentException("Usage: FsCopilot.Definitions.Compiler <native-header-output>");
var profiles = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Definitions", "controls"), "*.json")
    .OrderBy(path => path, StringComparer.Ordinal).Select(path => ControlDefinitionProfile.Load("simobjects/airplanes/" + Path.GetFileNameWithoutExtension(path) + "/aircraft.cfg")).ToArray();
if (profiles.Length is < 1 or > 32) throw new InvalidDataException("Native catalog requires 1..32 selected aircraft profiles.");
static string Q(string text) => JsonSerializer.Serialize(text);
static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
var text = new StringBuilder("// Generated from vanilla YAML and Definitions/controls/*.json. Do not edit.\n#pragma once\n#include <cstddef>\nnamespace fsc_controls {\n");
text.AppendLine("struct definition_axis { unsigned axis; const char* read; const char* read_units; double minimum, maximum; bool cockpit_reference; unsigned output_kind; const char* write; const char* write_units; double output_minimum, output_maximum; const double* output_positions = nullptr; const double* output_values = nullptr; size_t output_count = 0; const double* axis_input_response = nullptr; size_t axis_input_response_count = 0; };");
text.AppendLine("struct cockpit_definition { const char* name; const char* units; bool shared; double tolerance = .000001; const char* label = nullptr; };");
text.AppendLine("struct definition_profile { const char* aircraft; const char* adapter; unsigned engines; const definition_axis* axes; size_t count; const cockpit_definition* cockpit = nullptr; size_t cockpit_count = 0; const char* cockpit_identity = nullptr; };");
var cockpit = profiles.Select(p => CockpitDefinitionProfile.Load(p.Aircraft)).ToArray();
for (var index = 0; index < profiles.Length; ++index)
{
    foreach (var axis in profiles[index].Axes.Where(a => a.OutputBreakpoints is not null)) {
        text.AppendLine($"constexpr double definition_positions_{index}_{axis.Axis}[] = {{{string.Join(", ", axis.OutputBreakpoints!.Select(N))}}};");
        text.AppendLine($"constexpr double definition_values_{index}_{axis.Axis}[] = {{{string.Join(", ", axis.OutputValues!.Select(N))}}};");
    }
    foreach(var axis in profiles[index].Axes.Where(a=>a.AxisInputResponse is not null))
        text.AppendLine($"constexpr double definition_axis_response_{index}_{axis.Axis}[] = {{{string.Join(", ",axis.AxisInputResponse!.Select(N))}}};");
    text.AppendLine($"constexpr definition_axis definition_axes_{index}[] = {{");
    foreach (var axis in profiles[index].Axes)
        text.AppendLine($"    {{{axis.Axis}, {Q(axis.Read)}, {Q(axis.ReadUnits)}, {N(axis.Minimum)}, {N(axis.Maximum)}, {(axis.CockpitReference ? "true" : "false")}, {(int)axis.OutputKind}, {Q(axis.Write)}, {Q(axis.WriteUnits)}, {N(axis.OutputMinimum)}, {N(axis.OutputMaximum)}" +
            (axis.OutputBreakpoints is null ? ", nullptr, nullptr, 0" : $", definition_positions_{index}_{axis.Axis}, definition_values_{index}_{axis.Axis}, {axis.OutputBreakpoints.Length}") +
            (axis.AxisInputResponse is null ? "}," : $", definition_axis_response_{index}_{axis.Axis}, {axis.AxisInputResponse.Length}}},"));
    text.AppendLine("};");
    text.AppendLine($"constexpr cockpit_definition cockpit_definitions_{index}[] = {{");
    foreach (var entry in cockpit[index].Entries)
        text.AppendLine($"    {{{Q(entry.Definition.Get)}, {Q(entry.Definition.Units)}, {(entry.Definition.Shared ? "true" : "false")}, {N(cockpit[index].Tolerances[entry.Index])}, {Q(cockpit[index].Label(entry.Index))}}},");
    text.AppendLine("};");
}
text.AppendLine("constexpr definition_profile definition_profiles[] = {");
for (var index = 0; index < profiles.Length; ++index)
    text.AppendLine($"    {{{Q(profiles[index].Aircraft)}, {Q(profiles[index].Adapter)}, {profiles[index].Engines}, definition_axes_{index}, {profiles[index].Axes.Count}, cockpit_definitions_{index}, {cockpit[index].Entries.Count}, {Q(cockpit[index].Identity)}}},");
text.AppendLine("};\nconstexpr size_t definition_profile_count = sizeof(definition_profiles) / sizeof(definition_profiles[0]);\n}");
var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, text.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
Console.WriteLine($"Generated {profiles.Length} native definition profile(s), {profiles.Sum(p => p.Axes.Count)} control roles: {output}");
} catch (Exception error) {
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1; // Invalid definitions must not open a Windows crash dialog or hold build outputs.
}
