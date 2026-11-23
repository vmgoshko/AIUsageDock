using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using AIUsageDock.Models;

namespace AIUsageDock.Pages;

internal static class UsageHistoryChart
{
    public static byte[] Render(IReadOnlyList<UsageSample> samples, DateTimeOffset now)
    {
        const int width = 1000, height = 260, left = 54, right = 20, top = 40, bottom = 34;
        using var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(39, 39, 42));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font("Segoe UI", 11);
        using var text = new SolidBrush(Color.FromArgb(190, 190, 200));
        using var grid = new Pen(Color.FromArgb(65, 65, 72));
        using var sessionPen = new Pen(Color.FromArgb(52, 211, 153), 2.5f);
        using var weeklyPen = new Pen(Color.FromArgb(96, 165, 250), 2.5f);
        var start = now.AddHours(-24);
        float X(DateTimeOffset time) => left + (float)((time - start).TotalHours / 24) * (width - left - right);
        float Y(double used) => top + (float)(1 - Math.Clamp(used, 0, 100) / 100) * (height - top - bottom);
        for (var percentage = 0; percentage <= 100; percentage += 25)
        {
            var y = Y(percentage);
            g.DrawLine(grid, left, y, width - right, y);
            g.DrawString($"{percentage}%", font, text, 5, y - 9);
        }
        for (var hour = 0; hour <= 24; hour += 6)
        {
            var time = start.AddHours(hour);
            var x = X(time);
            g.DrawString(time.ToLocalTime().ToString("HH:mm"), font, text, x - 20, height - bottom + 8);
        }
        g.DrawLine(sessionPen, left, 17, left + 24, 17);
        g.DrawString("5h used", font, text, left + 32, 7);
        g.DrawLine(weeklyPen, left + 170, 17, left + 194, 17);
        g.DrawString("Week used", font, text, left + 202, 7);
        DrawSeries(true, sessionPen);
        DrawSeries(false, weeklyPen);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();

        void DrawSeries(bool session, Pen pen)
        {
            UsageSample? previous = null;
            using var marker = new SolidBrush(pen.Color);
            foreach (var sample in samples.Where(s => s.At >= start && s.At <= now))
            {
                var value = session ? sample.SessionUsed : sample.WeeklyUsed;
                if (value is null) { previous = null; continue; }
                var x = X(sample.At);
                var y = Y(value.Value);
                if (previous is not null && CanConnect(previous, sample, session))
                {
                    var before = session ? previous.SessionUsed!.Value : previous.WeeklyUsed!.Value;
                    // Step lines show observed quota changes without inventing intermediate readings.
                    g.DrawLine(pen, X(previous.At), Y(before), x, Y(before));
                    g.DrawLine(pen, x, Y(before), x, y);
                }
                else g.FillEllipse(marker, x - 3, y - 3, 6, 6);
                previous = sample;
            }
        }
    }

    internal static bool CanConnect(UsageSample a, UsageSample b, bool session) =>
        b.At > a.At && b.At - a.At <= TimeSpan.FromMinutes(5)
        && (session ? a.SessionUsed is not null && b.SessionUsed is not null && SameReset(a.SessionReset, b.SessionReset)
            : a.WeeklyUsed is not null && b.WeeklyUsed is not null && SameReset(a.WeeklyReset, b.WeeklyReset))
        && (session ? b.SessionUsed >= a.SessionUsed : b.WeeklyUsed >= a.WeeklyUsed);

    private static bool SameReset(DateTimeOffset? a, DateTimeOffset? b) =>
        // Claude reconstructs reset times from a relative countdown, which can vary slightly between reads.
        a == b || a is { } first && b is { } second && (first - second).Duration() <= TimeSpan.FromSeconds(2);
}
