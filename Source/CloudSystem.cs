using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace Clouds
{
	internal readonly struct ParticleSeedState
	{
		public readonly bool UseAutoRandomSeed;
		public readonly uint RandomSeed;

		public ParticleSeedState(bool useAutoRandomSeed, uint randomSeed)
		{
			UseAutoRandomSeed = useAutoRandomSeed;
			RandomSeed = randomSeed;
		}
	}

	public class CloudSystem
	{
		static readonly int WeatherTintProperty = Shader.PropertyToID("_WeatherTint");
		static readonly int WeatherContrastProperty = Shader.PropertyToID("_WeatherContrast");
		static readonly int WeatherOpacityProperty = Shader.PropertyToID("_WeatherOpacity");
		static readonly int WeatherEdgePowerProperty = Shader.PropertyToID("_WeatherEdgePower");
		static readonly int ClusterTextureProperty = Shader.PropertyToID("_ClusterTex");
		static readonly int ClusterCutoffProperty = Shader.PropertyToID("_ClusterCutoff");
		static readonly int ClusterFeatherProperty = Shader.PropertyToID("_ClusterFeather");
		static readonly int ClusterOffsetProperty = Shader.PropertyToID("_ClusterOffset");

		readonly Map map;
		readonly GameObject clouds;
		readonly ParticleSystem particles;
		readonly ParticleSystemRenderer renderer;
		readonly Material material;
		readonly float baseSpeed;
		readonly float baseAlpha;
		WeatherCloudProfile appliedProfile;
		bool hasAppliedProfile;
		float effectiveWindSpeed = 1f;
		Vector2 clusterOffset;

		public float nextAngle = -90f;

		public CloudSystem(Map map)
		{
			this.map = map;
			var alt = AltitudeLayer.MetaOverlays.AltitudeFor();
			var position = new Vector3(map.Size.x / 2f, alt, map.Size.z / 2f);
			var max = Math.Max(map.Size.x, map.Size.z);
			var localScale = Vector3.one * max / 25f;

			clouds = UnityEngine.Object.Instantiate(CloudAssets.cloudSystem);
			particles = clouds.GetComponent<ParticleSystem>();
			renderer = clouds.GetComponent<ParticleSystemRenderer>();
			renderer.enabled = false;
			baseSpeed = particles.main.simulationSpeed;
			material = renderer.materials[0];
			material.renderQueue = MatBases.FogOfWar.renderQueue + 100;
			baseAlpha = material.color.a;
			clouds.transform.position = position;
			clouds.transform.localScale = localScale;

			UpdateWeather(map.weatherManager, Current.Game?.tickManager, false, true);
			particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			particles.time = 0f;
			particles.Play(true);
			SynchronizeTime(Current.Game?.tickManager);
		}

		public void Destroy()
		{
			Log.Warning("CloudSystem destroyed");
			UnityEngine.Object.Destroy(clouds);
		}

		// Retained for binary compatibility with integrations that used the old helper.
		public static (float, FloatRange) LerpedValues(float currentMultiplier)
		{
			var cover = Mathf.Clamp01((1f - currentMultiplier) / 0.5f);
			var emission = Mathf.Lerp(8f, 30f, cover);
			var factor = Mathf.Lerp(1f, 1.5f, cover);
			return (emission, new FloatRange(factor, 4f * factor));
		}

		internal void UpdateWeather(
			WeatherManager weatherManager,
			TickManager tickManager,
			bool advanceCluster,
			bool force = false)
		{
			var profile = WeatherCloudProfiles.Effective(weatherManager);
			effectiveWindSpeed = weatherManager == null
				? 1f
				: Mathf.Clamp(
					weatherManager.CurWindSpeedFactor + 0.4f * weatherManager.CurWindSpeedOffset,
					0.35f,
					2f);

			if (advanceCluster)
			{
				var radians = Angle * Mathf.Deg2Rad;
				var direction = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
				clusterOffset += direction * (0.00015f * effectiveWindSpeed);
				clusterOffset.x -= Mathf.Floor(clusterOffset.x);
				clusterOffset.y -= Mathf.Floor(clusterOffset.y);
			}

			if (force || hasAppliedProfile == false || ProfilesDiffer(appliedProfile, profile))
			{
				appliedProfile = profile;
				hasAppliedProfile = true;
				Emission = profile.EmissionRate;
				Size = new FloatRange(profile.SizeFactor, 4f * profile.SizeFactor);
				material.SetColor(WeatherTintProperty, profile.Tint);
				material.SetFloat(WeatherContrastProperty, profile.Contrast);
				material.SetFloat(WeatherOpacityProperty, profile.Opacity);
				material.SetFloat(WeatherEdgePowerProperty, profile.EdgePower);
				material.SetFloat(ClusterCutoffProperty, profile.ClusterCutoff);
				material.SetFloat(ClusterFeatherProperty, profile.ClusterFeather);
			}

			if (advanceCluster || force)
				material.SetVector(
					ClusterOffsetProperty,
					new Vector4(clusterOffset.x, clusterOffset.y, 0f, 0f));

			SynchronizeTime(tickManager);
		}

		internal void SynchronizeTime(TickManager tickManager)
		{
			var paused = tickManager == null || tickManager.Paused;
			if (paused && particles.isPaused == false)
				particles.Pause(true);
			else if (paused == false && particles.isPaused)
				particles.Play(true);

			var timeSpeed = paused ? 0f : Math.Max(1, (int)tickManager.CurTimeSpeed);
			var targetSpeed = baseSpeed * effectiveWindSpeed * timeSpeed;
			if (Mathf.Approximately(Speed, targetSpeed) == false)
				Speed = targetSpeed;
		}

		internal ParticleSeedState CaptureSeedState()
		{
			return new ParticleSeedState(particles.useAutoRandomSeed, particles.randomSeed);
		}

		internal void RestoreSeedState(ParticleSeedState state)
		{
			particles.useAutoRandomSeed = false;
			particles.randomSeed = state.RandomSeed;
			particles.useAutoRandomSeed = state.UseAutoRandomSeed;
		}

		internal void RestartAndPrewarm(uint seed)
		{
			if (seed == 0)
				seed = 1;

			var wasPaused = particles.isPaused;
			var previousSpeed = particles.main.simulationSpeed;
			particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			particles.useAutoRandomSeed = false;
			particles.randomSeed = seed;

			var main = particles.main;
			main.simulationSpeed = 1f;
			particles.Simulate(MaxLifetime, true, true, true);
			particles.Play(true);
			main = particles.main;
			main.simulationSpeed = previousSpeed;
			if (wasPaused)
				particles.Pause(true);
		}

		static bool ProfilesDiffer(WeatherCloudProfile first, WeatherCloudProfile second)
		{
			return Mathf.Abs(first.Cover - second.Cover) > 0.0001f
				|| Mathf.Abs(first.Storm - second.Storm) > 0.0001f
				|| Mathf.Abs(first.EmissionRate - second.EmissionRate) > 0.0001f
				|| Mathf.Abs(first.SizeFactor - second.SizeFactor) > 0.0001f
				|| Mathf.Abs(first.Contrast - second.Contrast) > 0.0001f
				|| Mathf.Abs(first.Opacity - second.Opacity) > 0.0001f
				|| Mathf.Abs(first.EdgePower - second.EdgePower) > 0.0001f
				|| Mathf.Abs(first.ClusterCutoff - second.ClusterCutoff) > 0.0001f
				|| Mathf.Abs(first.ClusterFeather - second.ClusterFeather) > 0.0001f
				|| Mathf.Abs(first.Tint.r - second.Tint.r) > 0.0001f
				|| Mathf.Abs(first.Tint.g - second.Tint.g) > 0.0001f
				|| Mathf.Abs(first.Tint.b - second.Tint.b) > 0.0001f;
		}

		public bool IsAvailable => clouds != null && particles != null;
		public bool IsLoaded => clouds != null && particles != null;
		public float BaseSpeed => baseSpeed;
		public float BaseAlpha => baseAlpha;

		internal Map Map => map;
		internal WeatherCloudProfile AppliedProfile => appliedProfile;
		internal float EffectiveWindSpeed => effectiveWindSpeed;
		internal Vector2 ClusterOffset => clusterOffset;
		internal int ParticleCount => particles.particleCount;
		internal float MaxLifetime => particles.main.startLifetime.constantMax;
		internal bool ClusterTextureAssigned => material.GetTexture(ClusterTextureProperty) != null;
		internal bool GpuInstancingEnabled => renderer.enableGPUInstancing;
		internal bool MaterialInstancingEnabled => material.enableInstancing;
		internal bool ShaderSupported => material.shader?.isSupported == true;
		internal int ShaderPassCount => material.passCount;
		internal string ShaderName => material.shader?.name ?? string.Empty;
		internal bool UsesAutoRandomSeed => particles.useAutoRandomSeed;
		internal uint RandomSeed => particles.randomSeed;

		public bool Active
		{
			get => renderer.enabled;
			set => renderer.enabled = value;
		}

		public bool Pause
		{
			get => particles.isPaused;
			set
			{
				if (value)
					particles.Pause(true);
				else
					particles.Play(true);
			}
		}

		public float Alpha
		{
			get => material.color.a;
			set
			{
				var color = material.color;
				material.color = new Color(color.r, color.g, color.b, value);
			}
		}

		public float Speed
		{
			get => particles.main.simulationSpeed;
			set
			{
				var main = particles.main;
				main.simulationSpeed = value;
			}
		}

		public float Angle
		{
			get => clouds.transform.rotation.eulerAngles.y;
			set
			{
				var eulerAngles = clouds.transform.rotation.eulerAngles;
				eulerAngles.y = value;
				clouds.transform.rotation = Quaternion.Euler(eulerAngles);
			}
		}

		public float Emission
		{
			get => particles.emission.rateOverTime.constant;
			set
			{
				var emission = particles.emission;
				emission.rateOverTime = new ParticleSystem.MinMaxCurve(value);
			}
		}

		public FloatRange Size
		{
			get => new FloatRange(
				particles.main.startSize.constantMin,
				particles.main.startSize.constantMax);
			set
			{
				var main = particles.main;
				main.startSize = new ParticleSystem.MinMaxCurve(value.min, value.max);
			}
		}
	}
}
