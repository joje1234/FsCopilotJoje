using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace FsCopilot.Efb;

// The OS releases the exclusive handle on exit/crash. Unlike an owned Mutex,
// this lease can safely span an async Main and be disposed on another thread.
internal sealed class HelperInstance : IDisposable
{
    internal sealed record Peer(int Id, string Path, Version? Version, Func<bool> Stop);
    private FileStream? _lease;
    private HelperInstance(FileStream lease) => _lease = lease;
    public void Dispose() => Interlocked.Exchange(ref _lease, null)?.Dispose();

    internal static HelperInstance? Acquire(string directory, Version version, Func<IEnumerable<Peer>> inspect, Action<string> log)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "helper.lock");
        FileStream? Open() {
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { return null; }
        }
        bool Retire(Peer peer) {
            log($"Replacing older EFB helper PID {peer.Id}, version {peer.Version}: {peer.Path}");
            if (peer.Stop()) return true;
            log("The older helper could not be stopped. No second helper will be started; close the older process manually.");
            return false;
        }
        var lease = Open();
        if (lease is null) {
            // A previous version may own the lease. Stop only positively
            // identified older EFB helpers, never a same/newer or unknown app.
            foreach (var peer in inspect().Where(p => p.Version is not null && p.Version < version))
                if (!Retire(peer)) return null;
            lease = Open();
            if (lease is null) { log("An EFB helper already owns the startup lease. This duplicate launch is exiting."); return null; }
        }
        try {
            foreach (var peer in inspect()) {
                // Concurrent same-version launches lose this lease and exit;
                // do not turn a simultaneous startup into two processes waiting.
                if (peer.Version == version) continue;
                if (peer.Version is null || peer.Version > version) {
                    log($"Another EFB helper is already running (PID {peer.Id}, version {peer.Version}). This launch is exiting.");
                    lease.Dispose(); return null;
                }
                if (!Retire(peer)) { lease.Dispose(); return null; }
            }
            log("Exclusive EFB helper startup lease acquired.");
            return new(lease);
        } catch { lease.Dispose(); throw; }
    }

    [SupportedOSPlatform("windows")]
    internal static IEnumerable<Peer> Inspect(int self, int session)
    {
        foreach (var process in Process.GetProcessesByName("FsCopilot.Efb")) {
            using (process) {
                if (process.Id == self) continue;
                Peer? peer;
                try {
                    if (process.HasExited || process.SessionId != session) continue;
                    var path = process.MainModule?.FileName ?? "";
                    var info = path.Length == 0 ? null : FileVersionInfo.GetVersionInfo(path);
                    var version = string.IsNullOrEmpty(info?.FileVersion) ? null :
                        new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
                    var id = process.Id; var started = process.StartTime;
                    peer = new(id, path, version, () => Stop(id, started, path, session));
                } catch (InvalidOperationException) { continue; }
                catch (Win32Exception) { peer = new(process.Id, "unavailable", null, () => false); }
                yield return peer;
            }
        }
    }
    [SupportedOSPlatform("windows")]
    private static bool Stop(int id, DateTime started, string path, int session)
    {
        try {
            using var process = Process.GetProcessById(id);
            if (process.HasExited) return true;
            // Recheck identity at the point of termination to fence PID reuse.
            if (!string.Equals(process.ProcessName, "FsCopilot.Efb", StringComparison.OrdinalIgnoreCase) || process.SessionId != session || process.StartTime != started ||
                !string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase)) return false;
            process.Kill(entireProcessTree: false);
            return process.WaitForExit(2000);
        } catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (Win32Exception) { return false; }
    }
}
