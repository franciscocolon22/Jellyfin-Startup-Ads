using System;

namespace Jellyfin.Plugin.StartupAds.ClientInjection
{
    /// <summary>
    /// Pure string helpers for adding the plugin's client &lt;script&gt; tag to an HTML document.
    /// Kept free of ASP.NET types so it is trivially unit-testable.
    /// </summary>
    public static class IndexHtmlInjector
    {
        /// <summary>Marker id used to detect (and, if ever needed, remove) exactly our tag.</summary>
        public const string Marker = "startup-ads-inject";

        public static bool IsAlreadyInjected(string html)
            => html.Contains(Marker, StringComparison.Ordinal);

        /// <summary>
        /// Builds the script tag. <paramref name="pathPrefix"/> is any reverse-proxy / base-URL
        /// prefix (e.g. <c>"/jellyfin"</c>, or <c>""</c> for a root install). The <c>src</c> is
        /// <b>absolute</b>: jellyfin-web is served under <c>/web</c> but the plugin API is at the
        /// site root, so a relative <c>src</c> would 404 as <c>/web/StartupAds/ClientScript</c>.
        /// </summary>
        public static string ScriptTag(string pathPrefix = "")
        {
            var prefix = string.IsNullOrEmpty(pathPrefix) ? string.Empty : "/" + pathPrefix.Trim('/');
            return "<script id=\"" + Marker + "\" src=\"" + prefix + "/StartupAds/ClientScript\" defer></script>";
        }

        /// <summary>
        /// Returns <paramref name="html"/> with the script tag inserted immediately before the last
        /// <c>&lt;/body&gt;</c> (or appended if there is none). Idempotent: if the marker is already
        /// present the input is returned unchanged.
        /// </summary>
        public static string Inject(string html, string pathPrefix = "")
        {
            if (string.IsNullOrEmpty(html) || IsAlreadyInjected(html))
            {
                return html;
            }

            var tag = ScriptTag(pathPrefix);
            var idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            return idx >= 0
                ? html[..idx] + tag + html[idx..]
                : html + tag;
        }
    }
}
