using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ResoFiddler
{
    public class Helpers
    {
        public static async Task<string> GetFaviconUrlAsync(Uri uri)
        {
            string subdomain = $"{uri.Scheme}://{uri.Host}";
            string mainDomain = GetMainDomain(uri);

            // Try subdomain first
            string faviconUrl = await TryGetFaviconUrl(subdomain);
            if (faviconUrl != null)
            {
                return faviconUrl;
            }

            // If not found on subdomain, try main domain
            if (subdomain != mainDomain)
            {
                faviconUrl = await TryGetFaviconUrl(mainDomain);
                if (faviconUrl != null)
                {
                    return faviconUrl;
                }
            }

            return null;
        }

        public static string GetMainDomain(Uri uri)
        {
            string[] host = uri.Host.Split('.');
            string mainUri = uri.Host;

            if (host.Length > 2)
            {
                // For subdomains, we want to keep the last two parts
                mainUri = string.Join(".", host.Skip(host.Length - 2));
            }

            return $"{uri.Scheme}://{mainUri}";
        }

        private static async Task<string> TryGetFaviconUrl(string domain)
        {
            using var httpClient = new HttpClient();
            try
            {
                // Check if favicon.ico exists
                var faviconUrl = $"{domain}/favicon.ico";
                var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, faviconUrl));
                if (response.IsSuccessStatusCode)
                {
                    return faviconUrl;
                }

                // If favicon.ico doesn't exist, search for it in the HTML
                string html = await httpClient.GetStringAsync(domain);
                return ExtractFaviconUrlFromHtml(html, domain);
            }
            catch { return null; }
        }
        private static string ExtractFaviconUrlFromHtml(string html, string domain)
        {
            string[] patterns = {
        @"<link[^>]*rel=[""'](?:shortcut )?icon[""'][^>]*href=[""']([^""']+)",
        @"<link[^>]*href=[""']([^""']+)[^>]*rel=[""'](?:shortcut )?icon[""']"
    };

            foreach (string pattern in patterns)
            {
                Match match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string faviconUrl = match.Groups[1].Value;
                    return NormalizeUrl(faviconUrl, domain);
                }
            }

            return null;
        }

        private static string NormalizeUrl(string url, string domain)
        {
            if (url.StartsWith("//"))
            {
                return $"https:{url}";
            }
            else if (url.StartsWith("/"))
            {
                return $"{domain}{url}";
            }
            else if (!url.StartsWith("http"))
            {
                return $"{domain}/{url}";
            }
            return url;
        }

        public static async Task<bool> IsValidImageUrl(Uri url)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10); // Set a timeout to avoid hanging

                // Send a HEAD request to get headers without downloading the full content
                using var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                if (!response.IsSuccessStatusCode)
                    return false;

                // Check if the Content-Type header indicates an image
                if (response.Content.Headers.ContentType == null)
                    return false;

                string contentType = response.Content.Headers.ContentType.MediaType.ToLower();

                return contentType.StartsWith("image/");
            }
            catch { return false; }
        }
    }
}
