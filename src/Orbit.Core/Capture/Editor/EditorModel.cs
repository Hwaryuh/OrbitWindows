namespace Orbit.Core.Capture.Editor;

/// <summary>A text being typed on the picture: where, and what so far.</summary>
public sealed class TextEdit(PointD at, string text)
{
    public PointD At { get; } = at;
    public string Text { get; set; } = text;
}

/// <summary>
/// The editor's state and rules (Orbit: EditorModel.swift): the marks, the one being drawn, the
/// tool and its style, the crop, selection and undo of 100 steps. Everything is in points, the
/// picture's pixels × 96 / its DPI, as Orbit's points on a Retina capture. Text sizes and pixel
/// colors come from the caller, which owns fonts and the bitmap.
/// </summary>
public sealed class EditorModel(double width, double height, double pixelScale,
    Func<Annotation, (double Width, double Height)> measureText, Func<PointD, uint?> pixelAt)
{
    public const int UndoLimit = 100;

    private readonly List<Annotation> _annotations = [];
    private readonly Stack<State> _undo = new();
    private readonly Stack<State> _redo = new();
    private readonly Dictionary<ToolGroup, EditorTool> _lastTool = [];
    private List<PointD>? _stroke;
    private Annotation? _moveOrigin;
    private bool _moveRecorded;
    // The text being typed was reopened: its undo step is taken, and the commit takes none.
    private bool _textRecorded;
    private bool _adopting;

    private EditorTool _tool = EditorTool.Arrow;
    private uint _color = EditorTools.Palette[0];
    private double _width = 4, _cornerRadius = 3, _fontSize = 24, _strength = 5;
    private bool _filled, _curved;
    private TextWeight _weight = TextWeight.Bold;
    private TextStyle _textStyle = TextStyle.Shadow;
    private Guid? _selectedId;

    private readonly record struct State(Annotation[] Annotations, Area? Crop);

    /// <summary>Anything drawn changed; the canvas redraws.</summary>
    public event Action? Changed;

    /// <summary>The picker copied a color: its hex code.</summary>
    public event Action<string>? ColorPicked;

    public double Width { get; } = width;
    public double Height { get; } = height;
    /// <summary>Picture pixels per point (1.5 for a capture at 150 %).</summary>
    public double PixelScale { get; } = pixelScale;

    public IReadOnlyList<Annotation> Annotations => _annotations;
    public Annotation? Draft { get; private set; }
    public Area? Crop { get; private set; }
    public TextEdit? EditingText { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoDepth => _undo.Count;

    public EditorTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            SelectedId = null;
            CommitText();
            _lastTool[value.Group()] = value;
            Changed?.Invoke();
        }
    }

    /// <summary>The tool a group's button brings back: the one it last had.</summary>
    public EditorTool LastTool(ToolGroup group) => _lastTool.TryGetValue(group, out var tool) ? tool : group.Tools()[0];

    // Style for new marks; changing one also changes the selected mark, as one undo step.
    public uint Color { get => _color; set { _color = value; UpdateSelected(a => a with { Color = value }); } }
    public double StrokeWidth { get => _width; set { _width = value; UpdateSelected(a => a with { Width = value }); } }
    public bool Filled { get => _filled; set { _filled = value; UpdateSelected(a => a with { Filled = value }); } }
    public double CornerRadius { get => _cornerRadius; set { _cornerRadius = value; UpdateSelected(a => a with { CornerRadius = value }); } }
    public bool Curved { get => _curved; set { _curved = value; UpdateSelected(a => a with { Curved = value }); } }
    public double FontSize { get => _fontSize; set { _fontSize = value; UpdateSelected(a => a with { FontSize = value }); } }
    public TextWeight Weight { get => _weight; set { _weight = value; UpdateSelected(a => a with { Weight = value }); } }
    public TextStyle TextStyle { get => _textStyle; set { _textStyle = value; UpdateSelected(a => a with { TextStyle = value }); } }
    public double Strength { get => _strength; set { _strength = value; UpdateSelected(a => a with { Strength = value }); } }

    /// <summary>Selecting a mark shows its style in the toolbar.</summary>
    public Guid? SelectedId
    {
        get => _selectedId;
        set
        {
            _selectedId = value;
            if (Selected is { } a) Adopt(a);
            Changed?.Invoke();
        }
    }

    public Annotation? Selected => _selectedId is { } id ? _annotations.FirstOrDefault(a => a.Id == id) : null;

    /// <summary>The tool whose options the style panel shows: the selected mark's, else the one in hand.</summary>
    public EditorTool StyleTool => Selected?.Tool ?? Tool;

    public Area Bounds(Annotation a) => a.Bounds(a.Tool == EditorTool.Text ? measureText(a) : default);

    // MARK: Undo

    private State Current => new(_annotations.ToArray(), Crop);

    /// <summary>Remembers the state before a change.</summary>
    public void Record()
    {
        _undo.Push(Current);
        if (_undo.Count > UndoLimit)
        {
            // Drop the oldest: rebuild without the bottom entry.
            var kept = _undo.Take(UndoLimit).Reverse().ToArray();
            _undo.Clear();
            foreach (var s in kept) _undo.Push(s);
        }
        _redo.Clear();
    }

    public void Undo()
    {
        if (EditingText is not null)
        {
            // Undo while typing drops the text; a reopened one goes back to how it was.
            bool reopened = _textRecorded;
            EditingText = null;
            _textRecorded = false;
            if (!reopened)
            {
                Changed?.Invoke();
                return;
            }
        }
        if (!_undo.TryPop(out var last)) return;
        _redo.Push(Current);
        Restore(last);
        _selectedId = null;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (EditingText is not null || !_redo.TryPop(out var next)) return;
        _undo.Push(Current);
        Restore(next);
        Changed?.Invoke();
    }

    private void Restore(State s)
    {
        _annotations.Clear();
        _annotations.AddRange(s.Annotations);
        Crop = s.Crop;
    }

    public void DeleteSelected()
    {
        if (_selectedId is not { } id) return;
        Record();
        _annotations.RemoveAll(a => a.Id == id);
        _selectedId = null;
        Changed?.Invoke();
    }

    private void UpdateSelected(Func<Annotation, Annotation> change)
    {
        if (!_adopting && _selectedId is { } id && _annotations.FindIndex(a => a.Id == id) is var i and >= 0)
        {
            var updated = change(_annotations[i]);
            if (updated != _annotations[i])
            {
                Record();
                _annotations[i] = updated;
            }
        }
        Changed?.Invoke();
    }

    private void Adopt(Annotation a)
    {
        _adopting = true;
        try
        {
            Color = a.Color;
            StrokeWidth = a.Width;
            Filled = a.Filled;
            CornerRadius = a.CornerRadius;
            Curved = a.Curved;
            FontSize = a.FontSize;
            Weight = a.Weight;
            TextStyle = a.TextStyle;
            Strength = a.Strength;
        }
        finally
        {
            _adopting = false;
        }
    }

    private Annotation Make(EditorTool tool, IReadOnlyList<PointD> points) => new()
    {
        Tool = tool, Points = points, Color = _color, Width = _width, Filled = _filled, CornerRadius = _cornerRadius, Curved = _curved,
        FontSize = _fontSize, Weight = _weight, TextStyle = _textStyle, Strength = _strength,
    };

    // MARK: Drawing

    /// <summary>A drag from <paramref name="start"/> is at <paramref name="p"/>; Shift constrains it.</summary>
    public void DragChanged(PointD start, PointD p, bool shift)
    {
        switch (Tool)
        {
            case EditorTool.Select:
                if (_moveOrigin is null)
                {
                    if (HitTest(start) is not { } hit)
                    {
                        if (_selectedId is not null) SelectedId = null;
                        return;
                    }
                    SelectedId = hit.Id;
                    _moveOrigin = hit;
                    _moveRecorded = false;
                }
                int i = _annotations.FindIndex(a => a.Id == _moveOrigin.Id);
                if (i < 0 || (p == start && !_moveRecorded)) return;
                // One step for the whole move, and none for a plain click.
                if (!_moveRecorded) Record();
                _moveRecorded = true;
                _annotations[i] = _moveOrigin.Translated(p.X - start.X, p.Y - start.Y);
                break;
            case EditorTool.Pen or EditorTool.Highlight:
                _stroke ??= [start];
                _stroke.Add(p);
                Draft = Make(Tool, _stroke.ToArray());
                break;
            case EditorTool.Text or EditorTool.Counter or EditorTool.Picker:
                return;
            default:
                Draft = Make(Tool, [start, shift ? AnnotationGeometry.Constrained(Tool, start, p) : p]);
                break;
        }
        Changed?.Invoke();
    }

    /// <summary>The drag ended (a click is a drag under 3 points). <paramref name="clickCount"/> 2 is a double-click.</summary>
    public void DragEnded(PointD p, PointD start, int clickCount = 1)
    {
        bool isClick = p.DistanceTo(start) < 3;
        var draft = Draft;
        _moveOrigin = null;
        _stroke = null;
        Draft = null;
        switch (Tool)
        {
            case EditorTool.Select:
                if (!isClick) break;
                var hit = HitTest(p);
                if (hit is { Tool: EditorTool.Text } && clickCount >= 2)
                {
                    Edit(hit);
                    break;
                }
                SelectedId = hit?.Id;
                break;
            case EditorTool.Text:
                CommitText();
                if (HitTest(p) is { Tool: EditorTool.Text } text) Edit(text);
                else
                {
                    EditingText = new TextEdit(p, "");
                    _textRecorded = false;
                }
                break;
            case EditorTool.Counter:
                Record();
                int next = _annotations.Where(a => a.Tool == EditorTool.Counter).Select(a => a.Number).DefaultIfEmpty(0).Max() + 1;
                _annotations.Add(Make(EditorTool.Counter, [p]) with { Number = next });
                break;
            case EditorTool.Picker:
                PickColor(p);
                break;
            case EditorTool.Loupe:
                Record();
                _annotations.Add(draft ?? Make(EditorTool.Loupe, [start, p]));
                break;
            case EditorTool.Crop:
                if (draft is { } d && d.Rect.Width > 8 && d.Rect.Height > 8 && d.Rect.Intersect(new Area(0, 0, Width, Height)) is { } crop)
                {
                    Record();
                    Crop = crop;
                }
                break;
            default:
                if (draft is { } mark && (!isClick || mark.Tool == EditorTool.Pen))
                {
                    if (mark.Tool == EditorTool.Erase) mark = mark with { Fill = SurroundingColor(mark.Rect) };
                    Record();
                    _annotations.Add(mark);
                }
                break;
        }
        Changed?.Invoke();
    }

    /// <summary>Re-opens a placed text for editing, with its own style.</summary>
    private void Edit(Annotation hit)
    {
        Record();
        _annotations.RemoveAll(a => a.Id == hit.Id);
        _selectedId = null;
        _adopting = true;
        try
        {
            Color = hit.Color;
            FontSize = hit.FontSize;
            Weight = hit.Weight;
            TextStyle = hit.TextStyle;
        }
        finally
        {
            _adopting = false;
        }
        EditingText = new TextEdit(hit.Start, hit.Text);
        _textRecorded = true;
    }

    /// <summary>Places the text being typed; an empty one is dropped.</summary>
    public void CommitText()
    {
        if (EditingText is not { } editing) return;
        EditingText = null;
        bool recorded = _textRecorded;
        _textRecorded = false;
        var text = editing.Text.Trim();
        if (text.Length > 0)
        {
            if (!recorded) Record();
            _annotations.Add(Make(EditorTool.Text, [editing.At]) with { Text = text });
        }
        Changed?.Invoke();
    }

    public void ResetCrop()
    {
        if (Crop is null) return;
        Record();
        Crop = null;
        Changed?.Invoke();
    }

    /// <summary>The topmost mark within 8 points of <paramref name="p"/>.</summary>
    public Annotation? HitTest(PointD p) => _annotations.LastOrDefault(a => Bounds(a).Inflate(8).Contains(p.X, p.Y));

    // MARK: Pixels

    private void PickColor(PointD p)
    {
        if (pixelAt(p) is not { } c) return;
        Color = c | 0xFF000000;
        ColorPicked?.Invoke(EditorTools.Hex(c));
    }

    /// <summary>The average color of a thin ring just outside an area: what the eraser fills it with.</summary>
    public uint SurroundingColor(Area r)
    {
        double red = 0, green = 0, blue = 0;
        int n = 0;
        var ring = r.Inflate(3);
        double step = Math.Max(1, Math.Max(ring.Width, ring.Height) / 60);
        void Sample(double x, double y)
        {
            if (pixelAt(new PointD(x, y)) is not { } c) return;
            red += (c >> 16) & 0xFF;
            green += (c >> 8) & 0xFF;
            blue += c & 0xFF;
            n++;
        }
        for (double x = ring.X; x <= ring.Right; x += step)
        {
            Sample(x, ring.Y);
            Sample(x, ring.Bottom);
        }
        for (double y = ring.Y; y <= ring.Bottom; y += step)
        {
            Sample(ring.X, y);
            Sample(ring.Right, y);
        }
        if (n == 0) return 0xFFFFFFFF;
        return 0xFF000000 | (uint)Math.Round(red / n) << 16 | (uint)Math.Round(green / n) << 8 | (uint)Math.Round(blue / n);
    }
}
