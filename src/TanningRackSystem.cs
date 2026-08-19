using Vintagestory.API.Common;

namespace TanningRack;

public sealed class TanningRackSystem : ModSystem
{
    private readonly Dictionary<CollectibleObject, IReadOnlyList<TanningRackCraftOperation>> operations = new();

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("TanningRackBlock", typeof(BlockTanningRack));
        api.RegisterBlockEntityClass("TanningRack", typeof(BlockEntityTanningRack));
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        operations.Clear();

        foreach (CollectibleObject collectible in api.World.Collectibles)
        {
            ParseOperations(api, collectible);
        }
    }

    public IReadOnlyList<TanningRackCraftOperation> GetOperations(ItemStack? stack)
    {
        if (stack?.Collectible is null)
        {
            return Array.Empty<TanningRackCraftOperation>();
        }

        return operations.TryGetValue(stack.Collectible, out IReadOnlyList<TanningRackCraftOperation>? found)
            ? found
            : Array.Empty<TanningRackCraftOperation>();
    }

    public TanningRackCraftOperation? FindMatchingOperation(ItemStack? input, ItemSlot toolSlot)
    {
        TanningRackCraftOperation? match = null;

        foreach (TanningRackCraftOperation operation in GetOperations(input))
        {
            if (!operation.MatchesTool(toolSlot))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = operation;
        }

        return match;
    }

    public static bool HasRackTransform(CollectibleObject? collectible)
    {
        if (collectible?.Attributes is null)
        {
            return false;
        }

        try
        {
            return collectible.Attributes["onTanningRackTransform"].AsObject<ModelTransform?>(null) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool HasUsableDryTransition(ICoreAPI api, ItemStack? stack)
    {
        if (stack?.Collectible is null || !HasRackTransform(stack.Collectible))
        {
            return false;
        }

        TransitionableProperties? dry = stack.Collectible
            .GetTransitionableProperties(api.World, stack, null)?
            .FirstOrDefault(properties => properties.Type == EnumTransitionType.Dry);

        return dry?.TransitionedStack?.ResolvedItemstack?.Collectible is CollectibleObject output
            && HasRackTransform(output);
    }

    private void ParseOperations(ICoreAPI api, CollectibleObject collectible)
    {
        if (collectible.Attributes is null)
        {
            return;
        }

        var recipeJson = collectible.Attributes["tanningrack:craft"];
        if (!recipeJson.Exists)
        {
            return;
        }

        if (!recipeJson.IsArray())
        {
            api.Logger.Error("[{0}] {1} attribute tanningrack:craft must be an array.", Mod.Info.ModID, collectible.Code);
            return;
        }

        TanningRackCraftDefinition[]? definitions;
        try
        {
            definitions = recipeJson.AsObject<TanningRackCraftDefinition[]?>(null, collectible.Code.Domain);
        }
        catch (Exception exception)
        {
            api.Logger.Error("[{0}] Could not parse tanningrack:craft on {1}: {2}", Mod.Info.ModID, collectible.Code, exception.Message);
            return;
        }

        if (definitions is null || definitions.Length == 0)
        {
            api.Logger.Error("[{0}] {1} has an empty tanningrack:craft array.", Mod.Info.ModID, collectible.Code);
            return;
        }

        var parsed = new List<TanningRackCraftOperation>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (TanningRackCraftDefinition definition in definitions)
        {
            if (!TryCompile(api, collectible, definition, ids, out TanningRackCraftOperation? operation))
            {
                continue;
            }

            parsed.Add(operation!);
        }

        if (parsed.Count > 0)
        {
            operations[collectible] = parsed;
        }
    }

    private bool TryCompile(
        ICoreAPI api,
        CollectibleObject input,
        TanningRackCraftDefinition definition,
        HashSet<string> ids,
        out TanningRackCraftOperation? operation)
    {
        operation = null;
        string context = $"{input.Code} tanningrack:craft";

        if (string.IsNullOrWhiteSpace(definition.Id) || !ids.Add(definition.Id))
        {
            api.Logger.Error("[{0}] {1} has a missing or duplicate operation id.", Mod.Info.ModID, context);
            return false;
        }

        if (definition.Tool is null || (string.IsNullOrWhiteSpace(definition.Tool.Category) && string.IsNullOrWhiteSpace(definition.Tool.Code)))
        {
            api.Logger.Error("[{0}] {1}/{2} must define tool.category and/or tool.code.", Mod.Info.ModID, context, definition.Id);
            return false;
        }

        EnumTool? toolCategory = null;
        if (!string.IsNullOrWhiteSpace(definition.Tool.Category))
        {
            if (!Enum.TryParse(definition.Tool.Category, false, out EnumTool parsedCategory))
            {
                api.Logger.Error("[{0}] {1}/{2} has unknown tool category '{3}'.", Mod.Info.ModID, context, definition.Id, definition.Tool.Category);
                return false;
            }

            toolCategory = parsedCategory;
        }

        AssetLocation? toolCode = null;
        if (!string.IsNullOrWhiteSpace(definition.Tool.Code))
        {
            try
            {
                toolCode = AssetLocation.Create(definition.Tool.Code, input.Code.Domain);
            }
            catch (Exception exception)
            {
                api.Logger.Error("[{0}] {1}/{2} has invalid tool code '{3}': {4}", Mod.Info.ModID, context, definition.Id, definition.Tool.Code, exception.Message);
                return false;
            }
        }

        if (definition.Tool.MinimumTier < 0)
        {
            api.Logger.Error("[{0}] {1}/{2} has a negative minimumTier.", Mod.Info.ModID, context, definition.Id);
            return false;
        }

        if (definition.Output is null || !definition.Output.Resolve(api.World, $"{context}/{definition.Id}", input.Code))
        {
            api.Logger.Error("[{0}] {1}/{2} has an unresolved output.", Mod.Info.ModID, context, definition.Id);
            return false;
        }

        if (!float.IsFinite(definition.DurationSeconds) || definition.DurationSeconds < 0)
        {
            api.Logger.Error("[{0}] {1}/{2} has an invalid durationSeconds.", Mod.Info.ModID, context, definition.Id);
            return false;
        }

        if (definition.ToolDurabilityCost < 0)
        {
            api.Logger.Error("[{0}] {1}/{2} has a negative toolDurabilityCost.", Mod.Info.ModID, context, definition.Id);
            return false;
        }

        if (!Enum.TryParse(definition.OutputMode, true, out TanningRackOutputMode outputMode))
        {
            api.Logger.Error("[{0}] {1}/{2} has unknown outputMode '{3}'.", Mod.Info.ModID, context, definition.Id, definition.OutputMode);
            return false;
        }

        ItemStack resolvedOutput = definition.Output.ResolvedItemstack!;
        if (outputMode == TanningRackOutputMode.Rack)
        {
            if (resolvedOutput.StackSize != 1)
            {
                api.Logger.Error("[{0}] {1}/{2} rack output must have stackSize 1.", Mod.Info.ModID, context, definition.Id);
                return false;
            }

            if (!HasRackTransform(resolvedOutput.Collectible))
            {
                api.Logger.Error("[{0}] {1}/{2} rack output {3} has no onTanningRackTransform.", Mod.Info.ModID, context, definition.Id, resolvedOutput.Collectible.Code);
                return false;
            }
        }

        operation = new TanningRackCraftOperation(
            definition.Id,
            toolCategory,
            toolCode,
            definition.Tool.MinimumTier,
            resolvedOutput.Clone(),
            definition.DurationSeconds,
            outputMode,
            definition.ToolDurabilityCost);

        return true;
    }
}
