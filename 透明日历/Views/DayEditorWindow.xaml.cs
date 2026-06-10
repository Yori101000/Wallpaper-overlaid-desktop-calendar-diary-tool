using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using TransparentCalendar.Models;

namespace TransparentCalendar.Views;

public partial class DayEditorWindow : Window
{
    private readonly CalendarEntry _entry;
    private readonly ObservableCollection<TodoItem> _todos;

    public DayEditorWindow(DateTime date, CalendarEntry entry)
    {
        InitializeComponent();
        _entry = entry;
        _todos = new ObservableCollection<TodoItem>(_entry.Todos.Select(todo => new TodoItem
        {
            Text = todo.Text,
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

    private void DeleteTodo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TodoItem item)
        {
            _todos.Remove(item);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _entry.Todos = _todos
            .Where(todo => !string.IsNullOrWhiteSpace(todo.Text))
            .Select(todo => new TodoItem
            {
                Text = todo.Text.Trim(),
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
}
