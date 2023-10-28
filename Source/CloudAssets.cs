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
		public static readonly Dictionary<Map, CloudSystem> clouds = new();

		static CloudAssets()
		{
			assets = LoadAssetBundle();
			cloudSystem = assets.LoadAsset<GameObject>("CloudSystem");
			UnityEngine.Object.DontDestroyOnLoad(cloudSystem);
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

		public static CloudSystem CloudsFor(Map map, bool updateActication = false)
		{
			if (clouds.TryGetValue(map, out var cloudSystem) == false)
			{
				cloudSystem = new CloudSystem(map);
				clouds[map] = cloudSystem;
			}

			if (updateActication)
				foreach (var cloud in clouds.Values)
					cloud.Active = cloud == cloudSystem;

			return cloudSystem;
		}

		public static void ApplyToAll(Action<CloudSystem> action)
		{
			foreach (var cloud in clouds.Values)
				action(cloud);
		}

		public static void RemoveCloudsFor(Map map)
		{
			CloudsFor(map)?.Cleanup();
			_ = clouds.Remove(map);
		}
	}
}