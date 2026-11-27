using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace Clouds
{
	[StaticConstructorOnStartup]
	public static class CloudAssets
	{
		public static readonly AssetBundle assets;
		public static readonly GameObject cloudSystem;
		public static readonly Dictionary<Map, CloudSystem> clouds;
		static CloudSystem activeCloudSystem;

		static CloudAssets()
		{
			assets = LoadAssetBundle();
			if (assets == null)
				throw new InvalidOperationException("Clouds could not load its RimWorld 1.7 asset bundle.");
			cloudSystem = assets.LoadAsset<GameObject>("CloudSystem");
			if (cloudSystem == null)
				throw new InvalidOperationException("The Clouds asset bundle does not contain CloudSystem.");
			ReplaceCloudShader(cloudSystem);
			clouds = [];
			UnityEngine.Object.DontDestroyOnLoad(cloudSystem);
		}

		static void ReplaceCloudShader(GameObject prefab)
		{
			var shader = DefDatabase<ShaderDef>.GetNamed("CloudsCloudParticle").Shader;
			var replaced = 0;
			foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
				foreach (var material in renderer.sharedMaterials)
				{
					if (material == null || material.shader == null || material.shader.name != "Clouds/CloudParticle")
						continue;
					var renderQueue = material.renderQueue;
					material.shader = shader;
					material.renderQueue = renderQueue;
					replaced++;
				}
			if (replaced == 0)
				throw new InvalidOperationException("CloudSystem has no Clouds/CloudParticle material to replace.");
		}

		public static string GetModRootDirectory()
		{
			var me = LoadedModManager.GetMod<Clouds_Main>();
			return me.Content.RootDir;
		}

		public static AssetBundle LoadAssetBundle()
		{
			var platform = Application.platform switch
			{
				RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor => "win",
				RuntimePlatform.LinuxPlayer or RuntimePlatform.LinuxEditor => "linux",
				RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor => "mac",
				_ => throw new PlatformNotSupportedException($"Clouds does not support {Application.platform}.")
			};
			// Waylith uses the adjacent _linux bundle when a platform bundle lacks Vulkan shaders.
			var path = Path.Combine(GetModRootDirectory(), "1.7", "Resources", "clouds_" + platform);
			return AssetBundle.LoadFromFile(path);
		}

		public static void Cleanup()
		{
			DeactivateAll();
			foreach (var cloudSystem in clouds.Values)
				cloudSystem.Destroy();
			clouds.Clear();
			CloudVisibility.Cleanup();
		}

		public static CloudSystem CloudsFor(Map map, bool updateActication = false)
		{
			if (clouds.TryGetValue(map, out var cloudSystem) == false)
			{
				cloudSystem = new CloudSystem(map);
				clouds[map] = cloudSystem;
			}

			if (updateActication)
				ActivateOnly(cloudSystem);

			return cloudSystem;
		}

		public static void ActivateOnly(CloudSystem cloudSystem)
		{
			if (cloudSystem != null
				&& (CloudViewState.IsWorldView || CloudVisibility.IsAllowedOn(cloudSystem.Map) == false))
				cloudSystem = null;

			if (ReferenceEquals(activeCloudSystem, cloudSystem))
			{
				if (cloudSystem != null && cloudSystem.Active == false)
					cloudSystem.Active = true;
				return;
			}

			if (activeCloudSystem != null)
				activeCloudSystem.Active = false;

			activeCloudSystem = cloudSystem;
			if (activeCloudSystem != null)
				activeCloudSystem.Active = true;
		}

		public static void DeactivateAll()
		{
			ActivateOnly(null);
		}

		public static bool TryGetCloudsFor(Map map, out CloudSystem cloudSystem)
		{
			if (map == null)
			{
				cloudSystem = null;
				return false;
			}

			return clouds.TryGetValue(map, out cloudSystem);
		}

		public static void ApplyToAll(Action<CloudSystem> action)
		{
			foreach (var cloud in clouds.Values)
				action(cloud);
		}

		internal static CloudSystem ActiveCloudSystem => activeCloudSystem;

		internal static int ActiveCount
		{
			get
			{
				var count = 0;
				foreach (var cloud in clouds.Values)
					if (cloud.Active)
						count++;
				return count;
			}
		}

		public static void RemoveCloudsFor(Map map)
		{
			if (clouds.TryGetValue(map, out var cloudSystem))
			{
				if (ReferenceEquals(activeCloudSystem, cloudSystem))
					DeactivateAll();
				cloudSystem.Destroy();
				clouds.Remove(map);
			}

			CloudVisibility.Forget(map);
		}
	}
}
