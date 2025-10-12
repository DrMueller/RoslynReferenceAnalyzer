using Microsoft.CodeAnalysis;

namespace RoslynReferenceAnalyzer.Models
{
    public record SingleUsingReference(
        INamedTypeSymbol SourceType,
        IReadOnlyCollection<Document> TargetDocuments);
}