// https://github.com/microsoft/WSL/blob/70cf77fa39b1f5bd0b74a5772d24512d668c2878/doc/samples/WSLC-NextCloud/Program.cs


using System.Runtime.Loader;
using System.Text;
using Microsoft.WSL.Containers;

var missing = WslcService.GetMissingComponents();
if (missing.Any())
{
    Console.WriteLine($"WSL components are missing ({string.Join(", ", missing)}). Run: wsl --install");
    return;
}

// Storage lives beside the executable; no hard-coded absolute paths.
// The session storage directory must be empty for the session to be created (the SDK creates and reuses its own VHD inside it).
var baseDir = AppContext.BaseDirectory;
var sessionPath = Path.Combine(baseDir, "PetStoreStorage");

var sessionSettings = new SessionSettings("PetStore", sessionPath)
{
    CpuCount = 1,
    MemorySizeInMB = 1024,
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

var pullWireMock = session.PullImageAsync(new PullImageOptions("swaggerapi/petstore3"));
pullWireMock.Progress = (op, progress) => Console.WriteLine($"Pull: {progress.Status} {progress.CurrentBytes}/{progress.TotalBytes}");
await pullWireMock;

var initProcess = new ProcessSettings
{
    OutputMode = ProcessOutputMode.Event,
    EnvironmentVariables = new Dictionary<string, string>
    {
        ["SWAGGER_HOST"] = "http://petstore.swagger.io",
        ["SWAGGER_URL"] = "http://localhost:8080",
        ["SWAGGER_BASE_PATH"] = "/v3"
    }
};

var containerSettings = new ContainerSettings("swaggerapi/petstore3")
{
    Name = "PetStore-container",
    EnableAutoRemove = true,
    InitProcess = initProcess,

    NetworkingMode = ContainerNetworkingMode.Bridged,
    PortMappings = [new(8080, 8080, PortProtocol.TCP)],
};

using var container = session.CreateContainer(containerSettings);

container.InitProcess.OutputReceived += data => Console.Write(Encoding.UTF8.GetString(data));

container.Start();

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

container.Stop(Signal.SIGQUIT, TimeSpan.FromSeconds(1));

session.Terminate();