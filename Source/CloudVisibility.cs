using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;

namespace Clouds
{
	public enum CloudVisibilityMode
	{
		Automatic,
		Allow,
		Block
	}

	public sealed class CloudVisibilityExtension : DefModExtension
	{
		public CloudVisibilityMode mode = CloudVisibilityMode.Automatic;
	}

	internal enum CloudVisibilityReason
	{
		NoMap,
		MapMetadataUnavailable,
		ExplicitAllow,
		ExplicitBlock,
		VacuumBiome,
		SkyLightingDisabled,
		SpaceLayer,
		UndergroundGenerator,
		PocketMapWithoutSunShadows,
		FullyRoofed,
		Allowed
	}

	internal enum CloudVisibilitySource
	{
		Automatic,
		MapGeneratorExtension,
		BiomeExtension,
		PlanetLayerExtension
	}

	internal readonly struct CloudVisibilityDecision
	{
		internal readonly bool Allowed;
		internal readonly CloudVisibilityReason Reason;
		internal readonly CloudVisibilitySource Source;

		internal CloudVisibilityDecision(
			bool allowed,
			CloudVisibilityReason reason,
			CloudVisibilitySource source = CloudVisibilitySource.Automatic)
		{
			Allowed = allowed;
			Reason = reason;
			Source = source;
		}
	}

	internal readonly struct CloudVisibilityInputs
	{
		internal readonly bool HasMap;
		internal readonly CloudVisibilityMode GeneratorMode;
		internal readonly CloudVisibilityMode BiomeMode;
		internal readonly CloudVisibilityMode PlanetLayerMode;
		internal readonly bool InVacuum;
		internal readonly bool DisableSkyLighting;
		internal readonly bool IsSpaceLayer;
		internal readonly bool IsUndergroundGenerator;
		internal readonly bool IsPocketMap;
		internal readonly bool DisableSunShadows;
		internal readonly bool FullyRoofed;

		internal CloudVisibilityInputs(
			bool hasMap = true,
			CloudVisibilityMode generatorMode = CloudVisibilityMode.Automatic,
			CloudVisibilityMode biomeMode = CloudVisibilityMode.Automatic,
			CloudVisibilityMode planetLayerMode = CloudVisibilityMode.Automatic,
			bool inVacuum = false,
			bool disableSkyLighting = false,
			bool isSpaceLayer = false,
			bool isUndergroundGenerator = false,
			bool isPocketMap = false,
			bool disableSunShadows = false,
			bool fullyRoofed = false)
		{
			HasMap = hasMap;
			GeneratorMode = generatorMode;
			BiomeMode = biomeMode;
			PlanetLayerMode = planetLayerMode;
			InVacuum = inVacuum;
			DisableSkyLighting = disableSkyLighting;
			IsSpaceLayer = isSpaceLayer;
			IsUndergroundGenerator = isUndergroundGenerator;
			IsPocketMap = isPocketMap;
			DisableSunShadows = disableSunShadows;
			FullyRoofed = fullyRoofed;
		}

		internal CloudVisibilityInputs WithFullyRoofed(bool fullyRoofed)
		{
			return new CloudVisibilityInputs(
				HasMap,
				GeneratorMode,
				BiomeMode,
				PlanetLayerMode,
				InVacuum,
				DisableSkyLighting,
				IsSpaceLayer,
				IsUndergroundGenerator,
				IsPocketMap,
				DisableSunShadows,
				fullyRoofed);
		}
	}

	internal readonly struct CloudVisibilityEvaluation
	{
		internal readonly CloudVisibilityDecision Decision;
		internal readonly CloudVisibilityInputs Inputs;
		internal readonly MapGeneratorDef Generator;
		internal readonly BiomeDef Biome;
		internal readonly PlanetLayerDef PlanetLayer;
		internal readonly bool RoofGridScanned;
		internal readonly int RoofCellsChecked;

		internal CloudVisibilityEvaluation(
			CloudVisibilityDecision decision,
			CloudVisibilityInputs inputs,
			MapGeneratorDef generator,
			BiomeDef biome,
			PlanetLayerDef planetLayer,
			bool roofGridScanned,
			int roofCellsChecked)
		{
			Decision = decision;
			Inputs = inputs;
			Generator = generator;
			Biome = biome;
			PlanetLayer = planetLayer;
			RoofGridScanned = roofGridScanned;
			RoofCellsChecked = roofCellsChecked;
		}
	}

	internal static class CloudViewState
	{
		internal static WorldRenderMode CurrentMode
		{
			get
			{
				if (Find.World == null || Find.World.renderer == null)
					return WorldRenderMode.None;

				return WorldRendererUtility.CurrentWorldRenderMode;
			}
		}

		internal static bool IsWorldView => CurrentMode == WorldRenderMode.Planet;
	}

	public static class CloudVisibility
	{
		static readonly Dictionary<Map, CloudVisibilityEvaluation> evaluations = [];

		public static bool IsAllowedOn(Map map)
		{
			return Inspect(map, out _).Decision.Allowed;
		}

		internal static CloudVisibilityEvaluation Inspect(Map map, out bool cacheHit)
		{
			if (map == null)
			{
				cacheHit = false;
				var inputs = new CloudVisibilityInputs(hasMap: false);
				return new CloudVisibilityEvaluation(
					Decide(inputs),
					inputs,
					null,
					null,
					null,
					false,
					0);
			}

			if (evaluations.TryGetValue(map, out var evaluation))
			{
				cacheHit = true;
				return evaluation;
			}

			cacheHit = false;
			evaluation = EvaluateMap(map, out var cacheable);
			if (cacheable)
				evaluations[map] = evaluation;
			return evaluation;
		}

