// https://github.com/microsoft/WSL/blob/70cf77fa39b1f5bd0b74a5772d24512d668c2878/doc/samples/WSLC-NextCloud/Program.cs


using System.Runtime.Loader;
using System.Text;
using Microsoft.WSL.Containers;

var missing = WslcService.GetMissingComponents();
if (missing.Any())
{
    Console.WriteLine($"WSL components are missing ({string.Join(", ", missing)}). Run: wsl --install or wsl --update --pre-release");
    return;
}

// Storage lives beside the executable; no hard-coded absolute paths.
// The session storage directory must be empty for the session to be created (the SDK creates and reuses its own VHD inside it).
var baseDir = AppContext.BaseDirectory;
var sessionPath = Path.Combine(baseDir, "WireMockStorage");

var sessionSettings = new SessionSettings("WireMock_Net", sessionPath)
{
    CpuCount = 1,
    MemorySizeInMB = 1024
};

using var session = new Session(sessionSettings);

try
{
    session.Start();
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to start WSL session: {ex.Message}");
    return;
}

var pullWireMock = session.PullImageAsync(new PullImageOptions("sheyenrath/wiremock.net-alpine"));
pullWireMock.Progress = (op, progress) => Console.WriteLine($"Pull: {progress.Status} {progress.CurrentBytes}/{progress.TotalBytes}");
await pullWireMock;

var initProcessWireMock = new ProcessSettings
{
    OutputMode = ProcessOutputMode.Event,
};

var containerSettingsWireMock = new ContainerSettings("sheyenrath/wiremock.net-alpine")
{
    Name = "WireMock.Net-container",
    EnableAutoRemove = true,
    InitProcess = initProcessWireMock,

    NetworkingMode = ContainerNetworkingMode.Bridged,
    PortMappings = [new(9091, 80, PortProtocol.TCP)]
};

using var containerWireMock = session.CreateContainer(containerSettingsWireMock);

containerWireMock.InitProcess.OutputReceived += data => Console.Write(Encoding.UTF8.GetString(data));

containerWireMock.Start();

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

AssemblyLoadContext.Default.Unloading += ctx =>
{
    cts.Cancel();
};

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // Token was cancelled - proceed with cleanup
}

containerWireMock.Stop(Signal.SIGQUIT, TimeSpan.FromSeconds(1));

session.Terminate();