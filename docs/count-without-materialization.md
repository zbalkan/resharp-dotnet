# Count without result materialization

## Problem

`Regex.Count` currently computes the complete `ValueMatch` result set by calling
`llmatch_all` and then returns only `results.size`. The caller never observes the
materialized match spans.

The existing matcher has two semantically important phases:

1. a reverse pass collects candidate match starts in `ValueList<int>`;
2. an end-selection pass processes those starts in forward order, determines the
   POSIX match end, and rejects overlapping candidates.

The candidate-start list cannot simply be removed: its entries are not yet final
matches, and the second pass is what enforces match-end and non-overlap semantics.

## Design

Keep the existing candidate collection and end-selection algorithms unchanged.
For `Count`, replace only the terminal output sink.

Where the normal path performs:

```text
validate candidate
determine match end
enforce non-overlap
append ValueMatch(start, length)
```

the count-only path performs:

```text
validate candidate
determine match end
enforce non-overlap
count++
```

Dedicated count-only terminal methods mirror each existing `LengthLookup`
specialization:

- general skip path;
- general no-skip path;
- `FixedLength`;
- `RemainingSets`;
- `SetLookup`;
- complete literal `MatchOverride`.

The empty-input case uses the same nullable-state predicate as
`HandleZeroLengthString`.

## Correctness argument

Let the original terminal pass produce the sequence
`M = [m0, m1, ..., mk-1]`. Each append is guarded solely by matcher state,
candidate start position, match end, and the same non-overlap cursor
(`nextValidStart` or `pos`). No later decision reads the contents of a previously
appended `ValueMatch`; later decisions only read the non-overlap cursor.

The count-only pass preserves every state transition, candidate traversal,
match-end calculation, branch, and cursor update. It substitutes `count <- count + 1`
for each `matches.Add(...)`. Therefore it increments exactly once for every element
the original pass would append, so its result is `|M|`.

The literal override follows the same argument: the search cursor advances by the
same amount after every accepted occurrence; only the stored span is omitted.

## Complexity

If `s` is the number of candidate starts and `k` the number of final matches:

- original auxiliary storage: `O(s + k)`;
- direct-count auxiliary storage: `O(s)`;
- matching/verification work remains unchanged;
- `O(k)` `ValueMatch` stores and any associated pooled-buffer growth are removed.

The optimization should therefore scale with final match density. Sparse/no-match
workloads are expected to be close to neutral.

## Benchmark design

The benchmark constructs two otherwise identical RE# instances in the same
BenchmarkDotNet process:

- **Original**: `UseDirectCount = false`, preserving `llmatch_all(...).size`;
- **Direct**: `UseDirectCount = true`, using the count-only sink.

Both variants are validated for equal counts before measurement. The suite includes
high-density word/bounded-repeat workloads as well as lower-density date,
dictionary, and monster-regex workloads. Three launches, eight warmups, and twelve
measurement iterations are used, with memory diagnostics enabled.
