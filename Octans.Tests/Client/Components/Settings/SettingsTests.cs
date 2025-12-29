using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using NSubstitute;
using Octans.Client;
using Octans.Client.Components.Settings;
using Octans.Client.Services;
using Octans.Client.Settings;
using Xunit;

namespace Octans.Tests.Client.Components.Settings;

#pragma warning disable CS0618 // Type or member is obsolete
public class SettingsTests : TestContext
{
#pragma warning restore CS0618 // Type or member is obsolete
    private readonly ISettingsService _settingsService;
    private readonly ThemeService _themeService;
    private readonly FakeTimeProvider _timeProvider;
    private readonly IThemePreferenceService _themeJsInterop;

    public SettingsTests()
    {
        _settingsService = Substitute.For<ISettingsService>();
        _themeService = new ThemeService();
        _timeProvider = new FakeTimeProvider();
        _themeJsInterop = Substitute.For<IThemePreferenceService>();
        _themeJsInterop.LoadThemePreference(Arg.Any<CancellationToken>()).Returns("light");

        Services.AddSingleton(_settingsService);
        Services.AddSingleton(_themeJsInterop);
        Services.AddSingleton(_themeService);
        Services.AddSingleton<TimeProvider>(_timeProvider);
        Services.AddLogging();

        // Register the ViewModel
        Services.AddScoped<SettingsViewModel>();
    }

    [Fact]
    public void ShouldRenderSettings()
    {
        // Arrange
        _settingsService.LoadAsync().Returns(new SettingsModel());

        // Act
        // Using Render instead of RenderComponent as per warning
        var cut = Render<Octans.Client.Components.Settings.Settings>();

        // Assert
        Assert.NotNull(cut.Find("[data-test='settings-search']"));
        Assert.NotNull(cut.Find("[data-test='save-settings-button']"));
    }

    [Fact]
    public void ShouldLoadAndDisplaySettings()
    {
        // Arrange
        var settings = new SettingsModel
        {
            ImportSource = "test-import",
            AppRoot = "test-root"
        };
        _settingsService.LoadAsync().Returns(settings);

        // Act
        var cut = Render<Octans.Client.Components.Settings.Settings>();

        // Assert
        // Access VM via Services
        var vm = Services.GetRequiredService<SettingsViewModel>();
        cut.WaitForState(() => vm.Settings.ImportSource == "test-import");

        var importInput = cut.Find("[data-test='setting-import-source']");
        Assert.Equal("test-import", importInput.Attributes["value"]?.Value);
    }

    [Fact]
    public void ShouldNavigatePages()
    {
        // Arrange
        _settingsService.LoadAsync().Returns(new SettingsModel());
        var cut = Render<Octans.Client.Components.Settings.Settings>();

        // Act
        // Find "System" page link
        var systemPageLink = cut.Find("[data-test='settings-page-System']");
        systemPageLink.Click();

        // Assert
        // Now "System" page content should be visible
        // Check for "App Root" input which is on System page
        Assert.NotNull(cut.Find("[data-test='setting-app-root']"));
    }

    [Fact]
    public async Task ShouldSaveSettings()
    {
        // Arrange
        _settingsService.LoadAsync().Returns(new SettingsModel());
        var cut = Render<Octans.Client.Components.Settings.Settings>();

        // Wait for load
        var vm = Services.GetRequiredService<SettingsViewModel>();
        cut.WaitForState(() => vm.Settings != null);

        // Act
        var importInput = cut.Find("[data-test='setting-import-source']");
        await importInput.ChangeAsync(new ChangeEventArgs { Value = "new-import-source" });

        var saveButton = cut.Find("[data-test='save-settings-button']");

        // Start the click but don't await completion yet (as it waits for delay)
        var clickTask = saveButton.ClickAsync(new MouseEventArgs());

        // Assert
        // Verify SaveAsync was called with new value
        await _settingsService.Received().SaveAsync(Arg.Is<SettingsModel>(s => s.ImportSource == "new-import-source"));

        // Verify success message appears BEFORE delay finishes
        cut.WaitForState(() => cut.FindAll("[data-test='save-success-message']").Any());
        Assert.NotNull(cut.Find("[data-test='save-success-message']"));

        // Now advance time to complete the delay
        _timeProvider.Advance(TimeSpan.FromSeconds(3));

        // Await the click task to ensure clean exit
        await clickTask;

        // Verify success message disappears
        cut.WaitForState(() => !cut.FindAll("[data-test='save-success-message']").Any());
        Assert.Empty(cut.FindAll("[data-test='save-success-message']"));
    }
}
