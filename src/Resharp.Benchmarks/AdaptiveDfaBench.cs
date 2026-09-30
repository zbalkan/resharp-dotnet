using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Resharp.Benchmarks;

[SimpleJob(launchCount: 3, warmupCount: 8, iterationCount: 12)]
[Config(typeof(BenchConfig))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class AdaptiveDfaBench
{
    private Resharp.Regex adaptive = null!;
    private Resharp.Regex frozen = null!;
    private string pattern = "";
    private string haystack = "";

    [Params(64, 300)]
    public int MinimumPrefixLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (pattern, haystack) = CreateWorkload(MinimumPrefixLength);

        adaptive = new Resharp.Regex(
            pattern,
            CreateOptions(adaptiveStateIds: true, frozenFullDfa: false));
        frozen = new Resharp.Regex(
            pattern,
            CreateOptions(adaptiveStateIds: true, frozenFullDfa: true));

        if (!adaptive.IsFullDFA || !frozen.IsFullDFA)
            throw new InvalidOperationException(
                $"frozen DFA benchmark requires fully compiled DFAs; " +
                $"adaptive full={adaptive.IsFullDFA} states={adaptive.DfaStateCount}, " +
                $"frozen full={frozen.IsFullDFA} states={frozen.DfaStateCount}");

        if (adaptive.IsFrozenDFA || !frozen.IsFrozenDFA)
            throw new InvalidOperationException(
                $"invalid benchmark modes: adaptive frozen={adaptive.IsFrozenDFA}, " +
                $"frozen frozen={frozen.IsFrozenDFA}");

        int expected = adaptive.LongestEnd(haystack.AsSpan());
        int actual = frozen.LongestEnd(haystack.AsSpan());
        if (expected != actual)
            throw new InvalidOperationException(
                $"adaptive/frozen LongestEnd mismatch: {expected} != {actual}");

        if (actual != haystack.Length)
            throw new InvalidOperationException(
                $"synthetic workload should match the complete haystack: {actual} != {haystack.Length}");

        int expectedWidth = MinimumPrefixLength <= 64 ? 1 : 2;
        if (adaptive.DfaStateIdWidth != expectedWidth || frozen.DfaStateIdWidth != expectedWidth)
            throw new InvalidOperationException(
                $"workload should exercise {expectedWidth}-byte DFA state IDs, got " +
                $"adaptive={adaptive.DfaStateIdWidth}, frozen={frozen.DfaStateIdWidth}");

        Console.WriteLine(
            $"frozen-dfa min-prefix={MinimumPrefixLength} " +
            $"adaptive-states={adaptive.DfaStateCount} frozen-states={frozen.DfaStateCount} " +
            $"adaptive={adaptive.DfaTransitionBytes}B/{adaptive.DfaStateIdWidth}-byte " +
            $"frozen={frozen.DfaTransitionBytes}B/{frozen.DfaStateIdWidth}-byte");
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Match")]
    public int MatchAdaptive() => adaptive.LongestEnd(haystack.AsSpan());

    [Benchmark]
    [BenchmarkCategory("Match")]
    public int MatchFrozen() => frozen.LongestEnd(haystack.AsSpan());

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Build")]
    public Resharp.Regex BuildAdaptive() =>
        new(pattern, CreateOptions(adaptiveStateIds: true, frozenFullDfa: false));

    [Benchmark]
    [BenchmarkCategory("Build")]
    public Resharp.Regex BuildFrozen() =>
        new(pattern, CreateOptions(adaptiveStateIds: true, frozenFullDfa: true));

    private static ResharpOptions CreateOptions(bool adaptiveStateIds, bool frozenFullDfa)
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
        options.UseFrozenFullDfa = frozenFullDfa;
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
