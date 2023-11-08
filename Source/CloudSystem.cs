using System;
using UnityEngine;
using Verse;

namespace Clouds
{
	public class CloudSystem
	{
		readonly GameObject clouds;
		readonly ParticleSystem particles;
		readonly ParticleSystemRenderer renderer;
		readonly Material material;
		readonly float baseSpeed;
		readonly float baseAlpha;
		public float nextAngle = -90f;

		public CloudSystem(Map map)
		{
			var alt = AltitudeLayer.MetaOverlays.AltitudeFor();
			var position = new Vector3(map.Size.x / 2f, alt, map.Size.z / 2f);
			var max = Math.Max(map.Size.x, map.Size.z);
			var localScale = Vector3.one * max / 25f;

			clouds = UnityEngine.Object.Instantiate(CloudAssets.cloudSystem);
			particles = clouds.GetComponent<ParticleSystem>();
			renderer = clouds.GetComponent<ParticleSystemRenderer>();
			baseSpeed = particles.main.simulationSpeed;
			material = renderer.materials[0];
			material.renderQueue = MatBases.FogOfWar.renderQueue + 100;
			baseAlpha = material.color.a;
			clouds.transform.position = position;
			clouds.transform.localScale = localScale;

			var currentMultiplier = map.weatherManager.CurWeatherAccuracyMultiplier;
			var values = LerpedValues(currentMultiplier);
			Emission = values.Item1;
			Size = values.Item2;

			particles.Stop();
			particles.time = 0;
			particles.Play();
		}

		public void Destroy()
		{
			Log.Warning("CloudSystem destroyed");
			UnityEngine.Object.Destroy(clouds);
		}

		public static (float, FloatRange) LerpedValues(float currentMultiplier)
		{
			var emission = GenMath.LerpDoubleClamped(1, 0.5f, 8, 40, currentMultiplier);
			var f = GenMath.LerpDoubleClamped(1, 0.5f, 1f, 2f, currentMultiplier);
			var size = new FloatRange(f, 2 * f);
			return (emission, size);
		}

		public bool IsAvailable => clouds != null && particles != null;

		public bool IsLoaded => clouds != null && particles != null;
		public float BaseSpeed => baseSpeed;
		public float BaseAlpha => baseAlpha;

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
					particles.Pause();
				else
					particles.Play();
			}
		}

		public float Alpha
		{
			get => material.color.a;
			set
			{
				var color = material.color;
				material.color = new(color.r, color.g, color.b, value);
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
			get => new(particles.main.startSize.constantMin, particles.main.startSize.constantMax);
			set
			{
				var main = particles.main;
				main.startSize = new ParticleSystem.MinMaxCurve(value.min, value.max);
			}
		}
	}
}