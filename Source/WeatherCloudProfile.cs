using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Clouds
{
	internal readonly struct WeatherCloudProfile
	{
		public readonly float Obscurity;
		public readonly float Rain;
		public readonly float Snow;
		public readonly float Sand;
		public readonly float Precipitation;
		public readonly float Overcast;
		public readonly float Storm;
		public readonly float Cover;
		public readonly float EmissionRate;
		public readonly float SizeFactor;
		public readonly float Brightness;
		public readonly float Contrast;
		public readonly float Opacity;
		public readonly float EdgePower;
		public readonly Color Tint;
		public readonly float ClusterCutoff;
		public readonly float ClusterFeather;

		readonly Color hue;

		WeatherCloudProfile(
			float obscurity,
			float rain,
			float snow,
			float sand,
			float overcast,
			float storm,
			Color hue)
		{
			Obscurity = Mathf.Clamp01(obscurity);
			Rain = Mathf.Clamp01(rain);
			Snow = Mathf.Clamp01(snow);
			Sand = Mathf.Clamp01(sand);
			Precipitation = Mathf.Max(Rain, Mathf.Max(Snow, Sand));
			Overcast = Mathf.Clamp01(overcast);
			Storm = Mathf.Clamp01(storm);
			this.hue = hue;

			var baseCover = Mathf.Max(Obscurity, Mathf.Max(Precipitation, Overcast));
			var stormCover = Mathf.Clamp01(
				Mathf.Max(baseCover, 0.65f * Storm)
					+ 0.20f * Rain * Storm
					+ 0.10f * Overcast * Storm);
			var snowCover = Snow <= 0.001f
				? 0f
				: Mathf.Clamp01(0.55f + 0.65f * Snow);
			Cover = Mathf.Max(stormCover, snowCover);

			var emissionRate = Mathf.Lerp(6f, 22f, Cover);
			emissionRate = Mathf.Max(
				emissionRate,
				Mathf.Lerp(6f, 50f, Mathf.Clamp01(2f * Rain)));
			emissionRate = Mathf.Max(
				emissionRate,
				Mathf.Lerp(6f, 32f, Mathf.Clamp01(Snow / 0.6f)));
			var stormEmissionRate = Mathf.Lerp(
				18f,
				50f,
				Mathf.Clamp01(2f * Rain));
			EmissionRate = Mathf.Lerp(emissionRate, stormEmissionRate, Storm);

			var sizeFactor = Mathf.Clamp(
				1f + 0.30f * Cover + 0.04f * Rain + 0.14f * Snow + 0.08f * Storm,
				1f,
				1.45f);
			SizeFactor = Mathf.Lerp(sizeFactor, 1f, Storm);
			Contrast = Mathf.Clamp(
				1f + 0.04f * Cover + 0.18f * Storm - 0.03f * Snow - 0.02f * Rain,
				1f,
				1.25f);
			var opacity = Mathf.Clamp(
				1f - 0.25f * Rain - 0.10f * Snow - 0.12f * Obscurity,
				0.72f,
				1f);
			Opacity = Mathf.Lerp(opacity, 2f, Storm);
			var edgePower = Mathf.Clamp(
				1f - 0.28f * Rain - 0.35f * Snow - 0.30f * Obscurity,
				0.65f,
				1.55f);
			EdgePower = Mathf.Lerp(edgePower, 1f, Storm);

			var tint = hue;
			if (Rain > 0.001f)
			{
				tint = Color.Lerp(
					tint,
					new Color(0.66f, 0.77f, 0.89f, 1f),
					Mathf.Clamp01(0.35f + 0.80f * Rain));
			}
			if (Snow > 0.001f)
			{
				tint = Color.Lerp(
					tint,
					new Color(0.92f, 0.97f, 1f, 1f),
					Mathf.Clamp01(0.35f + 0.80f * Snow));
			}
			if (Sand > 0.001f)
			{
				tint = Color.Lerp(
					tint,
					new Color(0.86f, 0.76f, 0.61f, 1f),
					Mathf.Clamp01(0.30f + 0.75f * Sand));
			}
			if (Obscurity > 0.001f && Precipitation <= 0.001f)
			{
				tint = Color.Lerp(
					tint,
					new Color(0.78f, 0.82f, 0.86f, 1f),
					0.35f * Obscurity);
			}
			if (Storm > 0.001f)
			{
				tint = Color.Lerp(
					tint,
					new Color(0.32f, 0.36f, 0.42f, 1f),
					0.92f * Storm);
			}

			Tint = tint;
			Brightness = Luminance(tint);
			ClusterCutoff = Mathf.Clamp01(
				0.22f + 0.10f * Cover + 0.06f * Storm - 0.04f * Snow);
			ClusterFeather = Mathf.Clamp(
				0.38f - 0.14f * Storm + 0.08f * Snow + 0.04f * Rain,
				0.22f,
				0.48f);
		}

		public static WeatherCloudProfile From(WeatherDef weather)
		{
			if (weather == null)
				return new WeatherCloudProfile(0f, 0f, 0f, 0f, 0f, 0f, Color.white);

			var obscurity = Mathf.Clamp01((1f - weather.accuracyMultiplier) / 0.5f);
			var rain = Mathf.Clamp01(weather.rainRate / 2f);
			var snow = Mathf.Clamp01(weather.snowRate / 2f);
			var sand = Mathf.Clamp01(weather.sandRate / 2f);

			var sky = weather.skyColorsDay.sky;
			var skyLuminance = Luminance(sky);
			var darkness = Mathf.Clamp01((1f - skyLuminance) / 0.4f);
			var desaturation = Mathf.Clamp01((1.25f - weather.skyColorsDay.saturation) / 0.35f);
			var overcast = darkness * desaturation;
			var storm = HasLightning(weather)
				|| weather.windSpeedOffset > 0f && overcast > 0.1f
				? 1f
				: 0f;

			return new WeatherCloudProfile(
				obscurity,
				rain,
				snow,
				sand,
				overcast,
				storm,
				SubtleHue(sky, skyLuminance));
		}

		public static WeatherCloudProfile Lerp(
			WeatherCloudProfile from,
			WeatherCloudProfile to,
			float factor)
		{
			factor = Mathf.Clamp01(factor);
			return new WeatherCloudProfile(
				Mathf.Lerp(from.Obscurity, to.Obscurity, factor),
				Mathf.Lerp(from.Rain, to.Rain, factor),
				Mathf.Lerp(from.Snow, to.Snow, factor),
				Mathf.Lerp(from.Sand, to.Sand, factor),
				Mathf.Lerp(from.Overcast, to.Overcast, factor),
				Mathf.Lerp(from.Storm, to.Storm, factor),
				Color.Lerp(from.hue, to.hue, factor));
		}

		static bool HasLightning(WeatherDef weather)
		{
			var eventMakers = weather.eventMakers;
			if (eventMakers == null)
				return false;

			for (var index = 0; index < eventMakers.Count; index++)
			{
				var eventClass = eventMakers[index]?.eventClass;
				if (eventClass != null
					&& typeof(WeatherEvent_LightningFlash).IsAssignableFrom(eventClass))
					return true;
			}

			return false;
		}

		static Color SubtleHue(Color sky, float luminance)
		{
			if (luminance <= 0.05f)
				return Color.white;

			var normalized = new Color(
				sky.r / luminance,
				sky.g / luminance,
				sky.b / luminance,
				1f);
			var hue = Color.Lerp(Color.white, normalized, 0.08f);
			hue.r = Mathf.Clamp(hue.r, 0.94f, 1.06f);
			hue.g = Mathf.Clamp(hue.g, 0.94f, 1.06f);
			hue.b = Mathf.Clamp(hue.b, 0.94f, 1.06f);
			hue.a = 1f;
			return hue;
		}

		static float Luminance(Color color)
		{
			return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
		}
	}

	internal static class WeatherCloudProfiles
	{
		static readonly Dictionary<WeatherDef, WeatherCloudProfile> cache = new();

		public static WeatherCloudProfile For(WeatherDef weather)
		{
			if (weather == null)
				return WeatherCloudProfile.From(null);

			if (cache.TryGetValue(weather, out var profile) == false)
			{
				profile = WeatherCloudProfile.From(weather);
				cache.Add(weather, profile);
			}

			return profile;
		}

		public static WeatherCloudProfile Effective(WeatherManager manager)
		{
			if (manager == null)
				return WeatherCloudProfile.From(null);

			return WeatherCloudProfile.Lerp(
				For(manager.lastWeather),
				For(manager.curWeather),
				manager.TransitionLerpFactor);
		}
	}
}
