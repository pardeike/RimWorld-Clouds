using UnityEditor;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class CreateAssetBundles
{
	[MenuItem("Assets/Build Standalone AssetBundles")]
	public static void BuildStandaloneAssetBundles()
	{
		GenerateCloudClusterTexture.GenerateAndValidate();
		ConfigureAndValidateCloudRenderer();
		ValidateCloudEntryGeometry();
		AssetDatabase.SaveAssets();

		var path = "Assets/AssetBundles";
		PreBuildDirectoryCheck(path);
		Build(path, RuntimePlatform.WindowsPlayer, BuildTarget.StandaloneWindows64);
		Build(path, RuntimePlatform.LinuxPlayer, BuildTarget.StandaloneLinux64);
		Build(path, RuntimePlatform.OSXPlayer, BuildTarget.StandaloneOSX);
	}

	static void ConfigureAndValidateCloudRenderer()
	{
		var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CloudSystem.prefab");
		if (prefab == null)
			throw new IOException("CloudSystem.prefab could not be loaded for renderer validation.");

		var renderer = prefab.GetComponent<ParticleSystemRenderer>();
		if (renderer == null)
			throw new IOException("CloudSystem.prefab has no ParticleSystemRenderer.");
		if (renderer.renderMode != ParticleSystemRenderMode.Billboard)
			throw new IOException("CloudSystem.prefab must remain in Billboard render mode.");

		renderer.enableGPUInstancing = false;
		var vertexStreams = new List<ParticleSystemVertexStream>
		{
			ParticleSystemVertexStream.Position,
			ParticleSystemVertexStream.Normal,
			ParticleSystemVertexStream.Color,
			ParticleSystemVertexStream.UV,
			ParticleSystemVertexStream.StableRandomX
		};
		renderer.SetActiveVertexStreams(vertexStreams);
		EditorUtility.SetDirty(renderer);

		var activeVertexStreams = new List<ParticleSystemVertexStream>();
		renderer.GetActiveVertexStreams(activeVertexStreams);
		if (activeVertexStreams.Contains(ParticleSystemVertexStream.StableRandomX) == false)
			throw new IOException("CloudSystem.prefab is missing the StableRandomX vertex stream.");
	}

	static void ValidateCloudEntryGeometry()
	{
		const float normalizedMapHalfExtent = 12.5f;
		const float runtimeMaximumBaseSize = 4f;
		const float maximumWeatherSizeFactor = 1.45f;
		const float requiredEntryPadding = 2f;

		var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CloudSystem.prefab");
		if (prefab == null)
			throw new IOException("CloudSystem.prefab could not be loaded for entry-geometry validation.");

		var particles = prefab.GetComponent<ParticleSystem>();
		if (particles == null)
			throw new IOException("CloudSystem.prefab has no ParticleSystem.");

		var maximumParticleRadius =
			runtimeMaximumBaseSize * maximumWeatherSizeFactor * 0.5f;
		var upstreamSpawnDistance = -particles.shape.position.y;
		var requiredSpawnDistance =
			normalizedMapHalfExtent + maximumParticleRadius + requiredEntryPadding;
		if (upstreamSpawnDistance < requiredSpawnDistance)
		{
			throw new IOException(
				$"Cloud emitter is too close to the map: {upstreamSpawnDistance:0.###} "
				+ $"but at least {requiredSpawnDistance:0.###} is required.");
		}

		Debug.Log(
			$"Cloud entry geometry validated: emitter {upstreamSpawnDistance:0.###}, "
			+ $"largest radius {maximumParticleRadius:0.###}, "
			+ $"fully outside map by "
			+ $"{upstreamSpawnDistance - normalizedMapHalfExtent - maximumParticleRadius:0.###} "
			+ "normalized units.");
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
		var manifest = BuildPipeline.BuildAssetBundles(
			path,
			bundles,
			BuildAssetBundleOptions.ForceRebuildAssetBundle,
			target);
		if (manifest == null)
			throw new IOException($"Unity failed to build the Clouds asset bundle for {target}.");

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
