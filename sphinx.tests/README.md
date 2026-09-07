# Sphinx managed checks

Run `dotnet run --project sphinx.tests/sphinx.tests.csproj` from the repository root.
No KSA/native runtime is loaded. Tests compile the production PlacementMath source and verify
200 deterministic XYZ scale/rotation cases: grounded lower bounds, horizontal centering, local
translation, retained axis lengths and noncumulative repeatability. They also cover identity and
invalid, nonfinite, inverted or overflowing inputs. Full solution builds check native typed APIs.

The production `TextureMapping` and vertex layout are also linked. Checks cover scale-before-offset,
negative/out-of-range repeating UVs, matching backfaces, 200 successive edit/reset operations,
unchanged positions/normals and original source data, invalid/overflow rejection and the native
32-byte vertex stride / UV byte offset. No GPU is needed for these regression checks.

Production `CollisionGeometry` checks detect closed boxes, retain open/concave geometry, handle
reversed duplicate faces, reject overlapping box-face triangles and nonfinite vertices, skip
zero-area mesh triangles and enforce the triangle limit. Another 200 deterministic cases verify
that both triangle-mesh vertices and fitted-box poses match rendered XYZ scale/rotation/offset
transforms without mutating the source geometry.

These checks do not exercise live terrain queries, matrix upload, shaders, Vulkan synchronization
UI picking, actual Bepu contact filtering, bubble pooling or solver synchronization. See [native acceptance](../scope/statics.md#validation).
