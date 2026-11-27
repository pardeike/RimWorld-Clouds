# Clouds build and installation

Install .NET SDK `10.0.400` and Python 3. The companion restores the published
`RimBridgeServer.Sdk` 2.0.0 package from NuGet.org.

Run local checks and create the complete mod and ZIP:

```sh
env -u RIMWORLD_MOD_DIR ./scripts/build-quiet.sh -c Release
```

The command runs six delivery checks in disposable directories, builds gameplay
and its companion, synchronizes metadata, stages the public mod, and verifies
its ZIP. Success prints exactly `ok`. A failed step stops the sequence and
reports diagnostics with its full log path. Logs are under `artifacts/logs/`.
Cancellation stops the active subprocess group.

Outputs:

- Gameplay: `1.7/Assemblies/Clouds.dll` and `CrossPromotion.dll`.
- Companion: `artifacts/BridgeTools/Clouds/Clouds.BridgeTools.dll`.
- Complete mod: `artifacts/package/Clouds/`.
- Public ZIP: `artifacts/Clouds.zip`.

Both projects target .NET 10 and AnyCPU. `Directory.Build.props` owns the version.
The normal build synchronizes source `About/About.xml` and `About/Manifest.xml`
from `ModVersion`. Staging uses the same version. CrossPromotion `1.1.2` retains
its startup call. Its framework assembly uses an explicit `net472` fallback,
with `NU1701` suppressed for that package alone.

## Frozen payload

The published 1.6 DLL differs from the repository DLL. Staging starts from
`https://github.com/pardeike/RimWorld-Clouds/releases/download/v2.4.0.0/Clouds.zip`.
The expected SHA-256 is:

```text
4408984cdc50f5821e173d8c51b9e2bc81eeeef18759dc1e03a1e46f84c76507
```

The first build downloads the archive to ignored `artifacts/frozen/Clouds.zip`.
Later builds verify and reuse it. A mismatch stops staging. Do not substitute a
rebuilt old DLL or silently update this baseline.

Packaging preserves the published older DLLs and shared resources byte-for-byte.
It updates current metadata and adds an isolated `1.7` load folder. The source
`1.5/`, `1.6/`, `Resources/`, and `Originals/` trees remain frozen.

The three new `1.7/Resources/clouds_{win,linux,mac}` files are unchanged copies of
the existing platform bundles. Waylith can take Vulkan shaders from the adjacent
`clouds_linux` bundle when the native platform bundle lacks them. Keep all three
files together. The existing Unity project remains pinned to its original
editor and is not rebuilt by the normal mod command.

## Authorized paired installation

Set the explicit Mods directory:

```sh
RIMWORLD_MOD_DIR="/path/to/game/Mods" ./scripts/build-quiet.sh -c Release
```

The command refuses installation while RimWorld runs. It never starts or stops
the game. Paired installation requires sibling `Mods/../BridgeTools/`.

The existing physical-path deployment lock encloses the stopped-game check,
companion build, input verification, copy, and output verification. Installation
compares all frozen installed files before mutation. If they differ, it fails.
If they match, it leaves them in place without copying, deleting, or changing
their timestamps. This preserves their inodes and modification times.

Installation replaces the active `1.7/` tree, current metadata, companion bundle,
and ZIP. It verifies the complete installed file set, companion, and ZIP against
the local build. The companion goes to sibling `BridgeTools/Clouds/`. The SDK,
private Harmony, companion, and debug files stay out of the public package.

Pass `-p:BuildBridgeTools=false` for a public package without development tools.
This does not authorize publication. A build and disposable installation do not
prove a clean new game or rendered clouds.
