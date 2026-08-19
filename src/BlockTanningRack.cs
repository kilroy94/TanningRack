using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace TanningRack;

public sealed class BlockTanningRack : Block
{
    private static readonly WorldInteraction PlaceInteraction = new()
    {
        ActionLangCode = "tanningrack:blockhelp-place",
        MouseButton = EnumMouseButton.Right
    };

    private static readonly WorldInteraction TakeInteraction = new()
    {
        ActionLangCode = "tanningrack:blockhelp-take",
        MouseButton = EnumMouseButton.Right
    };

    private static readonly WorldInteraction ProcessInteraction = new()
    {
        ActionLangCode = "tanningrack:blockhelp-process",
        HotKeyCode = "shift",
        MouseButton = EnumMouseButton.Right
    };

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        PlacedPriorityInteract = true;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
        {
            return false;
        }

        return world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityTanningRack rack
            && rack.OnInteractStart(byPlayer, blockSel);
    }

    public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        return world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityTanningRack rack
            && rack.OnInteractStep(secondsUsed, byPlayer, blockSel);
    }

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityTanningRack rack)
        {
            rack.OnInteractStop(secondsUsed, byPlayer, blockSel);
        }
    }

    public override bool OnBlockInteractCancel(
        float secondsUsed,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel,
        EnumItemUseCancelReason cancelReason)
    {
        return world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityTanningRack rack
            || rack.OnInteractCancel(secondsUsed, byPlayer, blockSel, cancelReason);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(
        IWorldAccessor world,
        BlockSelection selection,
        IPlayer forPlayer)
    {
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is not BlockEntityTanningRack rack)
        {
            return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        }

        return rack.Inventory.Empty
            ? new[] { PlaceInteraction }
            : new[] { TakeInteraction, ProcessInteraction };
    }
}
