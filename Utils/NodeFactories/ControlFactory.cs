using Godot;

namespace BaseLib.Utils.NodeFactories;

internal class ControlFactory : NodeFactory<Control>
{
    internal ControlFactory() : base([])
    {
    }

    protected override Control CreateBareFromResource(object resource)
    {
        switch (resource)
        {
            case Texture2D img:
                var imgSize = img.GetSize();

                var visuals = new TextureRect()
                {
                    Name = img.ResourcePath,
                    Size = imgSize,
                    Texture = img,
                    PivotOffset = imgSize / 2,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
                };
                
                return visuals;
        }

        return base.CreateBareFromResource(resource);
    }

    protected override void GenerateNode(Node target, INodeInfo required)
    {
        
    }
}