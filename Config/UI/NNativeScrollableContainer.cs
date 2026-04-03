using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace BaseLib.Config.UI;

public partial class NNativeScrollableContainer : NScrollableContainer
{
    private Control _clipper;
    private TextureRect _fadeMask;
    private Gradient _maskGradient;
    private Control? _attachedContent;

    private float _topPadding;
    private float _bottomPadding;

    public const float ScrollbarGutterWidth = 60f;
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
        if (_attachedContent != null) _attachedContent.Resized -= OnContentResized;

        _attachedContent = contentPanel;
        _clipper.AddChild(contentPanel);
        SetContent(contentPanel);

        _attachedContent.Resized += OnContentResized;
        OnContainerResized(); // Initial setup
    }

    private void OnContainerResized()
    {
        var actualHeight = Size.Y;
        if (actualHeight <= 0) return;

        const float BottomFade = 70f;
        const float TopFade = 24f;

        _maskGradient.Offsets = [
            0f,
            BottomFade * 0.4f / actualHeight,
            BottomFade / actualHeight,
            FromTop(_topPadding + TopFade),
            FromTop(_topPadding)
        ];

        OnContentResized();
        return;

        float FromTop(float px) => 1f - px / actualHeight;
    }

    private void OnContentResized()
    {
        if (_attachedContent == null) return;

        var needsScroll = _attachedContent.Size.Y > _clipper.Size.Y;
        Scrollbar.Visible = needsScroll;
        _fadeMask.ClipChildren = needsScroll ? ClipChildrenMode.Only : ClipChildrenMode.Disabled;
        _fadeMask.SelfModulate = new Color(1f, 1f, 1f, needsScroll ? 1f : 0f);
    }
}