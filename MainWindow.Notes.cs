using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using TransparentCalendar.Models;
using TransparentCalendar.Native;
using TransparentCalendar.Services;
using TransparentCalendar.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfPoint = System.Windows.Point;
using WpfTypography = System.Windows.Documents.Typography;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;
using static TransparentCalendar.Models.CalendarQuery;
using static TransparentCalendar.Models.DateKeys;

namespace TransparentCalendar;

// 网页笔记视图：接收扩展推送、渲染卡片、编辑器 CRUD。
public partial class MainWindow : Window
{
    private void HandleNoteReceived()
    {
        Dispatcher.Invoke(() =>
        {
            _notes = _storage.LoadWebNotes();
            if (_mode == ViewMode.Note)
            {
                RenderWebNotes();
            }
        });
    }

    /// <summary>补齐历史数据里缺失的笔记 Id —— 编辑与删除都依赖它定位目标。</summary>
    private List<WebNoteGroup> LoadNotesWithIds()
    {
        var notes = _storage.LoadWebNotes();
        if (notes.All(note => !string.IsNullOrWhiteSpace(note.Id)))
        {
            return notes;
        }

        return _storage.UpdateWebNotes(list =>
        {
            foreach (var note in list.Where(note => string.IsNullOrWhiteSpace(note.Id)))
            {
                note.Id = Guid.NewGuid().ToString();
            }
        });
    }

