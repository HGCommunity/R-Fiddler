using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Threading.Tasks;
using FrooxEngine;
using System.Reflection;
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine.UIX;
using System.Collections;
using System.Security.Policy;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Linq;

namespace PrivacyNotif
{
	[HarmonyPatch]
	class PrivacyNotif : ResoniteMod
	{
		public override string Name => "ResoniteFiddler";
		public override string Author => "NepuShiro, Knackrack615";
		public override string Version => "1.0.0";
		public override string Link => "https://github.com/HGCommunity/ResoFiddler";
		private static readonly MethodInfo addNotificationMethod = AccessTools.Method(typeof(NotificationPanel), "AddNotification", new Type[] { typeof(string), typeof(string), typeof(Uri), typeof(colorX), typeof(NotificationType), typeof(string), typeof(Uri), typeof(IAssetProvider<AudioClip>) });
		private static List<string> TrustedDefaults = new List<string>();
		private static Uri previousUri;
		private static Uri ebicFavicon;
		private static DateTime previousUriChange;

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> ENABLED = new("Enabled", "If the hosts permission request should send a notif for all assets.", () => true);

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<string> TRUSTEDURI = new("TrustedURI", "Trusted URIs that don't need permission requests.", () => "google.com, imgur.com, reddit.com, youtube.com, facebook.com, twitter.com, wikipedia.org, wikimedia.org, discordapp.net, resonite.com, skyfrost-archive.resonite.com");

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<int> COOLDOWN = new("cooldown", "The Cooldown between mutliple Notifcations for the same URL in Seconds. 0 to disable", () => 5);

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> NOTIFSOUND = new("NotifSound", "Should there be a sound for the Notif?", () => false);

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<Uri> NOTIF_URI = new("notifuri", "Notifcation Sound Uri", () => new("resdb:///aba6554bd032a406c11b3b0bdb4e1214d2b12808891993e4fb498449f94e37a7.wav"));

		private static ModConfiguration config;

		private static Action<string, string, Uri, colorX, NotificationType, string, Uri, IAssetProvider<AudioClip>> addNotification;

		public override void OnEngineInit()
		{
			config = GetConfiguration();
			config.Save(true);

			Harmony harmony = new Harmony("dev.nepushiro.ResonitePrivacyNotif");
			harmony.PatchAll();

			TrustedDefaults = config.GetValue(TRUSTEDURI)
						.Split(',')
						.Select(s => s.Trim())
						.ToList();

			config.OnThisConfigurationChanged += (e) =>
			{
				if (e.Key == TRUSTEDURI)
				{
					TrustedDefaults = config.GetValue(TRUSTEDURI)
						.Split(',')
						.Select(s => s.Trim())
						.ToList();
				}
			};
		}

		[HarmonyPatch(typeof(AssetManager), nameof(AssetManager.GatherAsset))]
		[HarmonyPrefix]
		private static bool PatchRequester(AssetManager __instance, EngineAssetGatherer ___assetGatherer, ref ValueTask<GatherResult> __result, Uri __0, float __1, SkyFrost.Base.DB_Endpoint? __2)
		{
			if (!config.GetValue(ENABLED)) return true;

			__result = HandleRequest<GatherResult>(__instance, ___assetGatherer, __0, __1, __2);
			return false;
		}

		[HarmonyPatch(typeof(AssetManager), nameof(AssetManager.GatherAssetFile))]
		[HarmonyPrefix]
		private static bool PatchRequester2(AssetManager __instance, EngineAssetGatherer ___assetGatherer, ref ValueTask<string> __result, Uri __0, float __1, SkyFrost.Base.DB_Endpoint? __2)
		{
			if (!config.GetValue(ENABLED)) return true;

			__result = HandleRequest<string>(__instance, ___assetGatherer, __0, __1, __2);
			return false;
		}

		[HarmonyPatch(typeof(NotificationPanel))]
		private static class NotificationPanelPatches
		{
			[HarmonyPostfix]
			[HarmonyPatch("OnAttach")]
			private static void OnAttachPostfix(NotificationPanel __instance)
			{
				addNotification = AccessTools.MethodDelegate<Action<string, string, Uri, colorX, NotificationType, string, Uri, IAssetProvider<AudioClip>>>(addNotificationMethod, NotificationPanel.Current);
			}
		}

