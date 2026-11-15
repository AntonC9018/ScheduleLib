# Schedule serialization (#243)

Configure a reusable operation with `ScheduleSerializer.Serialize()`:

```csharp
var serialization = ScheduleSerializer.Serialize()
    .Input(schedule)
    .Output(cachePath)
    .Hash(sourceAndConfigurationIdentity);
await serialization.ExecuteAsync(cancellationToken);
```

`Input(Schedule)` and `Output(Stream)` or `Output(string path)` are required.
`Hash(string)` is optional and defaults to `""`. Missing configuration throws
`MissingSerializationConfigurationException`, whose `Configuration` identifies
`Input` or `Output`, before any output is touched. Null inputs, nonwritable
streams and empty/invalid paths are rejected when configured. Stream state and
filesystem errors can still fail execution; the original exception propagates.

Each successful setter replaces its previous value. Output forms replace each
other, so only the last selected stream or path is written. Rejected setters
leave prior values intact. Execution retains all configuration, including after
failure/cancellation, allowing correction and retry. The mutable builder is not
thread-safe. Each execution captures its configuration before asynchronous I/O.

Stream output writes at the current position without seeking, truncating or
closing the caller's stream, including on failure/cancellation. A repeated stream
execution continues at its current position; callers rewind/truncate or select a
new stream when they want a separate document. A failed stream write can leave
partial bytes, as before.

Path output resolves the full path when configured and opens files only at
execution. The shared `AtomicFilePublisher` stages in the destination filesystem,
flushes complete contents to disk, closes its file, checks cancellation and then
replaces the destination atomically. Errors or cancellation before publication
leave the old destination intact and clean up staging files. Parent directories
are created as needed. Concurrent writers still require caller coordination;
`ScheduleLoader` retains its cache lock across reading, rebuilding and publication.
The existing Integration `AtomicFile` entry point forwards to this shared helper.

Hash is the caller's cache-validity identity, stored verbatim in the unchanged
JSON model; it is not a digest of the output. The loader continues hashing source
content and configuration, accepting cached models only when that hash matches.
Omitting Hash is appropriate for exports and fixtures, and does not make those
outputs valid caches for a different loader identity. Deserialization and legacy
JSON handling are unchanged.

Required configuration was manually traced through every migrated execution:

- `ScheduleLoader.Load` supplies Input, path Output and the computed Hash on its
  cache-publication path, under the existing cache lock.
- `FmiPdfImport.GenerateAsync` supplies Input and path Output, using default Hash.
- ScheduleFromDoc regeneration and round-trip tests supply Input and stream
  Output, using default Hash. Their surrounding scopes own the streams.
- GroupCombination round-trip/schema tests supply Input, stream Output and Hash.
- Focused serializer tests deliberately omit requirements to check runtime
  validation. Cache tests exercise the loader and export default Hash.

No required-step analyzer, annotations or staged generator is involved.
