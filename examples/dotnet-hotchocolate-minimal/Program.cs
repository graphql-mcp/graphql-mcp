using GraphQL.MCP.HotChocolate;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>();

// One line of code — that's it.
builder.Services.AddHotChocolateMcp();

var app = builder.Build();

// Maps both /graphql and /mcp endpoints
app.UseGraphQLMcp();

app.Run();

/// <summary>
/// Sample query type for demonstration.
/// </summary>
public class Query
{
    /// <summary>
    /// Says hello to the given name.
    /// </summary>
    public string Hello(string name = "World") => $"Hello, {name}!";

    /// <summary>
    /// Returns a list of books.
    /// </summary>
    public List<Book> GetBooks() =>
    [
        new("The GraphQL Guide", new Author("John Resig", "john@example.com")),
        new("Learning MCP", new Author("Jane Smith", "jane@example.com")),
    ];

    /// <summary>
    /// Gets a book by its title.
    /// </summary>
    public Book? GetBookByTitle(string title) =>
        GetBooks().FirstOrDefault(b =>
            b.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
}

public record Book(string Title, Author Author);
public record Author(string Name, string Email);
