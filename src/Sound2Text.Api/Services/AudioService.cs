using System.Diagnostics;

namespace Sound2Text.Api.Services;

public static class AudioService
{
    // Decode any input to 16 kHz mono float samples via ffmpeg.
    public static async Task<float[]> DecodeAsync(string path, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in new[] { "-nostdin", "-v", "error", "-i", path, "-vn", "-ac", "1", "-ar", "16000", "-f", "f32le", "pipe:1" })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg not found");
        using var kill = ct.Register(() => { try { p.Kill(true); } catch { } });
        var err = p.StandardError.ReadToEndAsync(ct);
        using var ms = new MemoryStream();
        await p.StandardOutput.BaseStream.CopyToAsync(ms, ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) throw new InvalidOperationException("ffmpeg failed: " + await err);

        var samples = new float[ms.Length / 4];
        Buffer.BlockCopy(ms.GetBuffer(), 0, samples, 0, samples.Length * 4);
        return samples;
    }
}
