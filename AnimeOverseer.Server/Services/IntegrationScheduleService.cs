using System.Text.Json;
using AnimeOverseer.Server.BackgroundServices;
using AnimeOverseer.Server.Data;
using AnimeOverseer.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AnimeOverseer.Server.Services;

public class IntegrationScheduleService(AnimeDbContext db, SyncJobTrigger syncJobTrigger)
{
    public static readonly string[] CatalogueSources = ["AniList", "MyAnimeList", "AnimeSchedule"];

    public async Task<List<IntegrationSchedule>> GetAllAsync()
    {
        await EnsureDefaultAsync();
        return await db.IntegrationSchedules.AsNoTracking().OrderBy(s => s.Name).ThenBy(s => s.Id).ToListAsync();
    }

    // Replaces the former hosted nightly sync with an editable persisted schedule.
    public async Task EnsureDefaultAsync()
    {
        if (await db.IntegrationSchedules.AnyAsync()) return;
        db.IntegrationSchedules.Add(new IntegrationSchedule
        {
            Name = "Nightly AniList catalogue",
            Source = "AniList",
            Enabled = true,
            StartAt = DateTime.UtcNow.Date.AddHours(2),
            RecurrenceType = "Daily",
            Interval = 1,
            YearCount = 4
        });
        await db.SaveChangesAsync();
    }

    public async Task<int> SaveAsync(IntegrationSchedule input)
    {
        if (!CatalogueSources.Contains(input.Source)) throw new ArgumentException("Unknown catalogue source.");
        var schedule = input.Id == 0 ? new IntegrationSchedule() : await db.IntegrationSchedules.FindAsync(input.Id);
        if (schedule is null) throw new ArgumentException("Schedule was not found.");

        schedule.Name = string.IsNullOrWhiteSpace(input.Name) ? $"{input.Source} schedule" : input.Name.Trim();
        schedule.Source = input.Source;
        schedule.Enabled = input.Enabled;
        schedule.StartAt = DateTime.SpecifyKind(input.StartAt, DateTimeKind.Utc);
        schedule.RecurrenceType = input.RecurrenceType is "Daily" or "Weekly" or "Monthly" or "Yearly" ? input.RecurrenceType : "Daily";
        schedule.Interval = Math.Clamp(input.Interval, 1, 999);
        schedule.WeekdaysJson = NormalizeWeekdays(input.WeekdaysJson);
        schedule.MonthlyMode = input.MonthlyMode == "NthWeekday" ? "NthWeekday" : "DayOfMonth";
        schedule.DayOfMonth = Math.Clamp(input.DayOfMonth, 1, 31);
        schedule.NthWeek = Math.Clamp(input.NthWeek, 1, 5);
        schedule.Weekday = Math.Clamp(input.Weekday, 0, 6);
        schedule.EndType = input.EndType is "After" or "By" ? input.EndType : "Never";
        schedule.EndAfterOccurrences = Math.Clamp(input.EndAfterOccurrences, 1, 9999);
        schedule.EndBy = input.EndType == "By" ? input.EndBy?.Date : null;
        schedule.YearOffset = Math.Clamp(input.YearOffset, -100, 100);
        schedule.YearCount = Math.Clamp(input.YearCount, 1, 100);
        schedule.SeasonsJson = NormalizeSeasons(input.SeasonsJson);
        if (input.Id == 0) db.IntegrationSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return schedule.Id;
    }

    public async Task DeleteAsync(int id)
    {
        var schedule = await db.IntegrationSchedules.FindAsync(id);
        if (schedule is null) return;
        db.IntegrationSchedules.Remove(schedule);
        await db.SaveChangesAsync();
    }

    public IReadOnlyList<DateTime> GetNextOccurrences(IntegrationSchedule schedule, int count, DateTime? from = null)
        => ScheduleRecurrence.Next(schedule, from ?? DateTime.UtcNow, count);

    public async Task QueueDueAsync(CancellationToken ct = default)
    {
        await EnsureDefaultAsync();
        var now = DateTime.UtcNow;
        var schedules = await db.IntegrationSchedules.Where(s => s.Enabled).ToListAsync(ct);
        var queuedAny = false;
        foreach (var schedule in schedules)
        {
            if (schedule.EndType == "After" && schedule.QueuedOccurrences >= schedule.EndAfterOccurrences) continue;
            var next = ScheduleRecurrence.Next(schedule, schedule.LastQueuedAt ?? schedule.StartAt.AddTicks(-1), 1).FirstOrDefault();
            if (next == default || next > now) continue;
            // Source work is still serial. A second schedule for the same source waits for its turn.
            if (await db.SyncJobs.AnyAsync(j => j.JobType == schedule.Source && (j.Status == "Queued" || j.Status == "Running"), ct)) continue;

            db.SyncJobs.Add(new SyncJob
            {
                JobType = schedule.Source,
                QueuedAt = now,
                Parameters = JsonSerializer.Serialize(new { years = YearsFor(schedule, now), seasons = SeasonsFor(schedule) })
            });
            schedule.LastQueuedAt = now;
            schedule.QueuedOccurrences++;
            queuedAny = true;
        }
        if (queuedAny)
        {
            await db.SaveChangesAsync(ct);
            syncJobTrigger.Signal();
        }
    }

