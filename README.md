# Tanning Rack

Standalone Vintage Story 1.22.6 code mod implementing a physical, data-driven tanning rack.

The rack stores one compatible item, renders it using a collectible-defined `ModelTransform`, processes normal `Dry` transitions at a rack-specific speed, and supports instant or held tool operations defined by `tanningrack:craft`. Durable changes are performed on the server and synchronized through the BlockEntity inventory.

See [Implementation and testing](docs/IMPLEMENTATION.md), [Development](docs/DEVELOPMENT.md), and the original [Phase 1 feasibility study](docs/FEASIBILITY.md).

Build with:

```powershell
.\scripts\Build.ps1
```

Create a distributable ZIP with:

```powershell
.\scripts\Publish.ps1
```

The resulting archive is written to `artifacts/tanningrack_0.1.0.zip`.
