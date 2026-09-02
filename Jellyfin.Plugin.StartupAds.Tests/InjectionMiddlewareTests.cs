using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Plugin.StartupAds.ClientInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Jellyfin.Plugin.StartupAds.Tests
{
    /// <summary>
    /// Integration tests for the real ASP.NET Core pipeline (TestServer) — NOT a live Jellyfin.
    /// They prove the IStartupFilter is honoured and the middleware buffers + rewrites the body.
    /// </summary>
    public class InjectionMiddlewareTests
    {
        private const string Html = "<html><head><title>Jellyfin</title></head><body><div id=\"reactRoot\"></div></body></html>";

        private static TestServer BuildServer(string bodyContentType, string responseBody)
        {
            var builder = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddSingleton<IStartupFilter, StartupAdsStartupFilter>();
                })
                .Configure(app =>
                {
                    app.Run(async ctx =>
                    {
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = bodyContentType;
                        await ctx.Response.WriteAsync(responseBody);
                    });
                });

            return new TestServer(builder);
        }

        [Fact]
        public async Task InjectsScriptIntoIndexHtml()
        {
            using var server = BuildServer("text/html; charset=utf-8", Html);
            var body = await server.CreateClient().GetStringAsync("/web/index.html");

            Assert.Contains(IndexHtmlInjector.Marker, body);
            Assert.Contains("</script></body>", body);
        }

        [Fact]
        public async Task InjectsForBarePathAndRoot()
        {
            using var server = BuildServer("text/html", Html);
            var client = server.CreateClient();

            Assert.Contains(IndexHtmlInjector.Marker, await client.GetStringAsync("/web/"));
            Assert.Contains(IndexHtmlInjector.Marker, await client.GetStringAsync("/"));
        }

        [Fact]
        public async Task DoesNotTouchNonHtmlResponses()
        {
            using var server = BuildServer("application/javascript", "console.log('hi');");
            var body = await server.CreateClient().GetStringAsync("/web/main.js");

            Assert.DoesNotContain(IndexHtmlInjector.Marker, body);
            Assert.Equal("console.log('hi');", body);
        }

        [Fact]
        public async Task DoesNotTouchApiResponses()
        {
            using var server = BuildServer("text/html", Html);
            var body = await server.CreateClient().GetStringAsync("/Users/AuthenticateByName");
            Assert.DoesNotContain(IndexHtmlInjector.Marker, body);
        }

        [Fact]
        public async Task InjectsEvenWhenClientRequestsCompression()
        {
            using var server = BuildServer("text/html", Html);
            var client = server.CreateClient();
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, br");

            var resp = await client.GetAsync("/web/index.html");
            var body = await resp.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Contains(IndexHtmlInjector.Marker, body);
            Assert.Empty(resp.Content.Headers.ContentEncoding);
        }

        [Fact]
        public async Task DoesNotDoubleInjectWhenMarkerAlreadyPresent()
        {
            var already = IndexHtmlInjector.Inject(Html);
            using var server = BuildServer("text/html", already);
            var body = await server.CreateClient().GetStringAsync("/web/index.html");

            var first = body.IndexOf(IndexHtmlInjector.Marker, System.StringComparison.Ordinal);
            var last = body.LastIndexOf(IndexHtmlInjector.Marker, System.StringComparison.Ordinal);
            Assert.Equal(first, last);
        }
    }
}