    public static int[] YearsFor(IntegrationSchedule schedule, DateTime now)
        => Enumerable.Range(now.Year + schedule.YearOffset, schedule.YearCount).ToArray();

    public static string[] SeasonsFor(IntegrationSchedule schedule)
    {
        try { return JsonSerializer.Deserialize<string[]>(schedule.SeasonsJson)?.Where(s => s is "winter" or "spring" or "summer" or "fall").Distinct().ToArray() ?? []; }
        catch (JsonException) { return []; }
    }

    private static string NormalizeWeekdays(string value)
    {
        try { return JsonSerializer.Serialize(JsonSerializer.Deserialize<int[]>(value)?.Where(d => d is >= 0 and <= 6).Distinct().ToArray() ?? []); }
        catch (JsonException) { return "[]"; }
    }
    private static string NormalizeSeasons(string value)
    {
        try { return JsonSerializer.Serialize(JsonSerializer.Deserialize<string[]>(value)?.Where(s => s is "winter" or "spring" or "summer" or "fall").Distinct().ToArray() ?? []); }
        catch (JsonException) { return "[]"; }
    }
}

public static class ScheduleRecurrence
{
    public static IReadOnlyList<DateTime> Next(IntegrationSchedule schedule, DateTime afterExclusive, int count)
    {
        var result = new List<DateTime>();
        var date = Max(schedule.StartAt.Date, afterExclusive.Date);
        // The horizon prevents malformed configuration from producing an unbounded loop.
        for (var i = 0; i < 366 * 110 && result.Count < count; i++, date = date.AddDays(1))
        {
            var occurrence = date.Add(schedule.StartAt.TimeOfDay);
            if (occurrence <= afterExclusive || occurrence < schedule.StartAt || !Matches(schedule, occurrence)) continue;
            if (schedule.EndType == "By" && schedule.EndBy is { } endBy && occurrence.Date > endBy.Date) break;
            if (schedule.EndType == "After" && schedule.QueuedOccurrences + result.Count >= schedule.EndAfterOccurrences) break;
            result.Add(occurrence);
        }
        return result;
    }

    private static bool Matches(IntegrationSchedule s, DateTime occurrence)
    {
        var days = (occurrence.Date - s.StartAt.Date).Days;
        return s.RecurrenceType switch
        {
            "Daily" => days % s.Interval == 0,
            "Weekly" => WeeklyMatches(s, occurrence, days),
            "Monthly" => MonthlyMatches(s, occurrence),
            "Yearly" => occurrence.Month == s.StartAt.Month && occurrence.Day == Math.Min(s.StartAt.Day, DateTime.DaysInMonth(occurrence.Year, occurrence.Month)) && (occurrence.Year - s.StartAt.Year) % s.Interval == 0,
            _ => false
        };
    }

    private static bool WeeklyMatches(IntegrationSchedule s, DateTime occurrence, int days)
    {
        int[] weekdays;
        try { weekdays = JsonSerializer.Deserialize<int[]>(s.WeekdaysJson) ?? []; } catch { weekdays = []; }
        if (weekdays.Length == 0) weekdays = [(int)s.StartAt.DayOfWeek];
        return weekdays.Contains((int)occurrence.DayOfWeek) && days / 7 % s.Interval == 0;
    }
    private static bool MonthlyMatches(IntegrationSchedule s, DateTime occurrence)
    {
        var months = (occurrence.Year - s.StartAt.Year) * 12 + occurrence.Month - s.StartAt.Month;
        if (months < 0 || months % s.Interval != 0) return false;
        if (s.MonthlyMode == "DayOfMonth") return occurrence.Day == Math.Min(s.DayOfMonth, DateTime.DaysInMonth(occurrence.Year, occurrence.Month));
        var first = new DateTime(occurrence.Year, occurrence.Month, 1);
        var offset = ((s.Weekday - (int)first.DayOfWeek) + 7) % 7;
        var day = 1 + offset + (s.NthWeek - 1) * 7;
        if (s.NthWeek == 5 && day > DateTime.DaysInMonth(occurrence.Year, occurrence.Month)) day -= 7;
        return occurrence.Day == day;
    }
    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
