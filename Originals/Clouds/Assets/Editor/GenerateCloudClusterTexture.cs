using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class GenerateCloudClusterTexture
{
	const int Size = 256;
	const float MinimumUsefulRange = 0.45f;
	const string AssetPath = "Assets/cloud-cluster.png";

	public static void GenerateAndValidate()
	{
		var values = GenerateValues();
		ValidateClustering(values);

		var texture = new Texture2D(Size, Size, TextureFormat.RGB24, false, true);
		var pixels = new Color32[values.Length];
		for (var index = 0; index < values.Length; index++)
		{
			var value = (byte)Mathf.RoundToInt(Mathf.Clamp01(values[index]) * 255f);
			pixels[index] = new Color32(value, value, value, 255);
		}

		texture.SetPixels32(pixels);
		texture.Apply(false, false);
		var bytes = texture.EncodeToPNG();
		UnityEngine.Object.DestroyImmediate(texture);

		var fullPath = Path.GetFullPath(AssetPath);
		if (File.Exists(fullPath) == false || ByteArraysEqual(File.ReadAllBytes(fullPath), bytes) == false)
			File.WriteAllBytes(fullPath, bytes);

		AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
		var importer = AssetImporter.GetAtPath(AssetPath) as TextureImporter;
		if (importer == null)
			throw new InvalidOperationException("Unity did not create a texture importer for the cloud cluster mask.");

		importer.textureType = TextureImporterType.SingleChannel;
		var settings = new TextureImporterSettings();
		importer.ReadTextureSettings(settings);
		settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
		importer.SetTextureSettings(settings);
		importer.sRGBTexture = false;
		importer.mipmapEnabled = false;
		importer.alphaSource = TextureImporterAlphaSource.None;
		importer.wrapMode = TextureWrapMode.Repeat;
		importer.filterMode = FilterMode.Bilinear;
		importer.anisoLevel = 1;
		importer.npotScale = TextureImporterNPOTScale.None;
		importer.maxTextureSize = Size;
		importer.textureCompression = TextureImporterCompression.Uncompressed;
		importer.crunchedCompression = false;
		importer.SaveAndReimport();

		importer = AssetImporter.GetAtPath(AssetPath) as TextureImporter;
		settings = new TextureImporterSettings();
		importer?.ReadTextureSettings(settings);
		if (importer == null
			|| importer.textureType != TextureImporterType.SingleChannel
			|| settings.singleChannelComponent != TextureImporterSingleChannelComponent.Red)
		{
			throw new InvalidOperationException(
				"The cloud cluster mask must be imported from the red channel as a single-channel GPU texture.");
		}
	}

	static float[] GenerateValues()
	{
		var values = new float[Size * Size];
		for (var y = 0; y < Size; y++)
		{
			var v = (float)y / Size;
			for (var x = 0; x < Size; x++)
			{
				var u = (float)x / Size;
				var noise =
					0.55f * PeriodicValueNoise(u, v, 2, 0x1287)
					+ 0.30f * PeriodicValueNoise(u, v, 4, 0x51A3)
					+ 0.15f * PeriodicValueNoise(u, v, 8, 0x7C19);
				values[y * Size + x] = Mathf.SmoothStep(0f, 1f, noise);
			}
		}

		return values;
	}

	static void ValidateClustering(float[] values)
	{
		var minimum = 1f;
		var maximum = 0f;
		var sum = 0f;
		for (var index = 0; index < values.Length; index++)
		{
			minimum = Mathf.Min(minimum, values[index]);
			maximum = Mathf.Max(maximum, values[index]);
			sum += values[index];
		}

		if (maximum - minimum < MinimumUsefulRange)
			throw new InvalidOperationException(
				$"Cloud cluster mask range {maximum - minimum:F3} is below {MinimumUsefulRange:F2}.");

		Debug.Log(
			$"Cloud cluster mask validated: organic range {minimum:F3}..{maximum:F3}, mean {sum / values.Length:F3}; shader opacity floor remains 0.72.");
	}

	static float PeriodicValueNoise(float u, float v, int frequency, int seed)
	{
		var x = u * frequency;
		var y = v * frequency;
		var x0 = Mathf.FloorToInt(x);
		var y0 = Mathf.FloorToInt(y);
		var tx = Mathf.SmoothStep(0f, 1f, x - x0);
		var ty = Mathf.SmoothStep(0f, 1f, y - y0);
		var x1 = (x0 + 1) % frequency;
		var y1 = (y0 + 1) % frequency;
		x0 = PositiveModulo(x0, frequency);
		y0 = PositiveModulo(y0, frequency);

		var lower = Mathf.Lerp(Hash01(x0, y0, seed), Hash01(x1, y0, seed), tx);
		var upper = Mathf.Lerp(Hash01(x0, y1, seed), Hash01(x1, y1, seed), tx);
		return Mathf.Lerp(lower, upper, ty);
	}

	static float Hash01(int x, int y, int seed)
	{
		unchecked
		{
			var hash = (uint)seed;
			hash ^= (uint)x * 0x9E3779B9u;
			hash ^= (uint)y * 0x85EBCA6Bu;
			hash ^= hash >> 16;
			hash *= 0x7FEB352Du;
			hash ^= hash >> 15;
			hash *= 0x846CA68Bu;
			hash ^= hash >> 16;
			return (hash & 0x00FFFFFFu) / 16777215f;
		}
	}

	static int PositiveModulo(int value, int modulus)
	{
		var result = value % modulus;
		return result < 0 ? result + modulus : result;
	}

	static bool ByteArraysEqual(byte[] first, byte[] second)
	{
		if (first.Length != second.Length)
			return false;
		for (var index = 0; index < first.Length; index++)
			if (first[index] != second[index])
				return false;
		return true;
	}
}
