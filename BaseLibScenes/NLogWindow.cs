using System.Text;
using Godot;
using Range = System.Range;

namespace BaseLib.BaseLibScenes;

[GlobalClass]
public partial class NLogWindow : Window
{
    private static readonly LimitedLog _log = new(256);
    private static readonly List<NLogWindow> _listeners = [];
    
    public static void AddLog(string msg)
    {
        _log.Enqueue(msg);
        foreach (var window in _listeners)
        {
            window.Refresh();
        }
    }

    private Label? _logLabel;
    private int _scroll = 0;
    
    public override void _EnterTree()
    {
        base._EnterTree();
        _listeners.Add(this);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        _listeners.Remove(this);
    }
    
    public override void _Ready()
    {
        base._Ready();

        _logLabel = GetNode<Label>("Log");

        SizeChanged += UpdateText;
        CloseRequested += QueueFree;
        
        Refresh();
    }

    public override void _Input(InputEvent @event)
    {
        base._Input(@event);
        if (@event is not InputEventMouseButton mouseEvent) return;
        switch (mouseEvent.ButtonIndex)
        {
            case MouseButton.WheelDown:
                --_scroll;
                if (_scroll < 0) _scroll = 0;
                break;
            case MouseButton.WheelUp:
                ++_scroll;
                if (_log.Count == 0) _scroll = 0;
                else if (_scroll >= _log.Count) _scroll = _log.Count - 1;
                break;
        }
    }

    public void Refresh()
    {
        _scroll = 0;
        UpdateText();
    }
    
    public void UpdateText()
    {
        if (_logLabel is null) return;
        
        _logLabel.Text = _log.GetTail(_scroll, 1 + Size.Y / 13);
    }

    private class LimitedLog : Queue<string>
    {
        private int Limit { get; }
        
        public LimitedLog(int limit) : base(limit)
        {
            Limit = limit;
        }

        public new void Enqueue(string item)
        {
            while (Count >= Limit)
            {
                Dequeue();
            }
            base.Enqueue(item);
        }

        public string GetTail(int offset, int lineCount)
        {
            StringBuilder sb = new();
            
            int start = Count - (offset + lineCount);
            while (start < 0 && lineCount > 0)
            {
                sb.Append('\n');
                ++start;
                --lineCount;
            }
            
            if (lineCount <= 0) return sb.ToString();
            
            foreach (var line in this.Take(new Range(start, start + lineCount)))
            {
                sb.Append('\n').Append(line);
            }
            return sb.ToString();
        }
    }
}