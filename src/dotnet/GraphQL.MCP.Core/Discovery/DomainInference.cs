using System.Text.RegularExpressions;
using GraphQL.MCP.Abstractions.Canonical;

namespace GraphQL.MCP.Core.Discovery;

internal static partial class DomainInference
{
    private const string GeneralDomain = "general";

    public static string Infer(CanonicalOperation operation)
    {
        var fromReturnType = InferFromReturnTypeName(operation.ReturnType);
        if (fromReturnType is not null)
        {
            return fromReturnType;
        }

        var fromReturnFields = InferFromReturnTypeFields(operation.ReturnType);
        if (fromReturnFields is not null)
        {
            return fromReturnFields;
        }

        var fromField = InferFromValue(operation.GraphQLFieldName, stripLeadingNoise: true);
        if (fromField is not null)
        {
            return fromField;
        }

        var fromDescription = InferFromValue(operation.Description, stripLeadingNoise: true);
        if (fromDescription is not null)
        {
            return fromDescription;
        }

        foreach (var argument in operation.Arguments)
        {
            var fromArgument = InferFromValue(argument.Name, stripLeadingNoise: true);
            if (fromArgument is not null)
            {
                return fromArgument;
            }
        }

        return GeneralDomain;
    }

    private static string? InferFromReturnTypeName(CanonicalType type)
    {
        var inner = GetInnermostType(type);
        return inner.Kind is TypeKind.Object or TypeKind.Interface or TypeKind.Union
            ? InferFromValue(inner.Name, stripLeadingNoise: true)
            : null;
    }

    private static string? InferFromReturnTypeFields(CanonicalType type)
    {
        var inner = GetInnermostType(type);
        var candidates = new Dictionary<string, (int Score, int FirstSeen)>(StringComparer.OrdinalIgnoreCase);
        var seen = 0;

        if (inner.Fields is not null)
        {
            foreach (var field in inner.Fields)
            {
                AddCandidate(candidates, InferFromValue(field.Name, stripLeadingNoise: true), seen++);
            }
        }

        if (inner.PossibleTypes is not null)
        {
            foreach (var possibleType in inner.PossibleTypes)
            {
                AddCandidate(candidates, InferFromValue(possibleType.Name, stripLeadingNoise: false), seen++);

                if (possibleType.Fields is null)
                {
                    continue;
                }

                foreach (var field in possibleType.Fields)
                {
                    AddCandidate(candidates, InferFromValue(field.Name, stripLeadingNoise: true), seen++);
                }
            }
        }

        return candidates
            .OrderByDescending(static entry => entry.Value.Score)
            .ThenBy(static entry => entry.Value.FirstSeen)
            .ThenBy(static entry => entry.Key, StringComparer.Ordinal)
            .Select(static entry => entry.Key)
            .FirstOrDefault();
    }

    private static void AddCandidate(
        Dictionary<string, (int Score, int FirstSeen)> candidates,
        string? candidate,
        int order)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Equals(GeneralDomain, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (candidates.TryGetValue(candidate, out var existing))
        {
            candidates[candidate] = (existing.Score + 1, existing.FirstSeen);
            return;
        }

        candidates[candidate] = (1, order);
    }

    private static string? InferFromValue(string? value, bool stripLeadingNoise)
    {
        var tokens = NormalizeTokens(value, stripLeadingNoise);
        return tokens.Count == 0 ? null : tokens[0];
    }

    private static List<string> NormalizeTokens(string? value, bool stripLeadingNoise)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var tokens = SplitIdentifier(value);
        if (tokens.Count == 0)
        {
            return [];
        }

        tokens = MergeCompoundTokens(tokens);
        var normalized = tokens
            .Select(SingularizeToken)
            .Where(static token => token.Length > 1)
            .ToList();

        if (stripLeadingNoise)
        {
            while (normalized.Count > 0 &&
                (ActionPrefixes.Contains(normalized[0]) || NoiseTokens.Contains(normalized[0])))
            {
                normalized.RemoveAt(0);
            }
        }

        normalized = normalized
            .Where(static token => !StopWords.Contains(token))
            .ToList();

        while (normalized.Count > 0 && StructuralSuffixTokens.Contains(normalized[^1]))
        {
            normalized.RemoveAt(normalized.Count - 1);
        }

        while (normalized.Count > 0 && GenericDomainTokens.Contains(normalized[0]))
        {
            normalized.RemoveAt(0);
        }

