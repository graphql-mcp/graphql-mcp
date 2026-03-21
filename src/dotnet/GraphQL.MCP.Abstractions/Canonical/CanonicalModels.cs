namespace GraphQL.MCP.Abstractions.Canonical;

/// <summary>
/// A normalized representation of a GraphQL root-level operation (query or mutation field).
/// </summary>
public sealed class CanonicalOperation
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public OperationType OperationType { get; init; }
    public IReadOnlyList<CanonicalArgument> Arguments { get; init; } = [];
    public CanonicalType ReturnType { get; init; } = null!;
    public string GraphQLFieldName { get; init; } = null!;
}

/// <summary>
/// A normalized GraphQL argument.
/// </summary>
public sealed class CanonicalArgument
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public CanonicalType Type { get; init; } = null!;
    public bool IsRequired { get; init; }
    public object? DefaultValue { get; init; }
}

/// <summary>
/// A normalized GraphQL type descriptor.
/// </summary>
public sealed class CanonicalType
{
    public required string Name { get; init; }
    public TypeKind Kind { get; init; }
    public bool IsNonNull { get; init; }
    public bool IsList { get; init; }
    public CanonicalType? OfType { get; init; }
    public IReadOnlyList<CanonicalField>? Fields { get; init; }
    public IReadOnlyList<string>? EnumValues { get; init; }
    public IReadOnlyList<CanonicalType>? PossibleTypes { get; init; }
}

/// <summary>
/// A normalized field within a GraphQL object/interface type.
/// </summary>
public sealed class CanonicalField
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public CanonicalType Type { get; init; } = null!;
    public IReadOnlyList<CanonicalArgument>? Arguments { get; init; }
}

/// <summary>
/// GraphQL operation type.
/// </summary>
public enum OperationType
{
    Query,
    Mutation
}

/// <summary>
/// GraphQL type classification.
/// </summary>
public enum TypeKind
{
    Scalar,
    Object,
    InputObject,
    Enum,
    Interface,
    Union,
    List,
    NonNull
}
