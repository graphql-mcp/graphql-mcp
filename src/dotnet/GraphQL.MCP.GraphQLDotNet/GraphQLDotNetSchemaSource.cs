using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Canonical;
using GraphQL.Types;
using Microsoft.Extensions.Logging;
using CanonicalTypeKind = GraphQL.MCP.Abstractions.Canonical.TypeKind;

namespace GraphQL.MCP.GraphQLDotNet;

/// <summary>
/// Extracts canonical operations and types from a graphql-dotnet ISchema.
/// </summary>
public sealed class GraphQLDotNetSchemaSource : IGraphQLSchemaSource
{
    private readonly ISchema _schema;
    private readonly ILogger<GraphQLDotNetSchemaSource> _logger;

    public GraphQLDotNetSchemaSource(ISchema schema, ILogger<GraphQLDotNetSchemaSource> logger)
    {
        _schema = schema;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CanonicalOperation>> GetOperationsAsync(CancellationToken cancellationToken = default)
    {
        _schema.Initialize();

        var operations = new List<CanonicalOperation>();

        // Extract query fields
        if (_schema.Query is not null)
        {
            foreach (var field in _schema.Query.Fields)
            {
                if (field.Name.StartsWith("__", StringComparison.Ordinal))
                    continue;

                operations.Add(MapFieldToOperation(field, OperationType.Query));
            }
        }

        // Extract mutation fields
        if (_schema.Mutation is not null)
        {
            foreach (var field in _schema.Mutation.Fields)
            {
                if (field.Name.StartsWith("__", StringComparison.Ordinal))
                    continue;

                operations.Add(MapFieldToOperation(field, OperationType.Mutation));
            }
        }

        _logger.LogDebug("Extracted {Count} operations from graphql-dotnet schema", operations.Count);

        return Task.FromResult<IReadOnlyList<CanonicalOperation>>(operations);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, CanonicalType>> GetTypesAsync(CancellationToken cancellationToken = default)
    {
        _schema.Initialize();

        var types = new Dictionary<string, CanonicalType>();

        foreach (var graphType in _schema.AllTypes)
        {
            if (graphType.Name.StartsWith("__", StringComparison.Ordinal))
                continue;

            if (types.ContainsKey(graphType.Name))
                continue;

            types[graphType.Name] = MapNamedType(graphType, new HashSet<string>());
        }

        return Task.FromResult<IReadOnlyDictionary<string, CanonicalType>>(types);
    }

    private CanonicalOperation MapFieldToOperation(FieldType field, OperationType opType)
    {
        return new CanonicalOperation
        {
            Name = field.Name,
            Description = field.Description,
            OperationType = opType,
            GraphQLFieldName = field.Name,
            Arguments = field.Arguments?
                .Select(a => MapArgument(a, new HashSet<string>()))
                .ToList()
                ?? [],
            ReturnType = MapGraphType(field.ResolvedType!, new HashSet<string>())
        };
    }

    private CanonicalArgument MapArgument(QueryArgument arg, HashSet<string> visited)
    {
        return new CanonicalArgument
        {
            Name = arg.Name,
            Description = arg.Description,
            Type = MapGraphType(arg.ResolvedType!, new HashSet<string>(visited)),
            IsRequired = arg.ResolvedType is NonNullGraphType,
            DefaultValue = arg.DefaultValue
        };
    }

    private CanonicalType MapGraphType(IGraphType graphType, HashSet<string> visited)
    {
        if (graphType is NonNullGraphType nonNull)
        {
            var inner = MapGraphType(nonNull.ResolvedType!, visited);
            return new CanonicalType
            {
                Name = inner.Name,
                Kind = CanonicalTypeKind.NonNull,
                IsNonNull = true,
                OfType = inner
            };
        }

        if (graphType is ListGraphType listType)
        {
            var inner = MapGraphType(listType.ResolvedType!, visited);
            return new CanonicalType
            {
                Name = $"[{inner.Name}]",
                Kind = CanonicalTypeKind.List,
                IsList = true,
                OfType = inner
            };
        }

        if (graphType is INamedType)
        {
            return MapNamedType(graphType, visited);
        }

        return new CanonicalType { Name = graphType.ToString() ?? "Unknown", Kind = CanonicalTypeKind.Scalar };
    }

    private CanonicalType MapNamedType(IGraphType graphType, HashSet<string> visited)
    {
        if (visited.Contains(graphType.Name))
        {
            return CreateShellType(graphType);
        }

        var currentPath = new HashSet<string>(visited) { graphType.Name };

        switch (graphType)
        {
            case EnumerationGraphType enumType:
                return new CanonicalType
                {
                    Name = enumType.Name,
                    Kind = CanonicalTypeKind.Enum,
                    EnumValues = enumType.Values.Select(v => v.Name).ToList()
                };

            case ScalarGraphType scalar:
                return new CanonicalType
                {
                    Name = scalar.Name,
                    Kind = CanonicalTypeKind.Scalar
                };

            case IInputObjectGraphType inputType:
                return new CanonicalType
                {
                    Name = inputType.Name,
                    Kind = CanonicalTypeKind.InputObject,
                    Fields = inputType.Fields
                        .Select(f => new CanonicalField
                        {
                            Name = f.Name,
                            Description = f.Description,
                            Type = MapGraphType(f.ResolvedType!, currentPath)
                        })
                        .ToList()
                };

            case UnionGraphType unionType:
                return new CanonicalType
                {
                    Name = unionType.Name,
                    Kind = CanonicalTypeKind.Union,
                    PossibleTypes = unionType.PossibleTypes
                        .Select(t => MapNamedType(t, currentPath))
                        .ToList()
                };

            case IInterfaceGraphType interfaceType:
                return new CanonicalType
                {
                    Name = interfaceType.Name,
                    Kind = CanonicalTypeKind.Interface,
                    Fields = interfaceType.Fields
                        .Where(f => !f.Name.StartsWith("__", StringComparison.Ordinal))
                        .Select(f => MapField(f, currentPath))
                        .ToList(),
                    PossibleTypes = interfaceType.PossibleTypes
                        .Select(t => MapNamedType(t, currentPath))
                        .ToList()
                };

            case IObjectGraphType objectType:
                return new CanonicalType
                {
                    Name = objectType.Name,
                    Kind = CanonicalTypeKind.Object,
                    Fields = objectType.Fields
                        .Where(f => !f.Name.StartsWith("__", StringComparison.Ordinal))
                        .Select(f => MapField(f, currentPath))
                        .ToList()
                };

            default:
                return new CanonicalType
                {
                    Name = graphType.Name,
                    Kind = CanonicalTypeKind.Scalar
                };
        }
    }

    private CanonicalField MapField(FieldType field, HashSet<string> visited)
    {
        return new CanonicalField
        {
            Name = field.Name,
            Description = field.Description,
            Type = MapGraphType(field.ResolvedType!, new HashSet<string>(visited)),
            Arguments = field.Arguments is { Count: > 0 }
                ? field.Arguments.Select(a => MapArgument(a, new HashSet<string>(visited))).ToList()
                : null
        };
    }

    private static CanonicalType CreateShellType(IGraphType graphType) =>
        graphType switch
        {
            IInputObjectGraphType => new CanonicalType
            {
                Name = graphType.Name,
                Kind = CanonicalTypeKind.InputObject
            },
            UnionGraphType => new CanonicalType
            {
                Name = graphType.Name,
                Kind = CanonicalTypeKind.Union
            },
            IInterfaceGraphType => new CanonicalType
            {
                Name = graphType.Name,
                Kind = CanonicalTypeKind.Interface
            },
            IObjectGraphType => new CanonicalType
            {
                Name = graphType.Name,
                Kind = CanonicalTypeKind.Object
            },
            EnumerationGraphType => new CanonicalType
            {
                Name = graphType.Name,
                Kind = CanonicalTypeKind.Enum
            },
            _ => new CanonicalType
            {
                Name = graphType.Name,
                Kind = CanonicalTypeKind.Scalar
            }
        };
}
