using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace R_Fiddler;

[ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BasePlugin
{
    private static ManualLogSource PluginLog = null!;
    
    // Trust me defining those here is easier
    private const int CLEANUP_INTERVAL_MINUTES = 1;
    private const int COOLDOWN_MULTIPLIER = 2;
    private const char DOMAIN_SEPARATOR = '.';
    private const char PATH_SEPARATOR = '/';
    private const char QUERY_SEPARATOR = '?';

    private static readonly MethodInfo addNotificationMethod = AccessTools.Method(
        typeof(NotificationPanel),
        "AddNotification",
        new Type[] {
            typeof(string), typeof(string), typeof(Uri), typeof(colorX),
            typeof(NotificationType), typeof(string), typeof(Uri), typeof(IAssetProvider<AudioClip>)
        });

    // Locks
    private static readonly ConcurrentDictionary<string, DateTime> domainCooldowns = new();
    private static readonly ConcurrentDictionary<Uri, Task<string>> faviconCache = new();
    private static readonly ReaderWriterLockSlim trustedLock = new();
    private static HashSet<string> trustedDomains = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> idnCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object previousFaviconLock = new();
    private static Uri? previousFavicon;

    // Lazy initialization
    private static readonly Lazy<IdnMapping> idnMapping = new(() => new IdnMapping());

    // Cleanup timer
    private static Timer? cleanupTimer;
    private static DateTime lastCleanup = DateTime.MinValue;
    
    // BPNX CFG
    private static ConfigEntry<bool> configEnabled = null!;
    private static ConfigEntry<string> configTrustedUri = null!;
    private static ConfigEntry<int> configCooldown = null!;
    private static ConfigEntry<string> configPlaceholderUri = null!;
    private static ConfigEntry<bool> configNotifSound = null!;
    private static ConfigEntry<string> configNotifSoundUri = null!;
    
    private static Action<string, string, Uri, colorX, NotificationType, string, Uri, IAssetProvider<AudioClip>>? addNotification;

    public override void Load()
    {
        PluginLog = Log;
        InitializeConfig();
        HarmonyInstance.PatchAll();
        RebuildTrustedSet();
        InitializeCleanupTimer();
    }
    
    private void InitializeConfig()
    {
        configEnabled = Config.Bind("General", "Enabled", true, 
            "Toggle notifications for external asset loading.");
        
        configTrustedUri = Config.Bind("General", "TrustedURI", 
            "google.com, imgur.com, reddit.com, youtube.com, facebook.com, twitter.com, wikipedia.org, wikimedia.org, discordapp.net, discordapp.com, resonite.com",
            "List of trusted domains that won't trigger notifications.");
        
        configCooldown = Config.Bind("General", "Cooldown", 5,
            "Set a cooldown period (in seconds) between notifications for the same domain. 0 to disable");
        
        configPlaceholderUri = Config.Bind("General", "PlaceholderURI",
            "resdb:///264a3cdc5c149326aefd44d40b23a068032c716d3966ca5dc883775eb236ac10.webp",
            "Specify an image to use when no image is available.");
        
        configNotifSound = Config.Bind("General", "NotifSound", false,
            "Enable sound for notifications.");
        
        configNotifSoundUri = Config.Bind("General", "NotifURI",
            "resdb:///aba6554bd032a406c11b3b0bdb4e1214d2b12808891993e4fb498449f94e37a7.wav",
            "Set the sound file for notifications.");
        
        configTrustedUri.SettingChanged += (_, _) => RebuildTrustedSet();
    }
    
    
    private static void InitializeCleanupTimer()
    {
        cleanupTimer?.Dispose();
        cleanupTimer = new Timer(
            _ => CleanupCaches(),
            null,
            TimeSpan.FromMinutes(CLEANUP_INTERVAL_MINUTES),
            TimeSpan.FromMinutes(CLEANUP_INTERVAL_MINUTES)
        );
    }

    private static void RebuildTrustedSet()
    {
        var newSet = ParseTrustedDomains(configTrustedUri.Value);

        trustedLock.EnterWriteLock();
        try
        {
            trustedDomains = newSet;
        }
        finally
        {
            trustedLock.ExitWriteLock();
        }
    }

    private static HashSet<string> ParseTrustedDomains(string trustedUriString)
    {
        return new HashSet<string>(
            trustedUriString
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => NormalizeHost(s)),
            StringComparer.OrdinalIgnoreCase
        );
    }

    private static string NormalizeHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return string.Empty;

        host = host.Trim().Trim(DOMAIN_SEPARATOR);

        // Cache IDN conversions
        return idnCache.GetOrAdd(host, h =>
        {
            try
            {
                return idnMapping.Value.GetAscii(h).ToLowerInvariant();
            }
            catch
            {
                return h.ToLowerInvariant();
            }
        });
    }

    private static bool IsTrustedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        var normalizedHost = NormalizeHost(host);

        trustedLock.EnterReadLock();
        try
        {
            return trustedDomains.Any(trusted =>
                normalizedHost == trusted ||
                normalizedHost.EndsWith($"{DOMAIN_SEPARATOR}{trusted}", StringComparison.Ordinal));
        }
        finally
        {
            trustedLock.ExitReadLock();
        }
    }

    private static bool ShouldSkipNotification(Uri target, out string domain)
    {
        domain = target.Host.ToLowerInvariant();

        // Check if same as previous favicon
        lock (previousFaviconLock)
        {
            if (target == previousFavicon) return true;
        }

        // Check cooldown
        var cooldownSeconds = configCooldown.Value;
        if (cooldownSeconds <= 0) return false;

        var now = DateTime.UtcNow;
        if (domainCooldowns.TryGetValue(domain, out var lastNotification))
        {
            if (now - lastNotification < TimeSpan.FromSeconds(cooldownSeconds))
            {
                return true;
            }
        }

        // Update cooldown
        domainCooldowns[domain] = now;
        return false;
    }

    private static void CleanupCaches()
    {
        try
        {
            var now = DateTime.UtcNow;
            var cooldownSeconds = configCooldown.Value;

            if (cooldownSeconds > 0)
            {
                var cutoff = now - TimeSpan.FromSeconds(cooldownSeconds * COOLDOWN_MULTIPLIER);
                var expiredDomains = domainCooldowns
                    .Where(kvp => kvp.Value < cutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var domain in expiredDomains)
                {
                    domainCooldowns.TryRemove(domain, out _);
                }
            }

            // Clean up old favicon cache entries
            if (faviconCache.Count > 100)
            {
                faviconCache.Clear();
            }

            // Clean up IDN cache if it gets too large (we don't wanna explode)
            if (idnCache.Count > 500)
            {
                idnCache.Clear();
            }
        }
        catch (Exception ex)
        {
            PluginLog.LogError($"Error during cleanup: {ex}");
        }
    }

    [HarmonyPatch(typeof(AssetManager))]
    private static class AssetManagerPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("GatherAsset")]
        private static bool GatherAssetPrefix(AssetManager __instance, EngineAssetGatherer ___assetGatherer,
            ref ValueTask<GatherResult> __result, Uri __0, float __1, SkyFrost.Base.DB_Endpoint? __2)
        {
            if (!configEnabled.Value) return true;
            __result = HandleRequest<GatherResult>(__instance, ___assetGatherer, __0, __1, __2);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("GatherAssetFile")]
        private static bool GatherAssetFilePrefix(AssetManager __instance, EngineAssetGatherer ___assetGatherer,
            ref ValueTask<string> __result, Uri __0, float __1, SkyFrost.Base.DB_Endpoint? __2)
        {
            if (!configEnabled.Value) return true;
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
            if (addNotificationMethod != null && NotificationPanel.Current != null)
            {
                addNotification = (Action<string, string, Uri, colorX, NotificationType, string, Uri, IAssetProvider<AudioClip>>)
                    Delegate.CreateDelegate(
                        typeof(Action<string, string, Uri, colorX, NotificationType, string, Uri, IAssetProvider<AudioClip>>),
                        NotificationPanel.Current,
                        addNotificationMethod
                    );
            }
        }
    }

    private static async Task<bool> AddNotification(colorX backgroundColor, Uri target, string notificationText = "N/A")
    {
        if (!configEnabled.Value) return true;

        if (ShouldSkipNotification(target, out _)) return true;

        try
        {
            await PlayNotificationSound().ConfigureAwait(false);

            var world = Engine.Current.WorldManager.FocusedWorld;
            var uriDisplay = FormatUriDisplay(target);
            var worldThumbnail = await GetWorldThumbnail(world, target).ConfigureAwait(false);
            var favicon = await GetFaviconAsync(target).ConfigureAwait(false);

            lock (previousFaviconLock)
            {
                previousFavicon = favicon;
            }

            NotificationPanel.Current.RunSynchronously(() =>
            {
                if (addNotification != null)
                {
                    addNotification(null!, uriDisplay, worldThumbnail, backgroundColor,
                        GetNotificationType(), notificationText, favicon, null!);
                    AddHyperLink(NotificationPanel.Current, target);
                }
                else
                {
                    PluginLog.LogError("addNotification delegate is null - NotificationPanel may not be properly initialized");
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            PluginLog.LogError($"Error adding notification: {ex}");
            return true;
        }
    }

    private static async Task PlayNotificationSound()
    {
        if (!configNotifSound.Value) return;

        await Task.Run(() =>
        {
            StaticAudioClip? clip = null;
            var soundUri = new Uri(configNotifSoundUri.Value);

            NotificationPanel.Current.Slot.ForeachComponent<StaticAudioClip>(a =>
            {
                if (a.URL == soundUri)
                {
                    clip = a;
                    return false;
                }
                return true;
            });

            clip ??= NotificationPanel.Current.Slot.AttachAudioClip(soundUri, true);
            NotificationPanel.Current.Slot.PlayOneShot(clip, 1f, false, true, 1f, true, AudioDistanceSpace.Global);
        }).ConfigureAwait(false);
    }

    private static string FormatUriDisplay(Uri target)
    {
        var pathParts = target.AbsolutePath.Split(QUERY_SEPARATOR)[0].Split(PATH_SEPARATOR);
        var lastPart = pathParts.LastOrDefault() ?? string.Empty;
        return $"<nobr>{target.Host}...{lastPart}";
    }

    private static async Task<Uri> GetWorldThumbnail(World world, Uri target)
    {
        var placeholder = new Uri(configPlaceholderUri.Value);

        if (await Helpers.IsValidImageUrl(target).ConfigureAwait(false))
        {
            return target;
        }

        if (world != null && !world.IsUserspace() &&
            !string.Equals(world.Name, "local", StringComparison.OrdinalIgnoreCase))
        {
            var thumbnailUrl = world.GenerateSessionInfo()?.ThumbnailUrl;
            if (!string.IsNullOrEmpty(thumbnailUrl))
            {
                return new Uri(thumbnailUrl);
            }
        }

        return placeholder;
    }

    private static async Task<Uri> GetFaviconAsync(Uri target)
    {
        try
        {
            var faviconUrl = await faviconCache.GetOrAdd(target, async uri =>
            {
                return await Helpers.GetFaviconUrlAsync(uri).ConfigureAwait(false)
                       ?? configPlaceholderUri.Value;
            }).ConfigureAwait(false);

            return new Uri(faviconUrl);
        }
        catch
        {
            return new Uri(configPlaceholderUri.Value);
        }
    }

    private static void AddHyperLink(NotificationPanel notificationPanel, Uri uri)
    {
        try
        {
            var items = Traverse.Create(notificationPanel).Field("_items").GetValue<System.Collections.IList>();
            if (items?.Count > 0)
            {
                var root = Traverse.Create(items[items.Count - 1]).Field("root").GetValue<Slot>();
                if (root != null)
                {
                    var link = root.AttachComponent<Hyperlink>();
                    link.URL.Value = uri;
                    link.Reason.Value = "Open Asset in Browser";
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog.LogError($"Error adding hyperlink: {ex}");
        }
    }

    private static async ValueTask<T> HandleRequest<T>(AssetManager assetManager, EngineAssetGatherer assetGatherer,
        Uri uri, float priority, SkyFrost.Base.DB_Endpoint? endpointOverwrite)
    {
        if (IsAllowedUri(uri) || await AskForPermission(uri).ConfigureAwait(false))
        {
            if (typeof(T) == typeof(string))
            {
                var result = await assetGatherer.Gather(uri, priority, endpointOverwrite).ConfigureAwait(false);
                return (T)(object)await result.GetFile().ConfigureAwait(false);
            }
            else if (typeof(T) == typeof(GatherResult))
            {
                return (T)(object)await assetGatherer.Gather(uri, priority, endpointOverwrite).ConfigureAwait(false);
            }
        }
        else
        {
            PluginLog.LogDebug($"No permissions to load asset at {uri}");
        }

        return default;
    }

    private static bool IsAllowedUri(Uri uri)
    {
        return uri.Scheme == "resdb" ||
               uri.Scheme == "local" ||
               IsTrustedHost(uri.Host);
    }

    private static async Task<bool> AskForPermission(Uri target)
    {
        PluginLog.LogDebug($"Got Request for {target}");

        var (notificationText, notificationColor) = GetNotificationDetails(target.Scheme);
        return await AddNotification(notificationColor, target, notificationText).ConfigureAwait(false);
    }

    private static (string text, colorX color) GetNotificationDetails(string scheme)
    {
        var text = $"{scheme.ToUpperInvariant()} URI Requested";
        var color = scheme switch
        {
            "http" => RadiantUI_Constants.Dark.RED,
            "https" => RadiantUI_Constants.Dark.GREEN,
            "ws" => RadiantUI_Constants.Dark.RED,
            "wss" => RadiantUI_Constants.Dark.GREEN,
            _ => RadiantUI_Constants.Dark.PURPLE,
        };

        return (text, color);
    }

    private static NotificationType GetNotificationType()
    {
        return configNotifSound.Value
            ? NotificationType.Full
            : NotificationType.ToastOnly;
    }
}
