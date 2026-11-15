# Rented-buffer disposal and analyzer findings (2026-11-08)

Continues [#246][rollout] under epic [#269][epic]. Records the fixes made after the
GPT-6.1-Sol review of ScheduleLib PR [#226][pr] and the commit that cleared the
remaining analyzer errors in `IcsCalendarWriter` and `OneForEachEnumMember`.

## Disposal ordering defect

`RentedOneForEachEnumMemberArrayConverter.Read` returned its rented buffer before
enumerating the elements it had already parsed:

```csharp
catch
{
    ret.Dispose();
    foreach (var x in ret) { ... }
    throw;
}
```

`RentedBuffer.Dispose` clears arrays that contain references
([source][buffer]), so the enumeration observed cleared storage. Parsed
`IDisposable` elements were therefore never disposed, and the loop also read
storage that had already been returned for reuse by another renter.

The fix disposes the elements while the buffer is still owned, and returns the
buffer in `finally`:

```csharp
catch
{
    try
    {
        DisposeElements();
    }
    finally
    {
        ret.Dispose();
    }
    throw;
}
```

`Read` also clears the buffer immediately after renting it, so a partially read
JSON object cannot leave stale values visible to the element-disposal pass.
Cleanup exceptions still replace the original parse exception, matching the
previous behavior.

`RentedConverterTests.DisposesParsedElementsWhenReadingFails` covers the defect:
it deserializes two entries where the second has the wrong token type and asserts
that the successfully parsed disposable element was disposed exactly once.

## Analyzer findings

The full solution previously stopped at four analyzer errors:

- `IcsCalendarWriter.DefaultUid` computed two `FormatLocal(...)` arguments inline
  (CR0300). Both are now named locals, preserving argument order and values.
- `IcsCalendarWriter.Fold` tested a three-part surrogate-pair condition inline
  (CR0201). It is now `IsSurrogatePair(string, int)` with guard clauses; the
  high-surrogate, bounds and low-surrogate checks keep their original order and
  short-circuiting.
- `RentedOneForEachEnumMemberArrayConverter.Read` tested a three-part disposal
  predicate inline (CR0201). It is now `NeedsElementDisposal()` with guard
  clauses and the same predicate.

Extracted predicates are declared before their uses, following the shipped
guidance. No diagnostic was suppressed and no analyzer configuration changed.

## Validation

- `dotnet build All.sln -c Release --no-restore -t:Rebuild` → 0 errors, 146 warning
  lines (73 diagnostics printed twice).
- `Helper.Tests` → 52 passed, 1 existing skip.
- `Ics.Tests` → 14 passed.
- `ParsePdf.Tests` → 47 passed.

Changed files in this batch produce no warnings. The two CR0805 findings on the
compact page-reading chain remain, as recorded in the
[PDF reader review](pdf-reader-review-2026-11-07.md).

[buffer]: ../../src/ScheduleLib/ScheduleLib.Helper/Helper/RentedBuffer.cs#L38
[epic]: https://github.com/AntonC9018/ScheduleLib/issues/269
[pr]: https://github.com/AntonC9018/ScheduleLib/pull/226
[rollout]: https://github.com/AntonC9018/ScheduleLib/issues/246
