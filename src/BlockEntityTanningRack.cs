using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TanningRack;

public sealed class BlockEntityTanningRack : BlockEntityDisplay
{
    private sealed class ProcessingLease
    {
        public required string PlayerUid { get; init; }
        public required string OperationId { get; init; }
        public required AssetLocation InputCode { get; init; }
        public required long StartedAtMilliseconds { get; init; }
        public required long ExpiresAtMilliseconds { get; init; }
    }

    private readonly InventoryTanningRack inventory = new();
    private ProcessingLease? activeLease;
    private TanningRackSystem? rackSystem;

    public override InventoryBase Inventory => inventory;
    public override string InventoryClassName => "tanningrack";
    public override string AttributeTransformCode => "onTanningRackTransform";

    private ItemSlot ContentSlot => inventory[0];

    public override void Initialize(ICoreAPI api)
    {
        inventory.DryingSpeedMultiplier = Math.Max(0f, Block.Attributes?["dryingSpeedMultiplier"].AsFloat(4f) ?? 4f);
        rackSystem = api.ModLoader.GetModSystem<TanningRackSystem>();
        base.Initialize(api);
    }

    public bool OnInteractStart(IPlayer byPlayer, BlockSelection blockSel)
    {
        ClearExpiredLease();
        return byPlayer.Entity.Controls.Sneak
            ? BeginProcessing(byPlayer)
            : InteractNormally(byPlayer, blockSel);
    }

    public bool OnInteractStep(float secondsUsed, IPlayer byPlayer, BlockSelection blockSel)
    {
        ClearExpiredLease();

        if (!byPlayer.Entity.Controls.Sneak || ContentSlot.Empty)
        {
            return false;
        }

        TanningRackCraftOperation? operation = FindMatchingOperation(byPlayer);
        if (operation is null)
        {
            return false;
        }

        if (byPlayer is IClientPlayer clientPlayer)
        {
            clientPlayer.TriggerFpAnimation(EnumHandInteract.BlockInteract);
        }

        if (Api.Side == EnumAppSide.Server)
        {
            if (activeLease is null
                || activeLease.PlayerUid != byPlayer.PlayerUID
                || activeLease.OperationId != operation.Id
                || activeLease.InputCode != ContentSlot.Itemstack.Collectible.Code)
            {
                return false;
            }
        }

        return secondsUsed < operation.DurationSeconds;
    }

    public void OnInteractStop(float secondsUsed, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side != EnumAppSide.Server || activeLease?.PlayerUid != byPlayer.PlayerUID)
        {
            return;
        }

        TanningRackCraftOperation? operation = FindMatchingOperation(byPlayer);
        if (operation is null
            || activeLease.OperationId != operation.Id
            || ContentSlot.Itemstack?.Collectible.Code != activeLease.InputCode)
        {
            activeLease = null;
            return;
        }

        double serverSeconds = (Api.World.ElapsedMilliseconds - activeLease.StartedAtMilliseconds) / 1000d;
        if (secondsUsed + 0.1f >= operation.DurationSeconds && serverSeconds + 0.15d >= operation.DurationSeconds)
        {
            CompleteOperation(byPlayer, operation);
        }