        return normalized
            .Where(static token => !GenericDomainTokens.Contains(token))
            .ToList();
    }

    private static CanonicalType GetInnermostType(CanonicalType type)
    {
        var current = type;
        while (current.OfType is not null)
        {
            current = current.OfType;
        }

        return current;
    }

    private static List<string> SplitIdentifier(string value)
    {
        var matches = IdentifierTokenRegex().Matches(value);
        if (matches.Count == 0)
        {
            return [];
        }

        var tokens = new List<string>(matches.Count);
        foreach (Match match in matches)
        {
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                tokens.Add(match.Value);
            }
        }

        return tokens;
    }

    private static List<string> MergeCompoundTokens(List<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return tokens;
        }

        var merged = new List<string>(tokens.Count);
        for (var index = 0; index < tokens.Count; index++)
        {
            var current = tokens[index];
            if (index < tokens.Count - 1 &&
                CompoundTokenSuffixes.TryGetValue(current, out var suffixes) &&
                suffixes.Contains(tokens[index + 1]))
            {
                merged.Add(current + tokens[index + 1]);
                index++;
                continue;
            }

            merged.Add(current);
        }

        return merged;
    }

    private static string SingularizeToken(string token)
    {
        var lower = token.ToLowerInvariant();

        if (lower.Length <= 3)
        {
            return lower;
        }

        if (IrregularSingulars.TryGetValue(lower, out var singular))
        {
            return singular;
        }

        if (UnsingularizableTokens.Contains(lower))
        {
            return lower;
        }

        if (lower.EndsWith("ies", StringComparison.Ordinal))
        {
            return lower[..^3] + "y";
        }

        if (lower.EndsWith("ches", StringComparison.Ordinal) ||
            lower.EndsWith("shes", StringComparison.Ordinal) ||
            lower.EndsWith("xes", StringComparison.Ordinal) ||
            lower.EndsWith("zes", StringComparison.Ordinal) ||
            lower.EndsWith("ses", StringComparison.Ordinal))
        {
            return lower[..^2];
        }

        if (lower.EndsWith("s", StringComparison.Ordinal) &&
            !lower.EndsWith("ss", StringComparison.Ordinal) &&
            !lower.EndsWith("us", StringComparison.Ordinal) &&
            !lower.EndsWith("is", StringComparison.Ordinal) &&
            !lower.EndsWith("ics", StringComparison.Ordinal))
        {
            return lower[..^1];
        }

        return lower;
    }

    private static readonly HashSet<string> ActionPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "get", "list", "fetch", "find", "search", "create", "update", "delete", "remove", "add",
        "set", "upsert", "patch", "put", "replace", "count", "read", "lookup", "show", "load", "view", "return"
    };

    private static readonly HashSet<string> NoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "any", "each", "every", "the", "a", "an", "and", "or", "query", "mutation"
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "by", "for", "from", "in", "of", "on", "to", "with", "via", "using"
    };

    private static readonly HashSet<string> GenericDomainTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "apis", "graphql", "platform", "service", "services", "system", "systems",
        "query", "mutation", "entity", "entities"
    };

    private static readonly HashSet<string> StructuralSuffixTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "collection", "connection", "data", "detail", "details", "edge", "edges", "feed", "feeds",
        "info", "information", "item", "items", "list", "metadata", "meta", "metric", "metrics",
        "node", "nodes", "overview", "page", "pages", "payload", "payloads", "profile", "profiles",
        "record", "records", "report", "reports", "response", "responses", "result", "results",
        "stat", "stats", "summary", "summaries", "timeline", "total", "totals", "viewer"
    };

    private static readonly Dictionary<string, HashSet<string>> CompoundTokenSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Graph"] = new(StringComparer.OrdinalIgnoreCase) { "QL" },
        ["Open"] = new(StringComparer.OrdinalIgnoreCase) { "API" }
    };

    private static readonly Dictionary<string, string> IrregularSingulars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["children"] = "child",
        ["data"] = "data",
        ["feet"] = "foot",
        ["geese"] = "goose",
        ["indices"] = "index",
        ["men"] = "man",
        ["people"] = "person",
        ["series"] = "series",
        ["species"] = "species",
        ["teeth"] = "tooth",
        ["women"] = "woman"
    };

    private static readonly HashSet<string> UnsingularizableTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "analysis", "business", "status"
    };

    private static readonly Regex IdentifierTokenPattern =
        new(@"[A-Z]+(?=$|[A-Z][a-z]|\d)|[A-Z]?[a-z]+|\d+", RegexOptions.Compiled);

    private static Regex IdentifierTokenRegex() => IdentifierTokenPattern;
}
