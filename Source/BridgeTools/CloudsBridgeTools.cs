using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace Clouds.BridgeTools
{
	public sealed class CloudsBridgeTools
	{
		const string DefaultWeatherDefs = "Clear,Rain,DryThunderstorm,RainyThunderstorm,Fog,SnowHard";
		const string DefaultRootSizes = "18,25,35,50";
		sealed class SuiteRestoreState
		{
			public Map Map;
			public WeatherDef Weather;
			public float CameraRootSize;
			public TimeSpeed TimeSpeed;
			public bool HadCloudSystem;
			public ParticleSeedState ParticleSeed;
		}

		sealed class WeatherCaptureState
		{
			public string WeatherDefName;
			public float RootSize;
			public WeatherCloudProfile Profile;
			public bool ClusterTextureAssigned;
			public bool GpuInstancingEnabled;
			public string ShaderName;
			public int ParticleCount;
			public float Alpha;
		}

		[Tool(
			"clouds/get_weather_state",
			Description = "Read the current map, RimWorld weather transition, and effective Clouds GPU profile without changing game state.",
			ResultDescription = "Returns current/last weather definitions, transition progress, derived signals, applied particle values, shader state, and camera zoom.")]
		public static async Task<object> GetWeatherState(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken)
		{
			if (ctx == null)
				return new { success = false, error = "RimBridge SDK context was not injected." };

			return await ctx.MainThread.InvokeAsync(CaptureState, cancellationToken);
		}

		[Tool(
			"clouds/set_weather",
			Description = "Switch the current map to an exact active WeatherDef and immediately apply a requested point in RimWorld's normal weather transition.",
			ResultDescription = "Returns the resulting weather transition and effective Clouds GPU profile.")]
		public static async Task<object> SetWeather(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			[ToolParameter(Description = "Exact active WeatherDef defName, for example Rain or DryThunderstorm.")] string weatherDefName,
			[ToolParameter(Description = "Progress through RimWorld's 4000-tick weather transition, from 0 to 1.", DefaultValue = 1f)] float transitionProgress = 1f,
			[ToolParameter(Description = "Restart and prewarm the cloud particles so the requested density is visible immediately.", DefaultValue = true)] bool resetParticles = true)
		{
			if (ctx == null)
				return new { success = false, error = "RimBridge SDK context was not injected." };
			if (string.IsNullOrWhiteSpace(weatherDefName))
				return new { success = false, error = "An exact WeatherDef name is required." };
			if (float.IsNaN(transitionProgress)
				|| float.IsInfinity(transitionProgress)
				|| transitionProgress < 0f
				|| transitionProgress > 1f)
				return new { success = false, error = "transitionProgress must be between 0 and 1." };

			try
			{
				var result = await ctx.MainThread.InvokeAsync(
					() =>
					{
						var weather = FindWeather(weatherDefName);
						if (weather == null)
						{
							return new
							{
								success = false,
								error = $"No active WeatherDef has the exact name '{weatherDefName}'.",
								availableWeatherDefs = AvailableWeatherNames()
							} as object;
						}

						ApplyWeather(weather, transitionProgress, resetParticles);
						return CaptureState();
					},
					cancellationToken);
				await ctx.Game.NextFrameAsync(cancellationToken);
				return result;
			}
			catch (Exception exception)
			{
				return new
				{
					success = false,
					error = exception.Message,
					type = exception.GetType().FullName,
					availableWeatherDefs = await ctx.MainThread.InvokeAsync(
						AvailableWeatherNames,
						CancellationToken.None)
				};
			}
		}

		[Tool(
			"clouds/capture_weather_suite",
			Description = "Force weather profiles and capture the current map at several zoom levels with deterministic cloud populations.",
			ResultDescription = "Returns a RimBridge evidence manifest containing profile assertions and one screenshot per weather and zoom.")]
		public static async Task<object> CaptureWeatherSuite(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			[ToolParameter(Description = "Comma-separated exact WeatherDef names, or all.", DefaultValue = DefaultWeatherDefs)] string weatherDefs = DefaultWeatherDefs,
			[ToolParameter(Description = "Comma-separated camera root sizes from 12 through 60.", DefaultValue = DefaultRootSizes)] string rootSizes = DefaultRootSizes,
			[ToolParameter(Description = "Capture id used in screenshot file names; a UTC timestamp is generated when omitted.", DefaultValue = "")] string captureId = "",
			[ToolParameter(Description = "Restore camera, pause state, particle seed behavior, and the original weather when finished.", DefaultValue = true)] bool restoreState = true)
		{
			if (ctx == null)
				return new { success = false, error = "RimBridge SDK context was not injected." };

			var parsedRoots = ParseRootSizes(rootSizes, out var rootError);
			if (rootError != null)
				return new { success = false, error = rootError };

			var weatherSelection = await ctx.MainThread.InvokeAsync(
				() => SelectWeather(weatherDefs),
				cancellationToken);
			if (weatherSelection.Error != null)
				return new { success = false, error = weatherSelection.Error, availableWeatherDefs = weatherSelection.Available };

			captureId = SafeFilePart(string.IsNullOrWhiteSpace(captureId)
				? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
				: captureId);
			if (string.IsNullOrEmpty(captureId))
				return new { success = false, error = "The capture id contained no file-name-safe characters." };

			var manifest = RimBridgeEvidence.CreateManifest("clouds/weather_suite", captureId);
			manifest.modVersion = typeof(Clouds_Main).Assembly.GetName().Version?.ToString() ?? string.Empty;
			manifest.gameVersion = VersionControl.CurrentVersionStringWithRev;
			manifest.environment.modVersion = manifest.modVersion;
			manifest.environment.gameVersion = manifest.gameVersion;
			manifest.environment.details = new
			{
				weatherDefs = weatherSelection.Weather.Select(weather => weather.defName).ToArray(),
				rootSizes = parsedRoots.ToArray(),
				transitionProgress = 1f,
				deterministicParticles = true,
				clusterWorldPeriodCells = 64,
				clusterOpacityFloor = 0.72f,
				maximumEmissionRate = 50f,
				maximumSizeFactor = 1.45f,
				restoreState
			};

			SuiteRestoreState original = null;
			var weatherStates = new List<WeatherCaptureState>();
			try
			{
				original = await ctx.MainThread.InvokeAsync(CaptureSuiteRestoreState, cancellationToken);
				if (original.Map == null)
					throw new InvalidOperationException("No playable current map is loaded.");

				await ctx.MainThread.InvokeAsync(
					() =>
					{
						Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
						if (CloudAssets.TryGetCloudsFor(original.Map, out var clouds))
							clouds.SynchronizeTime(Find.TickManager);
					},
					cancellationToken);

				foreach (var weather in weatherSelection.Weather)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var weatherState = await ctx.MainThread.InvokeAsync(
						() => PrepareWeatherCapture(weather),
						cancellationToken);
					await ctx.Game.FramesAsync(2, cancellationToken);

					foreach (var rootSize in parsedRoots)
					{
						cancellationToken.ThrowIfCancellationRequested();
						var zoom = await ctx.Tools.CallAsync(
							"rimworld/set_camera_zoom",
							new { rootSize },
							cancellationToken: cancellationToken);
						if (zoom.Succeeded() == false)
							throw new InvalidOperationException($"Setting camera root size {rootSize} failed: {zoom.Error?.Message ?? zoom.Status}.");

						await ctx.Game.FramesAsync(2, cancellationToken);
						var actualRootSize = await ctx.MainThread.InvokeAsync(
							() => Find.CameraDriver.RootSize,
							cancellationToken);
						var rootLabel = rootSize.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', 'p');
						var fileName = $"clouds_{captureId}_{SafeFilePart(weather.defName)}_zoom_{rootLabel}";
						var screenshot = await ctx.Tools.CallAsync(
							"rimworld/take_screenshot",
							new
							{
								fileName,
								includeTargets = false,
								suppressMessage = true
							},
							cancellationToken: cancellationToken);
						string path = null;
						if (screenshot.Succeeded())
							screenshot.TryReadResult(out path, "path");

						var state = new WeatherCaptureState
						{
							WeatherDefName = weather.defName,
							RootSize = actualRootSize,
							Profile = weatherState.Profile,
							ClusterTextureAssigned = weatherState.ClusterTextureAssigned,
							GpuInstancingEnabled = weatherState.GpuInstancingEnabled,
							ShaderName = weatherState.ShaderName,
							ParticleCount = weatherState.ParticleCount,
							Alpha = weatherState.Alpha
						};
						weatherStates.Add(state);
						manifest.captures.Add(new RimBridgeEvidenceCapture
						{
							success = screenshot.Succeeded()
								&& string.IsNullOrWhiteSpace(path) == false
								&& File.Exists(path),
							label = $"{weather.defName}_zoom_{rootLabel}",
							kind = "weather_zoom_screenshot",
							path = path ?? string.Empty,
							capturedAtUtc = DateTimeOffset.UtcNow,
							details = DescribeCaptureState(state)
						});
					}
				}
			}
			catch (Exception exception)
			{
				manifest.errors.Add(new RimBridgeEvidenceError
				{
					stage = "weather_suite",
					message = exception.Message,
					details = new
					{
						type = exception.GetType().FullName,
						exception.StackTrace
					}
				});
			}
			finally
			{
				if (restoreState && original != null)
				{
					try
					{
						await ctx.MainThread.InvokeAsync(
							() => RestoreSuiteState(original),
							CancellationToken.None);
						await ctx.Game.FramesAsync(2, CancellationToken.None);
					}
					catch (Exception exception)
					{
						manifest.errors.Add(new RimBridgeEvidenceError
						{
							stage = "restore",
							message = exception.Message,
							details = exception.GetType().FullName
						});
					}
				}
			}

			var expectedCaptureCount = weatherSelection.Weather.Count * parsedRoots.Count;
			manifest.assertions.Add(RimBridgeEvidence.AreEqual(
				"all requested weather and zoom captures produced",
				expectedCaptureCount,
				manifest.captures.Count,
				details: new
				{
					requestedWeatherCount = weatherSelection.Weather.Count,
					requestedZoomCount = parsedRoots.Count,
					successfulCaptures = manifest.captures.Count(capture => capture.success)
				}));
			manifest.assertions.Add(RimBridgeEvidence.IsTrue(
				"all captures were saved successfully",
				manifest.captures.All(capture => capture.success),
				details: manifest.captures.Select(capture => new { capture.label, capture.success, capture.path }).ToArray()));
			manifest.assertions.Add(RimBridgeEvidence.IsTrue(
				"cloud profiles remain inside the bounded particle budget",
				weatherStates.All(state =>
					state.Profile.EmissionRate <= 50.0001f
					&& state.Profile.SizeFactor <= 1.4501f
					&& state.Profile.Opacity >= 0.7199f
					&& state.Profile.Opacity <= 2.0001f
					&& state.Profile.EdgePower >= 0.6499f
					&& state.Profile.EdgePower <= 1.5501f
					&& state.Alpha <= 0.51f),
				details: weatherStates.Select(DescribeCaptureState).ToArray()));
			manifest.assertions.Add(RimBridgeEvidence.IsTrue(
				"GPU shader clustering remains subtle and never cuts artificial holes",
				weatherStates.Count == expectedCaptureCount
					&& weatherStates.All(state =>
						state.ClusterTextureAssigned
						&& state.GpuInstancingEnabled
						&& state.ShaderName == "Clouds/CloudParticle"
						&& state.Profile.ClusterCutoff >= 0.22f),
				"The shared world-space mask only modulates cloud alpha between 0.72 and 1.0. Map readability comes from the bounded particle density, size, and base alpha.",
				new
				{
					clusterOpacityFloor = 0.72f,
					states = weatherStates.Select(DescribeCaptureState).ToArray()
				}));

			RimBridgeEvidence.Complete(manifest);
			return manifest;
		}

		static object CaptureState()
		{
			var map = Find.CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No playable current map is loaded.",
					mapEligible = false
				};
			}

			var manager = map.weatherManager;
			var eligible = CloudVisibility.IsAllowedOn(map);
			CloudAssets.TryGetCloudsFor(map, out var clouds);
			var effective = WeatherCloudProfiles.Effective(manager);
			return new
			{
				success = true,
				modVersion = typeof(Clouds_Main).Assembly.GetName().Version?.ToString(),
				gameVersion = VersionControl.CurrentVersionStringWithRev,
				map = new
				{
					id = map.uniqueID,
					name = map.info?.parent?.LabelCap.ToString(),
					size = new { x = map.Size.x, z = map.Size.z },
					eligible
				},
				weather = new
				{
					current = manager.curWeather?.defName,
					last = manager.lastWeather?.defName,
					transitionAge = manager.curWeatherAge,
					transitionTicks = WeatherManager.TransitionTicks,
					transitionProgress = manager.TransitionLerpFactor,
					currentProfile = DescribeProfile(WeatherCloudProfiles.For(manager.curWeather)),
					lastProfile = DescribeProfile(WeatherCloudProfiles.For(manager.lastWeather)),
					effectiveProfile = DescribeProfile(effective),
					windSpeedFactor = manager.CurWindSpeedFactor,
					windSpeedOffset = manager.CurWindSpeedOffset
				},
				clouds = clouds == null ? null : new
				{
					loaded = true,
					active = clouds.Active,
					paused = clouds.Pause,
					alpha = clouds.Alpha,
					emission = clouds.Emission,
					size = new { min = clouds.Size.min, max = clouds.Size.max },
					speed = clouds.Speed,
					effectiveWindSpeed = clouds.EffectiveWindSpeed,
					particleCount = clouds.ParticleCount,
					maxLifetime = clouds.MaxLifetime,
					clusterOffset = new { x = clouds.ClusterOffset.x, y = clouds.ClusterOffset.y },
					clusterTextureAssigned = clouds.ClusterTextureAssigned,
					gpuInstancingEnabled = clouds.GpuInstancingEnabled,
					materialInstancingEnabled = clouds.MaterialInstancingEnabled,
					shaderSupported = clouds.ShaderSupported,
					shaderPassCount = clouds.ShaderPassCount,
					shader = clouds.ShaderName,
					usesAutoRandomSeed = clouds.UsesAutoRandomSeed,
					randomSeed = clouds.RandomSeed
				},
				camera = new
				{
					rootSize = Find.CameraDriver.RootSize,
					zoom = Find.CameraDriver.CurrentZoom.ToString()
				}
			};
		}

		static void ApplyWeather(WeatherDef weather, float transitionProgress, bool resetParticles)
		{
			var map = Find.CurrentMap ?? throw new InvalidOperationException("No playable current map is loaded.");
			var manager = map.weatherManager;
			if (manager.curWeather != weather)
				manager.TransitionTo(weather);
			manager.curWeatherAge = Mathf.RoundToInt(WeatherManager.TransitionTicks * transitionProgress);
			manager.ResetSkyTargetLerpCache();

			if (CloudVisibility.IsAllowedOn(map) == false)
				return;

			var clouds = CloudAssets.CloudsFor(map, true);
			clouds.UpdateWeather(manager, Find.TickManager, false, true);
			if (resetParticles)
			{
				var seedState = clouds.CaptureSeedState();
				clouds.RestartAndPrewarm(StableSeed(weather.defName));
				clouds.RestoreSeedState(seedState);
				clouds.SynchronizeTime(Find.TickManager);
			}
		}

		static WeatherCaptureState PrepareWeatherCapture(WeatherDef weather)
		{
			ApplyWeather(weather, 1f, false);
			var map = Find.CurrentMap;
			if (CloudVisibility.IsAllowedOn(map) == false)
				throw new InvalidOperationException($"Clouds are disabled on the current map while preparing {weather.defName}.");

			var clouds = CloudAssets.CloudsFor(map, true);
			var seedState = clouds.CaptureSeedState();
			clouds.RestartAndPrewarm(StableSeed(weather.defName));
			clouds.RestoreSeedState(seedState);
			clouds.SynchronizeTime(Find.TickManager);
			return new WeatherCaptureState
			{
				WeatherDefName = weather.defName,
				RootSize = Find.CameraDriver.RootSize,
				Profile = clouds.AppliedProfile,
				ClusterTextureAssigned = clouds.ClusterTextureAssigned,
				GpuInstancingEnabled = clouds.GpuInstancingEnabled,
				ShaderName = clouds.ShaderName,
				ParticleCount = clouds.ParticleCount,
				Alpha = clouds.Alpha
			};
		}

		static SuiteRestoreState CaptureSuiteRestoreState()
		{
			var map = Find.CurrentMap;
			var state = new SuiteRestoreState
			{
				Map = map,
				Weather = map?.weatherManager?.curWeather,
				CameraRootSize = Find.CameraDriver?.RootSize ?? 0f,
				TimeSpeed = Find.TickManager?.CurTimeSpeed ?? TimeSpeed.Paused
			};
			if (map != null && CloudAssets.TryGetCloudsFor(map, out var clouds))
			{
				state.HadCloudSystem = true;
				state.ParticleSeed = clouds.CaptureSeedState();
			}
			return state;
		}

		static void RestoreSuiteState(SuiteRestoreState state)
		{
			if (state.Map == null)
				return;

			if (Find.CurrentMap == state.Map && state.Weather != null)
			{
				var manager = state.Map.weatherManager;
				manager.TransitionTo(state.Weather);
				manager.curWeatherAge = Mathf.RoundToInt(WeatherManager.TransitionTicks);
				manager.ResetSkyTargetLerpCache();
				if (CloudVisibility.IsAllowedOn(state.Map))
				{
					var clouds = CloudAssets.CloudsFor(state.Map, true);
					clouds.UpdateWeather(manager, Find.TickManager, false, true);
					if (state.HadCloudSystem)
					{
						clouds.RestartAndPrewarm(StableSeed(state.Weather.defName));
						clouds.RestoreSeedState(state.ParticleSeed);
					}
				}
			}

			Find.CameraDriver.SetRootSize(state.CameraRootSize);
			Find.TickManager.CurTimeSpeed = state.TimeSpeed;
			if (CloudAssets.TryGetCloudsFor(state.Map, out var restoredClouds))
				restoredClouds.SynchronizeTime(Find.TickManager);
		}

		static object DescribeProfile(WeatherCloudProfile profile)
		{
			return new
			{
				obscurity = profile.Obscurity,
				rain = profile.Rain,
				snow = profile.Snow,
				sand = profile.Sand,
				precipitation = profile.Precipitation,
				overcast = profile.Overcast,
				storm = profile.Storm,
				cover = profile.Cover,
				emission = profile.EmissionRate,
				sizeFactor = profile.SizeFactor,
				brightness = profile.Brightness,
				contrast = profile.Contrast,
				opacity = profile.Opacity,
				edgePower = profile.EdgePower,
				tint = new
				{
					r = profile.Tint.r,
					g = profile.Tint.g,
					b = profile.Tint.b
				},
				clusterCutoff = profile.ClusterCutoff,
				clusterFeather = profile.ClusterFeather
			};
		}

		static object DescribeCaptureState(WeatherCaptureState state)
		{
			return new
			{
				weatherDefName = state.WeatherDefName,
				rootSize = state.RootSize,
				profile = DescribeProfile(state.Profile),
				state.ClusterTextureAssigned,
				state.GpuInstancingEnabled,
				state.ShaderName,
				state.ParticleCount,
				state.Alpha
			};
		}

		static WeatherDef FindWeather(string defName)
		{
			var all = DefDatabase<WeatherDef>.AllDefsListForReading;
			for (var index = 0; index < all.Count; index++)
				if (string.Equals(all[index].defName, defName?.Trim(), StringComparison.Ordinal))
					return all[index];
			return null;
		}

		static string[] AvailableWeatherNames()
		{
			return DefDatabase<WeatherDef>.AllDefsListForReading
				.Select(weather => weather.defName)
				.OrderBy(name => name, StringComparer.Ordinal)
				.ToArray();
		}

		static (List<WeatherDef> Weather, string Error, string[] Available) SelectWeather(string value)
		{
			var available = AvailableWeatherNames();
			if (string.Equals(value?.Trim(), "all", StringComparison.OrdinalIgnoreCase))
			{
				return (
					DefDatabase<WeatherDef>.AllDefsListForReading
						.OrderBy(weather => weather.defName, StringComparer.Ordinal)
						.ToList(),
					null,
					available);
			}

			var result = new List<WeatherDef>();
			var names = (value ?? string.Empty)
				.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(name => name.Trim())
				.Where(name => name.Length > 0)
				.Distinct(StringComparer.Ordinal)
				.ToArray();
			if (names.Length == 0)
				return (result, "At least one WeatherDef name is required.", available);
			foreach (var name in names)
			{
				var weather = FindWeather(name);
				if (weather == null)
					return (result, $"No active WeatherDef has the exact name '{name}'.", available);
				result.Add(weather);
			}
			return (result, null, available);
		}

		static List<float> ParseRootSizes(string value, out string error)
		{
			var result = new List<float>();
			var parts = (value ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var part in parts)
			{
				if (float.TryParse(
					part.Trim(),
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out var rootSize) == false
					|| float.IsNaN(rootSize)
					|| float.IsInfinity(rootSize)
					|| rootSize < 12f
					|| rootSize > 60f)
				{
					error = $"Camera root size '{part.Trim()}' is invalid; use values from 12 through 60.";
					return result;
				}

				if (result.Any(existing => Mathf.Approximately(existing, rootSize)) == false)
					result.Add(rootSize);
			}

			if (result.Count == 0)
			{
				error = "At least one camera root size is required.";
				return result;
			}
			if (result.Count > 12)
			{
				error = "At most 12 camera root sizes may be captured in one suite.";
				return result;
			}

			error = null;
			return result;
		}

		static uint StableSeed(string value)
		{
			unchecked
			{
				var hash = 2166136261u;
				foreach (var character in value ?? string.Empty)
				{
					hash ^= character;
					hash *= 16777619u;
				}
				return hash == 0 ? 1u : hash;
			}
		}

		static string SafeFilePart(string value)
		{
			return new string((value ?? string.Empty)
				.Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_')
				.ToArray());
		}
	}
}
