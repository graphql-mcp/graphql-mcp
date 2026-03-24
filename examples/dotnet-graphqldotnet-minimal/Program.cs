using GraphQL;
using GraphQL.MCP.AspNetCore;
using GraphQL.MCP.GraphQLDotNet;
using GraphQL.Server;
using GraphQL.Types;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);

// Register graphql-dotnet
builder.Services.AddGraphQL(b => b
    .AddSchema<BookSchema>()
    .AddSystemTextJson()
    .AddGraphTypes(typeof(BookSchema).Assembly));

// One line of code — that's it.
builder.Services.AddGraphQLDotNetMcp();

var app = builder.Build();

// Map graphql-dotnet endpoint
app.UseGraphQL("/graphql");

// Map MCP endpoint
app.UseGraphQLMcp();

app.Run();

// --- Schema types ---

public class BookSchema : Schema
{
    public BookSchema(IServiceProvider provider) : base(provider)
    {
        Query = provider.GetRequiredService<BookQuery>();
    }
}

public class BookQuery : ObjectGraphType
{
    public BookQuery()
    {
        Name = "Query";

        Field<StringGraphType>("hello")
            .Description("Says hello to the given name.")
            .Argument<StringGraphType>("name", "The name to greet", configure: arg => arg.DefaultValue = "World")
            .Resolve(ctx =>
            {
                var name = ctx.GetArgument<string>("name") ?? "World";
                return $"Hello, {name}!";
            });

        Field<ListGraphType<BookType>>("books")
            .Description("Returns a list of books.")
            .Resolve(_ => BookData.GetBooks());

        Field<BookType>("bookByTitle")
            .Description("Gets a book by its title.")
            .Argument<NonNullGraphType<StringGraphType>>("title", "The title to search for")
            .Resolve(ctx =>
            {
                var title = ctx.GetArgument<string>("title");
                return BookData.GetBooks()
                    .FirstOrDefault(b => b.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
            });
    }
}

public class BookType : ObjectGraphType<Book>
{
    public BookType()
    {
        Name = "Book";
        Field(x => x.Title).Description("The title of the book.");
        Field<AuthorType>("author").Description("The author of the book.")
            .Resolve(ctx => ctx.Source.Author);
    }
}

public class AuthorType : ObjectGraphType<Author>
{
    public AuthorType()
    {
        Name = "Author";
        Field(x => x.Name).Description("The author's name.");
        Field(x => x.Email).Description("The author's email.");
    }
}

public record Book(string Title, Author Author);
public record Author(string Name, string Email);

public static class BookData
{
    public static List<Book> GetBooks() =>
    [
        new("The GraphQL Guide", new Author("John Resig", "john@example.com")),
        new("Learning MCP", new Author("Jane Smith", "jane@example.com")),
    ];
}
