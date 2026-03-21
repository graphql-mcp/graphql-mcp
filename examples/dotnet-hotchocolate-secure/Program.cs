using GraphQL.MCP.Abstractions;
using GraphQL.MCP.HotChocolate;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>();

// Full configuration — security controls, naming, auth
builder.Services.AddHotChocolateMcp(options =>
{
    options.ToolPrefix = "bookstore";
    options.MaxOutputDepth = 3;
    options.NamingPolicy = ToolNamingPolicy.VerbNoun;

    // Safety: block mutations, exclude sensitive fields
    options.AllowMutations = false;
    options.ExcludedFields.Add("internalNotes");
    options.ExcludedFields.Add("adminPanel");
    options.ExcludedTypes.Add("AuditLog");

    // Auth: forward caller's token to GraphQL
    options.Authorization.Mode = McpAuthMode.Passthrough;

    options.Transport = McpTransport.StreamableHttp;
});

var app = builder.Build();

app.UseGraphQLMcp();

app.Run();

// --- Schema Types ---

public class Query
{
    /// <summary>
    /// Search books by title or author name.
    /// </summary>
    public List<Book> SearchBooks(string? query, int limit = 10) =>
    [
        new(1, "The GraphQL Guide", "A comprehensive guide", 29.99m,
            new Author("John Resig"), "Internal: draft version"),
    ];

    /// <summary>
    /// Get a single book by ID.
    /// </summary>
    public Book? GetBook(int id) => SearchBooks(null).FirstOrDefault(b => b.Id == id);

    /// <summary>
    /// Get all categories.
    /// </summary>
    public List<string> GetCategories() => ["Fiction", "Non-Fiction", "Technical"];

    // This field will be excluded by ExcludedFields
    public string AdminPanel() => "secret admin data";

    // This field will be excluded because it returns AuditLog (ExcludedTypes)
    public AuditLog GetAuditLog() => new("2026-01-01", "system");
}

public class Mutation
{
    // Mutations are blocked (AllowMutations = false), so this won't appear as a tool
    public Book CreateBook(string title, string author) =>
        new(99, title, "", 0, new Author(author), "");
}

public record Book(int Id, string Title, string Description, decimal Price, Author Author, string InternalNotes);
public record Author(string Name);
public record AuditLog(string Timestamp, string Actor);
