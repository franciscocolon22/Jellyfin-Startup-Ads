using Jellyfin.Plugin.StartupAds.ClientInjection;
using Xunit;

namespace Jellyfin.Plugin.StartupAds.Tests
{
    public class IndexHtmlInjectorTests
    {
        [Fact]
        public void InjectsBeforeClosingBody()
        {
            var html = "<html><head></head><body><div id=\"app\"></div></body></html>";
            var result = IndexHtmlInjector.Inject(html);
            var tag = IndexHtmlInjector.ScriptTag();

            Assert.Contains(tag, result);
            Assert.EndsWith("</script></body></html>", result);
            Assert.Equal(html.Length + tag.Length, result.Length);
        }

        [Fact]
        public void IsIdempotent()
        {
            var once = IndexHtmlInjector.Inject("<body></body>");
            var twice = IndexHtmlInjector.Inject(once);
            Assert.Equal(once, twice);
            Assert.Equal(1, CountOccurrences(twice, IndexHtmlInjector.Marker));
        }

        [Fact]
        public void AppendsWhenNoBodyTag()
        {
            var result = IndexHtmlInjector.Inject("<html>no body close</html>");
            Assert.EndsWith(IndexHtmlInjector.ScriptTag(), result);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void EmptyInputReturnedUnchanged(string? html)
        {
            Assert.Equal(html, IndexHtmlInjector.Inject(html!));
        }

        [Fact]
        public void ScriptSrcIsAbsoluteFromSiteRoot()
        {
            // jellyfin-web is served under /web but the plugin API is at the root, so a relative
            // src would resolve to /web/StartupAds/ClientScript and 404.
            Assert.Contains("src=\"/StartupAds/ClientScript\"", IndexHtmlInjector.ScriptTag());
        }

        [Theory]
        [InlineData("", "src=\"/StartupAds/ClientScript\"")]
        [InlineData("/jellyfin", "src=\"/jellyfin/StartupAds/ClientScript\"")]
        [InlineData("jellyfin", "src=\"/jellyfin/StartupAds/ClientScript\"")]
        [InlineData("/media/jf/", "src=\"/media/jf/StartupAds/ClientScript\"")]
        public void ReverseProxyPrefixIsHonoured(string prefix, string expected)
        {
            Assert.Contains(expected, IndexHtmlInjector.ScriptTag(prefix));
            Assert.Contains(expected, IndexHtmlInjector.Inject("<body></body>", prefix));
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            var count = 0;
            var i = 0;
            while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                i += needle.Length;
            }

            return count;
        }
    }
}
