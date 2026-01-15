using Terminal.Gui;
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
    private readonly Color _defaultForeground = Color.White;
    private readonly Color _background = Color.Black;

    public int Count => _items.Count;
    public int Length => _items.Count > 0 ? _items.Max(i => i.Text.Length) : 0;
    public bool SuspendCollectionChangedEvent { get; set; }
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public bool IsMarked(int item) => false;
    public void SetMark(int item, bool value) { }

    public void Add(string text, Color? foregroundColor = null)
    {
        _items.Add(new ColoredListItem(text, foregroundColor));
        if (!SuspendCollectionChangedEvent)
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void Clear()
    {
        _items.Clear();
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

        // Handle start offset for horizontal scrolling
        if (start > 0 && start < text.Length)
            text = text[start..];
        else if (start >= text.Length)
            text = "";

        // Pad or truncate to width
        if (text.Length > width)
            text = text[..width];
        else
            text = text.PadRight(width);

        // Set colors based on selection state
        if (selected)
        {
            driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
        }
        else
        {
            var fg = listItem.ForegroundColor ?? _defaultForeground;
            driver.SetAttribute(new Terminal.Gui.Attribute(fg, _background));
        }

        // Position at col, line and render
        container.Move(col, line);
        driver.AddStr(text);
    }

    public IList ToList() => _items.Select(i => i.Text).ToList();

    public void Dispose() { }
}