		public static async Task<bool> AddNotification(colorX backgroundColor, Uri target, string notficationText = "N/A")
		{
			if (!config.GetValue(ENABLED) || target == ebicFavicon || (target == previousUri && DateTime.Now - previousUriChange < TimeSpan.FromSeconds(config.GetValue(COOLDOWN)))) return true;

			try
			{
				if (config.GetValue(NOTIFSOUND))
				{
					StaticAudioClip clip = null;
					NotificationPanel.Current.Slot.ForeachComponent<StaticAudioClip>((a) =>
					{
						if (a.URL == config.GetValue(NOTIF_URI))
						{
							clip = a;
							return false;
						}
						return true;
					});

					clip ??= NotificationPanel.Current.Slot.AttachAudioClip(config.GetValue(NOTIF_URI), true);
					NotificationPanel.Current.Slot.PlayOneShot(clip, 1f, false, 1f, parent: true, AudioDistanceSpace.Global);
				}

				World currentWorld = Engine.Current.WorldManager.FocusedWorld;
				User localUser = currentWorld.LocalUser;

				string uriString = string.IsNullOrEmpty(target.ToString()) ? "https://unknown.url" : target.ToString();
				Uri worldThumbnail = new Uri("https://pic.nepunep.xyz/u/wretchedundefinedintrepidundefinedfantail.png");
				Uri epicFavicon = new Uri(await Helpers.GetFaviconUrlAsync(uriString) ?? "https://pic.nepunep.xyz/u/wretchedundefinedintrepidundefinedfantail.png");
                ebicFavicon = epicFavicon;

                // Check if URI == GetFaviconUrlAsync(uriString) and dont show it to prevent duplicate notifications
                if (currentWorld != null && !currentWorld.IsUserspace() && currentWorld.Name.ToLower() != "local")
				{
					worldThumbnail = new(currentWorld?.GenerateSessionInfo()?.ThumbnailUrl);
				}

				if (!string.IsNullOrEmpty(uriString) && await Helpers.IsValidImageUrl(uriString))
				{
					worldThumbnail = target;
				}

				NotificationPanel.Current.RunSynchronously(() =>
				{
					addNotification(null, uriString, worldThumbnail, backgroundColor, Notif.Type, notficationText, epicFavicon, null);
					AddHyperLink(NotificationPanel.Current, target);
				});

				previousUri = target;
				previousUriChange = DateTime.Now;

				return true;
			}
			catch (Exception ex)
			{
				Msg("Error adding notification: " + ex);
				return true;
			}
		}

		private static void AddHyperLink(NotificationPanel notificationPanel, Uri uri)
		{
			try
			{
				// _items is List<NotificationPanel.NotificationItem>
				var items = Traverse.Create(notificationPanel).Field("_items").GetValue<IList>();
				var root = Traverse.Create(items[items.Count - 1]).Field("root").GetValue<Slot>();

				root.AttachComponent<Hyperlink>().URL.Value = uri;
			}
			catch (Exception ex)
			{
				Msg("Error adding world focus: " + ex);
			}
		}

		private static async Task<Uri> GetUserThumbnail(string userId)
		{
			try
			{
				var cloudUserProfile = (await Engine.Current.Cloud.Users.GetUser(userId))?.Entity?.Profile;
				Uri.TryCreate(cloudUserProfile?.IconUrl, UriKind.Absolute, out Uri thumbnail);
				thumbnail ??= OfficialAssets.Graphics.Thumbnails.AnonymousHeadset;

				return thumbnail;
			}
			catch (Exception ex)
			{
				Msg("Error getting user thumbnail: " + ex);
				return null;
			}
		}

		private static async ValueTask<T> HandleRequest<T>(AssetManager assetManager, EngineAssetGatherer assetGatherer, Uri uri, float priority, SkyFrost.Base.DB_Endpoint? endpointOverwrite)
		{
			if (uri.Scheme == "resdb" || uri.Scheme == "local" || TrustedDefaults.Any(a => uri.Host.EndsWith(a)) || uri.Host.EndsWith("resonite.com") || uri.AbsolutePath.Contains("favicon.ico") || await AskForPermission(uri))
			{
				if (typeof(T) == typeof(string))
				{
					return (T)(object)await (await assetGatherer.Gather(uri, priority, endpointOverwrite).ConfigureAwait(false)).GetFile().ConfigureAwait(false);
				}
				else if (typeof(T) == typeof(GatherResult))
				{
					return (T)(object)await assetGatherer.Gather(uri, priority, endpointOverwrite);
				}
			}
			else
			{
				Debug($"No permissions to load asset at {uri}");
			}
			return default;
		}

		private static async Task<bool> AskForPermission(Uri target)
		{
			Debug("Got Request for", target);

			string notificationText = $"{target.Scheme.ToUpper()} URI Requested";
			var notificationColor = target.Scheme switch
			{
				"http" => RadiantUI_Constants.Dark.RED,
				"https" => RadiantUI_Constants.Dark.GREEN,
				"wss" => RadiantUI_Constants.Dark.GREEN,
				_ => RadiantUI_Constants.Dark.PURPLE,
			};

			return await AddNotification(notificationColor, target, notificationText);
		}

		public class Notif
		{
			public static NotificationType Type
			{
				get
				{
					return config.GetValue(NOTIFSOUND) ? NotificationType.Full : NotificationType.ToastOnly;
				}
			}
		} 
	}
}
