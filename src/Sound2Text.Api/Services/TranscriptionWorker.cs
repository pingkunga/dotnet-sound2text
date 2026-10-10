using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sound2Text.Api.Data;

namespace Sound2Text.Api.Services;

public sealed class JobQueue
{
    readonly object _lk = new();
    Guid? _id;
    CancellationTokenSource? _cts;

    public Channel<Guid> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<Guid>();

    public void Begin(Guid id, CancellationTokenSource cts) { lock (_lk) { _id = id; _cts = cts; } }
    public void End() { lock (_lk) { _id = null; _cts = null; } }

    // Cancel the running job (only if it matches id, when given).
    public bool CancelRunning(Guid? id = null)
    {
        lock (_lk)
        {
            if (_cts is null || (id is not null && id != _id)) return false;
            _cts.Cancel();
            return true;
        }
    }
}

public sealed class TranscriptionWorker(IServiceScopeFactory scopes, JobQueue queue, ITranscriber transcriber,
    IOptions<WhisperOptions> opt, ILogger<TranscriptionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Requeue jobs interrupted by a restart.
        using (var s = scopes.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDb>();
            foreach (var id in db.Jobs.Where(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Running).Select(j => j.Id).ToList())
                queue.Channel.Writer.TryWrite(id);
        }

        await foreach (var id in queue.Channel.Reader.ReadAllAsync(ct))
        {
            try { await RunAsync(id, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Job {Id} crashed", id); }
        }
    }

    // Own context: Progress<T> callbacks run on the thread pool, the job's db is not thread-safe.
    void SaveProgress(Guid id, double p)
    {
        try
        {
            using var s = scopes.CreateScope();
            s.ServiceProvider.GetRequiredService<AppDb>().Jobs
                .Where(j => j.Id == id && j.Status == JobStatus.Running)
                .ExecuteUpdate(u => u.SetProperty(j => j.Progress, p));
        }
        catch (Exception e) { log.LogWarning(e, "Progress write failed for {Id}", id); }
    }

    async Task RunAsync(Guid id, CancellationToken hostCt)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();

        // Atomic claim: skips jobs cancelled or deleted while queued.
        var claimed = await db.Jobs.Where(j => j.Id == id && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running))
            .ExecuteUpdateAsync(u => u.SetProperty(j => j.Status, JobStatus.Running).SetProperty(j => j.Progress, 0.0).SetProperty(j => j.Error, (string?)null), hostCt);
        if (claimed == 0) return;
        var job = await db.Jobs.FindAsync([id], hostCt);
        if (job is null) return;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
        queue.Begin(id, cts);
        try
        {
            var sw = Stopwatch.StartNew();
            var samples = await AudioService.DecodeAsync(opt.Value.UploadPath(job), cts.Token);
            job.AudioSec = samples.Length / (double)Chunker.Rate;

            var segs = await transcriber.TranscribeAsync(samples, job.Language, job.Hint,
                new Progress<double>(p => SaveProgress(id, p)), cts.Token);

            job.SegmentsJson = JsonSerializer.Serialize(segs);
            job.Progress = 1; job.Status = JobStatus.Done; job.ElapsedSec = sw.Elapsed.TotalSeconds;
        }
        catch (OperationCanceledException)
        {
            // Host shutdown: stay Running so the job is requeued on next start.
            if (hostCt.IsCancellationRequested) return;
            job.Status = JobStatus.Cancelled;
        }
        catch (Exception e)
        {
            log.LogError(e, "Job {Id} failed", id);
            job.Status = JobStatus.Failed; job.Error = e.Message;
        }
        finally { queue.End(); }

        try { await db.SaveChangesAsync(CancellationToken.None); }
        catch (DbUpdateConcurrencyException) { } // job deleted meanwhile
    }
}
