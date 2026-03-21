using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Canonical;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.Logging;
using CanonicalTypeKind = GraphQL.MCP.Abstractions.Canonical.TypeKind;

namespace GraphQL.MCP.HotChocolate;

/// <summary>
/// Extracts canonical operations and types from Hot Chocolate's schema.
/// Uses IRequestExecutorResolver to lazily resolve the schema at runtime,
/// since Hot Chocolate does not register ISchema directly in the DI container.
/// </summary>
public sealed class HotChocolateSchemaSource : IGraphQLSchemaSource
{
    private readonly IRequestExecutorResolver _executorResolver;
    private readonly ILogger<HotChocolateSchemaSource> _logger;

    public HotChocolateSchemaSource(IRequestExecutorResolver executorResolver, ILogger<HotChocolateSchemaSource> logger)
    {
        _executorResolver = executorResolver;
        _logger = logger;
    }

    private async Task<ISchema> GetSchemaAsync(CancellationToken cancellationToken)
    {
        var executor = await _executorResolver.GetRequestExecutorAsync(cancellationToken: cancellationToken);
        return executor.Schema;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CanonicalOperation>> GetOperationsAsync(CancellationToken cancellationToken = default)
    {
        var schema = await GetSchemaAsync(cancellationToken);
        var operations = new List<CanonicalOperation>();

        // Extract query fields
        if (schema.QueryType is not null)
        {
            foreach (var field in schema.QueryType.Fields)
            {
                if (field.Name.StartsWith("__", StringComparison.Ordinal))
                    continue;

                operations.Add(MapFieldToOperation(field, OperationType.Query, schema));
            }
        }

        // Extract mutation fields
        if (schema.MutationType is not null)
        {
            foreach (var field in schema.MutationType.Fields)
            {
                if (field.Name.StartsWith("__", StringComparison.Ordinal))
                    continue;

                operations.Add(MapFieldToOperation(field, OperationType.Mutation, schema));
            }
        }

        _logger.LogDebug("Extracted {Count} operations from Hot Chocolate schema", operations.Count);

        return operations;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, CanonicalType>> GetTypesAsync(CancellationToken cancellationToken = default)
    {
        var schema = await GetSchemaAsync(cancellationToken);
        var types = new Dictionary<string, CanonicalType>();

        foreach (var namedType in schema.Types)
        {
            if (namedType.Name.StartsWith("__", StringComparison.Ordinal))
                continue;

            if (types.ContainsKey(namedType.Name))
                continue;

            types[namedType.Name] = MapNamedType(namedType, new HashSet<string>(), schema);
        }

        return types;
    }

    private CanonicalOperation MapFieldToOperation(IOutputField field, OperationType opType, ISchema schema)
    {
        return new CanonicalOperation
        {
            Name = field.Name,
            Description = field.Description,
            OperationType = opType,
            GraphQLFieldName = field.Name,
            Arguments = field.Arguments.Select(a => MapArgument(a, new HashSet<string>(), schema)).ToList(),
            ReturnType = MapType(field.Type, new HashSet<string>(), schema)
        };
    }

    private CanonicalArgument MapArgument(IInputField arg, HashSet<string> visited, ISchema schema)
    {
        return new CanonicalArgument
        {
            Name = arg.Name,
            Description = arg.Description,
            Type = MapType(arg.Type, new HashSet<string>(visited), schema),
            IsRequired = arg.Type.IsNonNullType(),
            DefaultValue = arg.DefaultValue
        };
    }

    private CanonicalType MapType(IType type, HashSet<string> visited, ISchema schema)
    {
        if (type is NonNullType nonNull)
        {
            var inner = MapType(nonNull.Type, visited, schema);
            return new CanonicalType
            {
                Name = inner.Name,
                Kind = CanonicalTypeKind.NonNull,
                IsNonNull = true,
                OfType = inner
            };
        }

        if (type is ListType listType)
        {
            var inner = MapType(listType.ElementType, visited, schema);
            return new CanonicalType
            {
                Name = $"[{inner.Name}]",
                Kind = CanonicalTypeKind.List,
                IsList = true,
                OfType = inner
            };
        }

        if (type is INamedType namedType)
        {
            return MapNamedType(namedType, visited, schema);
        }

        return new CanonicalType { Name = type.ToString() ?? "Unknown", Kind = CanonicalTypeKind.Scalar };
    }

    private CanonicalType MapNamedType(INamedType namedType, HashSet<string> visited, ISchema schema)
    {
        if (visited.Contains(namedType.Name))
        {
            return CreateShellType(namedType);
        }

        var currentPath = new HashSet<string>(visited) { namedType.Name };

        switch (namedType)
        {
            case ScalarType scalar:
                return new CanonicalType
                {
                    Name = scalar.Name,
                    Kind = CanonicalTypeKind.Scalar
                };

            case EnumType enumType:
                return new CanonicalType
                {
                    Name = enumType.Name,
                    Kind = CanonicalTypeKind.Enum,
                    EnumValues = enumType.Values.Select(v => v.Name).ToList()
                };

            case ObjectType objectType:
                return new CanonicalType
                {
                    Name = objectType.Name,
                    Kind = CanonicalTypeKind.Object,
                    Fields = objectType.Fields
                        .Where(f => !f.Name.StartsWith("__", StringComparison.Ordinal))
                        .Select(f => MapField(f, currentPath, schema))
                        .ToList()
                };

            case InputObjectType inputType:
                return new CanonicalType
                {
                    Name = inputType.Name,
                    Kind = CanonicalTypeKind.InputObject,
                    Fields = inputType.Fields
                        .Select(f => new CanonicalField
                        {
                            Name = f.Name,
                            Description = f.Description,
                            Type = MapType(f.Type, currentPath, schema)
                        })
                        .ToList()
                };

            case InterfaceType interfaceType:
                return new CanonicalType
                {
                    Name = interfaceType.Name,
                    Kind = CanonicalTypeKind.Interface,
                    Fields = interfaceType.Fields
                        .Where(f => !f.Name.StartsWith("__", StringComparison.Ordinal))
                        .Select(f => MapField(f, currentPath, schema))
                        .ToList(),
                    PossibleTypes = schema.GetPossibleTypes(interfaceType)
                        .Select(t => MapNamedType(t, currentPath, schema))
                        .ToList()
                };

            case UnionType unionType:
                return new CanonicalType
                {
                    Name = unionType.Name,
                    Kind = CanonicalTypeKind.Union,
                    PossibleTypes = unionType.Types.Values
                        .Select(t => MapNamedType(t, currentPath, schema))
                        .ToList()
                };

            default:
                return new CanonicalType
                {
                    Name = namedType.Name,
                    Kind = CanonicalTypeKind.Scalar
                };
        }
    }

    private CanonicalField MapField(IOutputField field, HashSet<string> visited, ISchema schema)
    {
        return new CanonicalField
        {
            Name = field.Name,
            Description = field.Description,
            Type = MapType(field.Type, new HashSet<string>(visited), schema),
            Arguments = field.Arguments.Count > 0
                ? field.Arguments.Select(a => MapArgument(a, new HashSet<string>(visited), schema)).ToList()
                : null
        };
    }

    private static CanonicalType CreateShellType(INamedType namedType) =>
        namedType switch
        {
            ObjectType => new CanonicalType
            {
                Name = namedType.Name,
                Kind = CanonicalTypeKind.Object
            },
            InputObjectType => new CanonicalType
            {
                Name = namedType.Name,
                Kind = CanonicalTypeKind.InputObject
            },
            InterfaceType => new CanonicalType
            {
                Name = namedType.Name,
                Kind = CanonicalTypeKind.Interface
            },
            UnionType => new CanonicalType
            {
                Name = namedType.Name,
                Kind = CanonicalTypeKind.Union
            },
            EnumType => new CanonicalType
            {
                Name = namedType.Name,
                Kind = CanonicalTypeKind.Enum
            },
            _ => new CanonicalType
            {
                Name = namedType.Name,
                Kind = CanonicalTypeKind.Scalar
            }
        };
}
