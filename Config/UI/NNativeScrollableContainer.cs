using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace BaseLib.Config.UI;

public partial class NNativeScrollableContainer : NScrollableContainer
{
    private Control _clipper;
    private TextureRect _fadeMask;
    private Gradient _maskGradient;

    private float _topPadding;
    private float _bottomPadding;

    public const float ScrollbarGutterWidth = 60f;
    private const float BottomFade = 70f;
    private const float TopFade = 24f;

    public float AvailableContentWidth => Mathf.Max(0f, Size.X - ScrollbarGutterWidth);

    public NNativeScrollableContainer(float topPadding = 0f, float bottomPadding = 0f)
    {
        Name = "NativeScrollableContainer";
        ClipChildren = ClipChildrenMode.Only;

        _topPadding = topPadding;
        _bottomPadding = bottomPadding;

        SetAnchorsPreset(LayoutPreset.FullRect);

        _maskGradient = new Gradient { Colors = [
            new Color(1f, 1f, 1f, 0f),
            new Color(1f, 1f, 1f, 0.4f),
            new Color(1f, 1f, 1f, 1f),
            new Color(1f, 1f, 1f, 1f),
            new Color(1f, 1f, 1f, 0f),
        ] };

        _fadeMask = new TextureRect
        {
            Name = "Mask",
            ClipChildren = ClipChildrenMode.Only,
            MouseFilter = MouseFilterEnum.Ignore,
            Texture = new GradientTexture2D
            {
                FillFrom = new Vector2(0f, 1f),
                FillTo = Vector2.Zero,
                Gradient = _maskGradient
            },
        };
        _fadeMask.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_fadeMask);

        _clipper = new Control
        {
            Name = "Clipper",
            ClipContents = true,
            OffsetTop = topPadding,
            OffsetBottom = -bottomPadding,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        // Leave some space for the scrollbar
        _clipper.SetAnchorsPreset(LayoutPreset.FullRect, true);
        _clipper.OffsetRight = -ScrollbarGutterWidth;
        _fadeMask.AddChild(_clipper);

        var scrollbar = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/scrollbar")).Instantiate<NScrollbar>();
        scrollbar.Name = "Scrollbar";

        scrollbar.SetAnchorsPreset(LayoutPreset.RightWide);
        scrollbar.OffsetLeft = -48f;
        scrollbar.OffsetRight = 0f;
        scrollbar.OffsetTop = topPadding + 64f;
        scrollbar.OffsetBottom = -bottomPadding - 64f;
        AddChild(scrollbar);

        Resized += OnContainerResized;
    }

    public void AttachContent(Control contentPanel)
    {
        if (_content != null) _content.Resized -= OnContentResized;

        _clipper.AddChild(contentPanel);
        SetContent(contentPanel);
        _content!.Resized += OnContentResized;
        OnContainerResized(); // Initial setup
    }

    private void OnContainerResized()
    {
        var actualHeight = Size.Y;
        if (actualHeight <= 0) return;

        _maskGradient.Offsets = [
            0f,
            BottomFade * 0.4f / actualHeight,
            BottomFade / actualHeight,
            FromTop(_topPadding + TopFade),
            FromTop(_topPadding)
        ];

        UpdateScrollLimitBottomOverride();
        OnContentResized();
        return;

        float FromTop(float px) => 1f - px / actualHeight;
    }

    public void ScrollToFocusedControl(bool skipAnimation)
    {
        if (_content == null || !IsVisibleInTree()) return;

        var focusedControl = GetViewport().GuiGetFocusOwner();
        if (focusedControl == null || focusedControl is NDropdownItem || !_content.IsAncestorOf(focusedControl)) return;

        var unclampedTarget = _content.GlobalPosition.Y - focusedControl.GlobalPosition.Y + ScrollViewportSize * 0.5f;
        _targetDragPosY = Mathf.Clamp(unclampedTarget, Mathf.Min(ScrollLimitBottom, 0f), 0f);

        // base._Process handles the Lerp etc.
        if (!skipAnimation) return;

        // Update position and scrollbar instantly so the _Process Lerp doesn't do anything
        _content.Position = _content.Position with
        {
            Y = _paddingTop + _targetDragPosY
        };

        if (ScrollLimitBottom >= 0.0f) return;
        var scrollFraction = Mathf.Clamp(_targetDragPosY / ScrollLimitBottom, 0.0, 1.0);
        Scrollbar.SetValueWithoutAnimation(scrollFraction * 100.0);
    }

    // Hack to "override" the non-virtual base method
    [HarmonyPatch(typeof(NScrollableContainer), nameof(NScrollableContainer.UpdateScrollLimitBottom))]
    public static class NScrollableContainer_UpdateScrollLimitBottom_Patch
    {
        public static bool Prefix(NScrollableContainer __instance)
        {
            if (__instance is not NNativeScrollableContainer self) return true;

            self.UpdateScrollLimitBottomOverride();
            return false;
        }
    }

    // Note: this is called on ItemRectChanged, which means it is called *on scroll* as well as on resize.
    private void UpdateScrollLimitBottomOverride()
    {
        if (_content == null) return;

        var wasVisible = Scrollbar.Visible;

        // We don't need nor want sub-pixel accuracy
        const float Epsilon = 1f;

        // Scroll fix #1: base game decides scrollbar visibility based solely on contentHeight > ViewportSize; this
        // doesn't take being scrolled down into account, so the scrollbar disappears when scrolled down if the content
        // "fits", despite some being offscreen.
        var contentFits = _content.Size.Y + _paddingTop + _paddingBottom - Epsilon <= ScrollViewportSize;
        var scrollIsAtTop = -_content.Position.Y <= _paddingTop + Epsilon;
        Scrollbar.Visible = !contentFits || !scrollIsAtTop;
        Scrollbar.MouseFilter = Scrollbar.Visible ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;

        // Prevent a jerk when the height changes to require scrolling: base._Process has been suspended and will
        // try to lerp if the target position doesn't match the current, so make it match
        if (!wasVisible && Scrollbar.Visible)
            _targetDragPosY = _content.Position.Y - _paddingTop;

        // Update the fade mask, to not fade the top/bottom if everything fits clearly, or if at the top
        _fadeMask.ClipChildren = Scrollbar.Visible ? ClipChildrenMode.Only : ClipChildrenMode.Disabled;
        _fadeMask.SelfModulate = new Color(1f, 1f, 1f, Scrollbar.Visible ? 1f : 0f);

        if (!Scrollbar.Visible) return;
        var scrollDistanceFromTop = Mathf.Max(0f, _paddingTop - _content.Position.Y);
        var topAlpha = 1f - Mathf.Clamp(scrollDistanceFromTop / TopFade, 0f, 1f);

        var colors = _maskGradient.Colors;
        colors[4] = new Color(1f, 1f, 1f, topAlpha);
        _maskGradient.Colors = colors;
    }

    // UpdateScrollLimitBottomOverride is called on resize by the base game + Harmony patch above, so this only needs to
    // handle the logic that method does not perform.
    private void OnContentResized()
    {
        if (_content == null) return;

        // Scroll fix #2: base game scrollbar value doesn't update when the content changes, so update it now.
        // Fixes jumps when you scroll after content size changes.
        Scrollbar.SetValueNoSignal(Mathf.Clamp((_content.Position.Y - _paddingTop) / ScrollLimitBottom, 0.0f, 1f) * 100.0);
    }
}