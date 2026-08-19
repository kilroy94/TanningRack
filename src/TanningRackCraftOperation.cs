using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace TanningRack;

internal sealed class TanningRackCraftDefinition
{
    public string Id { get; set; } = string.Empty;
    public TanningRackToolDefinition? Tool { get; set; }
    public JsonItemStack? Output { get; set; }
    public float DurationSeconds { get; set; }
    public string OutputMode { get; set; } = string.Empty;
    public int ToolDurabilityCost { get; set; }
}
internal sealed class TanningRackToolDefinition
{
    public string? Category { get; set; }
    public string? Code { get; set; }
    public int MinimumTier { get; set; }
}

public enum TanningRackOutputMode
{
    Rack,
    Eject
}

public sealed class TanningRackCraftOperation
{
    public string Id { get; }
    public EnumTool? ToolCategory { get; }
    public AssetLocation? ToolCode { get; }
    public int MinimumToolTier { get; }
    public ItemStack Output { get; }
    public float DurationSeconds { get; }
    public TanningRackOutputMode OutputMode { get; }
    public int ToolDurabilityCost { get; }

    internal TanningRackCraftOperation(
        string id,
        EnumTool? toolCategory,
        AssetLocation? toolCode,
        int minimumToolTier,
        ItemStack output,
        float durationSeconds,
        TanningRackOutputMode outputMode,
        int toolDurabilityCost)
    {
        Id = id;
        ToolCategory = toolCategory;
        ToolCode = toolCode;
        MinimumToolTier = minimumToolTier;
        Output = output;
        DurationSeconds = durationSeconds;
        OutputMode = outputMode;
        ToolDurabilityCost = toolDurabilityCost;
    }

    public bool MatchesTool(ItemSlot toolSlot)
    {
        ItemStack? stack = toolSlot.Itemstack;
        if (stack?.Collectible is null)
        {
            return false;
        }

        if (ToolCategory.HasValue && stack.Collectible.GetTool(toolSlot) != ToolCategory)
        {
            return false;
        }

        if (ToolCode is not null && !WildcardUtil.Match(ToolCode, stack.Collectible.Code))
        {
            return false;
        }

        return stack.Collectible.GetToolTier(toolSlot) >= MinimumToolTier;
    }
}
