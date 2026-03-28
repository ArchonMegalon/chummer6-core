using System.Diagnostics;
using System.Text.Json;

namespace Chummer.Benchmarks;

internal sealed record BenchmarkBudgetThreshold(
    string Name,
    double MaxMeanMilliseconds,
    long MaxAllocatedBytes);

internal sealed record BenchmarkBudgetConfig(
    int WarmupIterations,
    int MeasurementIterations,
    IReadOnlyList<BenchmarkBudgetThreshold> Workloads);

internal sealed record BenchmarkMeasurementResult(
    string Name,
    double MeanMilliseconds,
    long MeanAllocatedBytes,
    IReadOnlyList<double> ElapsedMeasurementsMs,
    IReadOnlyList<long> AllocatedMeasurementsBytes);

internal sealed record BenchmarkResultsDocument(
    DateTimeOffset MeasuredAtUtc,
    int WarmupIterations,
    int MeasurementIterations,
    IReadOnlyList<BenchmarkMeasurementResult> Workloads);

internal sealed record BenchmarkWorkload(
    string Name,
    Action Setup,
    Func<object?> Execute,
    Action Cleanup);

internal static class BenchmarkBudgetRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static int MeasureAndOptionallyCheck(string? budgetFilePath, string? resultFilePath, bool enforceBudgets)
    {
        BenchmarkBudgetConfig config = LoadConfig(budgetFilePath);
        BenchmarkWorkload[] workloads = MigrationWorkspaceBenchmarks.CreateBudgetWorkloads().ToArray();
        BenchmarkMeasurementResult[] results = workloads
            .Select(workload => Measure(workload, config.WarmupIterations, config.MeasurementIterations))
            .ToArray();

        WriteResults(resultFilePath, config, results);

        bool failed = false;
        foreach (BenchmarkMeasurementResult result in results)
        {
            Console.WriteLine(
                $"{result.Name}: mean={result.MeanMilliseconds:F2} ms, alloc={result.MeanAllocatedBytes} bytes");

            if (!enforceBudgets)
            {
                continue;
            }

            BenchmarkBudgetThreshold? threshold = config.Workloads.FirstOrDefault(item =>
                string.Equals(item.Name, result.Name, StringComparison.Ordinal));
            if (threshold is null)
            {
                Console.Error.WriteLine($"Missing performance budget for workload '{result.Name}'.");
                failed = true;
                continue;
            }

            if (result.MeanMilliseconds > threshold.MaxMeanMilliseconds)
            {
                Console.Error.WriteLine(
                    $"Workload '{result.Name}' exceeded mean budget. Actual {result.MeanMilliseconds:F2} ms, budget {threshold.MaxMeanMilliseconds:F2} ms.");
                failed = true;
            }

            if (result.MeanAllocatedBytes > threshold.MaxAllocatedBytes)
            {
                Console.Error.WriteLine(
                    $"Workload '{result.Name}' exceeded allocation budget. Actual {result.MeanAllocatedBytes} bytes, budget {threshold.MaxAllocatedBytes} bytes.");
                failed = true;
            }
        }

        return failed ? 1 : 0;
    }

    private static BenchmarkMeasurementResult Measure(BenchmarkWorkload workload, int warmupIterations, int measurementIterations)
    {
        workload.Setup();
        try
        {
            for (int i = 0; i < warmupIterations; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                object? warmup = workload.Execute();
                GC.KeepAlive(warmup);
            }

            List<double> elapsedMeasurements = new(measurementIterations);
            List<long> allocatedMeasurements = new(measurementIterations);
            for (int i = 0; i < measurementIterations; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long timestamp = Stopwatch.GetTimestamp();
                object? result = workload.Execute();
                double elapsedMs = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
                long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

                GC.KeepAlive(result);
                elapsedMeasurements.Add(elapsedMs);
                allocatedMeasurements.Add(Math.Max(0L, allocatedAfter - allocatedBefore));
            }

            long meanAllocatedBytes = (long)Math.Round(allocatedMeasurements.Average());
            return new BenchmarkMeasurementResult(
                workload.Name,
                MeanMilliseconds: elapsedMeasurements.Average(),
                MeanAllocatedBytes: meanAllocatedBytes,
                ElapsedMeasurementsMs: elapsedMeasurements,
                AllocatedMeasurementsBytes: allocatedMeasurements);
        }
        finally
        {
            workload.Cleanup();
        }
    }

    private static BenchmarkBudgetConfig LoadConfig(string? budgetFilePath)
    {
        if (string.IsNullOrWhiteSpace(budgetFilePath))
        {
            return new BenchmarkBudgetConfig(
                WarmupIterations: 1,
                MeasurementIterations: 3,
                Workloads: []);
        }

        BenchmarkBudgetConfig? config = JsonSerializer.Deserialize<BenchmarkBudgetConfig>(
            File.ReadAllText(budgetFilePath),
            JsonOptions);

        if (config is null)
        {
            throw new InvalidOperationException($"Unable to deserialize benchmark budget config '{budgetFilePath}'.");
        }

        return config;
    }

    private static void WriteResults(
        string? resultFilePath,
        BenchmarkBudgetConfig config,
        IReadOnlyList<BenchmarkMeasurementResult> results)
    {
        if (string.IsNullOrWhiteSpace(resultFilePath))
        {
            return;
        }

        string fullPath = Path.GetFullPath(resultFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        BenchmarkResultsDocument document = new(
            MeasuredAtUtc: DateTimeOffset.UtcNow,
            WarmupIterations: config.WarmupIterations,
            MeasurementIterations: config.MeasurementIterations,
            Workloads: results);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(document, JsonOptions));
    }
}
