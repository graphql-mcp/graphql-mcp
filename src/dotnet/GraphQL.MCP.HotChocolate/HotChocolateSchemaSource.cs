using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Canonical;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.Extensions.Logging;

namespace GraphQL.MCP.HotChocolate;

/// <summary>
/// Extracts canonical operations and types from Hot Chocolate's schema.
/// </summary>
public sealed class HotChocolateSchemaSource : IGraphQLSchemaSource
{
    private readonly ISchema _schema;
    private readonly ILogger<HotChocolateSchemaSource> _logger;

    public HotChocolateSchemaSource(ISchema schema, ILogger<HotChocolateSchemaSource> logger)
    {
        _schema = schema;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CanonicalOperation>> GetOperationsAsync(CancellationToken cancellationToken = default)
    {
        var operations = new List<CanonicalOperation>();

        // Extract query fields
        if (_schema.QueryType is not null)
        {
            foreach (var field in _schema.QueryType.Fields)
            {
                if (field.Name.StartsWith("__", StringComparison.Ordinal))
                    continue;

                operations.Add(MapFieldToOperation(field, OperationType.Query));
            }
        }

        // Extract mutation fields
        if (_schema.MutationType is not null)
        {
            foreach (var field in _schema.MutationType.Fields)
            {
                if (field.Name.StartsWith("__", StringComparison.Ordinal))
                    continue;

                operations.Add(MapFieldToOperation(field, OperationType.Mutation));
            }
        }

        _logger.LogDebug("Extracted {Count} operations from Hot Chocolate schema", operations.Count);

        return Task.FromResult<IReadOnlyList<CanonicalOperation>>(operations);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, CanonicalType>> GetTypesAsync(CancellationToken cancellationToken = default)
    {
        var types = new Dictionary<string, CanonicalType>();
        var visited = new HashSet<string>();

        foreach (var namedType in _schema.Types)
        {
            if (namedType.Name.StartsWith("__", StringComparison.Ordinal))
                continue;

            if (!visited.Add(namedType.Name))
                continue;

            types[namedType.Name] = MapNamedType(namedType, visited);
        }

        return Task.FromResult<IReadOnlyDictionary<string, CanonicalType>>(types);
    }

    private CanonicalOperation MapFieldToOperation(IOutputField field, OperationType opType)
    {
        var visited = new HashSet<string>();
        return new CanonicalOperation
        {
            Name = field.Name,
            Description = field.Description,
            OperationType = opType,
            GraphQLFieldName = field.Name,
            Arguments = field.Arguments.Select(a => MapArgument(a, visited)).ToList(),
            ReturnType = MapType(field.Type, visited)
        };
    }

    private CanonicalArgument MapArgument(IInputField arg, HashSet<string> visited)
    {
        return new CanonicalArgument
        {
            Name = arg.Name,
            Description = arg.Description,
            Type = MapType(arg.Type, visited),
            IsRequired = arg.Type.IsNonNullType(),
            DefaultValue = arg.DefaultValue
        };
    }

    private CanonicalType MapType(IType type, HashSet<string> visited)
    {
        if (type is NonNullType nonNull)
        {
            var inner = MapType(nonNull.Type, visited);
            return new CanonicalType
            {
                Name = inner.Name,
                Kind = TypeKind.NonNull,
                IsNonNull = true,
                OfType = inner
            };
        }

        if (type is ListType listType)
        {
            var inner = MapType(listType.ElementType, visited);
            return new CanonicalType
            {
                Name = $"[{inner.Name}]",
                Kind = TypeKind.List,
                IsList = true,
                OfType = inner
            };
        }

        if (type is INamedType namedType)
        {
            return MapNamedType(namedType, visited);
        }

        return new CanonicalType { Name = type.ToString() ?? "Unknown", Kind = TypeKind.Scalar };
    }

    private CanonicalType MapNamedType(INamedType namedType, HashSet<string> visited)
    {
        switch (namedType)
        {
            case ScalarType scalar:
                return new CanonicalType
                {
                    Name = scalar.Name,
                    Kind = TypeKind.Scalar
                };

            case EnumType enumType:
                return new CanonicalType
                {
                    Name = enumType.Name,
                    Kind = TypeKind.Enum,
                    EnumValues = enumType.Values.Select(v => v.Name).ToList()
                };

            case ObjectType objectType:
                if (!visited.Add(objectType.Name))
                {
                    return new CanonicalType
                    {
                        Name = objectType.Name,
                        Kind = TypeKind.Object
                    };
                }

                return new CanonicalType
                {
                    Name = objectType.Name,
                    Kind = TypeKind.Object,
                    Fields = objectType.Fields
                        .Where(f => !f.Name.StartsWith("__", StringComparison.Ordinal))
                        .Select(f => MapField(f, visited))
                        .ToList()
                };

            case InputObjectType inputType:
                if (!visited.Add(inputType.Name))
                {
                    return new CanonicalType
                    {
                        Name = inputType.Name,
                        Kind = TypeKind.InputObject
                    };
                }

                return new CanonicalType
                {
                    Name = inputType.Name,
                    Kind = TypeKind.InputObject,
                    Fields = inputType.Fields
                        .Select(f => new CanonicalField
                        {
                            Name = f.Name,
                            Description = f.Description,
                            Type = MapType(f.Type, visited)
                        })
                        .ToList()
                };

            case InterfaceType interfaceType:
                return new CanonicalType
                {
                    Name = interfaceType.Name,
                    Kind = TypeKind.Interface,
                    Fields = interfaceType.Fields
                        .Where(f => !f.Name.StartsWith("__", StringComparison.Ordinal))
                        .Select(f => MapField(f, visited))
                        .ToList(),
                    PossibleTypes = _schema.GetPossibleTypes(interfaceType)
                        .Select(t => MapNamedType(t, visited))
                        .ToList()
                };

            case UnionType unionType:
                return new CanonicalType
                {
                    Name = unionType.Name,
                    Kind = TypeKind.Union,
                    PossibleTypes = unionType.Types.Values
                        .Select(t => MapNamedType(t, visited))
                        .ToList()
                };

            default:
                return new CanonicalType
                {
                    Name = namedType.Name,
                    Kind = TypeKind.Scalar
                };
        }
    }

    private CanonicalField MapField(IOutputField field, HashSet<string> visited)
    {
        return new CanonicalField
        {
            Name = field.Name,
            Description = field.Description,
            Type = MapType(field.Type, visited),
            Arguments = field.Arguments.Count > 0
                ? field.Arguments.Select(a => MapArgument(a, visited)).ToList()
                : null
        };
    }
}
