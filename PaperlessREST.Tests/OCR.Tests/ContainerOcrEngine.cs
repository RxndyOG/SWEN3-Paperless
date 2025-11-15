using DotNet.Testcontainers.Containers;
using PaperlessOCR.Abstractions;
using System.Text;

public class ContainerOcrEngine : IOcrEngine
{
    private readonly IContainer _c;
    private readonly string _workDir; // host temp dir bound to /work

    public ContainerOcrEngine(IContainer container, string hostWorkDir)
    {
        _c = container;
        _workDir = hostWorkDir;
    }

    public async Task<string> ExtractAsync(string inputPath, string contentType, CancellationToken ct)
    {
        // Ensure file is inside the shared workdir
        var hostPath = EnsureInWorkdir(inputPath);
        var fileName = Path.GetFileName(hostPath);
        var baseName = Path.GetFileNameWithoutExtension(hostPath);
        var inContainerPath = $"/work/{fileName}";
        var outBase = $"/work/{baseName}";
        string pngPath;

        if (IsPdf(contentType, hostPath))
        {
            // pdftoppm -png -r 300 -singlefile -f 1 -l 1 /work/file.pdf /work/file
            await ExecOrThrow("pdftoppm", $"-png -r 300 -singlefile -f 1 -l 1 \"{inContainerPath}\" \"{outBase}\"");
            pngPath = $"{outBase}.png";
        }
        else
        {
            pngPath = inContainerPath;
        }

        // Ensure host-side file is present in the bound workdir (avoid fragile container-side 'test -f' exec)
        if (!File.Exists(hostPath))
            throw new InvalidOperationException($"host file not found: {hostPath}");

        // tesseract /work/file.png stdout
        var execResult = await _c.ExecAsync(new[] { "bash", "-lc", $"tesseract \"{pngPath}\" stdout" });
        var code = execResult.ExitCode;
        var outText = execResult.Stdout;
        var err = execResult.Stderr;

        // Detect common docker exec runtime failure message and give actionable guidance
        var combined = $"{outText}{err}";
        if (combined.Contains("OCI runtime exec failed", StringComparison.OrdinalIgnoreCase)
         || combined.Contains("unable to create new parent process", StringComparison.OrdinalIgnoreCase)
         || combined.Contains("namespace path", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"docker exec failed while running tesseract. This is usually a Docker/host issue (Docker engine stopped, WSL/VM issues, or bind-mount access). Raw output: '{combined}'. " +
                "Ensure Docker is running, the test host path is shared with Docker (on Windows/WSL), and restart Docker if necessary.");
        }

        if (code != 0)
            throw new InvalidOperationException($"tesseract failed (exit {code}). Stdout: '{outText}' Stderr: '{err}'");

        return outText ?? string.Empty;
    }

    static bool IsPdf(string contentType, string path)
      => contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
      || path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private string EnsureInWorkdir(string path)
    {
        // If caller saved into a random temp file elsewhere, copy into our bound workdir
        if (!Path.GetDirectoryName(path)!.Equals(_workDir, StringComparison.OrdinalIgnoreCase))
        {
            var dest = Path.Combine(_workDir, Path.GetFileName(path));
            File.Copy(path, dest, overwrite: true);
            return dest;
        }
        return path;
    }

    private async Task ExecOrThrow(string exe, string args)
    {
        var cmd = $"set -e; {exe} {args}";
        var execResult = await _c.ExecAsync(new[] { "bash", "-lc", cmd });
        var code = execResult.ExitCode;
        var outText = execResult.Stdout;
        var err = execResult.Stderr;
        if (code != 0)
            throw new InvalidOperationException($"{exe} failed (exit {code}). Stdout: '{outText}' Stderr: '{err}'");
    }
}
