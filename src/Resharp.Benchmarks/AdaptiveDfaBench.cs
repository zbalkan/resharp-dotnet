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

    [Params(64, 300)]
    public int MinimumPrefixLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (pattern, haystack) = CreateWorkload(MinimumPrefixLength);

        legacy = new Resharp.Regex(pattern, CreateOptions(adaptiveStateIds: false));
        adaptive = new Resharp.Regex(pattern, CreateOptions(adaptiveStateIds: true));

        if (!legacy.IsFullDFA || !adaptive.IsFullDFA)
            throw new InvalidOperationException(
                $"adaptive DFA benchmark requires a fully compiled DFA; " +
                $"legacy full={legacy.IsFullDFA} states={legacy.DfaStateCount}, " +
                $"adaptive full={adaptive.IsFullDFA} states={adaptive.DfaStateCount}");

        int expected = legacy.LongestEnd(haystack.AsSpan());
        int actual = adaptive.LongestEnd(haystack.AsSpan());
        if (expected != actual)
            throw new InvalidOperationException(
                $"legacy/adaptive LongestEnd mismatch: {expected} != {actual}");

        if (actual != haystack.Length)
            throw new InvalidOperationException(
                $"synthetic workload should match the complete haystack: {actual} != {haystack.Length}");

        if (adaptive.DfaStateIdWidth >= 4)
            throw new InvalidOperationException(
                $"workload did not produce narrow DFA state IDs: {adaptive.DfaStateIdWidth} bytes");

        if (adaptive.DfaTransitionBytes >= legacy.DfaTransitionBytes)
            throw new InvalidOperationException(
                $"adaptive table was not smaller: {adaptive.DfaTransitionBytes} >= {legacy.DfaTransitionBytes}");

        Console.WriteLine(
            $"adaptive-dfa min-prefix={MinimumPrefixLength} states={adaptive.DfaStateCount} " +
            $"legacy={legacy.DfaTransitionBytes}B/4-byte " +
            $"adaptive={adaptive.DfaTransitionBytes}B/{adaptive.DfaStateIdWidth}-byte");
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Match")]
    public int MatchInt32() => legacy.LongestEnd(haystack.AsSpan());

    [Benchmark]
    [BenchmarkCategory("Match")]
    public int MatchAdaptive() => adaptive.LongestEnd(haystack.AsSpan());

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

    private static (string Pattern, string Haystack) CreateWorkload(int minimumPrefixLength)
    {
        // The prefix produces roughly one DFA state per required character and
        // then remains in a single loop. The suffix contributes many distinct
        // minterms without creating the subset explosion of a periodic prefix.
        const char prefix0 = '\u0100';
        const char prefix1 = '\u0101';
        const int suffixAlternatives = 32;

        var pattern = new StringBuilder();
        pattern
            .Append('[').Append(prefix0).Append(prefix1).Append(']')
            .Append('{').Append(minimumPrefixLength).Append(",}")
            .Append("(?:");

        for (int i = 0; i < suffixAlternatives; i++)
        {
            if (i != 0)
                pattern.Append('|');

            char first0 = (char)(0x0200 + i * 4);
            char first1 = (char)(0x0201 + i * 4);
            char second0 = (char)(0x0202 + i * 4);
            char second1 = (char)(0x0203 + i * 4);

            pattern
                .Append('[').Append(first0).Append(first1).Append(']')
                .Append('[').Append(second0).Append(second1).Append(']');
        }

        pattern.Append(')');

        const int targetChars = 1 << 20;
        int prefixChars = Math.Max(minimumPrefixLength, targetChars - 2);
        var haystack = new StringBuilder(prefixChars + 2);
        haystack.Append(prefix0, prefixChars);
        haystack.Append((char)0x0200);
        haystack.Append((char)0x0202);

        return (pattern.ToString(), haystack.ToString());
    }
}
