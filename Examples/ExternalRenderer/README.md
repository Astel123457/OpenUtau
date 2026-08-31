# OpenUtau external renderer example

This is a small but complete external renderer bridge. It demonstrates phrase
layout and rendering, a custom curve, cooperative cancellation, host logging,
renderer-owned source analysis, progress and per-file errors. It produces a
quiet test tone rather than useful singing. The directory is intentionally
self-contained so it can become its own repository.

## Build

Inside the OpenUtau repository, the project references `OpenUtau.Core` directly:

```sh
dotnet build -c Release
```

In a standalone checkout, either provide a source checkout:

```sh
dotnet build -c Release -p:OpenUtauCoreProject=/path/to/OpenUtau/OpenUtau.Core/OpenUtau.Core.csproj
```

or compile against `OpenUtau.Core.dll` from the OpenUtau release you target:

```sh
dotnet build -c Release -p:OpenUtauCorePath=/path/to/OpenUtau.Core.dll
```

Copy this file into OpenUtau's `Resamplers` directory:

- `bin/Release/net10.0/Example.ExternalRenderer.dll`

Do not copy `OpenUtau.Core.dll`; the host supplies that shared assembly. Restart
OpenUtau, choose a classic singer, and select **Example External Renderer** from
the track's renderer menu.

The `[ExternalRenderer]` attribute declares the renderer identity, so no YAML
sidecar is needed. Attribute-only discovery instantiates the entrypoint once to
read `ApiVersion` and `Metadata`; keep its constructor and metadata getter fast,
deterministic, and free of engine initialization. Use a YAML manifest when the
host must discover all metadata without executing plugin code.

## Compatibility and lifecycle

- API version 1 is a preview contract. A host only loads a plugin when its
  `ApiVersion` exactly matches the host's supported version.
- Build against the oldest OpenUtau release you intend to support and test the
  resulting DLL against every supported release. Do not distribute
  `OpenUtau.Core.dll`; the host supplies its shared copy.
- OpenUtau creates fresh entrypoint, analysis-provider and renderer instances.
  Any of them may implement `IDisposable`; dispose native handles and unmanaged
  state there. Do not retain host objects in static fields.
- `Render` may run away from the UI thread. Observe the supplied cancellation
  token and honor declared `parallelism`; do not access Avalonia UI objects.
- Return renderer-owned failures as analysis results when one source fails.
  Throw cancellation and fatal initialization errors. OpenUtau records discovery
  failures in its log and continues loading other renderers.
- `RendererAnalysisService` owns analysis paths and basic freshness checks.
  `RendererCacheService` is for final phrase outputs, not intermediate wavtool
  files. A renderer should keep temporary phrase state in memory.

Metadata declared in C# is authoritative for an attribute-only plugin. For a
YAML-discovered plugin, YAML metadata is authoritative and the runtime renderer
must agree with the declared capabilities and expression definitions.

## Packaging native engines

A pure C# renderer only needs its bridge DLL and private managed dependencies.
For a native engine, ship or extract the correct `.dll`, `.so`, or `.dylib` for
each supported runtime, resolve it relative to `context.PluginDirectory`, and
release its handle from `Dispose`. Keep architecture-specific files in distinct
subdirectories to avoid filename collisions.

The example analysis format is optional, so it appears in the singer dialog's
analysis-generation menu but is not required before rendering. Set `required`
only when rendering cannot proceed without a valid analysis file.
