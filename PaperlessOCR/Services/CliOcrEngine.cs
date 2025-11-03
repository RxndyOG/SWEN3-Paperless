using PaperlessOCR.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;


namespace PaperlessOCR.Services
{
    public class CliOcrEngine : IOcrEngine
    {
        public Task<string> ExtractAsync(string inputPath, string contentType, CancellationToken ct)
        {
            string outBase = Path.Combine(Path.GetDirectoryName(inputPath)!, Path.GetFileNameWithoutExtension(inputPath));
            string pngPath;

            var isPdf = contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
                        || inputPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

            if (isPdf)
            {
                // First page only, one output file
                RunOrThrow("pdftoppm", $"-png -r 300 -singlefile -f 1 -l 1 \"{inputPath}\" \"{outBase}\"");
                pngPath = outBase + ".png";
            }
            else
            {
                pngPath = inputPath; // already an image
            }

            var text = RunTesseractToText(pngPath);
            if (pngPath != inputPath) TryDelete(pngPath);
            TryDelete(inputPath);
            return Task.FromResult(text);
        }

        static string RunTesseractToText(string imgPath)
        {
            var psi = new ProcessStartInfo("tesseract", $"\"{imgPath}\" stdout")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException($"Tesseract failed: {p.StandardError.ReadToEnd()}");
            return output;
        }

        static void RunOrThrow(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args) { RedirectStandardError = true, RedirectStandardOutput = true };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException($"{file} failed: {p.StandardError.ReadToEnd()}");
        }

        static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
    }
}
