using System.Globalization;
using System.Reflection;
using FsCopilot.Network;
using FsCopilot.Simulation;
using FsCopilot.Efb;
using System.Diagnostics;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FsCopilot", "Efb", "Logs");
Directory.CreateDirectory(logDirectory);
var logPath = Path.Combine(logDirectory, $"helper-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Console()
    .WriteTo.File(logPath, shared: true, outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}").CreateLogger();
try {
    Log.Information("[EFB] Helper {Version}; PID {Pid}; executable {Executable}; log {Log}",
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        Environment.ProcessId, Environment.ProcessPath, logPath);
    Log.Information("[EFB] Working directory {Directory}; application directory {Application}", Environment.CurrentDirectory, AppContext.BaseDirectory);
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The EFB helper requires Windows.");
    var options = args.Contains("--efb-backend", StringComparer.Ordinal) ? args : new[] { "--efb-backend" }.Concat(args).ToArray();
    var companion = EfbCompanionOptions.Parse(options);
    var diagnostic = companion is null ? SharedBackendOptions.Parse(options) : null;
    if (companion is null && diagnostic is null) throw new ArgumentException("Invalid EFB helper options.");
    var dry = companion?.DryRun ?? diagnostic!.DryRun;
    using var process = Process.GetCurrentProcess();
    using var instance = dry ? null : HelperInstance.Acquire(Path.GetDirectoryName(logDirectory)!,
        Assembly.GetExecutingAssembly().GetName().Version!, () => OperatingSystem.IsWindows()
            ? HelperInstance.Inspect(Environment.ProcessId, process.SessionId) : throw new PlatformNotSupportedException(),
        message => Log.Information("[EFB startup] {Message}", message));
    if (!dry && instance is null) return;
    if (!dry) File.WriteAllText(Path.Combine(logDirectory, "latest-log.txt"), logPath + Environment.NewLine);
    if (companion is not null) await SharedBackendRunner.RunFromEfb(companion);
    else await SharedBackendRunner.Run(diagnostic!);
} catch (Exception error) { Log.Error(error, "[EFB] Helper stopped"); Environment.ExitCode = 1; }
finally { Log.Information("[EFB] Helper exiting with code {Code}", Environment.ExitCode); Log.CloseAndFlush(); }
