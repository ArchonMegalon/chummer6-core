using BenchmarkDotNet.Running;

namespace Chummer.Benchmarks;

internal static class Program
{
    private static int Main(string[] args)
    {
        bool enforceBudgets = HasFlag(args, "--budget-check");
        bool measureOnly = HasFlag(args, "--measure");
        if (enforceBudgets || measureOnly)
        {
            string? budgetFilePath = GetOption(args, "--budget-file");
            string? resultFilePath = GetOption(args, "--result-file");
            return BenchmarkBudgetRunner.MeasureAndOptionallyCheck(budgetFilePath, resultFilePath, enforceBudgets);
        }

        BenchmarkRunner.Run<MigrationWorkspaceBenchmarks>();
        return 0;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(arg => string.Equals(arg, flag, StringComparison.Ordinal));
    }

    private static string? GetOption(string[] args, string optionName)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
