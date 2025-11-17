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

        Container = new ContainerBuilder()
            .WithImage("tesseractshadow/tesseract4re:latest")  // ⭐ GOOD IMAGE ⭐
            .WithWorkingDirectory("/work")
            .WithCommand("sleep", "infinity")                 // ⭐ stable keep-alive ⭐
            .WithBindMount(HostWorkDir, "/work", AccessMode.ReadWrite)
            .WithCleanUp(true)
            .Build();

        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        try { await Container.DisposeAsync(); } catch { }
        try { Directory.Delete(HostWorkDir, true); } catch { }
    }
}
