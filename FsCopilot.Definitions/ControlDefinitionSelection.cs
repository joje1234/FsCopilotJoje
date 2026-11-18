namespace FsCopilot.Simulation;

/// <summary>Loads an immutable local definition selection for one native aircraft context.</summary>
public sealed class ControlDefinitionSelection(Func<string, ControlDefinitionProfile>? load = null)
{
    private readonly Func<string, ControlDefinitionProfile> _load = load ?? ControlDefinitionProfile.Load;
    private string _key = "", _error = "";
    public ControlDefinitionProfile? Profile { get; private set; }
    public string? Validate(string native, string aircraft, string adapter, uint engines)
    {
        var key = native + "|" + aircraft;
        if (_key != key)
        {
            _key = key; Profile = null; _error = "";
            try { Profile = _load(aircraft); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { _error = "Aircraft control definitions are unavailable: " + error.Message; }
        }
        if (Profile is null) return _error.Length == 0 ? "Aircraft control definitions are unavailable." : _error;
        if (Profile.Engines != engines) return "The aircraft engine count does not match its control definitions.";
        if (Profile.Adapter != adapter) return "The EFB module and aircraft control definitions do not match. Build/install the matching package.";
        return null;
    }
}
