using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using System.Collections;
using System.Collections.Specialized;

namespace LazyKeyVault.Views;

/// <summary>
/// Represents a list item with optional color.
/// </summary>
public record ColoredListItem(string Text, Color? ForegroundColor = null);

/// <summary>
/// A list data source that supports colored items.
/// </summary>
public class ColoredListDataSource : IListDataSource
{
    private readonly List<ColoredListItem> _items = [];
    // Kept in sync with _items and handed out by reference (never copied) from ToList(), so that
    // ListView.KeystrokeNavigator - which captures that reference once, when Source is assigned,
    // and is otherwise never refreshed by the framework - still sees items added afterwards.
    private readonly List<string> _texts = [];
    private readonly Color _defaultForeground = ColorName16.White;
    private readonly Color _background = ColorName16.Black;

    public int Count => _items.Count;
    public int MaxItemLength => _items.Count > 0 ? _items.Max(i => i.Text.GetColumns()) : 0;
    public bool SuspendCollectionChangedEvent { get; set; }
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public bool IsMarked(int item) => false;
    public void SetMark(int item, bool value) { }

    public void Add(string text, Color? foregroundColor = null)
    {
        _items.Add(new ColoredListItem(text, foregroundColor));
        _texts.Add(text);
        if (!SuspendCollectionChangedEvent)
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void Clear()
    {
        _items.Clear();
        _texts.Clear();
        if (!SuspendCollectionChangedEvent)
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void Render(ListView container, bool selected, int item, int col, int line, int width, int start = 0)
    {
        if (item < 0 || item >= _items.Count) return;

        var driver = Application.Driver;
        if (driver == null) return;

        var listItem = _items[item];
        var text = listItem.Text;

        // Set colors based on selection state
        if (selected)
        {
            driver.SetAttribute(new Terminal.Gui.Drawing.Attribute(ColorName16.White, ColorName16.Blue));
        }
        else
        {
            var fg = listItem.ForegroundColor ?? _defaultForeground;
            driver.SetAttribute(new Terminal.Gui.Drawing.Attribute(fg, _background));
        }

        container.Move(col, line);

        // Measure/slice by display column (via GetColumns/ToRunes), not raw UTF-16 char count, so
        // wide or multi-byte characters (e.g. the "⚠" error-message prefix) aren't mis-measured or
        // sliced mid-character for horizontal scrolling - matching how ListWrapper<T> renders.
        if (string.IsNullOrEmpty(text) || start >= text.GetColumns())
        {
            driver.AddStr(new string(' ', width));
            return;
        }

        var runeCount = text.ToRunes().Length;
        var startIndex = Math.Min(start, Math.Max(0, runeCount - 1));
        var visible = text[startIndex..];
        var clipped = TextFormatter.ClipAndJustify(visible, width, Alignment.Start);
        driver.AddStr(clipped);

        var remaining = width - clipped.GetColumns();
        if (remaining > 0)
        {
            driver.AddStr(new string(' ', remaining));
        }
    }

    public IList ToList() => _texts;

    public void Dispose() { }
}
