using System.Reflection;
using DisputePortal.Modules.Disputes;
using DisputePortal.Modules.Notifications;
using DisputePortal.Modules.Transactions;
using NetArchTest.Rules;

namespace DisputePortal.ArchitectureTests;

/// <summary>
/// The module boundaries are what make this a modular monolith rather than a ball of mud.
/// These tests fail the build when a boundary is crossed.
/// </summary>
public class ModuleBoundaryTests
{
    private const string Transactions = "DisputePortal.Modules.Transactions";
    private const string Disputes = "DisputePortal.Modules.Disputes";
    private const string Notifications = "DisputePortal.Modules.Notifications";

    private static readonly Assembly TransactionsAssembly = typeof(TransactionsModule).Assembly;
    private static readonly Assembly DisputesAssembly = typeof(DisputesModule).Assembly;
    private static readonly Assembly NotificationsAssembly = typeof(NotificationsModule).Assembly;

    public static TheoryData<string> Modules => new() { Transactions, Disputes, Notifications };

    [Fact]
    public void Transactions_depends_on_no_other_module()
    {
        var result = Types.InAssembly(TransactionsAssembly).ShouldNot().HaveDependencyOnAny(Disputes, Notifications).GetResult();

        AssertPasses(result);
    }

    [Fact]
    public void Disputes_uses_only_the_contracts_of_Transactions()
    {
        var result = Types.InAssembly(DisputesAssembly)
            .ShouldNot().HaveDependencyOnAny(
                $"{Transactions}.Domain", $"{Transactions}.Application", $"{Transactions}.Infrastructure", $"{Transactions}.Controllers", Notifications)
            .GetResult();

        AssertPasses(result);
    }

    [Fact]
    public void Notifications_uses_only_the_contracts_of_Disputes()
    {
        var result = Types.InAssembly(NotificationsAssembly)
            .ShouldNot().HaveDependencyOnAny(
                $"{Disputes}.Domain", $"{Disputes}.Application", $"{Disputes}.Infrastructure", $"{Disputes}.Controllers", Transactions)
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
                $"{module}.Application", $"{module}.Infrastructure", $"{module}.Controllers",
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
            .ShouldNot().HaveDependencyOnAny($"{module}.Infrastructure", $"{module}.Controllers", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        AssertPasses(result);
    }

    private static Assembly AssemblyOf(string module) => module switch
    {
        Transactions => TransactionsAssembly,
        Disputes => DisputesAssembly,
        Notifications => NotificationsAssembly,
        _ => throw new ArgumentOutOfRangeException(nameof(module), module, null),
    };

    private static void AssertPasses(TestResult result) =>
        result.IsSuccessful.ShouldBeTrue($"Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
}
