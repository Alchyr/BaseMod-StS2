using BaseLib.Extensions;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace BaseLib.Utils.NodeFactories;

internal class NMerchantCharacterFactory : NodeFactory<NMerchantCharacter>
{
    internal NMerchantCharacterFactory() : base([])
    {
        
    }

    protected override NMerchantCharacter CreateBareFromResource(object resource)
    {
        switch (resource)
        {
            case Texture2D img:
                BaseLibMain.Logger.Info("Creating NMerchantCharacterFactory from Texture2D");
                
                var imgSize = img.GetSize();
                var node = new NMerchantCharacter();

                var visuals = new Sprite2D();
                node.AddUnique(visuals, "Visuals");
                visuals.Texture = img;
                visuals.Position = new(0, -imgSize.Y * 0.5f); //Sprite2D position is centered

                return node;
        }
        
        return base.CreateBareFromResource(resource);
    }

    protected override void GenerateNode(Node target, INodeInfo required)
    {
        //No named nodes, nothing to implement.
    }
}