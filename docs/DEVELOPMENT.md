# Development

## Installed target

This workspace was prepared against the locally installed Vintage Story **1.22.6** assemblies and vanilla assets. The executable/API file metadata is inconsistent (`VintagestoryAPI.dll` reports file version 1.22.0), but the API assembly version and the `game`/`survival` mod versions are 1.22.6. The project therefore targets `net10.0` and declares `game` and `survival` 1.22.6 minimum dependencies.

The default install lookup is `%APPDATA%\Vintagestory`. To use another installation, set `VINTAGE_STORY` to its root before building:

```powershell
$env:VINTAGE_STORY = 'D:\Games\Vintagestory'
.\scripts\Build.ps1
```

For a persistent MSBuild-only override, create the ignored `Directory.Build.props.user` and import it locally, or pass `/p:VintageStoryInstallDir=...` to `dotnet build`. Do not commit an absolute installation path.

## Project layout

- `TanningRack.csproj` — .NET 10 code-mod project and local assembly references.
- `src/` — mod registration, schema validation, block interaction, one-slot inventory, BlockEntity persistence, rendering, and transition integration.
- `assets/tanningrack/` — rack block/shape/recipe, language entries, and proof-of-concept items.
- `docs/FEASIBILITY.md` — API trace and recommended Phase 2 design.
- `scripts/Build.ps1` — compiles the project.
- `scripts/Publish.ps1` — stages only `modinfo.json`, the compiled DLL, and `assets/`, then writes `artifacts/tanningrack_0.1.0.zip`.
- `.research/` — ignored, disposable decompiled-source workspace; never packaged.
- `.runtime-test/` — ignored, disposable dedicated-server smoke-test data; never packaged.

## Local Vintage Story references

Relative to the game install root:

- `VintagestoryAPI.dll` and `VintagestoryAPI.xml` — public API and XML documentation.
- `VintagestoryLib.dll` — engine implementation used to verify synchronization and ticking behavior.
- `Mods/VSEssentials.dll` — `InWorldContainer` and core content systems.
- `Mods/VSSurvivalMod.dll` — survival BlockEntities, display renderers, and interaction examples.
- `assets/game`, `assets/survival`, `assets/creative` — vanilla JSON, shapes, textures, recipes, and patches.

ILSpy CLI is available locally and was used only to inspect the installed assemblies. A repeatable disposable research setup is:

```powershell
New-Item -ItemType Directory -Force .research\api, .research\essential, .research\survival | Out-Null
ilspycmd --disable-updatecheck --nested-directories -p -o .research\api "$env:APPDATA\Vintagestory\VintagestoryAPI.dll"
ilspycmd --disable-updatecheck --nested-directories -p -o .research\essential "$env:APPDATA\Vintagestory\Mods\VSEssentials.dll"
ilspycmd --disable-updatecheck --nested-directories -p -o .research\survival "$env:APPDATA\Vintagestory\Mods\VSSurvivalMod.dll"
```

Decompiled sources are a navigation aid, not redistributable project source. Re-check the installed assemblies after every game update because several recommended extension points are virtual APIs whose behavior can change.

## Build and publish

1. Run `scripts/Build.ps1` (or the default VS Code build task).
2. Run `scripts/Publish.ps1` for a release ZIP.
3. Copy the ZIP from `artifacts/` to the active Vintage Story data directory's `Mods` folder for testing.
4. Keep generated `bin/`, `obj/`, `build/`, `artifacts/`, and `.research/` content out of source control.

The publish script deliberately uses an allow-list. It cannot accidentally include decompiled vanilla code, local paths, editor files, or research notes in the distributable archive.
