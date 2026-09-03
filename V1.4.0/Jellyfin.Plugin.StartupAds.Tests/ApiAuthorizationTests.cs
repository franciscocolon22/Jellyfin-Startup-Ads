using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.StartupAds.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.StartupAds.Tests
{
    /// <summary>
    /// Guards the authorization contract. Jellyfin 10.11 removed the named
    /// <c>"DefaultAuthorization"</c> policy — referencing it makes the endpoint throw at
    /// request time (HTTP 500). User endpoints must use a bare <c>[Authorize]</c>; admin
    /// endpoints must use <c>Policy = "RequiresElevation"</c>.
    /// </summary>
    public class ApiAuthorizationTests
    {
        private static readonly MethodInfo[] Actions = typeof(StartupAdsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToArray();

        [Fact]
        public void NoEndpointReferencesTheRemovedDefaultAuthorizationPolicy()
        {
            var offenders = Actions
                .SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>())
                .Select(a => a.Policy)
                .Where(p => string.Equals(p, "DefaultAuthorization", StringComparison.Ordinal))
                .ToArray();

            Assert.Empty(offenders);
        }

        [Theory]
        [InlineData(nameof(StartupAdsController.GetConfig))]
        [InlineData(nameof(StartupAdsController.GetMedia))]
        [InlineData(nameof(StartupAdsController.GetBackground))]
        [InlineData(nameof(StartupAdsController.Track))]
        public void UserEndpointsRequireAuthenticationWithNoNamedPolicy(string method)
        {
            var action = Actions.Single(m => m.Name == method);
            var attrs = action.GetCustomAttributes<AuthorizeAttribute>().ToArray();
            Assert.NotEmpty(attrs);
            Assert.All(attrs, a => Assert.True(string.IsNullOrEmpty(a.Policy)));
        }

        [Theory]
        [InlineData(nameof(StartupAdsController.GetAds))]
        [InlineData(nameof(StartupAdsController.CreateAd))]
        [InlineData(nameof(StartupAdsController.UpdateAd))]
        [InlineData(nameof(StartupAdsController.DeleteAd))]
        [InlineData(nameof(StartupAdsController.SaveAdminConfig))]
        [InlineData(nameof(StartupAdsController.Scan))]
        [InlineData(nameof(StartupAdsController.ValidatePath))]
        [InlineData(nameof(StartupAdsController.Preview))]
        public void AdminEndpointsRequireElevation(string method)
        {
            var action = Actions.Single(m => m.Name == method);
            var attr = action.GetCustomAttributes<AuthorizeAttribute>().Single();
            Assert.Equal("RequiresElevation", attr.Policy);
        }

        [Theory]
        [InlineData(nameof(StartupAdsController.GetClientScript))]
        [InlineData(nameof(StartupAdsController.GetClientStyle))]
        public void OnlyStaticAssetsAreAnonymous(string method)
        {
            var action = Actions.Single(m => m.Name == method);
            Assert.NotEmpty(action.GetCustomAttributes<AllowAnonymousAttribute>());
        }
    }
}
