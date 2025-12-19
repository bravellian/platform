using Bravellian.Platform.Modularity;
using Shouldly;

namespace Bravellian.Platform.Tests.Modularity;

public sealed class ModuleNavLinkTests
{
    [Theory]
    [InlineData("", "/")]
    [InlineData("   ", "/")]
    [InlineData("/", "/")]
    [InlineData("home", "/home")]
    [InlineData("/home", "/home")]
    [InlineData("/home/", "/home")]
    [InlineData("home/", "/home")]
    [InlineData("/home/page", "/home/page")]
    [InlineData("home/page", "/home/page")]
    [InlineData("/home/page/", "/home/page")]
    [InlineData("//home", "/home")]
    [InlineData("/home//page", "/home/page")]
    [InlineData("///home///page///", "/home/page")]
    public void NormalizePath_handles_edge_cases(string input, string expected)
    {
        var link = ModuleNavLink.Create("Test", input);
        link.Path.ShouldBe(expected);
    }

    [Fact]
    public void Create_preserves_all_properties()
    {
        var link = ModuleNavLink.Create("Dashboard", "/home", 5, "dashboard-icon");
        
        link.Title.ShouldBe("Dashboard");
        link.Path.ShouldBe("/home");
        link.Order.ShouldBe(5);
        link.Icon.ShouldBe("dashboard-icon");
    }

    [Fact]
    public void Create_uses_default_values_when_not_specified()
    {
        var link = ModuleNavLink.Create("Settings", "/settings");
        
        link.Order.ShouldBe(0);
        link.Icon.ShouldBeNull();
    }
}
