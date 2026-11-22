using System.Reflection;
using DisputePortal.Modules.DebitOrders;
using DisputePortal.Modules.Disputes;
using DisputePortal.Modules.Notifications;
using NetArchTest.Rules;

namespace DisputePortal.ArchitectureTests;

/// <summary>
/// The module boundaries are what make this a modular monolith rather than a ball of mud.
/// These tests fail the build when a boundary is crossed.
/// </summary>
public class ModuleBoundaryTests
{
    private const string DebitOrders = "DisputePortal.Modules.DebitOrders";
    private const string Disputes = "DisputePortal.Modules.Disputes";
    private const string Notifications = "DisputePortal.Modules.Notifications";

    private static readonly Assembly DebitOrdersAssembly = typeof(DebitOrdersModule).Assembly;
    private static readonly Assembly DisputesAssembly = typeof(DisputesModule).Assembly;
    private static readonly Assembly NotificationsAssembly = typeof(NotificationsModule).Assembly;

    public static TheoryData<string> Modules => new() { DebitOrders, Disputes, Notifications };

    [Fact]
    public void DebitOrders_depends_on_no_other_module()
    {
        var result = Types.InAssembly(DebitOrdersAssembly).ShouldNot().HaveDependencyOnAny(Disputes, Notifications).GetResult();

        AssertPasses(result);
    }

    [Fact]
    public void Disputes_uses_only_the_contracts_of_DebitOrders()
    {
        var result = Types.InAssembly(DisputesAssembly)
            .ShouldNot().HaveDependencyOnAny(
                $"{DebitOrders}.Domain", $"{DebitOrders}.Application", $"{DebitOrders}.Infrastructure", $"{DebitOrders}.Endpoints", Notifications)
            .GetResult();

        AssertPasses(result);
    }

    [Fact]
    public void Notifications_uses_only_the_contracts_of_Disputes()
    {
        var result = Types.InAssembly(NotificationsAssembly)
            .ShouldNot().HaveDependencyOnAny(
                $"{Disputes}.Domain", $"{Disputes}.Application", $"{Disputes}.Infrastructure", $"{Disputes}.Endpoints", DebitOrders)
            .GetResult();

        AssertPasses(result);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void A_module_exposes_only_its_contracts_and_its_registration_class(string module)
    {
        var result = Types.InAssembly(AssemblyOf(module))
            .That().DoNotResideInNamespace($"{module}.Contracts")
            .And().DoNotResideInNamespaceEndingWith("Migrations")   // EF Core generates public migration classes
            .And().DoNotHaveNameEndingWith("Module")
            .Should().NotBePublic()
            .GetResult();

        AssertPasses(result);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void The_domain_does_not_depend_on_outer_layers_or_frameworks(string module)
    {
        var result = Types.InAssembly(AssemblyOf(module))
            .That().ResideInNamespace($"{module}.Domain")
            .ShouldNot().HaveDependencyOnAny(
                $"{module}.Application", $"{module}.Infrastructure", $"{module}.Endpoints",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql")
            .GetResult();

        AssertPasses(result);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void The_application_layer_does_not_depend_on_infrastructure_or_the_database(string module)
    {
        var result = Types.InAssembly(AssemblyOf(module))
            .That().ResideInNamespace($"{module}.Application")
            .ShouldNot().HaveDependencyOnAny($"{module}.Infrastructure", $"{module}.Endpoints", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        AssertPasses(result);
    }

    private static Assembly AssemblyOf(string module) => module switch
    {
        DebitOrders => DebitOrdersAssembly,
        Disputes => DisputesAssembly,
        Notifications => NotificationsAssembly,
        _ => throw new ArgumentOutOfRangeException(nameof(module), module, null),
    };

    private static void AssertPasses(TestResult result) =>
        result.IsSuccessful.ShouldBeTrue($"Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
}
