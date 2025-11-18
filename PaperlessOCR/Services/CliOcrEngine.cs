using PaperlessOCR.Abstractions;
using System.Diagnostics;
using System.Text;

namespace PaperlessOCR.Services
{
    public class CliOcrEngine : IOcrEngine
    {
        // how many pages max to OCR from a PDF
        private const int MaxPagesToOcr = 10;

        public Task<string> ExtractAsync(string inputPath, string contentType, CancellationToken ct)
        {
            var dir = Path.GetDirectoryName(inputPath)!;
            var baseName = Path.GetFileNameWithoutExtension(inputPath);
            var outBase = Path.Combine(dir, baseName);

            var isPdf =
                contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
                inputPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

            if (!isPdf)
            {
                // already an image -> just tesseract it
                var text = RunTesseractToText(inputPath, ct);
                TryDelete(inputPath);
                return Task.FromResult(text);
            }

            // ---------- PDF path ----------
            // Convert ALL pages to PNGs: outBase-1.png, outBase-2.png, ...
            // (no -singlefile, no -f/-l restriction)
            RunOrThrow("pdftoppm", $"-png -r 200 \"{inputPath}\" \"{outBase}\"");

            // Find generated PNG pages
            var pngFiles = Directory.GetFiles(dir, $"{baseName}-*.png")
                                    .OrderBy(p => p)
                                    .Take(MaxPagesToOcr)
                                    .ToList();

            if (!pngFiles.Any())
                throw new InvalidOperationException($"No PNG pages produced from PDF '{inputPath}'.");

            var sb = new StringBuilder();

            foreach (var png in pngFiles)
            {
                if (ct.IsCancellationRequested)
                    ct.ThrowIfCancellationRequested();

                var pageText = RunTesseractToText(png, ct);
                sb.AppendLine(pageText);
            }

            // cleanup
            foreach (var png in pngFiles)
                TryDelete(png);

            TryDelete(inputPath);

            var combinedText = sb.ToString();
            return Task.FromResult(combinedText);
        }

        static string RunTesseractToText(string imgPath, CancellationToken ct)
        {
            var psi = new ProcessStartInfo("tesseract", $"\"{imgPath}\" stdout")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;

            // simple cooperative cancellation
            while (!p.HasExited)
            {
                if (ct.IsCancellationRequested)
                {
                    try { p.Kill(); } catch { /* ignore */ }
                    ct.ThrowIfCancellationRequested();
                }

                Thread.Sleep(50);
            }

            var output = p.StandardOutput.ReadToEnd();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"Tesseract failed: {p.StandardError.ReadToEnd()}");

            return output;
        }

        static void RunOrThrow(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"{file} failed: {p.StandardError.ReadToEnd()}");
        }

        static void TryDelete(string p)
        {
            try
            {
                if (File.Exists(p))
                    File.Delete(p);
            }
            catch
            {
                // ignore cleanup errors
            }
        }
    }
}
