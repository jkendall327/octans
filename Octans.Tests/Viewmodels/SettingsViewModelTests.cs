using System.Runtime.InteropServices.JavaScript;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using NSubstitute;
using Octans.Client;
using Octans.Client.Components.Settings;
using Octans.Client.Services;
using Octans.Client.Settings;
using Octans.Core.Communication;
using Xunit;

namespace Octans.Tests.Viewmodels;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IThemeJsInterop _themeJsInterop;
    private readonly ThemeService _themeService;
    private readonly FakeTimeProvider _timeProvider;
    private readonly SettingsViewModel _sut;

    public SettingsViewModelTests()
    {
        _settingsService = Substitute.For<ISettingsService>();
        _settingsService.LoadAsync().Returns(new SettingsModel()); // Default setup

        _logger = Substitute.For<ILogger<SettingsViewModel>>();
        _themeJsInterop = Substitute.For<IThemeJsInterop>();

        // Default JS Setup
        _themeJsInterop.LoadThemePreferenceAsync().Returns("light");

        _themeService = new ThemeService();
        _timeProvider = new FakeTimeProvider();

        _sut = new SettingsViewModel(_settingsService, _logger, _themeJsInterop, _themeService, _timeProvider);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadSettings()
    {
        // Arrange
        var settings = new SettingsModel
        {
            Theme = "dark",
            AppRoot = "/test/root",
            LogLevel = "Warning",
            AspNetCoreLogLevel = "Error",
            ImportSource = "test-source",
            TagColor = "#123456"
        };
        _settingsService.LoadAsync().Returns(settings);
        _themeJsInterop.LoadThemePreferenceAsync().Returns("sepia");

        // Act
        await _sut.InitializeAsync();

        // Assert
        _sut.Settings.Theme.Should().Be("sepia"); // Takes preference
        _sut.Settings.AppRoot.Should().Be("/test/root");
        _sut.Settings.LogLevel.Should().Be("Warning");
        _sut.Settings.AspNetCoreLogLevel.Should().Be("Error");
        _sut.Settings.ImportSource.Should().Be("test-source");
        _sut.Settings.TagColor.Should().Be("#123456");

        // Verify theme was set
        _themeService.CurrentTheme.Should().Be("sepia");
        // Called twice: once by SetTheme event, once explicitly
        await _themeJsInterop.Received(2).SetThemeAsync("sepia");
    }

    [Fact]
    public async Task SaveConfiguration_ShouldSaveSettings()
    {
        // Arrange
        await _sut.InitializeAsync();
        _sut.Settings.AppRoot = "/new/root";

        // Act
        var saveTask = _sut.SaveConfiguration();

        // Assert - Initial state (saving)
        await _settingsService.Received(1).SaveAsync(Arg.Is<SettingsModel>(s => s.AppRoot == "/new/root"));
        _sut.SaveSuccess.Should().BeTrue();

        // Advance time to complete delay
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        await saveTask;

        // Assert - Final state
        _sut.SaveSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SaveConfiguration_ShouldHandleErrors()
    {
        // Arrange
        _settingsService.SaveAsync(Arg.Any<SettingsModel>()).Returns(Task.FromException(new InvalidOperationException("Save failed")));

        // Act
        await _sut.SaveConfiguration();

        // Assert
        _sut.SaveError.Should().BeTrue();
        _sut.ErrorMessage.Should().Be("Save failed");
        _sut.SaveSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ThemeChanged_ShouldUpdateThemeService()
    {
        // Arrange
        await _sut.InitializeAsync();
        _sut.Settings.Theme = "red";

        // Act
        await _sut.ThemeChanged();

        // Assert
        _themeService.CurrentTheme.Should().Be("red");
        // InitializeAsync called it with "light" (default mock). ThemeChanged called it with "red".
        await _themeJsInterop.Received(1).SetThemeAsync("red");
    }
}
