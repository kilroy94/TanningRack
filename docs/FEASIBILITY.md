# Phase 1 feasibility: Tanning Rack

Research target: locally installed Vintage Story 1.22.6 (`net10.0`). This document records implementation-relevant behavior observed in the installed API, engine, survival assemblies, and vanilla JSON. It is a design reference, not an implementation; Phase 1 contains no functional rack content.

## Conclusion

The proposed system is feasible. Vanilla can provide the one-slot inventory, ItemStack transition state, periodic server-side transition ticking, completed-stack replacement, persistence/synchronization, mesh generation, and JSON `ModelTransform`. Custom code is still required for eligibility checks, rack-specific drying speed, interaction routing, craft-schema validation, timed tool sessions, atomic output/tool updates, and rack placement transforms.

The largest design caveat is content rather than API capability: vanilla 1.22.6 hides do not have a `Dry` transition. Raw and salted hides use `Perish`; oiled hides use `Cure` to become pelts. The installed `Dry` example is raw bow staves. A Phase 2 proof of concept must deliberately patch or add a hide-like stage with a meaningful Dry output, or use a development item. It should not claim that the existing vanilla hide chain already dries.

## Recommended architecture

Use these classes:

- `BlockTanningRack : Block` routes placed-block interactions and provides interaction help.
- `BlockEntityTanningRack : BlockEntityDisplay` owns one slot, rendering, server transaction methods, and ephemeral timed-operation leases.
- `InventoryTanningRack : InventoryGeneric` contains exactly one `ItemSlot` and overrides `GetTransitionSpeedMul` for `EnumTransitionType.Dry`.
- `TanningRackSystem : ModSystem` registers the block and BlockEntity classes and validates/resolves data-driven craft definitions after assets are loaded.

Deriving from survival's `BlockEntityDisplay` intentionally makes `survival` a required dependency. That is appropriate for this mod and avoids reproducing a mature vanilla mesh/container implementation. `BlockEntityDisplay` already derives from `BlockEntityContainer`; the latter owns an `InWorldContainer`, registers a 10-second game tick, serializes its inventory, and drops contents server-side when broken.

The rack should keep only one real `ItemStack` in its inventory. Do not copy transition progress into separate BlockEntity fields. Timed use is a held action, so partial progress should cancel and should not be saved.

## Vanilla transition and drying behavior

### Definitions and stack state

`CollectibleObject.TransitionableProps` is populated by top-level `transitionableProps` or variant-resolved `transitionablePropsByType`. `CollectibleObject.GetTransitionableProperties(world, stack, entity)` is the supported lookup. A Dry check is therefore:

1. call `GetTransitionableProperties` for the actual stack;
2. find a property whose `Type == EnumTransitionType.Dry`;
3. separately require a valid `onTanningRackTransform` collectible attribute.

`TransitionableProperties` contains:

- `Type` (`EnumTransitionType`);
- `FreshHours` and `TransitionHours` (`NatFloat`);
- `TransitionedStack` (`JsonItemStack`);
- `TransitionRatio`.

Vanilla stores live progress under the ItemStack's `transitionstate` tree attribute. `CollectibleObject.UpdateAndGetTransitionStatesNative` creates and updates:

- `createdTotalHours`;
- `lastUpdatedTotalHours`;
- `freshHours[]`;
- `transitionHours[]`;
- `transitionedHours[]`.

Elapsed calendar hours are multiplied by `CollectibleObject.GetTransitionRateMul`. That method delegates to `inSlot.Inventory.GetTransitionSpeedMul(type, stack)` when the slot belongs to an inventory. A multiplier above 1 accelerates progress. There is no separate Dry global modifier.

When a state reaches 100% on the server, `CollectibleObject.OnTransitionNow` clones the resolved transitioned stack, applies `TransitionRatio`, and `SetFrom` replaces the existing stack in the slot. The slot is marked dirty. ItemStack attributes, including transition state, are already covered by normal ItemStack/inventory serialization.

### Does a custom BlockEntity inventory dry?

Yes, if it participates in the vanilla in-world container tick. `BlockEntityContainer.Initialize` initializes `InWorldContainer` and registers `OnTick` every 10 seconds. On the server, `InWorldContainer.OnTick` detects stacks for which `RequiresTransitionableTicking` is true and calls `UpdateAndGetTransitionStates` on each slot. It compares collectible codes before and after the call; when a transition changes the code, it invokes `MarkDirty(true)`, synchronizing and redrawing clients.

