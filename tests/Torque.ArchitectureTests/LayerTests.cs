using NetArchTest.Rules;

namespace Torque.ArchitectureTests;

/// <summary>
/// The dependency rule from docs/architecture.md, enforced by the build instead of by good intentions.
/// </summary>
public class LayerTests
{
    private static readonly System.Reflection.Assembly Domain = typeof(Torque.Domain.Common.Entity).Assembly;
    private static readonly System.Reflection.Assembly Application = typeof(Torque.Application.DependencyInjection).Assembly;

    [Fact]
    public void Domain_does_not_depend_on_frameworks_or_outer_layers() =>
        Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Torque.Application",
                "Torque.Infrastructure",
                "Torque.Api")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_web() =>
        Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny("Torque.Infrastructure", "Torque.Api", "Microsoft.AspNetCore", "Npgsql")
            .GetResult().IsSuccessful.ShouldBeTrue();
}
