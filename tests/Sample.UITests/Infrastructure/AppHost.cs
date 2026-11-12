using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Sample.UITests;

/// <summary>
/// Builds, installs and launches one MAUI head, and reports the host port its DevFlow agent is
/// reachable on. One subclass per head; <see cref="Create"/> picks it from <see cref="UITestSettings"/>.
/// </summary>
public abstract class AppHost : IAsyncDisposable
{
    public static AppHost Create() => UITestSettings.Platform switch
    {
        UIPlatform.iOS => new IosAppHost(),
        UIPlatform.Android => new AndroidAppHost(),
        UIPlatform.MacOS => new MacOSAppHost(),
        UIPlatform.Linux => new LinuxAppHost(),
        UIPlatform.Windows => new WindowsAppHost(),
        _ => throw new NotSupportedException(UITestSettings.Platform.ToString())
    };

    /// <summary>The project of the head under test, relative to the repo root.</summary>
    public abstract string ProjectPath { get; }

    /// <summary>The Shell the head navigates with — each desktop head carries its own.</summary>
    public string AppShellPath => Path.Combine(
        UITestSettings.RepoRoot,
        Path.GetDirectoryName(ProjectPath)!,
        "AppShell.xaml"
    );

    /// <summary>The platform name the DevFlow broker reports for this head's agent.</summary>
    protected abstract string AgentPlatform { get; }

    /// <summary>
    /// Whether a broker entry's "platform" belongs to this head. Equality with
    /// <see cref="AgentPlatform"/> by default; a head whose agent reports a different name than the one
    /// we launch it by overrides this.
    /// </summary>
    protected virtual bool MatchesAgentPlatform(string? platform)
        => String.Equals(platform, AgentPlatform, StringComparison.OrdinalIgnoreCase);

    /// <summary>App output, where the head can capture it (desktop heads), for post-mortem on a failure.</summary>
    public string LogFile => Path.Combine(UITestSettings.ResultsDirectory, "app.log");

    /// <summary>
    /// Failure lines the app printed to its own output since the last call. Only desktop heads have a
    /// captured stdout; on AppKit that is where ShellHandler reports a page whose native view could not be
    /// built — the page object still exists, so nothing else notices it rendered blank.
    /// </summary>
    public virtual IReadOnlyList<string> TakeOutputErrors() => [];

    public abstract Task BuildAsync();
    public abstract Task LaunchAsync();
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// True when the running app, though alive, should be replaced by a fresh process before the next
    /// test — a head-specific resource it can only exhaust by living long (Android's JNI global refs).
    /// </summary>
    public virtual Task<bool> NeedsFreshProcessAsync(MauiDevFlowDriver driver) => Task.FromResult(false);

    /// <summary>
    /// After a relaunch the broker can still list the old process's agent for a moment; agents that
    /// connected before this are ignored.
    /// </summary>
    public DateTimeOffset? AgentSince { get; set; }

    /// <summary>
    /// The host name the app uses to reach a server a test runs on this machine at <paramref name="port"/>.
    /// Loopback everywhere; a head behind a device boundary maps the port across first.
    /// </summary>
    public virtual Task<string> ExposeHostPortAsync(int port) => Task.FromResult("127.0.0.1");

    /// <summary>Port on this machine where the agent answers; heads behind a device boundary forward it.</summary>
    public virtual async Task<int> ResolvePortAsync(TimeSpan timeout)
    {
        if (UITestSettings.Port is int explicitPort)
            return explicitPort;

        return await DiscoverBrokerPortAsync(timeout) ?? 9223;
    }

