using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Octans.Client.Components.MainToolbar;
using Xunit;

namespace Octans.Tests.Viewmodels;

public class MainToolbarViewmodelTests
{
    private readonly FakeNavigationManager _navigationManager;
    private readonly MainToolbarViewmodel _sut;

    public MainToolbarViewmodelTests()
    {
        _navigationManager = new FakeNavigationManager();
        _sut = new MainToolbarViewmodel(_navigationManager);
        _sut.OnInitialized();
    }

    [Theory]
    [InlineData(Page.LocalFiles, "/imports?tab=files")]
    [InlineData(Page.WebUrls, "/imports?tab=url")]
    [InlineData(Page.Gallery, "/gallery")]
    [InlineData(Page.ImportGallery, "/imports?tab=gallery")]
    [InlineData(Page.Watchable, "/imports?tab=watchable")]
    [InlineData(Page.Downloaders, "/downloaders")]
    [InlineData(Page.Settings, "/settings")]
    [InlineData(Page.Downloads, "/downloads")]
    public async Task Navigate_ShouldNavigateToCorrectUrl(Page page, string expectedUrl)
    {
        // Act
        await _sut.Navigate(page);

        // Assert
        _navigationManager.Uri.Should().EndWith(expectedUrl);
    }

    private sealed class FakeNavigationManager : NavigationManager
    {
        public FakeNavigationManager()
        {
            Initialize("http://localhost/", "http://localhost/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}
