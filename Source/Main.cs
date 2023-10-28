using Brrainz;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Clouds
{
	public class Clouds_Main : Mod
	{
		public Clouds_Main(ModContentPack content) : base(content)
		{
			var harmony = new Harmony("net.pardeike.clouds");
			harmony.PatchAll();

			CrossPromotion.Install(76561197973010050);
		}
	}

	// remove cloudsystem when map is destroyed
	//
	[HarmonyPatch(typeof(MapDeiniter), "Deinit_NewTemp")]
	public static class MapDeiniter_Deinit_NewTemp_Patch
	{
		public static void Postfix(Map map)
		{
			CloudAssets.RemoveCloudsFor(map);
		}
	}

	// change direction of all cloudsystems
	//
	[HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
	public static class TickManager_DoSingleTick_Patch
	{
		public static void Postfix()
		{
			CloudAssets.ApplyToAll(clouds =>
			{
				var currentAngle = clouds.Angle;

				if (Rand.Chance(0.001f))
					clouds.nextAngle = (clouds.nextAngle + Rand.Range(-10, 10f) + 360) % 360;

				var delta = clouds.nextAngle - currentAngle;
				var absDelta = Math.Abs(delta);
				if (absDelta > 180)
					delta = delta > 0 ? delta - 360 : delta + 360;
				currentAngle += delta * 0.001f;
				currentAngle = (currentAngle + 360) % 360;

				clouds.Angle = currentAngle;
			});
		}
	}

	// match speed of all cloudsystems to game speed
	//
	[HarmonyPatch]
	public static class Current_ProgramState_Patch
	{
		public static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(TickManager), nameof(TickManager.TogglePaused));
			yield return AccessTools.PropertySetter(typeof(TickManager), nameof(TickManager.CurTimeSpeed));
		}

		public static void Postfix(TickManager __instance)
		{
			if (__instance == null || Current.Game == null)
				return;

			CloudAssets.ApplyToAll(clouds =>
			{
				clouds.Pause = __instance.Paused;
				var curWindSpeedFactor = Find.CurrentMap.weatherManager.CurWindSpeedFactor;
				clouds.Speed = clouds.BaseSpeed * curWindSpeedFactor * (int)__instance.curTimeSpeed;
			});
		}
	}

	// match cloudsystem size and amount to weather conditions
	//
	[HarmonyPatch(typeof(Map), nameof(Map.MapPreTick))]
	public static class Map_MapPreTick_Patch
	{
		static float lastMultiplier = -1f;

		public static (float, FloatRange) LerpedValues(float currentMultiplier)
		{
			var emission = GenMath.LerpDoubleClamped(1, 0.5f, 8, 40, currentMultiplier);
			var f = GenMath.LerpDoubleClamped(1, 0.5f, 1f, 2f, currentMultiplier);
			var size = new FloatRange(f, 2 * f);
			return (emission, size);
		}

		public static void Postfix(Map __instance)
		{
			var currentMultiplier = __instance.weatherManager.CurWeatherAccuracyMultiplier;
			if (lastMultiplier != currentMultiplier)
			{
				lastMultiplier = currentMultiplier;
				var values = LerpedValues(currentMultiplier);
				var clouds = CloudAssets.CloudsFor(__instance);
				clouds.Emission = values.Item1;
				clouds.Size = values.Item2;
			}
		}
	}

	// create cloudsystems as soon as we need them
	// update them when switching maps
	// make cloudsystem more transparent when zoomed in
	//
	[HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.Update))]
	public static class CameraDriver_Update_Patch
	{
		public static void Postfix(CameraDriver __instance)
		{
			var currentMap = Find.CurrentMap;
			if (currentMap == null)
				return;

			var clouds = CloudAssets.CloudsFor(currentMap, true);
			clouds.Alpha = GenMath.LerpDoubleClamped(20, 30, 0f, clouds.BaseAlpha, __instance.rootPos.y);
		}
	}
}