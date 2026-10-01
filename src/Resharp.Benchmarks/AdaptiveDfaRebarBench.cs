using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Resharp.Benchmarks;

[SimpleJob(launchCount: 3, warmupCount: 8, iterationCount: 12)]
[Config(typeof(BenchConfig))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class AdaptiveDfaRebarBench
{
    private Resharp.Regex original = null!;
    private Resharp.Regex adaptive = null!;
    private Resharp.Regex frozen = null!;
    private string haystack = "";

    [ParamsSource(nameof(BenchNames))]
    public string Name { get; set; } = "";

    public IEnumerable<string> BenchNames
    {
        get
        {
            var all = RebarData.BenchNames.Value;
            if (RebarData.NameSet is { } set)
                return all.Where(set.Contains);
            return RebarData.NameFilter is { } f
                ? all.Where(n => n.StartsWith(f))
                : all;
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        var bench = RebarData.BenchMap.Value[Name];
        haystack = bench.Haystack;

        original = new Resharp.Regex(
            bench.Pattern,
            CreateOptions(bench.CaseInsensitive, adaptiveStateIds: false, frozenFullDfa: false));
        adaptive = new Resharp.Regex(
            bench.Pattern,
            CreateOptions(bench.CaseInsensitive, adaptiveStateIds: true, frozenFullDfa: false));
        frozen = new Resharp.Regex(
            bench.Pattern,
            CreateOptions(bench.CaseInsensitive, adaptiveStateIds: true, frozenFullDfa: true));

        int expected = original.Count(haystack);
        int adaptiveResult = adaptive.Count(haystack);
        int frozenResult = frozen.Count(haystack);

        if (expected != adaptiveResult || expected != frozenResult)
            throw new InvalidOperationException(
                $"three-target Rebar mismatch for '{Name}': " +
                $"original={expected}, adaptive={adaptiveResult}, frozen={frozenResult}");

        Console.WriteLine(
            $"three-target-rebar name={Name} " +
            $"full=original:{original.IsFullDFA},adaptive:{adaptive.IsFullDFA},frozen:{frozen.IsFullDFA} " +
            $"frozen=original:{original.IsFrozenDFA},adaptive:{adaptive.IsFrozenDFA},frozen:{frozen.IsFrozenDFA} " +
            $"states=original:{original.DfaStateCount},adaptive:{adaptive.DfaStateCount},frozen:{frozen.DfaStateCount} " +
            $"widths=original:{original.DfaStateIdWidth},adaptive:{adaptive.DfaStateIdWidth},frozen:{frozen.DfaStateIdWidth} " +
            $"bytes=original:{original.DfaTransitionBytes},adaptive:{adaptive.DfaTransitionBytes},frozen:{frozen.DfaTransitionBytes}");
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("RebarDfa")]
    public int Original() => original.Count(haystack);

    [Benchmark]
    [BenchmarkCategory("RebarDfa")]
    public int Adaptive() => adaptive.Count(haystack);

    [Benchmark]
    [BenchmarkCategory("RebarDfa")]
    public int Frozen() => frozen.Count(haystack);

    private static ResharpOptions CreateOptions(
        bool ignoreCase,
        bool adaptiveStateIds,
        bool frozenFullDfa)
    {
        var options = ResharpOptions.HighThroughputDefaults;
        options.IgnoreCase = ignoreCase;
        options.UseAdaptiveDfaStateIds = adaptiveStateIds;
        options.UseFrozenFullDfa = frozenFullDfa;
        return options;
    }
}
