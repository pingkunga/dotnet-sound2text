using Sound2Text.Api.Data;

namespace Sound2Text.Api.Services;

public sealed class WhisperOptions
{
    public string ModelPath { get; set; } = "models/ggml-large-v3-q5_0.bin";
    public bool UseGpu { get; set; } = true;
    public int Threads { get; set; } = 4;
    public string DataDir { get; set; } = "data";
    public int MaxUploadMb { get; set; } = 500;

    public string UploadPath(Job j) => Path.Combine(DataDir, "uploads", j.Id.ToString("N") + Path.GetExtension(j.FileName));
}
