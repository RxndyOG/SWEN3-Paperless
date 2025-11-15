using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Configurations;

public sealed class OcrFixture : IAsyncLifetime
{
    public IContainer Container { get; private set; } = default!;
    public string HostWorkDir { get; } = Path.Combine(Path.GetTempPath(), "ocr-tools-work");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(HostWorkDir);

        // Image with both tesseract & pdftoppm (via ocrmypdf)
        // https://hub.docker.com/r/jbarlow83/ocrmypdf
        Container = new ContainerBuilder()
            .WithImage("jbarlow83/ocrmypdf:latest")
            .WithName($"ocr-tools-{Guid.NewGuid():N}")
            .WithWorkingDirectory("/work")
            // Keep container alive using a portable shell loop (sh is widely available)
            .WithEntrypoint("sh", "-c", "while true; do sleep 1; done")
            .WithBindMount(HostWorkDir, "/work", AccessMode.ReadWrite) // host <-> container
            .Build();

        await Container.StartAsync();

        // Wait briefly for container to transition to running state
        var attempts = 10;
        var delay = TimeSpan.FromMilliseconds(300);
        for (var i = 0; i < attempts && Container.State != TestcontainersStates.Running; i++)
            await Task.Delay(delay);

        if (Container.State != TestcontainersStates.Running)
        {
            // Gather logs and exit code to aid diagnosis
            var logs = await Container.GetLogsAsync();
            long exitCode = -1;
            try { exitCode = await Container.GetExitCodeAsync(); } catch { /* best-effort */ }

            throw new InvalidOperationException(
                $"Container failed to remain running after StartAsync. State={Container.State}, ExitCode={exitCode}. " +
                $"Container logs: Stdout='{logs.Stdout}' Stderr='{logs.Stderr}'. " +
                "Ensure Docker is running and the image can start with the provided entrypoint.");
        }
    }

    public async Task DisposeAsync()
    {
        try { await Container.StopAsync(); } catch { }
        try { await Container.DisposeAsync(); } catch { }
        try { Directory.Delete(HostWorkDir, recursive: true); } catch { }
    }
}