        activeLease = null;
    }

    public bool OnInteractCancel(
        float secondsUsed,
        IPlayer byPlayer,
        BlockSelection blockSel,
        EnumItemUseCancelReason cancelReason)
    {
        if (Api.Side == EnumAppSide.Server && activeLease?.PlayerUid == byPlayer.PlayerUID)
        {
            activeLease = null;
        }

        return true;
    }

    private bool InteractNormally(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (activeLease is not null)
        {
            return false;
        }

        ItemSlot handSlot = byPlayer.InventoryManager.ActiveHotbarSlot;

        if (ContentSlot.Empty)
        {
            if (handSlot.Empty || !CanPlace(handSlot.Itemstack))
            {
                return false;
            }

            if (Api.Side == EnumAppSide.Server)
            {
                AssetLocation insertedCode = handSlot.Itemstack.Collectible.Code;
                if (handSlot.TryPutInto(Api.World, ContentSlot, 1) != 1)
                {
                    return false;
                }

                ContentSlot.MarkDirty();
                MarkMeshesDirty();
                MarkDirty(true);
                Api.World.Logger.Audit("{0} put 1x {1} on a tanning rack at {2}.", byPlayer.PlayerName, insertedCode, Pos);
                Api.World.PlaySoundAt(Block.Sounds?.Place ?? GlobalConstants.DefaultBuildSound, byPlayer.Entity, byPlayer);
            }

            return true;
        }

        if (Api.Side == EnumAppSide.Server)
        {
            ItemStack removed = ContentSlot.TakeOut(1);
            ContentSlot.MarkDirty();

            byPlayer.InventoryManager.TryGiveItemstack(removed, false);
            if (removed.StackSize > 0)
            {
                Api.World.SpawnItemEntity(removed, Pos.ToVec3d().Add(0.5, 0.75, 0.5), new Vec3d(0, 0.1, 0));
            }

            activeLease = null;
            MarkMeshesDirty();
            MarkDirty(true);
            Api.World.Logger.Audit("{0} took 1x {1} from a tanning rack at {2}.", byPlayer.PlayerName, removed.Collectible.Code, Pos);
            Api.World.PlaySoundAt(Block.Sounds?.Place ?? GlobalConstants.DefaultBuildSound, byPlayer.Entity, byPlayer);
        }

        return true;
    }

    private bool BeginProcessing(IPlayer byPlayer)
    {
        if (ContentSlot.Empty || activeLease is not null)
        {
            return false;
        }

        TanningRackCraftOperation? operation = FindMatchingOperation(byPlayer);
        if (operation is null)
        {
            return false;
        }

        if (Api.Side == EnumAppSide.Server)
        {
            if (operation.DurationSeconds <= 0)
            {
                CompleteOperation(byPlayer, operation);
            }
            else
            {
                activeLease = new ProcessingLease
                {
                    PlayerUid = byPlayer.PlayerUID,
                    OperationId = operation.Id,
                    InputCode = ContentSlot.Itemstack.Collectible.Code,
                    StartedAtMilliseconds = Api.World.ElapsedMilliseconds,
                    ExpiresAtMilliseconds = Api.World.ElapsedMilliseconds + (long)((operation.DurationSeconds + 5f) * 1000f)
                };
            }
        }

        return true;
    }

    private bool CompleteOperation(IPlayer byPlayer, TanningRackCraftOperation operation)
    {
        ItemSlot toolSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
        ItemStack? input = ContentSlot.Itemstack;

        if (input is null || !operation.MatchesTool(toolSlot))
        {
            return false;
        }

        if (!Api.World.Claims.TryAccess(byPlayer, Pos, EnumBlockAccessFlags.Use)
            || Pos.DistanceTo(byPlayer.Entity.Pos.X, byPlayer.Entity.Pos.Y, byPlayer.Entity.Pos.Z) > 6)
        {
            return false;
        }

        TanningRackCraftOperation? currentMatch = rackSystem?.FindMatchingOperation(input, toolSlot);
        if (currentMatch?.Id != operation.Id)
        {
            return false;
        }

        ItemStack output = operation.Output.Clone();

        if (operation.OutputMode == TanningRackOutputMode.Rack)
        {
            ContentSlot.Itemstack = output;
            ContentSlot.MarkDirty();
        }
        else
        {
            ContentSlot.Itemstack = null;
            ContentSlot.MarkDirty();
            Api.World.SpawnItemEntity(output, Pos.ToVec3d().Add(0.5, 0.75, 0.5), new Vec3d(0, 0.12, 0));
        }

        if (operation.ToolDurabilityCost > 0 && !toolSlot.Empty)
        {
            toolSlot.Itemstack.Collectible.DamageItem(Api.World, byPlayer.Entity, toolSlot, operation.ToolDurabilityCost);
        }

        toolSlot.MarkDirty();
        MarkMeshesDirty();
        MarkDirty(true);
        Api.World.Logger.Audit(
            "{0} completed tanning rack operation {1} on {2} at {3}, producing {4}.",
            byPlayer.PlayerName,
            operation.Id,
            input.Collectible.Code,
            Pos,
            output.Collectible.Code);

        return true;
    }

    private bool CanPlace(ItemStack stack)
    {
        if (!TanningRackSystem.HasRackTransform(stack.Collectible))
        {
            return false;
        }

        if (TanningRackSystem.HasUsableDryTransition(Api, stack))
        {
            return true;
        }

        return rackSystem?.GetOperations(stack).Count > 0;
    }

    private TanningRackCraftOperation? FindMatchingOperation(IPlayer byPlayer)
    {
        return rackSystem?.FindMatchingOperation(ContentSlot.Itemstack, byPlayer.InventoryManager.ActiveHotbarSlot);
    }

    private void ClearExpiredLease()
    {
        if (activeLease is not null && Api.Side == EnumAppSide.Server && Api.World.ElapsedMilliseconds > activeLease.ExpiresAtMilliseconds)
        {
            activeLease = null;
        }
    }

    protected override void OnTick(float dt)
    {
        base.OnTick(dt);
        ClearExpiredLease();
    }

    protected override float[][] genTransformationMatrices()
    {
        return new[]
        {
            new Matrixf()
                .Translate(0.5f, 0f, 0.5f)
                .Translate(-0.5f, 0f, -0.5f)
                .Values
        };
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        activeLease = null;
        RedrawAfterReceivingTreeAttributes(worldForResolving);
    }

    public override void OnBlockRemoved()
    {
        activeLease = null;
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        activeLease = null;
        base.OnBlockUnloaded();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        if (ContentSlot.Empty)
        {
            dsc.AppendLine(Lang.Get("tanningrack:contents-empty"));
            return;
        }

        dsc.AppendLine(ContentSlot.Itemstack.GetName());

        TransitionState? dry = ContentSlot.Itemstack.Collectible
            .UpdateAndGetTransitionState(Api.World, ContentSlot, EnumTransitionType.Dry);

        if (dry is not null)
        {
            dsc.AppendLine(Lang.Get("tanningrack:drying-progress", Math.Round(dry.TransitionLevel * 100f)));
            dsc.AppendLine(Lang.Get("tanningrack:drying-speed", inventory.DryingSpeedMultiplier));
        }
    }
}
