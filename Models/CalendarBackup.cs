namespace TransparentCalendar.Models;

public sealed class CalendarBackup
{
    public int Version { get; set; } = 1;
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public AppSettings Settings { get; set; } = new();
    public Dictionary<string, CalendarEntry> Entries { get; set; } = [];
}
