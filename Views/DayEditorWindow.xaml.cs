using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using TransparentCalendar.Models;
using static TransparentCalendar.Models.CalendarQuery;
using static TransparentCalendar.Models.DateKeys;

namespace TransparentCalendar.Views;

/// <summary>待推迟到目标日期的待办。点"保存"时才真正提交。</summary>
public sealed record PendingPostpone(DateTime TargetDate, TodoItem Todo);

public partial class DayEditorWindow : Window
{
    private readonly DateTime _date;
    private readonly CalendarEntry _entry;
    private readonly ObservableCollection<TodoItem> _todos;
    private readonly List<PendingPostpone> _pendingPostpones = [];

    /// <summary>对话框以"保存"关闭后，由调用方提交这些推迟。</summary>
    public IReadOnlyList<PendingPostpone> PendingPostpones => _pendingPostpones;

    public DayEditorWindow(DateTime date, CalendarEntry entry)
    {
        InitializeComponent();
        _date = date;
        _entry = entry;
        _todos = new ObservableCollection<TodoItem>(_entry.Todos.Select(todo => todo.Clone()));

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

    /// <summary>
    /// 只在本窗口内登记，不立即落盘 —— 否则点了"推迟"再点"取消"也无法回滚。
    /// </summary>
    private void PostponeTodo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TodoItem item
            || item.IsDone
            || string.IsNullOrWhiteSpace(item.Text))
        {
            return;
        }

        var targetDate = _date.AddDays(1);
        var postponedFromDate = string.IsNullOrWhiteSpace(item.PostponedFromDate)
            ? DateKey(_date)
            : item.PostponedFromDate;

        var postponedTodo = item.Clone();
        postponedTodo.Text = item.Text.Trim();
        postponedTodo.PostponedFromDate = postponedFromDate;
        postponedTodo.PostponedDays = CalculatePostponedDays(postponedFromDate, targetDate);
        postponedTodo.IsDone = false;

        _pendingPostpones.Add(new PendingPostpone(targetDate, postponedTodo));
        _todos.Remove(item);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _entry.Todos = BuildTodoList();
        _entry.Diary = DiaryText.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingPostpones.Clear();
        DialogResult = false;
    }

    private List<TodoItem> BuildTodoList()
    {
        return _todos
            .Where(todo => !string.IsNullOrWhiteSpace(todo.Text))
            .Select(todo =>
            {
                var copy = todo.Clone();
                copy.Text = copy.Text.Trim();
                return copy;
            })
            .ToList();
    }
}