    /// <summary>
    /// Asks the DevFlow broker which port this head's agent registered on. `agent wait` gives up if the
    /// broker daemon is slow to bind, so this polls `list` instead, which starts the broker itself.
    /// </summary>
    protected async Task<int?> DiscoverBrokerPortAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await ProcessRunner.Run(MauiCli, "devflow list --json", TimeSpan.FromSeconds(30));
                var start = result.Output.IndexOf('[');
                if (result.ExitCode == 0 && start >= 0)
                {
                    var agent = JsonNode.Parse(result.Output[start..])!.AsArray()
                        .Where(a => MatchesAgentPlatform(a?["platform"]?.GetValue<string>()))
                        .Where(a => AgentSince == null
                            || !DateTimeOffset.TryParse(a?["connectedAt"]?.GetValue<string>(), out var connected)
                            || connected >= AgentSince)
                        .OrderByDescending(a => a?["connectedAt"]?.GetValue<string>())
                        .FirstOrDefault();

                    if (agent?["port"]?.GetValue<int>() is int port)
                        return port;
                }
            }
            catch
            {
                // the broker isn't up yet; keep polling
            }
            await Task.Delay(2000);
        }
        return null;
    }

    protected static string MauiCli => OperatingSystem.IsWindows()
        ? "maui"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "maui");

    protected string ProjectFullPath => Path.Combine(UITestSettings.RepoRoot, ProjectPath);
    protected string ProjectDirectory => Path.GetDirectoryName(ProjectFullPath)!;

    protected Task BuildProject(string tfm, string extra = "")
        => ProcessRunner
            .DotNet(
                $"build {ProcessRunner.Quote(ProjectFullPath)} -f {tfm} -c {UITestSettings.Configuration} {extra}",
                TimeSpan.FromMinutes(30)
            )
            .ContinueWith(t => t.Result.EnsureSuccess($"Building {ProjectPath} ({tfm})"));

    /// <summary>Newest match under the head's bin folder — a stale RID folder must never win.</summary>
    protected string FindOutput(string pattern, bool directory = false)
    {
        var bin = Path.Combine(ProjectDirectory, "bin", UITestSettings.Configuration);
        var matches = directory
            ? Directory.EnumerateDirectories(bin, pattern, SearchOption.AllDirectories).Select(d => (FileSystemInfo)new DirectoryInfo(d))
            : Directory.EnumerateFiles(bin, pattern, SearchOption.AllDirectories).Select(f => (FileSystemInfo)new FileInfo(f));

        return matches.OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
            ?? throw new FileNotFoundException($"No '{pattern}' under {bin} — build the head first or unset SHINY_UITEST_NOBUILD");
    }

    protected static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}


public sealed class IosAppHost : AppHost
{
    const string BundleId = "org.shiny.controlsample";
    string udid = null!;

    public override string ProjectPath => Path.Combine("samples", "Sample", "Sample.csproj");
    protected override string AgentPlatform => "iOS";

    public override Task BuildAsync() => BuildProject("net10.0-ios");

    public override async Task LaunchAsync()
    {
        udid = await ResolveSimulatorAsync();

        // mlaunch (what -t:Run uses) breaks on a space in the dotnet path, and -t:Run never
        // recompiles anyway, so install + launch go through simctl directly.
        var app = FindOutput("Sample.app", directory: true);
        await ProcessRunner.Run("xcrun", $"simctl terminate {udid} {BundleId}");
        (await ProcessRunner.Run("xcrun", $"simctl install {udid} {ProcessRunner.Quote(app)}"))
            .EnsureSuccess("simctl install");

        // A system permission prompt (camera, photos, …) sits over the app and swallows every
        // later interaction, so grant up front. Best effort — older runtimes lack some services.
        foreach (var service in new[] { "camera", "microphone", "photos", "photos-add", "location", "contacts", "calendar", "reminders", "motion", "speech-recognition" })
            await ProcessRunner.Run("xcrun", $"simctl privacy {udid} grant {service} {BundleId}");

        (await ProcessRunner.Run("xcrun", $"simctl launch {udid} {BundleId}"))
            .EnsureSuccess("simctl launch");
    }

