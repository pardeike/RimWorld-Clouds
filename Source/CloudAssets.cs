using System.IO;
using UnityEngine;
using Verse;

namespace Clouds
{
	[StaticConstructorOnStartup]
	public static class CloudAssets
	{
		static readonly AssetBundle assets = LoadAssetBundle();
		static readonly GameObject cloudSystem = assets.LoadAsset<GameObject>("CloudSystem");
		static GameObject clouds;
		static ParticleSystem particles;
		static float baseSpeed;

		static CloudAssets()
		{
			Object.DontDestroyOnLoad(cloudSystem);
		}

		public static GameObject CreateClouds()
		{
			clouds = Object.Instantiate(cloudSystem);
			particles = clouds.GetComponent<ParticleSystem>();
			baseSpeed = particles.main.simulationSpeed;
			return clouds;
		}

		public static bool IsLoaded => clouds != null && particles != null;
		public static float BaseSpeed => baseSpeed;

		public static bool Active
		{
			get => clouds.activeSelf;
			set => clouds.SetActive(value);
		}

		public static bool Pause
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

		public static float Speed
		{
			get => particles.main.simulationSpeed;
			set
			{
				var main = particles.main;
				main.simulationSpeed = value;
			}
		}

		public static float Angle
		{
			get => clouds.transform.rotation.eulerAngles.y;
			set
			{
				var eulerAngles = clouds.transform.rotation.eulerAngles;
				eulerAngles.y = value;
				clouds.transform.rotation = Quaternion.Euler(eulerAngles);
			}
		}

		public static string GetModRootDirectory()
		{
			var me = LoadedModManager.GetMod<Clouds_Main>();
			return me.Content.RootDir;
		}

		public static AssetBundle LoadAssetBundle()
		{
			var path = Path.Combine(GetModRootDirectory(), "Resources", "clouds");
			return AssetBundle.LoadFromFile(path);
		}
	}
}