    private void RenderWebNotes()
    {
        WebNoteListPanel.Children.Clear();

        if (_noteListener is { IsRunning: true })
        {
            var port = _noteListener.Port;
            NoteListenerStatus.Text = $"监听中 · 端口 {port}";
            NoteListenerStatus.Foreground = TodoBadgeBrush;
            BookmarkletText.Text =
                $"javascript:(function(){{var t=window.getSelection()?.toString()||'';var u=location.href;var n=document.title;" +
                $"var x=new XMLHttpRequest();x.open('POST','http://localhost:{port}/save',true);" +
                $"x.setRequestHeader('Content-Type','application/json');" +
                $"x.send(JSON.stringify({{url:u,title:n,text:t}}));}})();";
        }
        else
        {
            NoteListenerStatus.Text = "未启动 · 端口被占用，浏览器保存功能不可用";
            NoteListenerStatus.Foreground = ImportantMarkerBrush;
            BookmarkletText.Text = "（监听未启动，无法生成书签代码）";
        }

        var visibleNotes = _notes
            .Where(NoteMatchesSearch)
            .OrderByDescending(note => note.UpdatedAt)
            .ToList();

        if (visibleNotes.Count == 0)
        {
            WebNoteListPanel.Children.Add(new TextBlock
            {
                Text = _searchText.Length > 0
                    ? "没有匹配的笔记。"
                    : "暂无笔记，点击右侧 + 添加 添加网页笔记。",
                Foreground = TextBrush(_settings.TextOpacity * 0.55),
                FontSize = ScaledFont(FontScale.Hint, 13),
                Margin = new Thickness(0, 20, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var note in visibleNotes)
        {
            WebNoteListPanel.Children.Add(CreateNoteCard(note));
        }
    }

    private bool NoteMatchesSearch(WebNoteGroup note)
    {
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return true;
        }

        return note.Title.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase)
            || note.Url.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase)
            || note.Notes.Any(text => text.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase));
    }

    private Border CreateNoteCard(WebNoteGroup note)
    {
        var innerStack = new StackPanel();

        var titleBtn = new WpfButton
        {
            Content = note.Title,
            Tag = note,
            HorizontalContentAlignment = WpfHorizontalAlignment.Left,
            Foreground = TextBrush(_settings.TextOpacity),
            FontWeight = FontWeights.SemiBold,
            FontSize = ScaledFont(FontScale.CardTitle, 14),
            Cursor = WpfCursors.Hand,
            Background = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };
        titleBtn.Click += NoteTitle_Click;
        innerStack.Children.Add(titleBtn);

        if (!string.IsNullOrWhiteSpace(note.Url))
        {
            innerStack.Children.Add(new TextBlock
            {
                Text = note.Url,
                Foreground = TextBrush(_settings.TextOpacity * 0.55),
                FontSize = ScaledFont(FontScale.Footnote, 11),
                Margin = new Thickness(0, 2, 0, 4),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        if (note.Notes.Count > 0)
        {
            var preview = string.Join(" ", note.Notes);
            if (preview.Length > 80)
            {
                preview = preview[..80] + "...";
            }

            innerStack.Children.Add(new TextBlock
            {
                Text = note.Notes.Count > 1 ? $"（{note.Notes.Count} 条）{preview}" : preview,
                Foreground = TextBrush(_settings.TextOpacity * 0.72),
                FontSize = ScaledFont(FontScale.Detail),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
        }

        var actionBar = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };

        var editBtn = new WpfButton
        {
            Content = "编辑",
            Tag = note,
            MinWidth = 40,
            Height = 24,
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 12,
            Cursor = WpfCursors.Hand,
            Background = ActionButtonBrush,
            BorderThickness = new Thickness(1),
            BorderBrush = ActionButtonBorderBrush,
            Padding = new Thickness(4, 0, 4, 0)
        };
        editBtn.Click += EditNote_Click;
        actionBar.Children.Add(editBtn);

        var delBtn = new WpfButton
        {
            Content = "删除",
            Tag = note,
            MinWidth = 40,
            Height = 24,
            FontSize = 12,
            Cursor = WpfCursors.Hand,
            Background = DeleteButtonBrush,
            BorderThickness = new Thickness(1),
            BorderBrush = DeleteButtonBorderBrush,
            Padding = new Thickness(4, 0, 4, 0)
        };
        delBtn.Click += DeleteNote_Click;
        actionBar.Children.Add(delBtn);

        innerStack.Children.Add(actionBar);

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            Background = ListItemBrush,
            BorderBrush = NoteBorderBrush,
            BorderThickness = new Thickness(1),
            Child = innerStack
        };
    }

    private void NoteTitle_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: WebNoteGroup note })
        {
            return;
        }

        // 走的是 ShellExecute，必须先确认是 http/https，否则等于把任意协议交给系统执行。
        if (!WebUrl.TryValidate(note.Url, out var url))
        {
            WpfMessageBox.Show(this, "该笔记的网址无效或协议不受支持（仅支持 http/https）。", "透明日历");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"打开链接失败：{url}", ex);
            WpfMessageBox.Show(this, $"打开链接失败：{ex.Message}", "透明日历");
        }
    }

    private void AddNote_Click(object? sender, RoutedEventArgs e)
    {
        _editingNoteId = null;
        NoteTitleInput.Text = string.Empty;
        NoteUrlInput.Text = string.Empty;
        NoteContentInput.Text = string.Empty;
        NoteEditorPanel.Visibility = Visibility.Visible;
        NoteTitleInput.Focus();
    }

    private void EditNote_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: WebNoteGroup group })
        {
            return;
        }

        // 记录 Id 而非对象引用：浏览器扩展随时可能推送新笔记并整体换掉 _notes。
        _editingNoteId = group.Id;
        NoteTitleInput.Text = group.Title;
        NoteUrlInput.Text = group.Url;
        NoteContentInput.Text = string.Join("\n", group.Notes);
        NoteEditorPanel.Visibility = Visibility.Visible;
        NoteTitleInput.Focus();
    }

    private void NoteEditorSave_Click(object? sender, RoutedEventArgs e)
    {
        var title = (NoteTitleInput.Text ?? string.Empty).Trim();
        var rawUrl = (NoteUrlInput.Text ?? string.Empty).Trim();

        if (!WebUrl.TryValidate(rawUrl, out var url))
        {
            WpfMessageBox.Show(this, "请输入有效的 http/https 网址。", "提示");
            return;
        }

        // 一行一条，与读取时的 string.Join("\n", ...) 对称，避免把多条摘录压成一条。
        var lines = (NoteContentInput.Text ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var editingId = _editingNoteId;
        var resolvedTitle = string.IsNullOrWhiteSpace(title) ? WebUrl.ExtractDomain(url) : title;

        _notes = _storage.UpdateWebNotes(notes =>
        {
            var target = editingId is null ? null : notes.Find(note => note.Id == editingId);
            if (target is not null)
            {
                target.Title = resolvedTitle;
                target.Url = url;
                target.Notes = lines;
                target.UpdatedAt = DateTime.Now;
                return;
            }

            notes.Add(new WebNoteGroup
            {
                Id = Guid.NewGuid().ToString(),
                Title = resolvedTitle,
                Url = url,
                Notes = lines,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
        });

        _editingNoteId = null;
        NoteEditorPanel.Visibility = Visibility.Collapsed;
        RenderWebNotes();
    }

    private void NoteEditorCancel_Click(object? sender, RoutedEventArgs e)
    {
        _editingNoteId = null;
        NoteEditorPanel.Visibility = Visibility.Collapsed;
    }

    private void DeleteNote_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: WebNoteGroup group })
        {
            return;
        }

        var noteCount = group.Notes.Count;
        var detail = noteCount > 0 ? $"（含 {noteCount} 条摘录）" : string.Empty;
        var confirmed = WpfMessageBox.Show(
            this,
            $"确定删除笔记「{group.Title}」{detail}吗？此操作无法撤销。",
            "透明日历",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (confirmed != MessageBoxResult.OK)
        {
            return;
        }

        var id = group.Id;
        _notes = _storage.UpdateWebNotes(notes => notes.RemoveAll(note => note.Id == id));

        if (string.Equals(_editingNoteId, id, StringComparison.Ordinal))
        {
            _editingNoteId = null;
            NoteEditorPanel.Visibility = Visibility.Collapsed;
        }

        RenderWebNotes();
    }
}