    async Task<string> ResolveSimulatorAsync()
    {
        var json = (await ProcessRunner.Run("xcrun", "simctl list devices available --json"))
            .EnsureSuccess("simctl list")
            .Output;

        var devices = JsonNode.Parse(json)!["devices"]!.AsObject()
            .SelectMany(runtime => runtime.Value!.AsArray())
            .Select(d => (
                Udid: d!["udid"]!.GetValue<string>(),
                Name: d["name"]!.GetValue<string>(),
                State: d["state"]!.GetValue<string>()
            ))
            .ToList();

        var device = UITestSettings.Device is string wanted
            ? devices.FirstOrDefault(d => d.Udid == wanted || d.Name == wanted)
            : devices.FirstOrDefault(d => d.State == "Booted" && d.Name.Contains("iPhone"));

        if (device == default)
            device = devices.FirstOrDefault(d => d.Name.Contains("iPhone"));

        if (device == default)
            throw new InvalidOperationException("No iOS simulator available. Set SHINY_UITEST_DEVICE to a simulator UDID or name.");

        if (device.State != "Booted")
        {
            await ProcessRunner.Run("xcrun", $"simctl boot {device.Udid}");
            await ProcessRunner.Run("xcrun", $"simctl bootstatus {device.Udid} -b", TimeSpan.FromMinutes(5));
        }
        return device.Udid;
    }

    public override async ValueTask DisposeAsync()
    {
        if (udid != null && !UITestSettings.Attach)
            await ProcessRunner.Run("xcrun", $"simctl terminate {udid} {BundleId}");
    }
}


public sealed class AndroidAppHost : AppHost
{
    const string PackageId = "org.shiny.controlsample";
    const int DeviceAgentPort = 9223;
    string serial = null!;
    int? forwardedPort;

    public override string ProjectPath => Path.Combine("samples", "Sample", "Sample.csproj");
    protected override string AgentPlatform => "Android";

