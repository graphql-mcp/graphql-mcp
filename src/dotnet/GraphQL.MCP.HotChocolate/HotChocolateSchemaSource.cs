using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Canonical;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.Extensions.Logging;
using CanonicalTypeKind = GraphQL.MCP.Abstractions.Canonical.TypeKind;

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

        foreach (var namedType in _schema.Types)
        {
            if (namedType.Name.StartsWith("__", StringComparison.Ordinal))
                continue;

            if (types.ContainsKey(namedType.Name))
                continue;

            types[namedType.Name] = MapNamedType(namedType, new HashSet<string>());
        }

        return Task.FromResult<IReadOnlyDictionary<string, CanonicalType>>(types);
    }

    private CanonicalOperation MapFieldToOperation(IOutputField field, OperationType opType)
    {
        return new CanonicalOperation
        {
            Name = field.Name,
            Description = field.Description,
            OperationType = opType,
            GraphQLFieldName = field.Name,
            Arguments = field.Arguments.Select(a => MapArgument(a, new HashSet<string>())).ToList(),
            ReturnType = MapType(field.Type, new HashSet<string>())
        };
    }

    private CanonicalArgument MapArgument(IInputField arg, HashSet<string> visited)
    {
        return new CanonicalArgument
        {
            Name = arg.Name,
            Description = arg.Description,
            Type = MapType(arg.Type, new HashSet<string>(visited)),
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
                Kind = CanonicalTypeKind.NonNull,
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
                Kind = CanonicalTypeKind.List,
                IsList = true,
                OfType = inner
            };
        }

        if (type is INamedType namedType)
        {
            return MapNamedType(namedType, visited);
        }

        return new CanonicalType { Name = type.ToString() ?? "Unknown", Kind = CanonicalTypeKind.Scalar };
    }

    private CanonicalType MapNamedType(INamedType namedType, HashSet<string> visited)
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
                        .Select(f => MapField(f, currentPath))
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
                            Type = MapType(f.Type, currentPath)
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
                        .Select(f => MapField(f, currentPath))
                        .ToList(),
                    PossibleTypes = _schema.GetPossibleTypes(interfaceType)
                        .Select(t => MapNamedType(t, currentPath))
                        .ToList()
                };

            case UnionType unionType:
                return new CanonicalType
                {
                    Name = unionType.Name,
                    Kind = CanonicalTypeKind.Union,
                    PossibleTypes = unionType.Types.Values
                        .Select(t => MapNamedType(t, currentPath))
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

    private CanonicalField MapField(IOutputField field, HashSet<string> visited)
    {
        return new CanonicalField
        {
            Name = field.Name,
            Description = field.Description,
            Type = MapType(field.Type, new HashSet<string>(visited)),
            Arguments = field.Arguments.Count > 0
                ? field.Arguments.Select(a => MapArgument(a, new HashSet<string>(visited))).ToList()
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