		internal static CloudVisibilityDecision Decide(CloudVisibilityInputs inputs)
		{
			var knownDecision = DecideKnownSignals(inputs);
			if (knownDecision.HasValue)
				return knownDecision.Value;

			if (inputs.FullyRoofed)
				return new CloudVisibilityDecision(false, CloudVisibilityReason.FullyRoofed);

			return new CloudVisibilityDecision(true, CloudVisibilityReason.Allowed);
		}

		internal static int CachedMapCount => evaluations.Count;

		internal static void Forget(Map map)
		{
			if (map != null)
				evaluations.Remove(map);
		}

		internal static void Cleanup()
		{
			evaluations.Clear();
		}

		static CloudVisibilityEvaluation EvaluateMap(Map map, out bool cacheable)
		{
			var generator = map.generatorDef;
			if (TryGetMapMetadata(map, out var biome, out var planetLayer) == false)
			{
				cacheable = false;
				var unavailableInputs = new CloudVisibilityInputs(hasMap: true);
				return new CloudVisibilityEvaluation(
					new CloudVisibilityDecision(false, CloudVisibilityReason.MapMetadataUnavailable),
					unavailableInputs,
					generator,
					null,
					null,
					false,
					0);
			}

			cacheable = true;
			var inputs = new CloudVisibilityInputs(
				generatorMode: ExtensionMode(generator),
				biomeMode: ExtensionMode(biome),
				planetLayerMode: ExtensionMode(planetLayer),
				inVacuum: biome?.inVacuum == true,
				disableSkyLighting: biome?.disableSkyLighting == true,
				isSpaceLayer: planetLayer?.isSpace == true,
				isUndergroundGenerator: generator?.isUnderground == true,
				isPocketMap: map.info.isPocketMap,
				disableSunShadows: map.info?.disableSunShadows == true);

			var knownDecision = DecideKnownSignals(inputs);
			if (knownDecision.HasValue)
			{
				return new CloudVisibilityEvaluation(
					knownDecision.Value,
					inputs,
					generator,
					biome,
					planetLayer,
					false,
					0);
			}

			var fullyRoofed = IsCompletelyRoofed(map, out var roofGridScanned, out var roofCellsChecked);
			inputs = inputs.WithFullyRoofed(fullyRoofed);
			return new CloudVisibilityEvaluation(
				Decide(inputs),
				inputs,
				generator,
				biome,
				planetLayer,
				roofGridScanned,
				roofCellsChecked);
		}

		static CloudVisibilityDecision? DecideKnownSignals(CloudVisibilityInputs inputs)
		{
			if (inputs.HasMap == false)
				return new CloudVisibilityDecision(false, CloudVisibilityReason.NoMap);

			var decision = ExtensionDecision(inputs.GeneratorMode, CloudVisibilitySource.MapGeneratorExtension);
			if (decision.HasValue)
				return decision;

			decision = ExtensionDecision(inputs.BiomeMode, CloudVisibilitySource.BiomeExtension);
			if (decision.HasValue)
				return decision;

			decision = ExtensionDecision(inputs.PlanetLayerMode, CloudVisibilitySource.PlanetLayerExtension);
			if (decision.HasValue)
				return decision;

			if (inputs.InVacuum)
				return new CloudVisibilityDecision(false, CloudVisibilityReason.VacuumBiome);
			if (inputs.DisableSkyLighting)
				return new CloudVisibilityDecision(false, CloudVisibilityReason.SkyLightingDisabled);
			if (inputs.IsSpaceLayer)
				return new CloudVisibilityDecision(false, CloudVisibilityReason.SpaceLayer);
			if (inputs.IsUndergroundGenerator)
				return new CloudVisibilityDecision(false, CloudVisibilityReason.UndergroundGenerator);
			if (inputs.IsPocketMap && inputs.DisableSunShadows)
				return new CloudVisibilityDecision(false, CloudVisibilityReason.PocketMapWithoutSunShadows);

			return null;
		}

		static CloudVisibilityDecision? ExtensionDecision(
			CloudVisibilityMode mode,
			CloudVisibilitySource source)
		{
			switch (mode)
			{
				case CloudVisibilityMode.Allow:
					return new CloudVisibilityDecision(true, CloudVisibilityReason.ExplicitAllow, source);
				case CloudVisibilityMode.Block:
					return new CloudVisibilityDecision(false, CloudVisibilityReason.ExplicitBlock, source);
				default:
					return null;
			}
		}

		static CloudVisibilityMode ExtensionMode(Def def)
		{
			return def?.GetModExtension<CloudVisibilityExtension>()?.mode
				?? CloudVisibilityMode.Automatic;
		}

		static bool TryGetMapMetadata(
			Map map,
			out BiomeDef biome,
			out PlanetLayerDef planetLayer)
		{
			biome = null;
			planetLayer = null;
			if (map.info == null)
				return false;
			if (map.info.isPocketMap == false && Find.WorldGrid == null)
				return false;

			try
			{
				var tile = map.TileInfo;
				if (tile == null)
					return false;

				biome = tile.PrimaryBiome;
				planetLayer = tile.Layer?.Def;
				return biome != null;
			}
			catch (Exception)
			{
				return false;
			}
		}

		static bool IsCompletelyRoofed(
			Map map,
			out bool roofGridScanned,
			out int roofCellsChecked)
		{
			roofGridScanned = false;
			roofCellsChecked = 0;

			var roofGrid = map.roofGrid;
			var cellIndices = map.cellIndices;
			var cellCount = cellIndices.NumGridCells;
			if (roofGrid == null || cellCount <= 0)
				return false;

			roofGridScanned = true;
			for (var index = 0; index < cellCount; index++)
			{
				roofCellsChecked++;
				if (roofGrid.Roofed(index) == false)
					return false;
			}

			return true;
		}
	}
}
