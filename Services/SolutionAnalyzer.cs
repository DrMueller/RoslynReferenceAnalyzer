using System.Collections.Concurrent;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
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

            var prodProjects = sln.Projects.Where(p => !IsTestProject(p)).ToArray();

            // Lets just use application layer for the time being
            prodProjects = prodProjects.Where(p => p.Name.EndsWith("Application", StringComparison.OrdinalIgnoreCase)).ToArray();

            var docIsTest = new Dictionary<DocumentId, bool>();
            foreach (var projects in sln.Projects)
            foreach (var d in projects.Documents)
            {
                docIsTest[d.Id] = IsTestProject(projects);
            }

            var prodTypes = await CreateProdTypesAsync(prodProjects);

            var bag = new ConcurrentBag<SingleUsingReference>();

            await Parallel.ForEachAsync(prodTypes, async (type, ct) =>
            {
                var references = await SymbolFinder.FindReferencesAsync(type, sln, ct);
                var allRefLocations = new List<Document>();
                var hasNonTestRef = false;

                foreach (var refItem in references)
                {
                    foreach (var loc in refItem.Locations)
                    {
                        var doc = sln.GetDocument(loc.Document.Id);
                        if (doc is null)
                        {
                            continue;
                        }

                        var isTest = docIsTest.TryGetValue(doc.Id, out var flag) && flag;

                        if (!isTest)
                        {
                            hasNonTestRef = true;
                        }

                        allRefLocations.Add(doc);
                    }
                }

                if ((!hasNonTestRef && allRefLocations.Count > 0) || !allRefLocations.Any())
                {
                    bag.Add(new SingleUsingReference(type, allRefLocations));
                }
            });

            return bag;
        }

        private static bool IsTestProject(Project p)
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
                        var symbol = model.GetDeclaredSymbol(node) as INamedTypeSymbol;
                        if (symbol is null)
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