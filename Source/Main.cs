using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Clouds
{
	public class Clouds_Main : Mod
	{
		public Clouds_Main(ModContentPack content) : base(content)
		{
			var harmony = new Harmony("net.pardeike.clouds");
			harmony.PatchAll();
		}
	}

	[HarmonyPatch(typeof(Map), nameof(Map.FinalizeInit))]
	public static class Map_FinalizeInit_Patch
	{
		public static void Postfix(Map __instance)
		{
			var clouds = CloudAssets.CreateClouds();
			CloudAssets.Speed = 0.5f;

			var alt = AltitudeLayer.MetaOverlays.AltitudeFor();
			clouds.transform.position = new Vector3(__instance.Size.x / 2f, alt, __instance.Size.z / 2f);
			var max = Math.Max(__instance.Size.x, __instance.Size.z);
			clouds.transform.localScale = Vector3.one * max / 25f;
		}
	}

	[HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
	public static class TickManager_DoSingleTick_Patch
	{
		static float nextAngle = -90f;

		public static void Postfix()
		{
			if (Rand.Chance(0.001f))
				nextAngle = (nextAngle + Rand.Range(-10, 10f) + 360) % 360;

			var currentAngle = CloudAssets.Angle;

			var delta = nextAngle - currentAngle;
			var absDelta = Math.Abs(delta);
			if (absDelta > 180)
				delta = delta > 0 ? delta - 360 : delta + 360;
			currentAngle += delta * 0.001f;
			currentAngle = (currentAngle + 360) % 360;

			CloudAssets.Angle = currentAngle;
		}
	}

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
			if (__instance != null && Current.Game != null)
			{
				CloudAssets.Pause = __instance.Paused;
				CloudAssets.Speed = CloudAssets.BaseSpeed * __instance.TickRateMultiplier;
			}
		}
	}
}