    static string Adb
    {
        get
        {
            var sdk = Environment.GetEnvironmentVariable("ANDROID_HOME")
                ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
                ?? (OperatingSystem.IsWindows()
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk")
                    : OperatingSystem.IsMacOS()
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Android", "sdk")
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Android", "Sdk"));
            return Path.Combine(sdk, "platform-tools", OperatingSystem.IsWindows() ? "adb.exe" : "adb");
        }
    }

    static string Emulator => Path.Combine(
        Path.GetDirectoryName(Path.GetDirectoryName(Adb)!)!,
        "emulator",
        OperatingSystem.IsWindows() ? "emulator.exe" : "emulator"
    );

    Task<ProcessResult> AdbShell(string command)
        => ProcessRunner.Run(Adb, $"-s {serial} shell {command}");

    // adb reverse rather than the emulator's 10.0.2.2: the app's request then carries a 127.0.0.1 Host
    // header, which a loopback-bound HttpListener accepts (it answers any other host with an HTML 400)
    public override async Task<string> ExposeHostPortAsync(int port)
    {
        (await ProcessRunner.Run(Adb, $"-s {serial} reverse tcp:{port} tcp:{port}")).EnsureSuccess("adb reverse");
        return "127.0.0.1";
    }

    double? pixelsPerDp;

    /// <summary>
    /// A real finger tap at a point in window dp, through the OS input pipeline. DevFlow's tap invokes
    /// the recognizer or the control's action directly, so it can't see a view that swallows the touch
    /// before the control under it gets it - this can.
    /// </summary>
    public async Task TouchTap(double x, double y)
    {
        var scale = await PixelsPerDp();
        (await AdbShell($"input tap {(int)(x * scale)} {(int)(y * scale)}")).EnsureSuccess("adb input tap");
    }

    /// <summary>A real finger drag between two points in window dp, taking <paramref name="durationMs"/>.</summary>
    public async Task TouchSwipe(double x1, double y1, double x2, double y2, int durationMs)
    {
        var scale = await PixelsPerDp();
        (await AdbShell($"input swipe {(int)(x1 * scale)} {(int)(y1 * scale)} {(int)(x2 * scale)} {(int)(y2 * scale)} {durationMs}"))
            .EnsureSuccess("adb input swipe");
    }

    async Task<double> PixelsPerDp()
    {
        if (pixelsPerDp is { } known)
            return known;

        // "Physical density: 420", plus "Override density: 480" when one is set - the override wins.
        var output = (await AdbShell("wm density")).Output;
        var dpi = output.Split('\n')
            .Select(line => line.Split(':'))
            .Where(parts => parts.Length == 2 && Int32.TryParse(parts[1].Trim(), out _))
            .Select(parts => Int32.Parse(parts[1].Trim()))
            .Last();

        pixelsPerDp = dpi / 160d;
        return pixelsPerDp.Value;
    }

    /// <summary>
    /// The native view hierarchy as uiautomator sees it, bounds in screen pixels. For views DevFlow can't
    /// see - a handler's RecyclerView items are created with ToPlatform outside the MAUI tree.
    /// </summary>
    public async Task<System.Xml.Linq.XElement> NativeHierarchy()
    {
        (await AdbShell("uiautomator dump /sdcard/shiny-uitest.xml")).EnsureSuccess("uiautomator dump");
        var xml = (await AdbShell("cat /sdcard/shiny-uitest.xml")).EnsureSuccess("reading the uiautomator dump").Output;
        return System.Xml.Linq.XElement.Parse(xml[xml.IndexOf('<')..]);
    }

    /// <summary>Screen pixels per dp, for comparing native bounds with MAUI sizes.</summary>
    public Task<double> Density() => PixelsPerDp();

    public override async Task BuildAsync()
    {
        serial = await ResolveDeviceAsync();
        var install = $"-t:Install -p:AdbTarget={ProcessRunner.Quote("-s " + serial)}";

        // Optional isolated output, so an Android build never shares src/*/obj with another head building at once.
        if (ArtifactsPath is string artifacts)
            install += $" --artifacts-path {ProcessRunner.Quote(artifacts)}";
        await BuildProject("net10.0-android", install);

        // A clean build once shipped an app that crashed on every page with a core Java peer: the sample
        // got core's net10.0 assembly through Shiny.Maui.Controls.Desktop (then without an Android TFM),
        // the Java stub scanner read that copy ("Found 0 Java types"), and CarouselAdapter,
        // StaggeredGridAdapter, BlurPreDrawListener ... died with "no Java peer type found". Desktop now
        // has android/ios assets, so this should never happen; if it does, fail here, loudly, rather than
        // with a crash on whichever page happens to come first.
        var emptyScans = EmptyCoreJavaScans();
        if (emptyScans.Count > 0)
            throw new InvalidOperationException(
                "The Android build scanned Shiny.Maui.Controls as having no Java types, so every Android adapter/listener " +
                "in it would crash with \"no Java peer type found\". Something is handing the Android head core's net10.0 " +
                "assembly again (a reference without an Android TFM). Empty scans:\n" + String.Join("\n", emptyScans)
            );
    }

    /// <summary>Per-ABI Java stub scans of the core package that came out empty (see <see cref="BuildAsync"/>).</summary>
    List<string> EmptyCoreJavaScans()
    {
        var root = ArtifactsPath is string artifacts
            ? Path.Combine(artifacts, "obj", "Sample")
            : Path.Combine(ProjectDirectory, "obj", UITestSettings.Configuration, "net10.0-android");
        if (!Directory.Exists(root))
            return [];

        return Directory
            .EnumerateFiles(root, "Shiny.Maui.Controls.jlo.xml", SearchOption.AllDirectories)
            .Where(f => f.Contains("android", StringComparison.OrdinalIgnoreCase))
            .Where(f => new FileInfo(f).Length < 64 && File.ReadAllText(f).Contains("<api />"))
            .ToList();
    }

    public override async Task LaunchAsync()
    {
        serial ??= await ResolveDeviceAsync();

        // -t:Install leaves debug.mono.extra pointing at a debugger port, and the app then aborts at
        // launch with "Error accepting stdout and stderr". Clear it every time.
        await AdbShell("setprop debug.mono.extra \"''\"");
        await AdbShell($"am force-stop {PackageId}");
        // monkey refuses to run on a freshly booted emulator ("SYS_KEYS has no physical keys"), so
        // resolve the launcher activity (its crc64 name changes per build) and start it directly.
        var activity = (await AdbShell($"cmd package resolve-activity --brief -c android.intent.category.LAUNCHER {PackageId}"))
            .EnsureSuccess("Resolving the Android sample's launcher activity")
            .Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.StartsWith(PackageId + "/"))
            ?? throw new InvalidOperationException($"{PackageId} has no launcher activity — is it installed?");

        (await AdbShell($"am start -W -n {activity}"))
            .EnsureSuccess("Launching the Android sample");
    }

