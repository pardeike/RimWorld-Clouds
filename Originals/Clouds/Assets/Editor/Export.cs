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
		BuildPipeline.BuildAssetBundles(path, BuildAssetBundleOptions.None, target);
		var fron = path + "/clouds";
		var to = "../../Resources/Clouds" + platform.ToString();
		File.Copy(fron, to, true);
	}

	static void PreBuildDirectoryCheck(string directory)
	{
		if (!Directory.Exists(directory))
			Directory.CreateDirectory(directory);
	}
}