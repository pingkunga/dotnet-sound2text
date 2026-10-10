namespace Sound2Text.Api.Services;

public static class Chunker
{
    public const int Rate = 16000;
    const int Frame = Rate / 10; // 100 ms

    // Cut at the quietest 100 ms frame in [min,max] so words are not split.
    public static List<(int Start, int End)> Split(float[] s, double minSec = 15, double maxSec = 30)
    {
        var rms = new double[s.Length / Frame];
        for (var i = 0; i < rms.Length; i++)
        {
            double sum = 0;
            for (var j = i * Frame; j < (i + 1) * Frame; j++) sum += s[j] * s[j];
            rms[i] = Math.Sqrt(sum / Frame);
        }

        var res = new List<(int, int)>();
        var start = 0;
        while (start < s.Length)
        {
            if (s.Length - start <= maxSec * Rate) { res.Add((start, s.Length)); break; }
            var first = start / Frame;
            var lo = first + (int)(minSec * 10);
            var hi = Math.Min(rms.Length - 1, first + (int)(maxSec * 10));
            var best = lo;
            for (var i = lo; i <= hi; i++)
                if (rms[i] < rms[best]) best = i;
            var end = (best + 1) * Frame;
            res.Add((start, end));
            start = end;
        }
        return res;
    }

    public static bool IsSilent(float[] s, int start, int end, float threshold = 0.003f)
    {
        double sum = 0;
        for (var i = start; i < end; i++) sum += s[i] * s[i];
        return Math.Sqrt(sum / Math.Max(1, end - start)) < threshold;
    }
}
