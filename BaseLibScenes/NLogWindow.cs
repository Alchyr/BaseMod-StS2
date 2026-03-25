using System.Text.RegularExpressions;
using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace BaseLib.BaseLibScenes;

[GlobalClass]
public partial class NLogWindow : Window
{
    private static readonly LimitedLog _log = new(256);
    private static readonly List<NLogWindow> _listeners = [];

    public static void AddLog(string msg)
    {
        EnsureLogLimit();
        _log.Enqueue(msg);
        foreach (var window in _listeners)
        {
            window.Refresh();
        }
    }

    private ScrollContainer? _scrollContainer;
    private RichTextLabel? _logLabel;
    private OptionButton? _logLevelDropdown;
    private LineEdit? _filterInput;
    private Button? _regexButton;

    private string _filterText = "";
    private Regex? _regex;

    private bool _isFollowingLog = true;

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
        EnsureLogLimit();

        _scrollContainer = GetNode<ScrollContainer>("MainVBox/Scroll");
        _logLabel = GetNode<RichTextLabel>("MainVBox/Scroll/Log");
        _logLevelDropdown = GetNode<OptionButton>("MainVBox/TopBarContainer/TopBarHBox/LogLevelOption");
        _filterInput = GetNode<LineEdit>("MainVBox/TopBarContainer/TopBarHBox/FilterText");
        _regexButton = GetNode<Button>("MainVBox/TopBarContainer/TopBarHBox/RegexButton");

        _logLabel.AddThemeFontOverride("normal_font", ResourceLoader.Load<Font>("res://fonts/source_code_pro_medium.ttf"));

        foreach (var level in Enum.GetValues<LogLevel>())
        {
            _logLevelDropdown.AddItem(level.ToString());
        }

        _logLevelDropdown.Selected = (int)LogLevel.Info;

        _logLevelDropdown.ItemSelected += (_) => Refresh();
        _filterInput.TextChanged += (_) => UpdateFilter();
        _regexButton.Toggled += (_) => UpdateFilter();

        SizeChanged += UpdateText;
        CloseRequested += QueueFree;

        var scrollbar = _scrollContainer.GetVScrollBar();
        scrollbar.ValueChanged += OnScrollbarValueChanged;

        _isFollowingLog = true;
        Refresh();
    }

    private void UpdateFilter()
    {
        _filterText = _filterInput?.Text ?? "";

        if (_regexButton?.ButtonPressed != true || string.IsNullOrEmpty(_filterText))
            _regex = null;
        else
        {
            try
            {
                _regex = new Regex(_filterText, RegexOptions.IgnoreCase);
                _filterInput?.RemoveThemeColorOverride("font_color");
            }
            catch
            {
                _filterInput?.AddThemeColorOverride("font_color", new Color(1, 0.4f, 0.4f));
            }
        }

        Refresh();
    }

    public void Refresh()
    {
        UpdateText();
    }

    private void UpdateText()
    {
        if (_logLabel is null || _scrollContainer is null || _logLevelDropdown is null) return;

        _isFollowingLog = _isFollowingLog || IsNearBottom();
        _logLabel.Clear();

        var minLevel = (LogLevel)_logLevelDropdown.Selected;

        foreach (var line in _log.Where(MatchesFilter))
        {
            LimitedLog.RenderLine(line, minLevel, _logLabel);
        }

        if (_isFollowingLog)
        {
            CallDeferred(nameof(ScrollToBottom));
        }
    }

    private bool MatchesFilter(string line)
    {
        if (string.IsNullOrEmpty(_filterText)) return true;
        return _regex?.IsMatch(line) ?? line.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
    }

    private void ScrollToBottom()
    {
        if (_scrollContainer is null) return;

        _scrollContainer.ScrollVertical = (int)_scrollContainer.GetVScrollBar().MaxValue;
        _isFollowingLog = true;
    }

    private void OnScrollbarValueChanged(double value)
    {
        if (_scrollContainer is null) return;
        
        _isFollowingLog = IsNearBottom(_scrollContainer.GetVScrollBar(), value);
    }

    private bool IsNearBottom()
    {
        if (_scrollContainer is null) return true;

        var scrollbar = _scrollContainer.GetVScrollBar();
        return IsNearBottom(scrollbar, scrollbar.Value);
    }

    private static bool IsNearBottom(VScrollBar scrollbar, double value)
    {
        double bottomValue = scrollbar.MaxValue - scrollbar.Page;
        return bottomValue - value <= 8;
    }

    private static void EnsureLogLimit()
    {
        int configuredLimit = (int)BaseLibConfig.LimitedLogSize;
        if (_log.Limit == configuredLimit) return;

        _log.SetLimit(configuredLimit);
    }

    private class LimitedLog : Queue<string>
    {
        public int Limit { get; private set; }

        private static readonly Color ErrorColor = Color.FromHtml("#ff6d6d");
        private static readonly Color WarnColor = Color.FromHtml("#ffd866");
        private static readonly Color DebugColor = Color.FromHtml("#7fdfff");

        public LimitedLog(int limit) : base(limit)
        {
            Limit = limit;
        }

        public void SetLimit(int limit)
        {
            Limit = limit;
            while (Count > Limit)
            {
                Dequeue();
            }
        }

        public new void Enqueue(string item)
        {
            while (Count >= Limit)
            {
                Dequeue();
            }
            base.Enqueue(item);
        }

        public static void RenderLine(string line, LogLevel minLevel, RichTextLabel? label)
        {
            if (label is null) return;
            if (TryGetBracketLevel(line) < minLevel) return;

            var color = GetColorForLine(line);
            if (color is not null) label.PushColor(color.Value);

            label.AddText(line);
            label.Newline();

            if (color is not null) label.Pop();
        }

        private static LogLevel TryGetBracketLevel(string line)
        {
            if (!line.StartsWith('[')) return LogLevel.Info;

            int closeIndex = line.IndexOf(']');
            if (closeIndex <= 1) return LogLevel.Info;

            var levelStr = line[1..closeIndex];
            return Enum.TryParse<LogLevel>(levelStr, ignoreCase: true, out var level)
                ? level
                : LogLevel.Error; // Default to error to ensure it's shown
        }

        private static Color? GetColorForLine(string line) => TryGetBracketLevel(line) switch
        {
            LogLevel.Error => ErrorColor,
            LogLevel.Warn  => WarnColor,
            LogLevel.Info  => null,
            _              => DebugColor, // VeryDebug, Load, Debug
        };
    }
}