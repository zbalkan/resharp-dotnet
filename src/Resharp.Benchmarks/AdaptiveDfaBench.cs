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
    private Resharp.Regex original = null!;
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

        original = new Resharp.Regex(
            pattern,
            CreateOptions(adaptiveStateIds: false, frozenFullDfa: false));
        adaptive = new Resharp.Regex(
            pattern,
            CreateOptions(adaptiveStateIds: true, frozenFullDfa: false));
        frozen = new Resharp.Regex(
            pattern,
            CreateOptions(adaptiveStateIds: true, frozenFullDfa: true));

        if (!original.IsFullDFA || !adaptive.IsFullDFA || !frozen.IsFullDFA)
            throw new InvalidOperationException(
                $"three-target DFA benchmark requires fully compiled DFAs; " +
                $"original full={original.IsFullDFA} states={original.DfaStateCount}, " +
                $"adaptive full={adaptive.IsFullDFA} states={adaptive.DfaStateCount}, " +
                $"frozen full={frozen.IsFullDFA} states={frozen.DfaStateCount}");

        if (original.IsFrozenDFA || adaptive.IsFrozenDFA || !frozen.IsFrozenDFA)
            throw new InvalidOperationException(
                $"invalid benchmark modes: original frozen={original.IsFrozenDFA}, " +
                $"adaptive frozen={adaptive.IsFrozenDFA}, frozen frozen={frozen.IsFrozenDFA}");

        int expected = original.LongestEnd(haystack.AsSpan());
        int adaptiveResult = adaptive.LongestEnd(haystack.AsSpan());
        int frozenResult = frozen.LongestEnd(haystack.AsSpan());
        if (expected != adaptiveResult || expected != frozenResult)
            throw new InvalidOperationException(
                $"three-target LongestEnd mismatch: " +
                $"original={expected}, adaptive={adaptiveResult}, frozen={frozenResult}");

        if (frozenResult != haystack.Length)
            throw new InvalidOperationException(
                $"synthetic workload should match the complete haystack: " +
                $"{frozenResult} != {haystack.Length}");

        int expectedWidth = MinimumPrefixLength <= 64 ? 1 : 2;
        if (original.DfaStateIdWidth != sizeof(int)
            || adaptive.DfaStateIdWidth != expectedWidth
            || frozen.DfaStateIdWidth != expectedWidth)
            throw new InvalidOperationException(
                $"unexpected DFA state widths: original={original.DfaStateIdWidth}, " +
                $"adaptive={adaptive.DfaStateIdWidth}, frozen={frozen.DfaStateIdWidth}");

        Console.WriteLine(
            $"three-target-dfa min-prefix={MinimumPrefixLength} " +
            $"states=original:{original.DfaStateCount},adaptive:{adaptive.DfaStateCount},frozen:{frozen.DfaStateCount} " +
            $"bytes=original:{original.DfaTransitionBytes},adaptive:{adaptive.DfaTransitionBytes},frozen:{frozen.DfaTransitionBytes} " +
            $"widths=original:{original.DfaStateIdWidth},adaptive:{adaptive.DfaStateIdWidth},frozen:{frozen.DfaStateIdWidth}");
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Match")]
    public int MatchOriginal() => original.LongestEnd(haystack.AsSpan());

    [Benchmark]
    [BenchmarkCategory("Match")]
    public int MatchAdaptive() => adaptive.LongestEnd(haystack.AsSpan());

    [Benchmark]
    [BenchmarkCategory("Match")]
    public int MatchFrozen() => frozen.LongestEnd(haystack.AsSpan());

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Build")]
    public Resharp.Regex BuildOriginal() =>
        new(pattern, CreateOptions(adaptiveStateIds: false, frozenFullDfa: false));

    [Benchmark]
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