This means a loaded tanning rack can use the normal Dry transition without its own timer. Progress catches up from `lastUpdatedTotalHours` when next evaluated, including after chunk reload, but no processing call occurs while the rack is unloaded. The normal calendar delta is applied on the next eligible tick. `InWorldContainer` also delays processing when its room reports unloaded chunks.

### Rack drying multiplier

There is a non-obvious vanilla default: `InWorldContainer.Inventory_OnAcquireTransitionSpeed` returns 0.25 for Dry and Melt (and 1 for most other non-Perish transitions). `InventoryBase.InvokeTransitionSpeedDelegates` multiplies delegate results, so merely assigning a generic inventory multiplier can unintentionally combine with that quarter-speed rule.

The clean implementation is a small `InventoryGeneric` subclass:

- for `Dry`, return the rack's explicit configured multiplier directly;
- for other transition types, call `base.GetTransitionSpeedMul` so normal Perish/room behavior remains intact.

Because `GetTransitionSpeedMul` is virtual, this cleanly reuses the normal transition system while avoiding a brittle compensating `4 * desiredRate` event handler. The rack multiplier should be a block attribute/config value with a documented default (recommended proof-of-concept default: 4.0). Dry completion stays entirely vanilla.

One content validation should be added: a Dry input's `TransitionedStack` should resolve, and its rack-retained output should also define `onTanningRackTransform`. Otherwise completion is technically valid but the new item has no guaranteed rack-specific visual placement.

## Rendering and `onTanningRackTransform`

Reuse `ModelTransform`; do not introduce a new transform DTO. `ModelTransformNoDefaults`/`ModelTransform` already support JSON translation, degree rotation, origin, uniform `scale`, and per-axis `scaleXYZ`:

```json
"attributes": {
  "onTanningRackTransform": {
    "translation": { "x": 0.0, "y": 0.1, "z": 0.0 },
    "rotation": { "x": 90, "y": 0, "z": 0 },
    "origin": { "x": 0.5, "y": 0.5, "z": 0.5 },
    "scale": 1.0
  }
}
```

`BlockEntityDisplay` is the closest vanilla implementation. Override `AttributeTransformCode` to return `"onTanningRackTransform"`. Its `applyDefaultTranforms` reads the collectible attribute with `AsObject<ModelTransform>()`, calls `EnsureDefaultValues`, and applies it to the generated mesh. Its mesh pipeline supports:

- a collectible's `displayedShape` attribute;
- `IContainedMeshSource` for special contained meshes;
- default block meshes;
- tessellated item shapes/textures.

`genTransformationMatrices` should contribute only the rack's slot anchor and block orientation. Per-item translation/rotation/scale belongs in `onTanningRackTransform`. This separation makes transforms fully JSON-authored and compatible with `.tfedit` conventions.

The closest vanilla examples are `BlockEntityAntlerMount` (one-slot `InventoryGeneric`, custom `AttributeTransformCode`, insert/take) and `BlockEntityDisplayCase`/`BlockEntityShelf` (stored-item display and redraw). `BlockEntityDisplay` caches default meshes by collectible code, so a normal transition to a different code selects a different mesh. Insert, remove, craft replacement, and ejection must call `MarkDirty(true)` and invalidate local display matrices/meshes as appropriate. `FromTreeAttributes` on the client should call the same redraw path used by display BlockEntities, covering chunk load and world reload.

## Recommended `tanningrack:craft` schema

Collectible `attributes` are the right location. The namespaced key avoids collisions. Use an array so future inputs can expose more than one tool operation without changing the data shape:

```json
"attributes": {
  "onTanningRackTransform": {
    "translation": { "x": 0, "y": 0.1, "z": 0 },
    "rotation": { "x": 90, "y": 0, "z": 0 },
    "scale": 1
  },
  "tanningrack:craft": [
    {
      "id": "scrape",
      "tool": {
        "category": "Knife",
        "code": "game:knife-*",
        "minimumTier": 0
      },
      "output": {
        "type": "item",
        "code": "tanningrack:scraped-hide-medium",
        "stackSize": 1
      },
      "durationSeconds": 1.5,
      "outputMode": "rack",
      "toolDurabilityCost": 1
    }
  ]
}
```

Exact semantics:

- `id`: required unique diagnostic/interaction identifier within the input collectible.
- `tool.category`: optional case-sensitive `EnumTool` name, checked with `CollectibleObject.GetTool(slot)`.
- `tool.code`: optional full `AssetLocation` wildcard, checked with `WildcardUtil.Match`. At least one of category/code is required; if both are present, both must match.
- `tool.minimumTier`: optional non-negative integer checked with `GetToolTier`.
- `output`: required vanilla `JsonItemStack`; resolve it at asset-finalization time. `type`, `code`, attributes, and `stackSize` retain vanilla meanings.
- `durationSeconds`: optional finite number >= 0; zero means complete instantly during the start interaction.
- `outputMode`: required `rack` or `eject`.
- `toolDurabilityCost`: optional integer >= 0, default 0.

Outputs should be concrete codes. For variants, use normal `attributesByType` to supply a concrete recipe per input variant. Do not place unresolved `*` or `{size}` placeholders in `JsonItemStack.output` unless Phase 2 deliberately adds and documents custom substitution rules.

Reject the entire operation at load time if enums are unknown, output cannot resolve, values are negative/non-finite, output mode is unknown, neither tool constraint exists, or a rack-retained output lacks `onTanningRackTransform`. If several operations match the same held tool, reject the interaction as ambiguous instead of silently choosing array order.

This array is a small recommended change from the illustrative single object. It directly supports future multi-tool/multi-stage content while remaining simple for the proof of concept.

## Interaction design

The placed block should own the interaction lifecycle because the operation is data attached to the rack's input, not behavior that every possible tool should implement.

Normal right click:

- Empty rack: first test for Dry plus a valid transform; otherwise test for at least one valid craft operation plus a valid transform. Transfer exactly one item with `ItemSlot.TryPutInto`.
- Occupied rack: take exactly one, try the player's inventory, then spawn any remainder at the rack.

Crouch + right click:

- Require an occupied rack, `byPlayer.Entity.Controls.Sneak`, a matching operation, a valid held tool, and claim `Use` access.
- Set the block's `PlacedPriorityInteract` so the placed block receives sneak-use before the held item's own interaction.
- Return true from `OnBlockInteractStart` only for a valid operation. For duration zero, complete server-side immediately.
- Continue through `OnBlockInteractStep` while the tool/rack/operation remains valid. Mirror vanilla timed patterns: the client stops when `secondsUsed >= duration`; the server keeps the authoritative session until stop/cancel.
- `OnBlockInteractCancel` clears the session and allows early release to cancel.
- `OnBlockInteractStop` completes only when the required duration was reached and all state still validates.

Relevant API methods are `Block.OnBlockInteractStart/Step/Stop/Cancel`. `BlockGroundStorage` demonstrates forwarding all four methods to a BlockEntity. `BlockBehaviorHarvestable` demonstrates a configurable `EnumTool`, duration threshold, client effects, and server-only completion. `ItemScrapWeaponKit` demonstrates a held-duration conversion and side guards. These are better references than adding behaviors to every tool.

For tool damage call `heldStack.Collectible.DamageItem(world, byPlayer.Entity, activeHotbarSlot, cost)` only after output creation and rack mutation have succeeded. The method updates durability, destroys a tool at zero if requested, and marks the held slot dirty.

## Persistence, synchronization, and multiplayer

`BlockEntityContainer.ToTreeAttributes/FromTreeAttributes` delegates inventory serialization to `InWorldContainer`, which serializes the full ItemStack and its attributes. This preserves transition progress without custom fields. `OnStoreCollectibleMappings` and `OnLoadCollectibleMappings` also handle stored stack ID remapping for schematics/world changes.

All durable mutations must be server-authoritative:

- the client may validate enough to start animation and return the correct interaction result;
- only the server transfers items, replaces the rack stack, spawns outputs, and damages tools;
- after a successful transaction call `MarkDirty(true)` so the full BlockEntity tree is sent and the mesh is redrawn;
- mark the active hotbar slot dirty after any transfer or durability change.

For timed operations, keep an in-memory server lease containing player UID, operation ID, expected input identity, expected tool identity/slot, and server start time. Allow only one lease per rack. Do not persist it: release, disconnect, unload, block break, or server restart cancels an unfinished held action. At completion, re-read and revalidate the rack stack, tool, claim, range/selection as available, duration, and output before making any mutation.

Server world/block updates execute serially, but two players can start against the same visible state. A one-lease rule plus final revalidation prevents double outputs. The completion transaction order should be:

