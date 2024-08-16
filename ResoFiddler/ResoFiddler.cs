using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;

namespace ResoFiddler
{
	[HarmonyPatch]
	class ResoFiddler : ResoniteMod
	{
		public override string Name => "R-Fiddler";
		public override string Author => "NepuShiro, Knackrack615";
		public override string Version => "1.0.0";
		public override string Link => "https://github.com/HGCommunity/R-Fiddler";
		private static readonly MethodInfo addNotificationMethod = AccessTools.Method(typeof(NotificationPanel), "AddNotification", new Type[] { typeof(string), typeof(string), typeof(Uri), typeof(colorX), typeof(NotificationType), typeof(string), typeof(Uri), typeof(IAssetProvider<AudioClip>) });
		private static List<string> TrustedDefaults = new List<string>();
		private static Uri previousUri;
		private static Uri previousFavicon;
		private static DateTime previousUriChange;

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> ENABLED = new("Enabled", "If the hosts permission request should send a notif for all assets.", () => true);

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<string> TRUSTEDURI = new("TrustedURI", "Trusted URIs that don't need permission requests.", () => "google.com, imgur.com, reddit.com, youtube.com, facebook.com, twitter.com, wikipedia.org, wikimedia.org, discordapp.net, resonite.com, skyfrost-archive.resonite.com");

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<int> COOLDOWN = new("cooldown", "The Cooldown between mutliple Notifcations for the same URL in Seconds. 0 to disable", () => 5);

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<Uri> PLACEHOLDERURI = new("placeholderuri", "The image to use when there's no image available", () => new("https://resonite.com/assets/images/logo.jpg"));

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> NOTIFSOUND = new("NotifSound", "Should there be a sound for the Notif?", () => false);

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<Uri> NOTIFSOUNDURI = new("notifuri", "Notifcation Sound Uri", () => new("resdb:///aba6554bd032a406c11b3b0bdb4e1214d2b12808891993e4fb498449f94e37a7.wav"));

		private static ModConfiguration config;

		private static Action<string, string, Uri, colorX, NotificationType, string, Uri, IAssetProvider<AudioClip>> addNotification;

		public override void OnEngineInit()
		{
			config = GetConfiguration();
			config.Save(true);

			Harmony harmony = new Harmony("dev.nepushiro.ResoniteR-Fiddler");
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

		[HarmonyPatch(typeof(AssetManager))]
		private static class AssetManagerPatch 
		{
			
			[HarmonyPrefix]
			[HarmonyPatch("GatherAsset")]
			private static bool GatherAssetPrefix(AssetManager __instance, EngineAssetGatherer ___assetGatherer, ref ValueTask<GatherResult> __result, Uri __0, float __1, SkyFrost.Base.DB_Endpoint? __2)
			{
				if (!config.GetValue(ENABLED)) return true;

				__result = HandleRequest<GatherResult>(__instance, ___assetGatherer, __0, __1, __2);
				return false;
			}

			[HarmonyPrefix]
			[HarmonyPatch("GatherAssetFile")]
			private static bool GatherAssetFilePrefix(AssetManager __instance, EngineAssetGatherer ___assetGatherer, ref ValueTask<string> __result, Uri __0, float __1, SkyFrost.Base.DB_Endpoint? __2)
			{
				if (!config.GetValue(ENABLED)) return true;

				__result = HandleRequest<string>(__instance, ___assetGatherer, __0, __1, __2);
				return false;
			}
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
			if (!config.GetValue(ENABLED) || target == previousFavicon || (target == previousUri && DateTime.Now - previousUriChange < TimeSpan.FromSeconds(config.GetValue(COOLDOWN)))) return true;

			try
			{
				if (config.GetValue(NOTIFSOUND))
				{
					StaticAudioClip clip = null;
					NotificationPanel.Current.Slot.ForeachComponent<StaticAudioClip>((a) =>
					{
						if (a.URL == config.GetValue(NOTIFSOUNDURI))
						{
							clip = a;
							return false;
						}
						return true;
					});

					clip ??= NotificationPanel.Current.Slot.AttachAudioClip(config.GetValue(NOTIFSOUNDURI), true);
					NotificationPanel.Current.Slot.PlayOneShot(clip, 1f, false, 1f, parent: true, AudioDistanceSpace.Global);
				}

				World currentWorld = Engine.Current.WorldManager.FocusedWorld;
				User localUser = currentWorld.LocalUser;

				string[] uriPath = target.AbsolutePath.Split('?')[0].Split('/');
				string uriStringified = $" {target.Host}...{string.Join("", uriPath.Skip(uriPath.Length - 1))} ";
				
				Uri worldThumbnail = config.GetValue(PLACEHOLDERURI);
				Uri defaultFavicon = config.GetValue(PLACEHOLDERURI);
				
				Uri favicon = new Uri(await Helpers.GetFaviconUrlAsync(target));
				Uri userIcon = await GetUserThumbnail(localUser?.UserID ?? Engine.Current?.Cloud?.CurrentUserID ?? "U-Resonite");
				if (favicon != null)
				{
					defaultFavicon = favicon;
				}
				else if (userIcon != null)
				{
					defaultFavicon = userIcon;
				}
				
				previousFavicon = defaultFavicon;

				if (currentWorld != null && !currentWorld.IsUserspace() && currentWorld.Name.ToLower() != "local")
				{
					worldThumbnail = new(currentWorld?.GenerateSessionInfo()?.ThumbnailUrl);
				}

				if (await Helpers.IsValidImageUrl(target))
				{
					worldThumbnail = target;
				}

				NotificationPanel.Current.RunSynchronously(() =>
				{
					addNotification(null, uriStringified, worldThumbnail, backgroundColor, Notif.Type, notficationText, defaultFavicon, null);
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
				"ws" => RadiantUI_Constants.Dark.RED,
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
