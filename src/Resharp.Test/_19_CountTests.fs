[<Xunit.Collection("Sequential")>]
module Resharp.Test._19_CountTests

open System
open Resharp
open Xunit

let private makeRegex pattern direct =
    let options = ResharpOptions()
    options.UseDirectCount <- direct
    Regex(pattern, options)

let private assertCountEquivalent (pattern: string) (input: string) =
    let direct = makeRegex pattern true
    let materialized = makeRegex pattern false

    use matches = direct.ValueMatches(input.AsSpan())
    let expected = matches.Count

    Assert.Equal(expected, materialized.Count(input.AsSpan()))
    Assert.Equal(expected, direct.Count(input.AsSpan()))

[<Theory>]
[<InlineData("foo", "foo foofoo x foo")>]
[<InlineData("aa", "aaaaa")>]
[<InlineData("[A-Za-z]{8,13}", "abcdefgh abcdefghijkl z abcdefghijklm")>]
[<InlineData("\\b[0-9A-Za-z_]+\\b", "one two three_4 five")>]
[<InlineData("a+", "baaa ca aaaaa")>]
[<InlineData("a*", "bbb")>]
[<InlineData("^a.*b$", "a middle b")>]
[<InlineData("(?<=x)a+", "xaa xxaaa ya")>]
[<InlineData("foo", "")>]
[<InlineData("a*", "")>]
let direct_count_matches_materialized_semantics (pattern: string) (input: string) =
    assertCountEquivalent pattern input

[<Fact>]
let direct_count_remains_equivalent_after_lazy_dfa_growth () =
    let pattern = "(?:ab|ac|ad|ae|af|ag|ah|ai|aj|ak)+"
    let inputs = [|
        "abacadaeafagahaiajak"
        "zz ababababab zz"
        "akajaiahagafaeadacab"
        "nothing here"
    |]

    let direct = makeRegex pattern true
    let materialized = makeRegex pattern false

    for _ = 1 to 4 do
        for input in inputs do
            Assert.Equal(materialized.Count(input.AsSpan()), direct.Count(input.AsSpan()))
