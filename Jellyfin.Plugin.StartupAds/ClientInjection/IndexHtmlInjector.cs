using System;

namespace Jellyfin.Plugin.StartupAds.ClientInjection
{
    /// <summary>
    /// Pure string helpers for adding / removing the plugin's client &lt;script&gt; tag in an
    /// HTML document. Kept free of ASP.NET types so it is trivially unit-testable.
    /// </summary>
    public static class IndexHtmlInjector
    {
        /// <summary>Marker id used to detect and remove exactly our tag.</summary>
        public const string Marker = "startup-ads-inject";

        /// <summary>The tag inserted into index.html. <c>src</c> is relative so it works under any base path.</summary>
        public const string ScriptTag =
            "<script id=\"" + Marker + "\" src=\"StartupAds/ClientScript\" defer></script>";

        public static bool IsAlreadyInjected(string html)
            => html.Contains(Marker, StringComparison.Ordinal);

        /// <summary>
        /// Returns <paramref name="html"/> with <see cref="ScriptTag"/> inserted immediately before
        /// the last <c>&lt;/body&gt;</c> (or appended if there is none). Idempotent: if the marker is
        /// already present the input is returned unchanged.
        /// </summary>
        public static string Inject(string html)
        {
            if (string.IsNullOrEmpty(html) || IsAlreadyInjected(html))
            {
                return html;
            }

            var idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            return idx >= 0
                ? html[..idx] + ScriptTag + html[idx..]
                : html + ScriptTag;
        }
    }
}
