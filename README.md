# Clouds

Clouds adds particle clouds to RimWorld.

That is the point: simple atmosphere, moving across the map, making the world feel a little less still.

No giant system. No complicated rules. Just clouds.

## RimWorld 1.6 map compatibility

Clouds never renders on the world view. On map views, it makes one cached
map-wide decision from RimWorld's biome, map generator, planet layer, pocket-map
metadata, and initial roof topology. Vanilla roofs still use RimWorld's stencil
mask in the custom GPU shader, so this does not add per-frame cell scanning.

Mods that create a map without exposing one of RimWorld's standard underground,
no-sky, vacuum, space, or fully-roofed signals can opt in explicitly on their
`MapGeneratorDef`, `BiomeDef`, or `PlanetLayerDef`:

```xml
<modExtensions>
  <li Class="Clouds.CloudVisibilityExtension">
    <mode>Block</mode>
  </li>
</modExtensions>
```

The available modes are `Automatic` (the default), `Allow`, and `Block`.
Generator extensions take precedence over biome extensions, which take
precedence over planet-layer extensions. `Allow` can correct an unusual
open-sky map that resembles an interior, but it never enables clouds on the
world view.

----

If you are looking for the Steam Workshop versions:
https://steamcommunity.com/id/brrainz/myworkshopfiles/

For mod support/feedback, visit my Discord:
https://discord.gg/CYnWvrbNhD

Support me with as little as $1:
https://patreon.com/pardeike

ENJOY
/Brrainz
