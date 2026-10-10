using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sound2Text.Api.Data;
using Sound2Text.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<WhisperOptions>(builder.Configuration.GetSection("Whisper"));
var opt = builder.Configuration.GetSection("Whisper").Get<WhisperOptions>() ?? new();
Directory.CreateDirectory(Path.Combine(opt.DataDir, "uploads"));

builder.Services.AddDbContext<AppDb>(o => o.UseSqlite($"Data Source={Path.Combine(opt.DataDir, "jobs.db")}"));
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddSingleton<ITranscriber, WhisperNetTranscriber>();
builder.Services.AddHostedService<TranscriptionWorker>();
builder.Services.Configure<FormOptions>(f => f.MultipartBodyLengthLimit = opt.MaxUploadMb * 1024L * 1024);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = opt.MaxUploadMb * 1024L * 1024);

var app = builder.Build();
using (var s = app.Services.CreateScope())
    s.ServiceProvider.GetRequiredService<AppDb>().Database.EnsureCreated();

app.UseDefaultFiles();
app.UseStaticFiles();

string[] langs = ["th", "en", "auto"];

app.MapPost("/api/jobs", async (HttpRequest req, AppDb db, JobQueue q, IOptions<WhisperOptions> o) =>
{
    var form = await req.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0) return Results.BadRequest("file required");
    var lang = form["language"].ToString();
    if (!langs.Contains(lang)) lang = "th";

    var job = new Job { FileName = Path.GetFileName(file.FileName), Language = lang, Hint = form["hint"].ToString() };
    await using (var fs = File.Create(o.Value.UploadPath(job))) await file.CopyToAsync(fs);

    db.Jobs.Add(job);
    await db.SaveChangesAsync();
    q.Channel.Writer.TryWrite(job.Id);
    return Results.Accepted($"/api/jobs/{job.Id}", new { id = job.Id });
});

app.MapGet("/api/jobs/{id:guid}", async (Guid id, AppDb db) =>
    await db.Jobs.FindAsync(id) is { } j
        ? Results.Ok(new { j.Id, j.FileName, j.Language, Status = j.Status.ToString(), j.Progress, j.Error, j.ElapsedSec, j.AudioSec, segments = j.GetSegments() })
        : Results.NotFound());

app.MapGet("/api/jobs/{id:guid}/export", async (Guid id, string? fmt, AppDb db) =>
{
    if (await db.Jobs.FindAsync(id) is not { Status: JobStatus.Done } j) return Results.NotFound();
    var (body, mime, ext) = Exporter.Build(j.GetSegments(), fmt ?? "txt");
    return Results.File(System.Text.Encoding.UTF8.GetBytes(body), mime + "; charset=utf-8", Path.GetFileNameWithoutExtension(j.FileName) + "." + ext);
});

app.MapGet("/api/jobs", async (AppDb db) =>
    await db.Jobs.OrderByDescending(j => j.CreatedUtc).Take(100)
        .Select(j => new { j.Id, j.FileName, j.Language, Status = j.Status.ToString(), j.Progress, j.CreatedUtc }).ToListAsync());

app.MapPost("/api/jobs/{id:guid}/cancel", async (Guid id, AppDb db, JobQueue q) =>
{
    var n = await db.Jobs.Where(j => j.Id == id && j.Status == JobStatus.Queued)
        .ExecuteUpdateAsync(u => u.SetProperty(j => j.Status, JobStatus.Cancelled));
    return n > 0 || q.CancelRunning(id) ? Results.Ok() : Results.NotFound();
});

app.MapDelete("/api/jobs/{id:guid}", async (Guid id, AppDb db, JobQueue q, IOptions<WhisperOptions> o) =>
{
    if (await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id) is not { } job) return Results.NotFound();
    q.CancelRunning(id);
    await db.Jobs.Where(j => j.Id == id).ExecuteDeleteAsync();
    try { File.Delete(o.Value.UploadPath(job)); } catch (IOException) { }
    return Results.NoContent();
});

app.MapPost("/api/jobs/clear-queue", async (AppDb db, JobQueue q) =>
{
    var n = await db.Jobs.Where(j => j.Status == JobStatus.Queued)
        .ExecuteUpdateAsync(u => u.SetProperty(j => j.Status, JobStatus.Cancelled));
    var running = q.CancelRunning();
    return Results.Ok(new { cancelled = n + (running ? 1 : 0) });
});

app.MapGet("/healthz", (IOptions<WhisperOptions> o) =>
    Results.Ok(new { status = "ok", gpu = o.Value.UseGpu, model = File.Exists(o.Value.ModelPath) }));

app.Run();
