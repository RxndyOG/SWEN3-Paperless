using DotNet.Testcontainers.Containers;
using PaperlessOCR.Abstractions;
using System.Text;

public class ContainerOcrEngine : IOcrEngine
{
    private readonly IContainer _c;
    private readonly string _workDir;

    public ContainerOcrEngine(IContainer container, string hostWorkDir)
    {
        _c = container;
        _workDir = hostWorkDir;
    }

    public async Task<string> ExtractAsync(string inputPath, string contentType, CancellationToken ct)
    {
        var hostPath = EnsureInWorkdir(inputPath);
        var fileName = Path.GetFileName(hostPath);
        var baseName = Path.GetFileNameWithoutExtension(hostPath);
        var inContainerPath = $"/work/{fileName}";
        var outBase = $"/work/{baseName}";
        string pngPath;

        Console.WriteLine($"[OCR] ExtractAsync: input={hostPath}, contentType={contentType}");

        if (IsPdf(contentType, hostPath))
        {
            Console.WriteLine("[OCR] Converting PDF to PNG via pdftoppm...");
            await ExecWithTimeout(new[] { "bash", "-lc", $"pdftoppm -png -r 300 -singlefile -f 1 -l 1 \"{inContainerPath}\" \"{outBase}\"" },
                                  TimeSpan.FromSeconds(30), "pdftoppm");
            pngPath = $"{outBase}.png";
        }
        else
        {
            pngPath = inContainerPath;
        }

        Console.WriteLine($"[OCR] Using PNG path: {pngPath}");

        // sanity check host file present
        if (!File.Exists(hostPath))
            throw new InvalidOperationException($"host file not found: {hostPath}");

        Console.WriteLine("[OCR] Running tesseract...");

        var execResult = await ExecWithTimeout(
            new[] { "bash", "-lc", $"tesseract \"{pngPath}\" stdout" },
            TimeSpan.FromSeconds(60),
            "tesseract");

        var code = execResult.ExitCode;
        var outText = execResult.Stdout;
        var err = execResult.Stderr;

        var combined = $"{outText}{err}";
        if (combined.Contains("OCI runtime exec failed", StringComparison.OrdinalIgnoreCase)
         || combined.Contains("unable to create new parent process", StringComparison.OrdinalIgnoreCase)
         || combined.Contains("namespace path", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"docker exec failed while running tesseract. Raw output: '{combined}'.");
        }

        if (code != 0)
            throw new InvalidOperationException($"tesseract failed (exit {code}). Stdout: '{outText}' Stderr: '{err}'");

        Console.WriteLine("[OCR] tesseract finished OK.");
        return outText ?? string.Empty;
    }

    private async Task<ExecResult> ExecWithTimeout(string[] cmd, TimeSpan timeout, string tool)
    {
        using var cts = new CancellationTokenSource(timeout);
        var result = await _c.ExecAsync(cmd, cts.Token);
        if (cts.IsCancellationRequested)
            throw new TimeoutException($"{tool} inside container timed out after {timeout.TotalSeconds} seconds.");
        return result;
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
