module Resharp.Test._19_AdaptiveDfaTests

open System
open System.Text
open System.Threading.Tasks
open Xunit
open Resharp

let private createOptions (adaptive: bool) (frozen: bool) =
    let options = ResharpOptions()
    options.InitialDfaCapacity <- 512
    options.MaxDfaCapacity <- 4096
    options.DfaThreshold <- 2048
    options.FindPotentialStartSizeLimit <- 0
    options.MaxPrefixLength <- 0
    options.FindLookaroundPrefix <- false
    options.StartsetInferenceLimit <- 0
    options.UseDotnetUnicode <- false
    options.UseAdaptiveDfaStateIds <- adaptive
    options.UseFrozenFullDfa <- frozen
    options

let private createWorkload (minimumPrefixLength: int) =
    let prefix0 = '\u0100'
    let prefix1 = '\u0101'
    let suffixAlternatives = 32

    let pattern = StringBuilder()
    pattern
        .Append('[').Append(prefix0).Append(prefix1).Append(']')
        .Append('{').Append(minimumPrefixLength).Append(",}")
        .Append("(?:")
    |> ignore

    for i = 0 to suffixAlternatives - 1 do
        if i <> 0 then
            pattern.Append('|') |> ignore

        let first0 = char (0x0200 + i * 4)
        let first1 = char (0x0201 + i * 4)
        let second0 = char (0x0202 + i * 4)
        let second1 = char (0x0203 + i * 4)

        pattern
            .Append('[').Append(first0).Append(first1).Append(']')
            .Append('[').Append(second0).Append(second1).Append(']')
        |> ignore

    pattern.Append(')') |> ignore

    let prefixChars = max minimumPrefixLength 2048
    let haystack = StringBuilder(prefixChars + 2)
    haystack.Append(prefix0, prefixChars) |> ignore
    haystack.Append(char 0x0200) |> ignore
    haystack.Append(char 0x0202) |> ignore

    pattern.ToString(), haystack.ToString()

let private assertEquivalent (minimumPrefixLength: int) (expectedWidth: int) =
    let pattern, haystack = createWorkload minimumPrefixLength
    let legacy = Regex(pattern, createOptions false false)
    let adaptive = Regex(pattern, createOptions true false)
    let frozen = Regex(pattern, createOptions true true)

    Assert.True(legacy.IsFullDFA)
    Assert.True(adaptive.IsFullDFA)
    Assert.True(frozen.IsFullDFA)
    Assert.False(adaptive.IsFrozenDFA)
    Assert.True(frozen.IsFrozenDFA)
    Assert.Equal(4, legacy.DfaStateIdWidth)
    Assert.Equal(expectedWidth, adaptive.DfaStateIdWidth)
    Assert.Equal(expectedWidth, frozen.DfaStateIdWidth)
    Assert.True(adaptive.DfaTransitionBytes < legacy.DfaTransitionBytes)

    let legacyIsMatch = legacy.IsMatch(haystack)
    let legacyCount = legacy.Count(haystack)
    let legacyFirstEnd = legacy.FirstEnd(haystack)
    let legacyLongestEnd = legacy.LongestEnd(haystack)

    Assert.Equal(legacyIsMatch, adaptive.IsMatch(haystack))
    Assert.Equal(legacyCount, adaptive.Count(haystack))
    Assert.Equal(legacyFirstEnd, adaptive.FirstEnd(haystack))
    Assert.Equal(legacyLongestEnd, adaptive.LongestEnd(haystack))

    Assert.Equal(legacyIsMatch, frozen.IsMatch(haystack))
    Assert.Equal(legacyCount, frozen.Count(haystack))
    Assert.Equal(legacyFirstEnd, frozen.FirstEnd(haystack))
    Assert.Equal(legacyLongestEnd, frozen.LongestEnd(haystack))

    legacy, frozen, haystack

[<Fact>]
let ``full DFA uses byte state IDs when state count fits`` () =
    let _, adaptive, _ = assertEquivalent 64 1
    Assert.InRange(adaptive.DfaStateCount, 1, int Byte.MaxValue)

[<Fact>]
let ``full DFA uses ushort state IDs above byte range`` () =
    let _, adaptive, _ = assertEquivalent 300 2
    Assert.True(adaptive.DfaStateCount > int Byte.MaxValue)
    Assert.InRange(adaptive.DfaStateCount, int Byte.MaxValue + 1, int UInt16.MaxValue)

[<Fact>]
let ``compact full DFA remains stable under concurrent reads`` () =
    let _, adaptive, haystack = assertEquivalent 300 2
    let expectedCount = adaptive.Count(haystack)
    let expectedEnd = adaptive.LongestEnd(haystack)

    Parallel.For(
        0,
        max 8 (Environment.ProcessorCount * 2),
        fun _ ->
            for _ = 1 to 20 do
                Assert.Equal(expectedCount, adaptive.Count(haystack))
                Assert.Equal(expectedEnd, adaptive.LongestEnd(haystack))
    )
    |> ignore