    /// <summary>
    /// Shell keeps every page it has shown alive, and on Android each one pins its native views as JNI
    /// global references. Past ~90% of the runtime's GREF limit (46,080 of 51,200 on the emulator) monodroid
    /// forces a full GC on every new reference — "N outstanding GREFs. Performing a full GC!" several times a
    /// second — and the main thread stalls for 30-95s at a time: page types never appear in the tree, the
    /// screenshot fails, and Shell reports route ''. A bare route sweep reaches it after ~70 pages; the
    /// per-control tests, which also add content (chat messages, grid edits, regrouping), reached it after ~20
    /// routes (A* through Docking, 7 min). So the app is restarted after <see cref="RoutesPerLaunch"/> distinct
    /// pages, or as soon as monodroid reports the GREF pressure anyway. (A cheap live GREF count isn't
    /// available: it needs debug.mono.log=gref, which slows the app several-fold, and Mono owns SIGQUIT.)
    /// </summary>
    public override async Task<bool> NeedsFreshProcessAsync(MauiDevFlowDriver driver)
    {
        if (driver.RoutesSinceConnect.Count >= RoutesPerLaunch)
            return true;

        var pid = (await AdbShell($"pidof {PackageId}")).Output.Trim();
        if (pid.Length == 0)
            return false;

        // -s keeps it to the one tag, so this stays cheap even with a full log buffer
        var gc = await ProcessRunner.Run(Adb, $"-s {serial} logcat -d -s monodroid-gc:W");
        return gc.Output
            .Split('\n')
            .Any(line => line.Contains("outstanding GREFs", StringComparison.Ordinal) && IsPidColumn(line, pid));
    }

    /// <summary>The default logcat format puts the pid in its own column ("10-13 22:50:05.125 12490 12490 W …").</summary>
    static bool IsPidColumn(string line, string pid)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 2 && parts[2] == pid;
    }

    /// <summary>SHINY_UITEST_ANDROID_ARTIFACTS: build the Android head with this --artifacts-path (absolute or repo-relative).</summary>
    static string? ArtifactsPath
        => Environment.GetEnvironmentVariable("SHINY_UITEST_ANDROID_ARTIFACTS") is { Length: > 0 } path
            ? Path.GetFullPath(path, UITestSettings.RepoRoot)
            : null;

    static int RoutesPerLaunch
        => Int32.TryParse(Environment.GetEnvironmentVariable("SHINY_UITEST_ANDROID_ROUTES_PER_LAUNCH"), out var n) && n > 0 ? n : 25;

    public override async Task<int> ResolvePortAsync(TimeSpan timeout)
    {
        serial ??= await ResolveDeviceAsync();

        // Host 9223 is often taken by an iOS or desktop agent, so the device port goes out on a free one.
        var hostPort = UITestSettings.Port ?? forwardedPort ?? FreePort();
        (await ProcessRunner.Run(Adb, $"-s {serial} forward tcp:{hostPort} tcp:{DeviceAgentPort}"))
            .EnsureSuccess("adb forward");
        forwardedPort = hostPort;
        return hostPort;
    }

    async Task<string> ResolveDeviceAsync()
    {
        if (UITestSettings.Device is string wanted)
            return wanted;

        var attached = await ListDevicesAsync();
        if (attached.Count > 0)
            return attached[0];

        // A snapshot boot can restore another DevFlow app squatting on the agent port, so cold boot.
        ProcessRunner.StartLogged(
            Emulator,
            $"-avd {UITestSettings.Avd} -no-snapshot -no-audio",
            Path.Combine(UITestSettings.ResultsDirectory, "emulator.log")
        );

        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < deadline)
        {
            attached = await ListDevicesAsync();
            if (attached.Count > 0)
            {
                var booted = await ProcessRunner.Run(Adb, $"-s {attached[0]} shell getprop sys.boot_completed");
                if (booted.Output.Trim() == "1")
                    return attached[0];
            }
            await Task.Delay(3000);
        }
        throw new TimeoutException($"Android emulator '{UITestSettings.Avd}' did not boot within 5 minutes");
    }

    static async Task<List<string>> ListDevicesAsync()
    {
        var output = (await ProcessRunner.Run(Adb, "devices")).Output;
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .Select(line => line.Split('\t'))
            .Where(parts => parts.Length == 2 && parts[1] == "device")
            .Select(parts => parts[0])
            .ToList();
    }

    public override async ValueTask DisposeAsync()
    {
        if (serial == null)
            return;

        if (forwardedPort is int port)
            await ProcessRunner.Run(Adb, $"-s {serial} forward --remove tcp:{port}");

        if (!UITestSettings.Attach)
            await AdbShell($"am force-stop {PackageId}");
    }
}


