using FluentAssertions;
using GraphQL.MCP.HotChocolate;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GraphQL.MCP.Tests.HotChocolateAdapter;

public class HotChocolateSchemaSourceTests
{
    [Fact]
    public async Task Should_preserve_fields_for_repeated_sibling_object_types()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGraphQLServer().AddQueryType<Query>();

        using var serviceProvider = services.BuildServiceProvider();
        var executorResolver = serviceProvider.GetRequiredService<IRequestExecutorResolver>();
        var executor = await executorResolver.GetRequestExecutorAsync();
        var sut = new HotChocolateSchemaSource(
            executor.Schema,
            NullLogger<HotChocolateSchemaSource>.Instance);

        var operations = await sut.GetOperationsAsync();
        var checkout = operations.Single(op => op.GraphQLFieldName == "checkout");
        var checkoutFields = checkout.ReturnType.Fields!.ToDictionary(field => field.Name);

        var shippingFields = checkoutFields["shippingAddress"].Type.Fields!;
        var billingFields = checkoutFields["billingAddress"].Type.Fields!;

        shippingFields.Select(field => field.Name).Should().Contain(["street", "city"]);
        billingFields.Select(field => field.Name).Should().Contain(["street", "city"]);
    }

    public sealed class Query
    {
        public Checkout Checkout() => new(
            new Address("123 Main St", "Springfield"),
            new Address("456 Side St", "Shelbyville"));
    }

    public sealed record Checkout(Address ShippingAddress, Address BillingAddress);

    public sealed record Address(string Street, string City);
}
