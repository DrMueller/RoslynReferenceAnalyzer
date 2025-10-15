using System.Collections.Concurrent;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynReferenceAnalyzer.Models;

namespace RoslynReferenceAnalyzer.Services
{
    internal static class SolutionAnalyzer
    {
        internal static async Task<IReadOnlyCollection<SingleUsingReference>> AnalyzeAsync(string solutionPath)
        {
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }

            using var ws = MSBuildWorkspace.Create();
            var sln = await ws.OpenSolutionAsync(solutionPath);
            var prodProjects = sln.Projects.Where(p => !CheckIsTestProject(p)).ToArray();
            prodProjects = prodProjects.Where(p => p.Name.EndsWith("Application", StringComparison.OrdinalIgnoreCase)).ToArray();
            var prodTypes = await CreateProdTypesAsync(prodProjects);
            var bag = new ConcurrentBag<SingleUsingReference>();

            await Parallel.ForEachAsync(prodTypes, async (type, ct) =>
            {
                var onlyUsedByTestsOrMediatr = await UsedOnlyByTestsOrMediatRHandlersAsync(sln, type, ct);

                if (onlyUsedByTestsOrMediatr)
                {
                    bag.Add(new SingleUsingReference(type));
                }
            });

            return bag;
        }

        private static async Task<bool> UsedOnlyByTestsOrMediatRHandlersAsync(
            Solution solution,
            ISymbol candidate,
            CancellationToken ct = default)
        {
            var refs = await SymbolFinder.FindReferencesAsync(candidate, solution, ct);
            var usedOnlyByTestsOrMediatr = true;

            foreach (var r in refs)
            {
                foreach (var loc in r.Locations)
                {
                    var doc = solution.GetDocument(loc.Document.Id);
                    if (doc is null)
                    {
                        continue;
                    }

                    if (CheckIsTestProject(doc.Project))
                    {
                        continue;
                    }

                    var isMediatrHandler = await CheckIsMediatRHandler(doc, candidate);
                    if (isMediatrHandler)
                    {
                        continue;
                    }

                    usedOnlyByTestsOrMediatr = false;
                    break;
                }
            }

            return usedOnlyByTestsOrMediatr;
        }

        private static async Task<bool> CheckIsMediatRHandler(Document doc, ISymbol candidate)
        {
            var model = await doc.GetSemanticModelAsync();
            var root = await doc.GetSyntaxRootAsync();
            if (model is null || root is null)
            {
                return false;
            }

            var classDeclaration = root
                .DescendantNodesAndSelf()
                .OfType<ClassDeclarationSyntax>()
                .SingleOrDefault(f => f.Identifier.Text.EndsWith("Handler"));

            if (classDeclaration is null)
            {
                return false;
            }

            var baseListSyntax = classDeclaration.DescendantNodes()
                .OfType<BaseListSyntax>()
                .Single();

            var identifiers = baseListSyntax.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .ToList();

            return identifiers.Any(g => g.Identifier.Text == candidate.Name);
        }

        private static bool CheckIsTestProject(Project p)
        {
            return p.Name.Contains("Tests", StringComparison.OrdinalIgnoreCase) && !p.Name.EndsWith("Testing.Common");
        }

        private static async Task<HashSet<INamedTypeSymbol>> CreateProdTypesAsync(IReadOnlyCollection<Project> projects)
        {
            var types = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var proj in projects)
            {
                var compilation = await proj.GetCompilationAsync();
                if (compilation is null)
                {
                    continue;
                }

                foreach (var doc in proj.Documents)
                {
                    var root = await doc.GetSyntaxRootAsync();
                    if (root is null)
                    {
                        continue;
                    }

                    var model = await doc.GetSemanticModelAsync();
                    if (model is null)
                    {
                        continue;
                    }

                    foreach (var node in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                    {
                        if (model.GetDeclaredSymbol(node) is not { } symbol)
                        {
                            continue;
                        }

                        if (symbol.IsImplicitlyDeclared)
                        {
                            continue;
                        }

                        if (!symbol.Name.EndsWith("Query") && !symbol.Name.EndsWith("Command"))
                        {
                            continue;
                        }

                        types.Add(symbol);
                    }
                }
            }

            return types;
        }
    }
}