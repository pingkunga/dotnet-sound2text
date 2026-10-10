using Microsoft.Extensions.Options;
using Sound2Text.Api.Data;
using Whisper.net;

namespace Sound2Text.Api.Services;

public interface ITranscriber
{
    Task<List<Seg>> TranscribeAsync(float[] samples, string language, string? hint, IProgress<double> progress, CancellationToken ct);
}

public sealed class WhisperNetTranscriber : ITranscriber, IDisposable
{
    readonly WhisperFactory _factory;
    readonly int _threads;

    public WhisperNetTranscriber(IOptions<WhisperOptions> o)
    {
        var opt = o.Value;
        _threads = opt.Threads;
        _factory = WhisperFactory.FromPath(opt.ModelPath, new WhisperFactoryOptions { UseGpu = opt.UseGpu });
    }

    public async Task<List<Seg>> TranscribeAsync(float[] samples, string language, string? hint, IProgress<double> progress, CancellationToken ct)
    {
        var chunks = Chunker.Split(samples);
        var result = new List<Seg>();
        var prev = "";

        for (var i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (st, en) = chunks[i];
            if (!Chunker.IsSilent(samples, st, en))
            {
                var b = _factory.CreateBuilder()
                    .WithLanguage(language)
                    .WithThreads(_threads)
                    .WithTemperature(0f)
                    .WithNoSpeechThreshold(0.6f)
                    .WithNoContext();
                var prompt = string.Join(' ', new[] { hint, Tail(prev) }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (prompt.Length > 0) b = b.WithPrompt(prompt);

                await using var proc = b.Build();
                var offset = (double)st / Chunker.Rate;
                var texts = new List<string>();
                await foreach (var seg in proc.ProcessAsync(samples.AsMemory(st, en - st), ct))
                {
                    var t = seg.Text.Trim();
                    if (t.Length == 0) continue;
                    result.Add(new Seg(offset + seg.Start.TotalSeconds, offset + seg.End.TotalSeconds, t, seg.Language));
                    texts.Add(t);
                }
                prev = string.Concat(texts);
            }
            progress.Report((i + 1) / (double)chunks.Count);
        }
        return Dedupe(result);
    }

    static string Tail(string s) => s.Length > 120 ? s[^120..] : s;

    // Drop consecutive identical segments (repeat loops).
    static List<Seg> Dedupe(List<Seg> segs)
    {
        var o = new List<Seg>();
        foreach (var s in segs)
            if (o.Count == 0 || o[^1].Text != s.Text) o.Add(s);
        return o;
    }

    public void Dispose() => _factory.Dispose();
}
