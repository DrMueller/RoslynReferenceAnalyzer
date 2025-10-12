using RoslynReferenceAnalyzer.Models;
using RoslynReferenceAnalyzer.Services;
using Spectre.Console;

namespace RoslynReferenceAnalyzer
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var filePath = AnsiConsole.Ask<string>("Enter the [green]solution file path[/]:");

            var usings = await AnalyzeAsync(filePath);

            var table = new Table();
            table.AddColumn("Source");
            table.AddColumn("Target");

            var sortedUsings = usings.OrderBy(u => u.SourceType.ToDisplayString()).ToArray();

            foreach (var line in sortedUsings)
            {
                var targets = string.Join(", ", line.TargetDocuments.Select(d => d.Name).OrderBy(f => f));

                table.AddRow(line.SourceType.ToDisplayString(), targets);
            }

            AnsiConsole.Write(table);
        }

        private static async Task<IReadOnlyCollection<SingleUsingReference>> AnalyzeAsync(string slnFilePath)
        {
            return await AnsiConsole.Status()
                .Spinner(Spinner.Known.Circle)
                .StartAsync("Analyzing...", async _ => await SolutionAnalyzer.AnalyzeAsync(slnFilePath));
        }
    }
}