/// <summary>A head that runs as a plain process on this machine: its output can be captured, and killed.</summary>
public abstract class DesktopAppHost : AppHost
{
    Process? process;

    protected abstract string ExecutablePath();
    protected virtual string ExecutableArguments => "";
    protected virtual IDictionary<string, string>? Environment => null;
    protected abstract string ProcessName { get; }

    public override async Task LaunchAsync()
    {
        foreach (var stale in Process.GetProcessesByName(ProcessName))
        {
            try { stale.Kill(entireProcessTree: true); await stale.WaitForExitAsync(); } catch { }
        }

        process = ProcessRunner.StartLogged(ExecutablePath(), ExecutableArguments, LogFile, Environment);
    }

    public bool HasExited => process?.HasExited ?? false;

    /// <summary>The app's process: the one launched here, else (attached) the running one by name.</summary>
    protected int? ProcessId => process?.Id ?? Process.GetProcessesByName(ProcessName).FirstOrDefault()?.Id;

    /// <summary>Kills the app the watchdog found wedged, so pending driver calls fail now rather than time out.</summary>
    public void Kill()
    {
        try { process?.Kill(entireProcessTree: true); } catch { }
    }

    long outputOffset;

    static readonly string[] OutputFailureMarkers =
    [
        "Failed to create page view",
        "Unhandled exception",
        "Unable to find a IElementHandler",
        "NotImplementedException"
    ];

    public override IReadOnlyList<string> TakeOutputErrors()
    {
        if (!File.Exists(LogFile))
            return [];

        try
        {
            using var stream = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (outputOffset > stream.Length)
                outputOffset = 0; // relaunched: new log

            stream.Seek(outputOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            outputOffset = stream.Length;

            return text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => OutputFailureMarkers.Any(m => line.Contains(m, StringComparison.Ordinal)))
                .Distinct()
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (process is { HasExited: false })
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch { }
        }
        process?.Dispose();
    }
}


public sealed class MacOSAppHost : DesktopAppHost
{
    public override string ProjectPath => Path.Combine("samples", "Sample.MacOS", "Sample.MacOS.csproj");
    protected override string AgentPlatform => "macOS";
    protected override string ProcessName => "Sample.MacOS";

    public override Task BuildAsync() => BuildProject("net10.0-macos");

    // Running the bundle's binary directly (rather than `open`) keeps stdout — ShellHandler prints
    // navigation failures there, and on AppKit those are otherwise invisible.
    protected override string ExecutablePath()
        => Path.Combine(FindOutput("Sample.MacOS.app", directory: true), "Contents", "MacOS", "Sample.MacOS");

    /// <summary>
    /// A real mouse click at a point in window points (an element's DevFlow WindowBounds), through the
    /// window server - see <see cref="MacMouse"/>. <paramref name="contentHeight"/> is the height DevFlow
    /// measures the window's content at, which puts the title bar (if any) above it.
    /// </summary>
    public async Task Click(double x, double y, double contentHeight)
    {
        var pid = ProcessId ?? throw new InvalidOperationException("Sample.MacOS is not running.");

        // A click on a window in the background only brings it forward; the control never sees it.
        // `open` on a running app's bundle activates it rather than starting another.
        (await ProcessRunner.Run("open", [FindOutput("Sample.MacOS.app", directory: true)])).EnsureSuccess("open Sample.MacOS");
        await Task.Delay(500);

        var frame = MacMouse.MainWindowFrame(pid) ?? throw new InvalidOperationException("Sample.MacOS has no window on screen.");
        var titleBar = frame.Height - contentHeight;
        await MacMouse.Click(frame.X + x, frame.Y + titleBar + y);
    }

