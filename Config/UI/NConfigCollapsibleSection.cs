using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace BaseLib.Config.UI;

/// <summary>
/// Represents a full collapsible section: header, content, and visibility logic. Use
/// <see cref="SimpleModConfig.CreateCollapsibleSection"/> to create an instance of this class, then call
/// section.ContentContainer.AddChild() to add your content.
/// </summary>
public partial class NConfigCollapsibleSection : VBoxContainer, ISelectionReticle
{
    /// <summary> Add the section contents here; everything inside will be hidden when the section is collapsed.</summary>
    public VBoxContainer ContentContainer { get; private set; } = null!;
    public NSelectionReticle? Reticle { get; set; }

    private Tween? _focusTween;
    private Tween? _rotationTween;
    private Control? _focusTarget;
    private TextureRect? _arrow;
    private RichTextLabel? _label;

    public event Action<bool>? OnToggled;

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;

            var focusOwner = GetViewport()?.GuiGetFocusOwner();
            var focusInSection = focusOwner != null && ContentContainer.IsAncestorOf(focusOwner);

            _isExpanded = value;
            OnToggled?.Invoke(value);

            if (!_isExpanded && focusInSection)
                _focusTarget?.TryGrabFocus();

            if (_arrow == null) return;

            _rotationTween?.Kill();
            _rotationTween = CreateTween();
            _rotationTween.TweenProperty(_arrow, "rotation_degrees", _isExpanded ? 90f : 0f, 0.16f)
                .SetEase(Tween.EaseType.Out)
                .SetTrans(Tween.TransitionType.Cubic);
        }
    }

    // Use SimpleModConfig.CreateCollapsibleSection instead (or at least this class's .Create inside BaseLib)
    private NConfigCollapsibleSection() { }

    public override void _Ready()
    {
        base._Ready();

        if (_focusTarget == null)
            throw new InvalidOperationException(
                "NConfigSectionHeader inserted into tree without FocusTarget set");

        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;

        ((ISelectionReticle)this).SetupSelectionReticle(_focusTarget);
        Reticle!.OffsetLeft += 4f;

        _focusTarget.FocusEntered += OnFocus;
        _focusTarget.FocusExited += OnUnfocus;
        _focusTarget.MouseEntered += OnHover;
        _focusTarget.MouseExited += OnUnhover;
        _focusTarget.GuiInput += HandleInput;

        _focusTarget.FocusMode = FocusModeEnum.All;
        _focusTarget.MouseFilter = MouseFilterEnum.Stop;
        _focusTarget.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    // [Obsolete] would be nice, but Godot's generated files cause a warning on BaseLib build (or a compile failure with error: true)
    /// <summary>
    /// Don't call AddChild on the section; call .ContentContainer.AddChild instead.
    /// Children added to the section itself will never be hidden.
    /// </summary>
    /// <exception cref="InvalidOperationException">When called.</exception>
    public new void AddChild(Node node, bool forceReadableName = false, InternalMode internalMode = InternalMode.Disabled)
    {
        throw new InvalidOperationException("Don't call NConfigCollapsibleSection.AddChild; use .ContentContainer.AddChild instead, " +
                                            "or the node won't get hidden on collapse.");
    }

    private void HandleInput(InputEvent @event)
    {
        var isMouseClick = @event is InputEventMouseButton { ButtonIndex: MouseButton.Left } && @event.IsReleased();
        var isControllerSelect = @event.IsActionReleased(MegaInput.select);

        if (!isMouseClick && !isControllerSelect) return;
        IsExpanded = !IsExpanded;
        GetViewport().SetInputAsHandled();
    }

    private void OnFocus() { if (NControllerManager.Instance!.IsUsingController) AnimateHeader(isActive: true); }
    private void OnUnfocus() => AnimateHeader(isActive: false);
    private void OnHover() => AnimateHeader(isActive: true);
    private void OnUnhover() => AnimateHeader(isActive: false);

    private void AnimateHeader(bool isActive)
    {
        _focusTween?.Kill();
        if (_arrow == null && _label == null) return;

        var duration = isActive ? 0.05 : 0.5;
        var color = isActive ? StsColors.gold : Colors.White;
        var scale = isActive ? Vector2.One * 1.16f : Vector2.One;

        _focusTween = CreateTween().SetParallel();
        if (!isActive)
            _focusTween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);

        if (_arrow != null)
        {
            _focusTween.TweenProperty(_arrow, "modulate", color, duration);
            _focusTween.TweenProperty(_arrow, "scale", scale, duration);
        }

        if (_label != null)
            _focusTween.TweenProperty(_label, "modulate", color, duration);
    }

    /// <summary>
    /// Prefer using <see cref="SimpleModConfig.CreateCollapsibleSection"/> instead.
    /// </summary>
    internal static NConfigCollapsibleSection Create(string labelName, RichTextLabel label, bool alignToTop = false, bool collapsedByDefault = false)
    {
        var vSizeFlags = alignToTop ? SizeFlags.ShrinkBegin : SizeFlags.ShrinkCenter;
        var id = labelName.Replace(" ", "");

        var section = new NConfigCollapsibleSection
        {
            Name = "CollapsibleSection_" + id
        };

        var headerContainer = new MarginContainer
        {
            Name = "Header_" + id
        };
        headerContainer.AddThemeConstantOverride("margin_top", alignToTop ? 0 : 16);
        headerContainer.AddThemeConstantOverride("margin_bottom", 16);

        var clipper = new Control
        {
            Name = "Clipper_" + id,
            ClipContents = false, // Toggled on collapse
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };

        // The clickable/focusable part of the header (indicator arrow + text)
        var focusTarget = new MarginContainer
        {
            Name = "FocusTarget_" + id,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = vSizeFlags,
        };

        var arrow = new TextureRect
        {
            Name = "Indicator",
            Texture = PreloadManager.Cache.GetTexture2D("BaseLib/images/config/collapse_expand.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = new Vector2(40, 40),
            CustomMinimumSize = new Vector2(40, 40),
            PivotOffset = new Vector2(20, 20),
            RotationDegrees = 90f
        };

        // The hbox overrides rotation, so the arrow can't be a direct child
        var arrowWrapper = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(40, 40),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = vSizeFlags

        };
        arrowWrapper.AddChild(arrow);

        var headerHbox = new HBoxContainer();
        headerHbox.AddThemeConstantOverride("separation", 16);
        headerHbox.AddChild(arrowWrapper);
        headerHbox.AddChild(label);

        focusTarget.AddChild(headerHbox);
        headerContainer.AddChild(focusTarget);

        section._focusTarget = focusTarget;
        section._arrow = arrow;
        section._label = label;

        var contentContainer = new VBoxContainer
        {
            Name = "SectionContent_" + id,
            AnchorRight = 1.0f
        };

        contentContainer.AddThemeConstantOverride("separation", 16);
        section.ContentContainer = contentContainer;

        contentContainer.MinimumSizeChanged += () =>
        {
            if (section.IsExpanded && !clipper.HasMeta("active_tween"))
                clipper.CustomMinimumSize = new Vector2(0, contentContainer.GetCombinedMinimumSize().Y);
        };

        clipper.AddChild(contentContainer);

        // Replaces the content when a section is collapsed, then tweens to 0 height
        var spacer = new Control
        {
            Name = "Spacer_" + id,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Visible = false
        };

        if (collapsedByDefault)
        {
            arrow.RotationDegrees = 0f;
            section._isExpanded = false;
            clipper.Visible = false;
        }

        section.OnToggled += isExpanded =>
        {
            AnimateSectionVisibility(clipper, contentContainer, spacer, isExpanded);
        };

        // Bypass our AddChild replacement
        Node sectionNode = section;
        sectionNode.AddChild(headerContainer);
        sectionNode.AddChild(clipper);
        sectionNode.AddChild(spacer);

        return section;
    }

    // clipper performs the actual clipping (of contextBox) when the size changes.
    // spacer is used when hiding a section: Visible is set to false with no animation, but the spacer is added
    // to take up the same amount of space, which then animates to 0.
    // This way, the user can't enter the collapsing section, which they can if Visible == true. They can enter an
    // expanding section, which should be fine.
    private static void AnimateSectionVisibility(Control clipper, Control contentBox, Control spacer, bool isExpanded)
    {
        const float SizeAnimDuration = 0.22f;
        const float AlphaAnimDuration = 0.3f;

        Tween? tween;
        if (clipper.HasMeta("active_tween"))
        {
            tween = clipper.GetMeta("active_tween").As<Tween?>();
            if (tween != null && tween.IsValid())
                tween.Kill();
        }

        tween = clipper.CreateTween();
        clipper.SetMeta("active_tween", tween);
        tween.Finished += () => clipper.RemoveMeta("active_tween");

        if (isExpanded)
        {
            // Expand this section
            var currentHeight = spacer.Visible ? spacer.CustomMinimumSize.Y : 0f;

            spacer.Visible = false;
            spacer.CustomMinimumSize = Vector2.Zero;
            clipper.CustomMinimumSize = new Vector2(0, currentHeight);
            clipper.ClipContents = true;
            clipper.Visible = true;

            if (currentHeight <= 1f)
                clipper.Modulate = new Color(1, 1, 1, 0);

            var targetHeight = contentBox.GetCombinedMinimumSize().Y;

            tween.TweenProperty(clipper, "custom_minimum_size:y", targetHeight, SizeAnimDuration)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            tween.Parallel().TweenProperty(clipper, "modulate:a", 1f, AlphaAnimDuration);

            tween.Parallel().TweenCallback(Callable.From(() =>
            {
                clipper.CustomMinimumSize = new Vector2(0, contentBox.GetCombinedMinimumSize().Y);
                clipper.ClipContents = false;
            })).SetDelay(SizeAnimDuration);
        }
        else
        {
            // Hide this section: set Visible = false and add a content-sized spacer that tweens to 0 height.
            // This ensures the user can't enter the section while collapsing, which would cause plenty of issues.
            var currentHeight = clipper.Size.Y;

            spacer.CustomMinimumSize = new Vector2(0, currentHeight);
            spacer.Visible = true;
            clipper.Visible = false;

            tween.TweenProperty(spacer, "custom_minimum_size:y", 0f, SizeAnimDuration)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            tween.TweenCallback(Callable.From(() =>
            {
                spacer.Visible = false;
                spacer.CustomMinimumSize = Vector2.Zero;
            }));
        }
    }
}