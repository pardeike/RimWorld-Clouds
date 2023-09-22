using UnityEditor;
using System.IO;

public class CreateAssetBundles
{
	[MenuItem("Assets/Build Standalone AssetBundles")]
	static void BuildStandaloneAssetBundles()
	{
		var path = "Assets/AssetBundles";
		PreBuildDirectoryCheck(path);
		BuildPipeline.BuildAssetBundles(path, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
	}


	static void PreBuildDirectoryCheck(string directory)
	{
		if (!Directory.Exists(directory))
			Directory.CreateDirectory(directory);
	}
}
