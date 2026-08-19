using Vintagestory.API.Common;

namespace TanningRack;

public sealed class InventoryTanningRack : InventoryGeneric
{
    public float DryingSpeedMultiplier { get; set; } = 4f;

    public InventoryTanningRack()
        : base(1, "tanningrack-0", null)
    {
        this[0].MaxSlotStackSize = 1;
    }

    public override float GetTransitionSpeedMul(EnumTransitionType transType, ItemStack stack)
    {
        return transType == EnumTransitionType.Dry
            ? DryingSpeedMultiplier
            : base.GetTransitionSpeedMul(transType, stack);
    }
}
