# FS Copilot EFB control test

A standalone MSFS 2024 Community package for the first local test: assign
**Steering** or **Engine quadrant** to **Me** or a **Mock copilot**, and check
whether your physical controls can still operate the aircraft. The initial
aircraft for validation is the Black Square Baron 58P.

The package runs entirely inside MSFS. It does not require the FS Copilot desktop
app, another player, a supported-aircraft YAML profile, or a network connection.
The existing shared-cockpit application and bridge are unchanged.

## Build on Windows

Prerequisites:

- Visual Studio C++ build tools and the **MSFS2024** platform toolset installed
  by the Microsoft Flight Simulator 2024 SDK.
- Node.js 16 or later. The EFB scripts use no npm packages.

From the repository root, run in PowerShell:

```powershell
.\FsCopilot.Efb\build.ps1
```

If MSBuild is not discovered automatically:

```powershell
.\FsCopilot.Efb\build.ps1 -MSBuildPath "C:\path\to\MSBuild.exe"
```

The script generates the EFB JavaScript, rebuilds the native WASM module, checks
its exports, and creates:

```text
artifacts/efb-control-test/fscopilot-controls-test/
artifacts/efb-control-test/fscopilot-controls-test.zip
```

Copy the **fscopilot-controls-test** folder into your MSFS 2024 Community folder,
then restart the simulator. Its `manifest.json` and `layout.json` must be directly
inside that folder. Open the EFB and select **FS Copilot Controls**.

Alternatively, after running the build script, open `EfbControlTest.xml` through
MSFS Developer Mode and use **Build All In Project** before loading a flight.
Use either the mounted DevMode package or the Community copy, so the test module
is loaded once.

## First Baron 58P test

1. Load the Baron on the ground, solo, with autopilot off. Leave the FS Copilot
   desktop app closed for this initial test.
2. Open **FS Copilot Controls**. Wait for **Simulator connected**. Both groups
   initially belong to **Me**.
3. Move the yoke, pedals, trim, and each quadrant lever. Confirm the aircraft
   responds normally. **Events seen** and the last event/value help identify
   which commands are reaching the test module.
4. Set **Engine quadrant → Mock copilot**. Wait for confirmation. Move both
   throttle, propeller and mixture levers, including any hardware buttons that
   command them. They should no longer operate the corresponding aircraft
   controls; steering should still work. Check the **Blocked** counter.
5. Set **Engine quadrant → Me**. It should respond again. This first prototype
   reconnects immediately, so position the hardware before doing this.
6. Repeat with **Steering → Mock copilot**. Check pitch, roll, rudder/nosewheel,
   left/right brakes, and trim, while the engine quadrant remains enabled.
7. Assign both groups to the mock copilot, close/reopen the EFB, and confirm the
   assignments remain. **Restore both to Me** should restore both groups.
8. Loading a different aircraft resets ownership to **Me**. Unloading the module
   removes its input masks.

For any failure, record the aircraft/version, which control moved, its MSFS
binding name, and the EFB's last event and counters. If the aircraft moves but
**Events seen** does not change, that input probably bypasses these event paths.

## Scope of the prototype

The native module subscribes to standard simulator control events and masks them
before the simulator's default-priority handler when **Mock copilot** is selected.
It does not repeatedly write or freeze aircraft state. All throttle, propeller
and mixture commands form one quadrant group; steering, brakes and trim form
the other. General and per-engine events for engines 1–4 are included.

This is **event masking**, not source-aware hardware filtering. Keyboard and
cockpit actions that send the same events are also masked. Direct writes to
SimVars/LVars, aircraft-specific InputEvents, vendor utilities, and higher-priority
aircraft handlers may bypass the mask. Compatibility with the Black Square Baron
58P must be established by the in-simulator test above; the event list alone is
not proof of successful suppression. Parking brake, flaps, gear and unrelated
switches are outside these two groups in this prototype.

The mock copilot does not fly or hold the aircraft. This build does not implement
physical-axis alignment/pickup, multiplayer handover, resync, or a binding editor.
Those remain later stages after this input-suppression experiment succeeds.

The EFB shows pending changes until the simulator replies to a configuration
barrier. Missing/old bridge replies disable commands. A configuration exception
or confirmation timeout closes the module's dedicated SimConnect connection,
which releases its masks, and reports an error. Reload the package to retry.
Closing the EFB alone does not release masks.

## Local verification and preview

Build the JavaScript and run the dependency-free protocol/controller tests:

```text
node FsCopilot.Efb/scripts/generate-events.mjs
node FsCopilot.Efb/scripts/build-ui.mjs
node FsCopilot.Efb/tests/controller.test.cjs
node FsCopilot.Efb/tests/registration.test.cjs
node FsCopilot.Efb/tests/package.test.mjs
```

`preview.html` displays the same UI in a browser. It deliberately has no fake
simulator: controls stay disabled, and a banner labels it as a browser preview.

The native tests compile the actual module against small simulator contract
stubs. With a normal C++14 compiler:

```text
c++ -std=c++14 -Wall -Wextra -Werror -I FsCopilot.Efb/tests/stubs FsCopilot.Efb/tests/native_test.cpp -o native-test
./native-test
```

The optional DOM test uses the test-only `linkedom` package. Set `FSC_DOM_MODULE`
to its installed path and run `node FsCopilot.Efb/tests/dom.test.cjs`.

These tests cover command validation, independent groups, confirmation ordering,
late messages, restore, aircraft changes, failures, and the rendered controls.
They cannot validate the simulator's masking implementation or aircraft behavior.

## SDK references

- [EFB apps and packaging](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/efb/electronic-flight-bag-api/)
- [Notification group masking](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/events-and-data/simconnect_addclienteventtonotificationgroup/)
- [SimConnect priorities](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/SimConnect_API_Reference.htm)
- [CommBus](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/javascript/communication-api/)
- [Standard control event names](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/key-events-index/)

The small EFB registration adapter uses the runtime globals and interface shown
in Rafael Coelho's MIT-licensed minimal EFB example; see `THIRD_PARTY_NOTICES.txt`.
