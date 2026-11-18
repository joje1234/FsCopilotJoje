using System.Globalization;
using System.Reflection;
using FsCopilot.Network;
using FsCopilot.Simulation;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FsCopilot", "Efb", "Logs");
Directory.CreateDirectory(logDirectory);
var logPath = Path.Combine(logDirectory, $"helper-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Console()
    .WriteTo.File(logPath, shared: true, outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}").CreateLogger();
try {
    File.WriteAllText(Path.Combine(logDirectory, "latest-log.txt"), logPath + Environment.NewLine);
    Log.Information("[EFB] Helper {Version}; PID {Pid}; executable {Executable}; log {Log}",
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        Environment.ProcessId, Environment.ProcessPath, logPath);
    Log.Information("[EFB] Working directory {Directory}; application directory {Application}", Environment.CurrentDirectory, AppContext.BaseDirectory);
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The EFB helper requires Windows.");
    var options = args.Contains("--efb-backend", StringComparer.Ordinal) ? args : new[] { "--efb-backend" }.Concat(args).ToArray();
    if (EfbCompanionOptions.Parse(options) is { } companion) await SharedBackendRunner.RunFromEfb(companion);
    else if (SharedBackendOptions.Parse(options) is { } diagnostic) await SharedBackendRunner.Run(diagnostic);
    else throw new ArgumentException("Invalid EFB helper options.");
} catch (Exception error) { Log.Error(error, "[EFB] Helper stopped"); Environment.ExitCode = 1; }
finally { Log.Information("[EFB] Helper exiting with code {Code}", Environment.ExitCode); Log.CloseAndFlush(); }
