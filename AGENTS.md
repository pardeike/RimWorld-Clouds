# Project workflow

- This Mac's checkout is authoritative. Preserve unrelated edits.
- Run `env -u RIMWORLD_MOD_DIR ./scripts/build-quiet.sh -c Release` for local checks, the companion, staging, and ZIP.
- For authorized paired installation, set `RIMWORLD_MOD_DIR` to the selected Mods directory and run the same command.
- Read `scripts/README.md` for the published SDK 2.0.0 dependency and frozen-baseline prerequisites.
- Build current code for .NET 10 and AnyCPU into `1.7/`.
- Bump `ModVersion` in `Directory.Build.props` for each port. The normal build must synchronize both About and Manifest versions.
- Never rebuild, rewrite, or delete `1.5/`, `1.6/`, root `Resources/`, or `Originals/` during this port.
- Package older content from the checksum-pinned published archive, not the different repository 1.6 DLL.
- If installed older or shared files differ from that baseline, fail before changing installed files.
- Leave matching installed older files in place, including their inodes and modification times.
- Put current resource changes under `1.7/`. Keep the three bundle copies together so Waylith can find adjacent Linux shaders.
- Do not deploy to the real game or launch RimWorld without explicit authorization. Use disposable Mods directories for installer tests.
