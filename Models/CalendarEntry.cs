using System.Text.Json.Serialization;

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
    public string PostponedFromDate { get; set; } = string.Empty;
    public int PostponedDays { get; set; }
    public bool IsDone { get; set; }

    [JsonIgnore]
    public string PostponedLabel => PostponedDays > 0 ? $"已推迟 {PostponedDays} 天" : string.Empty;

    public TodoItem Clone()
    {
        return new TodoItem
        {
            Text = Text,
            Priority = string.IsNullOrWhiteSpace(Priority) ? "普通" : Priority,
            PostponedFromDate = PostponedFromDate,
            PostponedDays = PostponedDays,
            IsDone = IsDone
        };
    }
}
