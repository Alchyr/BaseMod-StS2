using System.Text.RegularExpressions;
using BaseLib.Config;
using BaseLib.Extensions;
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
    private Label? _logLevelLabel;
    private OptionButton? _logLevelDropdown;
    private LineEdit? _filterInput;
    private Button? _regexButton;
    private Button? _inverseButton;

    private string _filterText = "";
    private Regex? _regex;
    private bool _settingChanged;

    private bool _isFollowingLog = true;
    private int _currentFontSize; // Set on load

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
        // Fix hilarious issue of resting causing the log window to fade to gray
        OwnWorld3D = true;

        base._Ready();
        EnsureLogLimit();

        _scrollContainer = GetNode<ScrollContainer>("MainVBox/Scroll");
        _logLabel = GetNode<RichTextLabel>("MainVBox/Scroll/Log");
        _logLevelLabel = GetNode<Label>("MainVBox/TopBarContainer/TopBarHBox/LogLevelLabel");
        _logLevelDropdown = GetNode<OptionButton>("MainVBox/TopBarContainer/TopBarHBox/LogLevelOption");
        _filterInput = GetNode<LineEdit>("MainVBox/TopBarContainer/TopBarHBox/FilterText");
        _regexButton = GetNode<Button>("MainVBox/TopBarContainer/TopBarHBox/RegexButton");
        _inverseButton = GetNode<Button>("MainVBox/TopBarContainer/TopBarHBox/InverseButton");

        _logLabel.AddThemeFontOverride("normal_font", ResourceLoader.Load<Font>("res://fonts/source_code_pro_medium.ttf"));

        foreach (var level in Enum.GetValues<LogLevel>())
        {
            _logLevelDropdown.AddItem(level.ToString());
        }

        _logLevelDropdown.Selected = BaseLibConfig.LastLogLevel;
        _regexButton.ButtonPressed = BaseLibConfig.LogUseRegex;
        _inverseButton.ButtonPressed = BaseLibConfig.LogInvertFilter;
        _filterInput.Text = BaseLibConfig.LogLastFilter;
        _currentFontSize = (int)BaseLibConfig.LogFontSize;

        _filterInput.TextChanged += (_) => { _settingChanged = true; UpdateFilter(); };
        _regexButton.Toggled += (_) => { _settingChanged = true; UpdateFilter(); };
        _inverseButton.Toggled += (_) => { _settingChanged = true; Refresh(); ScrollToBottomAsync(); };
        _logLevelDropdown.ItemSelected += (_) => { _settingChanged = true; Refresh(); ScrollToBottomAsync(); };

        SizeChanged += OnSizeChanged;
        CloseRequested += QueueFree;
        _logLabel.Finished += () => { if (_isFollowingLog) ScrollToBottomAsync(); };

        var scrollbar = _scrollContainer.GetVScrollBar();
        scrollbar.ValueChanged += OnScrollbarValueChanged;

        _isFollowingLog = true;

        SetFontSize(_currentFontSize, false);
        ApplyMinSizeForScale();
        UpdateFilter(); // Also calls Refresh()
    }

    private void ApplyMinSizeForScale()
    {
        float s = ContentScaleFactor > 0f ? ContentScaleFactor : 1f;
        MinSize = new Vector2I((int)(360 * s), (int)(66 * s));
    }

    private void ApplyChromeFontSize(int size)
    {
        _logLevelLabel?.AddThemeFontSizeOverrideAll(size);
        _logLevelDropdown?.AddThemeFontSizeOverrideAll(size);
        _filterInput?.AddThemeFontSizeOverrideAll(size);
        _regexButton?.AddThemeFontSizeOverrideAll(size);
        _inverseButton?.AddThemeFontSizeOverrideAll(size);

        int dim = Mathf.Max(28, (int)(size * 1.25f));
        if (_regexButton is not null)
            _regexButton.CustomMinimumSize = new Vector2(dim, dim);
        if (_inverseButton is not null)
            _inverseButton.CustomMinimumSize = new Vector2(dim, dim);
    }

    private void OnSizeChanged()
    {
        BaseLibConfig.LogLastSizeX = Size.X;
        BaseLibConfig.LogLastSizeY = Size.Y;
        UpdateText();
        ModConfig.SaveDebounced<BaseLibConfig>();
    }

    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what != NotificationWMPositionChanged) return;

        BaseLibConfig.LogLastPosX = Position.X;
        BaseLibConfig.LogLastPosY = Position.Y;
        ModConfig.SaveDebounced<BaseLibConfig>();
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

        // Jump to the end on filter changes. If we ARE following, Refresh does this.
        if (!_isFollowingLog) ScrollToBottomAsync();
    }

    public void Refresh()
    {
        if (!IsNodeReady()) return;
        UpdateText();

        if (!_settingChanged) return;

        _settingChanged = false;
        BaseLibConfig.LastLogLevel = _logLevelDropdown!.Selected;
        BaseLibConfig.LogInvertFilter = _inverseButton!.ButtonPressed;
        BaseLibConfig.LogUseRegex = _regexButton!.ButtonPressed;
        BaseLibConfig.LogLastFilter = _filterText;
        ModConfig.SaveDebounced<BaseLibConfig>();
    }

    private void UpdateText()
    {
        if (!IsNodeReady()) return;
        if (_logLabel is null || _scrollContainer is null || _logLevelDropdown is null) return;

        _isFollowingLog = _isFollowingLog || IsNearBottom();
        _logLabel.Clear();

        var minLevel = (LogLevel)_logLevelDropdown.Selected;

        foreach (var line in _log.Where(MatchesFilter))
        {
            LimitedLog.RenderLine(line, minLevel, _logLabel);
        }

        if (_isFollowingLog) ScrollToBottomAsync();
    }

    private async void ScrollToBottomAsync()
    {
        try
        {
            // If we got here because RichTextLabel.Finished fired, we still need to draw the frame
            // before scroll offsets are valid
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            if (_scrollContainer is null) return;

            var scrollbar = _scrollContainer.GetVScrollBar();
            _scrollContainer.ScrollVertical = (int)scrollbar.MaxValue;
            _isFollowingLog = true;
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private bool MatchesFilter(string line)
    {
        if (string.IsNullOrEmpty(_filterText)) return true;
        var isMatch = _regex?.IsMatch(line) ?? line.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
        return _inverseButton?.ButtonPressed == true ? !isMatch : isMatch;
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

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { CtrlPressed: true } mouseEvent) return;
        if (mouseEvent.ButtonIndex != MouseButton.WheelUp && mouseEvent.ButtonIndex != MouseButton.WheelDown) return;
        if (!mouseEvent.IsReleased()) return; // Don't double-count: pressed, then released
        ChangeFontSize(mouseEvent.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
        GetViewport().SetInputAsHandled();
    }

    private void ChangeFontSize(int deltaPx) =>
        SetFontSize((int)Mathf.Clamp(BaseLibConfig.LogFontSize + deltaPx, 8, 48));

    private void SetFontSize(int newSize, bool save = true)
    {
        _logLabel?.AddThemeFontSizeOverrideAll(newSize);
        ApplyChromeFontSize(newSize);
        _currentFontSize = newSize;
        ScrollToBottomAsync();

        if (!save) return;
        BaseLibConfig.LogFontSize = newSize;
        ModConfig.SaveDebounced<BaseLibConfig>();
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