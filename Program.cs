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
            var sortedUsings = usings.OrderBy(u => u.SourceType.Name).ToArray();

            foreach (var line in sortedUsings)
            {
                table.AddRow(line.SourceType.Name);
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