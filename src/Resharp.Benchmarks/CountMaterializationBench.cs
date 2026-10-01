using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Resharp.Benchmarks;

[SimpleJob(launchCount: 3, warmupCount: 8, iterationCount: 12)]
[MemoryDiagnoser]
[Config(typeof(BenchConfig))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CountMaterializationBench
{
    private Resharp.Regex original = null!;
    private Resharp.Regex direct = null!;
    private string haystack = "";

    [ParamsSource(nameof(BenchNames))]
    public string Name { get; set; } = "";

    public IEnumerable<string> BenchNames => new[]
    {
        "curated/08-words/all-english",
        "curated/08-words/long-english",
        "curated/10-bounded-repeat/letters-en",
        "curated/10-bounded-repeat/context",
        "curated/03-date/ascii",
        "curated/12-dictionary/single",
        "resharp/02-monster/context-case-ig",
        "resharp/02-monster/dictionary-case-ig",
    }.Where(RebarData.BenchMap.Value.ContainsKey);

    [GlobalSetup]
    public void Setup()
    {
        var bench = RebarData.BenchMap.Value[Name];
        haystack = bench.Haystack;

        original = new Resharp.Regex(
            bench.Pattern,
            CreateOptions(bench.CaseInsensitive, directCount: false));
        direct = new Resharp.Regex(
            bench.Pattern,
            CreateOptions(bench.CaseInsensitive, directCount: true));

        int expected = original.Count(haystack);
        int actual = direct.Count(haystack);

        if (expected != actual)
            throw new InvalidOperationException(
                $"Count mismatch for '{Name}': original={expected}, direct={actual}");

        double densityPerKib =
            haystack.Length == 0 ? 0 : expected * 1024.0 / haystack.Length;

        Console.WriteLine(
            $"count-materialization name={Name} chars={haystack.Length} " +
            $"matches={expected} matches-per-kib={densityPerKib:F3}");
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Count")]
    public int Original() => original.Count(haystack);

    [Benchmark]
    [BenchmarkCategory("Count")]
    public int Direct() => direct.Count(haystack);

    private static ResharpOptions CreateOptions(bool ignoreCase, bool directCount)
    {
        var options = ResharpOptions.HighThroughputDefaults;
        options.IgnoreCase = ignoreCase;
        options.UseDirectCount = directCount;
        return options;
    }
}
