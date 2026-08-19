# Implementation and testing

## Implemented behavior

The proof of concept registers `TanningRackBlock`, `TanningRack` BlockEntity, and `TanningRackSystem` through the code mod.

- Normal right-click on an empty rack inserts exactly one compatible item.
- Normal right-click on an occupied rack returns the item to the player or drops any remainder.
- Crouch + right-click with a matching tool starts a configured operation.
- Releasing a timed operation early cancels it.
- Successful completion replaces the rack item or ejects the configured output and applies tool durability once.
- Only the server moves items, creates outputs, or damages tools.
- One server-side processing lease per rack prevents concurrent completions. Leases are cancelled on unload/removal and expire after interrupted use.
- The one-slot inventory, ItemStack transition state, and completed outputs persist through normal BlockEntity serialization.
- Stored items are rendered by `BlockEntityDisplay` using `onTanningRackTransform` as a vanilla `ModelTransform`.
- `InventoryTanningRack.GetTransitionSpeedMul` returns the rack's configured rate for `Dry`, while all other transitions retain vanilla inventory behavior.

## Data attributes

Every item that can be inserted must define a valid transform:

```json
"onTanningRackTransform": {
  "translation": { "x": 0, "y": 0, "z": 0.485 },
  "rotation": { "x": 90, "y": 0, "z": 0 },
  "origin": { "x": 0.5, "y": 0.5, "z": 0.5 },
  "scale": 0.58
}
```

A Dry-compatible input also needs a normal `transitionableProps` entry of type `Dry`. Its transitioned output must have its own rack transform.

Tool processing uses an operation array:

```json
"tanningrack:craft": [
  {
    "id": "scrape-test-hide",
    "tool": {
      "category": "Knife",
      "code": "game:knife-*",
      "minimumTier": 0
    },
    "output": {
      "type": "item",
      "code": "tanningrack:test-scraped-hide",
      "stackSize": 1
    },
    "durationSeconds": 2.5,
    "outputMode": "rack",
    "toolDurabilityCost": 1
  }
]
```

At least one of `tool.category` or `tool.code` is required; when both are present both must match. Output mode is `rack` or `eject`. Rack outputs must have stack size one and define `onTanningRackTransform`. Definitions are parsed, resolved, and validated during asset finalization. Invalid definitions are logged and disabled.

## Proof-of-concept content

- `tanningrack:test-raw-hide` has a normal 24-hour Dry transition to `tanningrack:test-dried-hide`. The rack block config is 4×, so it requires approximately six in-game hours on the rack versus 24 hours in an ordinary 1× inventory.
- `tanningrack:test-workable-hide` requires a `Knife`, takes 2.5 seconds of held crouch + right-click, costs one durability, and becomes `tanningrack:test-scraped-hide` on the rack.
- The rack is craftable from seven sticks and is also available in creative inventory.

The test items deliberately use the mod namespace rather than patching vanilla hides. Vanilla 1.22.6 raw/salted hides use Perish and oiled hides use Cure, so changing them would alter the established tanning balance.

## Manual test checklist

1. Build and publish with `scripts/Publish.ps1` and place the ZIP in the active `Mods` directory.
2. Start a creative world and obtain `tanningrack:tanningrack`, `tanningrack:test-raw-hide`, and `tanningrack:test-workable-hide` from creative inventory.
3. Place the rack and right-click it with one test raw hide. Confirm one item leaves the hand and appears vertically on the rack.
4. Inspect the block info and advance time. Confirm the progress reports 4× and the item becomes Test Dried Hide while remaining visibly on the rack.
5. Remove the dried hide with normal right-click.
6. Insert Test Workable Hide, hold a `game:knife-*`, then hold crouch + right-click. Release before 2.5 seconds and confirm no output or durability loss.
7. Repeat for at least 2.5 seconds. Confirm the item becomes Test Scraped Hide on the rack and the knife loses exactly one durability.
8. Save/reload with an input and an output on the rack. Confirm contents and visuals persist.
9. On a multiplayer server, have two players attempt the same operation. Confirm only one can own the processing lease and only one output is produced.

## Automated validation performed

- Release and Debug compilation against the installed 1.22.6 assemblies: zero warnings and zero errors.
- Strict JSON parsing for every mod asset.
- Release archive content inspection.
- Headless Vintage Story 1.22.6 dedicated-server smoke test using the packaged ZIP. The server discovered the mod, loaded all block/item/recipe assets, finalized assets, registered `TanningRackSystem`, entered `RunGame`, and reported no mod errors.
