using System.Text;
using Sound2Text.Api.Data;

namespace Sound2Text.Api.Services;

public static class Exporter
{
    public static (string Body, string Mime, string Ext) Build(List<Seg> segs, string fmt)
    {
        var sb = new StringBuilder();
        switch (fmt)
        {
            case "srt":
                for (var i = 0; i < segs.Count; i++)
                    sb.Append(i + 1).Append('\n').Append(Ts(segs[i].Start, ',')).Append(" --> ").Append(Ts(segs[i].End, ',')).Append('\n').Append(segs[i].Text).Append("\n\n");
                return (sb.ToString(), "application/x-subrip", "srt");
            case "vtt":
                sb.Append("WEBVTT\n\n");
                foreach (var s in segs)
                    sb.Append(Ts(s.Start, '.')).Append(" --> ").Append(Ts(s.End, '.')).Append('\n').Append(s.Text).Append("\n\n");
                return (sb.ToString(), "text/vtt", "vtt");
            default:
                foreach (var s in segs) sb.Append(s.Text).Append('\n');
                return (sb.ToString(), "text/plain", "txt");
        }
    }

    static string Ts(double sec, char sep)
    {
        var t = TimeSpan.FromSeconds(sec);
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{sep}{t.Milliseconds:000}";
    }
}
