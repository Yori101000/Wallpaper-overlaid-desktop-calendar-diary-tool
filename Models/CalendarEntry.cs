namespace TransparentCalendar.Models;

public sealed class CalendarEntry
{
    public string Date { get; set; } = string.Empty;
    public List<TodoItem> Todos { get; set; } = [];
    public string Diary { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class TodoItem
{
    public string Text { get; set; } = string.Empty;
    public string Priority { get; set; } = "普通";
    public bool IsDone { get; set; }
}
