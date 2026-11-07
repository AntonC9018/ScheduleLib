# Vector mathematics library research

Checked 2026-11-07 for the calculation cleanup in #246. Adopted for the reviewed
PDF geometry and deadline color interpolation, with the midpoint decision below.

## Recommendation

Use **OpenTK.Mathematics 4.9.4** for this scope. Its `Vector2d` supplies double
coordinates, overloaded arithmetic, `Length`, `LengthSquared`, normalization and
perpendicular vectors. Its vector `Lerp` and scalar `MathHelper.Lerp` cover the
midpoint and interpolation cases. These are established library operations, so
there is no need to create our own vector type or squared-length helper.
See the [pinned vector source][open-vector] and [scalar source][open-math].

The mathematics package is independent of OpenTK's graphics/windowing packages;
its only declared dependency is `System.Runtime.CompilerServices.Unsafe >= 6.0.0`.
It targets .NET Standard 2.1 and .NET Core 3.1, compatible with this repository's
`net11.0` target. It uses the MIT license. See [package metadata][open-package]
and the [pinned project][open-project].

## Alternatives checked

| Package | Principal type | Fit |
| --- | --- | --- |
| OpenTK.Mathematics 4.9.4 | `Vector2d` | Recommended; direct API coverage. |
| Silk.NET.Maths 2.23.0 | `Vector2D<double>` | Useful if generic coordinates are needed. |
| MathNet.Spatial 0.6.0 | `Point2D`, `Vector2D` | Distinct points; fewer interpolation APIs. |

[Silk's pinned vector][silk-vector] and [operations][silk-ops] confirm its generic
API. Its .NET 5 asset has no package dependencies; other assets may require
`Ultz.Bcl.Half` and `Microsoft.Bcl.HashCode`. It is MIT licensed and usable
independently of Silk graphics bindings. See [package metadata][silk-package].

Math.NET has an explicit [point midpoint operation][math-point], but its pinned
`Point2D` and [`Vector2D`][math-vector] do not expose `Lerp`. Its .NET Standard 2.0
package depends on `MathNet.Numerics` and `System.Diagnostics.Contracts`. It is MIT
licensed; the latest stable package remains 0.6.0 from 2020. See
[package metadata][math-package].

## Use in ScheduleLib

- In `PdfTableReader.LetterAnchor`, convert the PdfPig endpoints into `Vector2d`
  at the library boundary. Use `end - start`, `.Length`, scalar division and
  `.PerpendicularLeft` for displacement, direction and normal offset. Preserve
  the existing zero-length fallback to `Vector2d.UnitX`. Convert the result back
  to `PdfPoint` at the boundary.
- Use `Midpoint` extensions for vectors and scalar coordinates. The maintainer
  selected exactly `(start + end) * 0.5`, preserving the existing sum-then-scale
  calculation rather than using halfway interpolation.
- `PdfBox.CenterX` and `CenterY` already name their geometric meaning. Their
  generated numbered temporaries are replaced by scalar midpoint extension calls.
- In `ColorHelper.Interpolate`, use `MathHelper.Lerp((float) from, (float) to, t)`
  and preserve the final integer cast. Keep the existing clamp in `ColorHelper.Lerp`.
  This replaces the manual formula while retaining the color policy in its owner.
- The shared helper project references the package and owns the midpoint extensions
  in `ScheduleLib.Mathematics`. `ParsePdf` and `Integration` receive the dependency
  through their existing helper reference. PDF coordinate conversions stay in the importer.

## Numerical behavior to verify during implementation

OpenTK's scalar and vector interpolation calculate `a + t * (b - a)` without
clamping. This matches the current color formula's float operation sequence,
including its truncating final conversion. Silk's vector interpolation instead
uses `a * (1 - t) + b * t`. See the pinned interpolation sources above.

Halfway interpolation can round differently from the old `(a + b) / 2` formula;
the selected midpoint extension avoids that change. Use existing PDF extraction
tests to check the migration, especially letters near cell boundaries.
Keep zero-length direction handling explicit.
OpenTK normalization multiplies by a reciprocal; dividing the vector by its
already-computed length preserves the current division sequence more directly.

One unrelated defect is visible in the pinned OpenTK source: `Vector2d.SizeInBytes`
uses `Unsafe.SizeOf<Vector2>()`. The proposed arithmetic APIs do not use that field;
avoid relying on it for memory layout. This does not invalidate the checked vector
operations, but it is a reason to verify the particular APIs rather than assume
every package member is correct.

The research itself installed no packages and performed no runtime comparison.
Adoption validation passes all 35 existing PDF tests and the Release CLI test-project
build with zero errors. The changed call sites and new extensions introduce no
analyzer findings; existing unrelated warnings remain visible.

[open-package]: https://www.nuget.org/packages/OpenTK.Mathematics/4.9.4
[open-project]:
  https://github.com/opentk/opentk/blob/4.9.4/src/OpenTK.Mathematics/OpenTK.Mathematics.csproj
[open-vector]:
  https://github.com/opentk/opentk/blob/4.9.4/src/OpenTK.Mathematics/Vector/Vector2d.cs
[open-math]: https://github.com/opentk/opentk/blob/4.9.4/src/OpenTK.Mathematics/MathHelper.cs
[silk-package]: https://www.nuget.org/packages/Silk.NET.Maths/2.23.0
[silk-vector]:
  https://github.com/dotnet/Silk.NET/blob/v2.23.0/src/Maths/Silk.NET.Maths/Vector2D.cs
[silk-ops]:
  https://github.com/dotnet/Silk.NET/blob/v2.23.0/src/Maths/Silk.NET.Maths/Vector2D.Ops.cs
[math-package]: https://www.nuget.org/packages/MathNet.Spatial/0.6.0
[math-point]:
  https://github.com/mathnet/mathnet-spatial/blob/v0.6.0/src/Spatial/Euclidean/Point2D.cs
[math-vector]:
  https://github.com/mathnet/mathnet-spatial/blob/v0.6.0/src/Spatial/Euclidean/Vector2D.cs
