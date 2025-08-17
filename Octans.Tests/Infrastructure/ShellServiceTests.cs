using FluentAssertions;
using Octans.Client;

namespace Octans.Tests.Infrastructure;

public class ShellServiceTests
{
    [Fact]
    public void IsShellVisible_InitiallyTrue()
    {
        var sut = new ShellService();

        sut.IsShellVisible.Should().BeTrue();
    }

    [Fact]
    public void HideShell_ChangesVisibilityToFalse()
    {
        var sut = new ShellService();

        sut.HideShell();

        sut.IsShellVisible.Should().BeFalse();
    }

    [Fact]
    public void ShowShell_ChangesVisibilityToTrue()
    {
        var sut = new ShellService();
        sut.HideShell();

        sut.ShowShell();

        sut.IsShellVisible.Should().BeTrue();
    }

    [Fact]
    public void HideShell_RaisesShellVisibilityChangedEvent()
    {
        var sut = new ShellService();
        var eventRaised = false;
        sut.ShellVisibilityChanged += () => eventRaised = true;

        sut.HideShell();

        eventRaised.Should().BeTrue();
    }

    [Fact]
    public void ShowShell_RaisesShellVisibilityChangedEvent()
    {
        var sut = new ShellService();
        sut.HideShell();
        var eventRaised = false;
        sut.ShellVisibilityChanged += () => eventRaised = true;

        sut.ShowShell();

        eventRaised.Should().BeTrue();
    }

    [Fact]
    public void HideShell_WhenAlreadyHidden_DoesNotRaiseEvent()
    {
        var sut = new ShellService();
        sut.HideShell();
        var eventRaisedCount = 0;
        sut.ShellVisibilityChanged += () => eventRaisedCount++;

        sut.HideShell();

        eventRaisedCount.Should().Be(0);
    }

    [Fact]
    public void ShowShell_WhenAlreadyVisible_DoesNotRaiseEvent()
    {
        var sut = new ShellService();
        var eventRaisedCount = 0;
        sut.ShellVisibilityChanged += () => eventRaisedCount++;

        sut.ShowShell();

        eventRaisedCount.Should().Be(0);
    }
}