1. resolve/clone the output before changing anything;
2. revalidate rack input, tool, lease, and access;
3. replace or clear the rack stack;
4. spawn ejected output only on the server;
5. apply durability once;
6. mark player slot and BlockEntity dirty/redraw;
7. clear the lease.

If any validation fails, perform none of steps 3-6. Never trust client-reported operation IDs, elapsed time, outputs, or durability costs.

## API limitations and risks

- Vanilla hides currently use Perish/Cure, not Dry. The content pipeline needs an explicit design decision before Phase 2.
- The in-world Dry 0.25 multiplier is implementation behavior in `InWorldContainer`, not obvious from the public transition model. The custom inventory override should have a focused regression test after game updates.
- `BlockEntityDisplay` and `BlockEntityContainer` live in `VSSurvivalMod.dll`, and `InWorldContainer` lives in `VSEssentials.dll`. This is stable vanilla precedent but creates explicit game/survival version coupling.
- `BlockEntityDisplay`'s ordinary mesh cache key is collectible code. Same-code outputs whose visual mesh depends on stack attributes need a custom cache key or `IContainedMeshSource`; different-code stage outputs are straightforward.
- `MarkDirty(false)` synchronizes the BlockEntity but does not request a block redraw. Every visible stack change should use `MarkDirty(true)`.
- Asset attributes are untyped JSON. Phase 2 must parse once, validate loudly, and cache resolved immutable definitions rather than repeatedly accepting malformed values during interaction.
- A rack-retained transition/craft output needs its own transform. Reusing the input transform after the collectible changes would require extra persisted state and is not recommended.
- A stack may have both Dry and craft metadata. Per the proposed design, Dry eligibility has priority on insertion; document this ordering for content authors.

## Vanilla references for Phase 2

Assemblies and installed source locations are listed in `docs/DEVELOPMENT.md`. High-value symbols/files:

- `Vintagestory.API.Common.CollectibleObject`: `GetTransitionableProperties`, `RequiresTransitionableTicking`, `UpdateAndGetTransitionState(s)`, `GetTransitionRateMul`, `SetTransitionState`, `OnTransitionNow`, `DamageItem`.
- `TransitionableProperties`, `TransitionState`, `EnumTransitionType`, `JsonItemStack`, `ModelTransform`, `ModelTransformNoDefaults`, `InventoryBase`, `InventoryGeneric`, `ItemSlot`.
- `Vintagestory.GameContent.InWorldContainer`: periodic transition processing, speed delegate, serialization, chunk modification, and transition sync callback.
- `BlockEntityContainer`: inventory lifecycle, 10-second tick, drops, tree serialization, collectible mappings.
- `BlockEntityDisplay`: mesh generation/cache, `AttributeTransformCode`, transform application, redraw, tessellation.
- `BlockEntityAntlerMount`: one-slot display inventory and single-item put/take.
- `BlockEntityDisplayCase` and `BlockEntityShelf`: displayed inventories, transforms, transition information, redraw after tree updates.
- `BlockGroundStorage`: full block-to-BlockEntity interaction lifecycle forwarding.
- `BlockBehaviorHarvestable`: timed tool-category block use and server-only completion.
- `ItemScrapWeaponKit`: held duration and server-side conversion.
- Vanilla JSON `survival/itemtypes/toolhead/bowstave.json`: actual Dry transition.
- Vanilla JSON `survival/itemtypes/resource/hide.json`: actual raw/salted Perish and oiled-hide Cure transitions.
- Vanilla JSON examples containing `onDisplayTransform`, `onshelfTransform`, and `onAntlerMountTransformByType`: established `ModelTransform` schema.

## Recommended decisions before implementation

1. Confirm what hide stage should genuinely use `Dry` and what its output should be. For the first proof, a namespaced development hide item is safer than altering the balance of the vanilla hide chain; a narrowly scoped vanilla patch is possible if compatibility is preferred.
2. Approve the array form of `tanningrack:craft`, concrete outputs, AND semantics when both tool category and wildcard code are supplied, and rejection of ambiguous matches.
3. Require `onTanningRackTransform` on every rack-resident input and output rather than carrying an old transform across a collectible change.
4. Choose the default rack Dry multiplier; 4.0 is a clear proof-of-concept value and is independent of vanilla's in-world 0.25 factor under the recommended inventory override.
5. Treat timed progress as cancellable and ephemeral. Persisting partial held-use progress would be a different mechanic and adds unnecessary race/desync surface.
