using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using TransparentCalendar.Models;

namespace TransparentCalendar.Views;

public partial class DayEditorWindow : Window
{
    private readonly DateTime _date;
    private readonly CalendarEntry _entry;
    private readonly Action<DateTime, TodoItem> _postponeTodo;
    private readonly ObservableCollection<TodoItem> _todos;

    public DayEditorWindow(DateTime date, CalendarEntry entry, Action<DateTime, TodoItem> postponeTodo)
    {
        InitializeComponent();
        _date = date;
        _entry = entry;
        _postponeTodo = postponeTodo;
        _todos = new ObservableCollection<TodoItem>(_entry.Todos.Select(todo => new TodoItem
        {
            Text = todo.Text,
            Priority = string.IsNullOrWhiteSpace(todo.Priority) ? "普通" : todo.Priority,
            PostponedFromDate = todo.PostponedFromDate,
            PostponedDays = todo.PostponedDays,
            IsDone = todo.IsDone
        }));

        DateTitle.Text = date.ToString("yyyy 年 M 月 d 日 dddd", CultureInfo.GetCultureInfo("zh-CN"));
        TodoItems.ItemsSource = _todos;
        DiaryText.Text = _entry.Diary;
    }

    private void AddTodo_Click(object sender, RoutedEventArgs e)
    {
        _todos.Add(new TodoItem());
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || e.OriginalSource is System.Windows.Controls.Button)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // 拖动过程中释放鼠标可能抛出异常，不影响窗口状态。
        }
    }

    private void DeleteTodo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TodoItem item)
        {
            _todos.Remove(item);
        }
    }

    private void PostponeTodo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TodoItem item || item.IsDone || string.IsNullOrWhiteSpace(item.Text))
        {
            return;
        }

        var targetDate = _date.AddDays(1);
        var postponedFromDate = string.IsNullOrWhiteSpace(item.PostponedFromDate)
            ? DateKey(_date)
            : item.PostponedFromDate;
        var postponedDays = CalculatePostponedDays(postponedFromDate, targetDate);
        var postponedTodo = new TodoItem
        {
            Text = item.Text.Trim(),
            Priority = string.IsNullOrWhiteSpace(item.Priority) ? "普通" : item.Priority,
            PostponedFromDate = postponedFromDate,
            PostponedDays = postponedDays,
            IsDone = false
        };

        _todos.Remove(item);
        SaveTodosToEntry();
        _postponeTodo(targetDate, postponedTodo);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _entry.Todos = _todos
            .Where(todo => !string.IsNullOrWhiteSpace(todo.Text))
            .Select(todo => new TodoItem
            {
                Text = todo.Text.Trim(),
                Priority = string.IsNullOrWhiteSpace(todo.Priority) ? "普通" : todo.Priority,
                PostponedFromDate = todo.PostponedFromDate,
                PostponedDays = todo.PostponedDays,
                IsDone = todo.IsDone
            })
            .ToList();
        _entry.Diary = DiaryText.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SaveTodosToEntry()
    {
        _entry.Todos = _todos
            .Where(todo => !string.IsNullOrWhiteSpace(todo.Text))
            .Select(todo => new TodoItem
            {
                Text = todo.Text.Trim(),
                Priority = string.IsNullOrWhiteSpace(todo.Priority) ? "普通" : todo.Priority,
                PostponedFromDate = todo.PostponedFromDate,
                PostponedDays = todo.PostponedDays,
                IsDone = todo.IsDone
            })
            .ToList();
        _entry.UpdatedAt = DateTime.Now;
    }

    private static int CalculatePostponedDays(string postponedFromDate, DateTime targetDate)
    {
        return DateTime.TryParseExact(
            postponedFromDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var fromDate)
            ? Math.Max(1, (targetDate.Date - fromDate.Date).Days)
            : 1;
    }

    private static string DateKey(DateTime date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
