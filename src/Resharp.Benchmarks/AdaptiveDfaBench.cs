using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Resharp.Benchmarks;

[ShortRunJob]
[Config(typeof(BenchConfig))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class AdaptiveDfaBench
{
    private Resharp.Regex legacy = null!;
    private Resharp.Regex adaptive = null!;
    private string pattern = "";
    private string haystack = "";

    [Params(96, 300)]
    public int PatternLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (pattern, haystack) = CreateWorkload(PatternLength);

        legacy = new Resharp.Regex(pattern, CreateOptions(adaptiveStateIds: false));
        adaptive = new Resharp.Regex(pattern, CreateOptions(adaptiveStateIds: true));

        if (!legacy.IsFullDFA || !adaptive.IsFullDFA)
            throw new InvalidOperationException("adaptive DFA benchmark requires a fully compiled DFA");

        int expected = legacy.Count(haystack);
        int actual = adaptive.Count(haystack);
        if (expected != actual)
            throw new InvalidOperationException($"legacy/adaptive count mismatch: {expected} != {actual}");

        if (adaptive.DfaStateIdWidth >= 4)
            throw new InvalidOperationException(
                $"workload did not produce narrow DFA state IDs: {adaptive.DfaStateIdWidth} bytes");

        if (adaptive.DfaTransitionBytes >= legacy.DfaTransitionBytes)
            throw new InvalidOperationException(
                $"adaptive table was not smaller: {adaptive.DfaTransitionBytes} >= {legacy.DfaTransitionBytes}");

        Console.WriteLine(
            $"adaptive-dfa length={PatternLength} states={adaptive.DfaStateCount} " +
            $"legacy={legacy.DfaTransitionBytes}B/4-byte " +
            $"adaptive={adaptive.DfaTransitionBytes}B/{adaptive.DfaStateIdWidth}-byte");
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Match")]
    public int MatchInt32() => legacy.Count(haystack);

    [Benchmark]
    [BenchmarkCategory("Match")]
    public int MatchAdaptive() => adaptive.Count(haystack);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Build")]
    public Resharp.Regex BuildInt32() =>
        new(pattern, CreateOptions(adaptiveStateIds: false));

    [Benchmark]
    [BenchmarkCategory("Build")]
    public Resharp.Regex BuildAdaptive() =>
        new(pattern, CreateOptions(adaptiveStateIds: true));

    private static ResharpOptions CreateOptions(bool adaptiveStateIds)
    {
        var options = new ResharpOptions
        {
            InitialDfaCapacity = 512,
            MaxDfaCapacity = 4096,
            DfaThreshold = 2048,
            FindPotentialStartSizeLimit = 0,
            MaxPrefixLength = 0,
            FindLookaroundPrefix = false,
            StartsetInferenceLimit = 0,
            UseDotnetUnicode = false,
        };

        options.UseAdaptiveDfaStateIds = adaptiveStateIds;
        return options;
    }

    private static (string Pattern, string Haystack) CreateWorkload(int length)
    {
        const int classCount = 32;
        var pattern = new StringBuilder(length * 4);
        var oneMatch = new StringBuilder(length);

        for (int i = 0; i < length; i++)
        {
            int cls = i % classCount;
            char first = (char)(0x0100 + cls * 2);
            char second = (char)(0x0101 + cls * 2);

            pattern.Append('[').Append(first).Append(second).Append(']');
            oneMatch.Append(first);
        }

        const int targetChars = 1 << 20;
        int repeats = Math.Max(1, targetChars / oneMatch.Length);
        var haystack = new StringBuilder(oneMatch.Length * repeats);

        for (int i = 0; i < repeats; i++)
            haystack.Append(oneMatch);

        return (pattern.ToString(), haystack.ToString());
    }
}