    /// <summary>Whether the app has a menu (a picker's pop-up, a context menu) open on screen.</summary>
    public bool HasOpenMenu()
        => MacMouse.HasOpenMenu(ProcessId ?? throw new InvalidOperationException("Sample.MacOS is not running."));
}


public sealed class LinuxAppHost : DesktopAppHost
{
    public override string ProjectPath => Path.Combine("samples", "Sample.Linux", "Sample.Linux.csproj");
    protected override string AgentPlatform => "Linux";
    protected override string ProcessName => "Sample.Linux";

    public override Task BuildAsync() => BuildProject("net10.0");

    protected override string ExecutablePath()
        => Path.Combine(Path.GetDirectoryName(FindOutput("Sample.Linux.dll"))!, "Sample.Linux");

    /// <summary>
    /// The GTK4 head can also run on macOS against Homebrew's gtk4 (brew install gtk4). The backend
    /// P/Invokes Linux sonames (libgtk-4.so.1, libfontconfig.so.1), so a scratch folder of
    /// libX.so.N → libX.N.dylib links goes on the dyld fallback path. The apphost is launched directly:
    /// SIP strips DYLD_* from anything started through /bin/zsh.
    /// </summary>
    protected override IDictionary<string, string>? Environment
    {
        get
        {
            if (!OperatingSystem.IsMacOS())
                return null;

            var shims = Path.Combine(UITestSettings.ResultsDirectory, "gtk-dylib-shims");
            Directory.CreateDirectory(shims);
            foreach (var dylib in Directory.EnumerateFiles(HomebrewLib, "lib*.dylib"))
            {
                // libgtk-4.1.dylib → libgtk-4.so.1 and libgtk-4.so
                var name = Path.GetFileNameWithoutExtension(dylib);
                var dot = name.IndexOf('.');
                var stem = dot < 0 ? name : name[..dot];
                var major = dot < 0 ? null : name[(dot + 1)..].Split('.')[0];

                CreateLink(Path.Combine(shims, stem + ".so"), dylib);
                if (!String.IsNullOrEmpty(major) && major.All(Char.IsDigit))
                    CreateLink(Path.Combine(shims, $"{stem}.so.{major}"), dylib);
            }
            return new Dictionary<string, string>
            {
                ["DYLD_FALLBACK_LIBRARY_PATH"] = $"{shims}:{HomebrewLib}"
            };
        }
    }

    public const string HomebrewLib = "/opt/homebrew/lib";

    public static bool HomebrewGtkInstalled => File.Exists(Path.Combine(HomebrewLib, "libgtk-4.1.dylib"));

    static void CreateLink(string link, string target)
    {
        if (File.Exists(link) || Directory.Exists(link))
            return;

        try { File.CreateSymbolicLink(link, target); } catch (IOException) { }
    }
}


public sealed class WindowsAppHost : DesktopAppHost
{
    const string Tfm = "net10.0-windows10.0.19041.0";

    public override string ProjectPath => Path.Combine("samples", "Sample", "Sample.csproj");
    protected override string AgentPlatform => "WinUI";
    protected override string ProcessName => "Sample";

    /// <summary>
    /// The agent calls this head "WinUI"; the suite calls it "windows". Getting this wrong is silent and
    /// fatal: no broker entry ever matches, ResolvePortAsync times out and falls back to 9223 - which the
    /// broker itself answers on - so the driver connects to the broker, finds no HomePage, and every test
    /// in the session fails in InitializeAsync. "Windows" is accepted too in case the agent is renamed.
    /// </summary>
    protected override bool MatchesAgentPlatform(string? platform)
        => String.Equals(platform, "WinUI", StringComparison.OrdinalIgnoreCase)
            || String.Equals(platform, "Windows", StringComparison.OrdinalIgnoreCase);

    // The sample is unpackaged (WindowsPackageType=None), so the exe runs straight from bin.
    public override Task BuildAsync() => BuildProject(Tfm);

    protected override string ExecutablePath()
        => FindOutput("Sample.exe");
}
