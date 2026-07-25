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
			cloudSystem = assets.LoadAsset<GameObject>("CloudSystem");
			clouds = [];
			UnityEngine.Object.DontDestroyOnLoad(cloudSystem);
		}

		public static string GetModRootDirectory()
		{
			var me = LoadedModManager.GetMod<Clouds_Main>();
			return me.Content.RootDir;
		}

		public static AssetBundle LoadAssetBundle()
		{

			var path = Path.Combine(GetModRootDirectory(), "Resources", "Clouds" + Application.platform);
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
