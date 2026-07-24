using UnityEditor;
using System.IO;
using UnityEngine;

public class CreateAssetBundles
{
	[MenuItem("Assets/Build Standalone AssetBundles")]
	static void BuildStandaloneAssetBundles()
	{
		var path = "Assets/AssetBundles";
		PreBuildDirectoryCheck(path);
		Build(path, RuntimePlatform.WindowsPlayer, BuildTarget.StandaloneWindows64);
		Build(path, RuntimePlatform.LinuxPlayer, BuildTarget.StandaloneLinux64);
		Build(path, RuntimePlatform.OSXPlayer, BuildTarget.StandaloneOSX);
	}

	static void Build(string basePath, RuntimePlatform platform, BuildTarget target)
	{
		var path = basePath + "/" + target;
		PreBuildDirectoryCheck(path);
		var bundles = new[]
		{
			new AssetBundleBuild
			{
				assetBundleName = "clouds",
				assetNames = new[] { "Assets/CloudSystem.prefab" }
			}
		};
		BuildPipeline.BuildAssetBundles(path, bundles, BuildAssetBundleOptions.None, target);

		var from = path + "/clouds";
		var resources = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Resources"));
		PreBuildDirectoryCheck(resources);
		var to = Path.Combine(resources, "Clouds" + platform);
		File.Copy(from, to, true);
	}

	static void PreBuildDirectoryCheck(string directory)
	{
		if (!Directory.Exists(directory))
			Directory.CreateDirectory(directory);
	}
}
