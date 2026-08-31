using System.Text;
using OpenUtau.Core;
using OpenUtau.Classic;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Examples.ExternalRenderer;

[ExternalRenderer("org.openutau.example.external-renderer", "Example External Renderer")]
public sealed class ExampleRendererPlugin : IOpenUtauRendererPlugin {
    public int ApiVersion => ExternalRendererRegistry.ApiVersion;

    public RendererPluginMetadata Metadata => new() {
        Capabilities = new RendererCapabilitiesManifest {
            cancellation = true,
            parallelism = 1,
        },
        Expressions = new Dictionary<string, UExpressionDescriptor> {
            ["tone"] = new("Tone", "tone", -100, 100, 0) {
                type = UExpressionType.Curve,
            },
        },
        AnalysisFormats = new Dictionary<string, AnalysisFormatManifest> {
            ["example"] = new() {
                name = "Example source analysis",
                path = "{wav_dir}/{wav_stem}.example-analysis",
                canGenerate = true,
            },
        },
    };

    public IRenderer CreateRenderer(RendererPluginContext context) =>
        new ExampleRenderer(context.RendererName, context.Logger);

    public IRendererAnalysisProvider CreateAnalysisProvider(RendererPluginContext context) =>
        new ExampleAnalysisProvider(context.Analysis);
}

/// <summary>
/// A deliberately simple renderer which emits a quiet 220 Hz test tone for
/// every phrase. Replace this class with an engine-specific implementation.
/// </summary>
public sealed class ExampleRenderer : IRenderer {
    readonly string name;
    readonly Serilog.ILogger logger;

    public ExampleRenderer(string name, Serilog.ILogger logger) {
        this.name = name;
        this.logger = logger;
    }

    public USingerType SingerType => USingerType.Classic;
    public bool SupportsRenderPitch => false;

    public bool SupportsExpression(UExpressionDescriptor descriptor) =>
        string.Equals(descriptor.abbr, "tone", StringComparison.OrdinalIgnoreCase);

    public RenderResult Layout(RenderPhrase phrase) => new() {
        leadingMs = phrase.leadingMs,
        positionMs = phrase.positionMs,
        estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
    };

    public Task<RenderResult> Render(
        RenderPhrase phrase,
        Progress progress,
        int trackNo,
        CancellationTokenSource cancellation,
        bool isPreRender = false) {
        cancellation.Token.ThrowIfCancellationRequested();
        var result = Layout(phrase);
        var sampleCount = Math.Max(0, (int)(result.estimatedLengthMs / 1000 * 44100));
        result.samples = new float[sampleCount];
        var toneCurve = phrase.curves.FirstOrDefault(curve =>
            string.Equals(curve.Item1, "tone", StringComparison.OrdinalIgnoreCase))?.Item2;
        for (var i = 0; i < sampleCount; i++) {
            if ((i & 4095) == 0) cancellation.Token.ThrowIfCancellationRequested();
            var curveIndex = toneCurve == null || toneCurve.Length == 0
                ? 0
                : Math.Min(toneCurve.Length - 1, (int)((long)i * toneCurve.Length / sampleCount));
            var curveValue = toneCurve == null || toneCurve.Length == 0 ? 0 : toneCurve[curveIndex];
            var frequency = 220 * MathF.Pow(2, curveValue / 1200);
            result.samples[i] = 0.1f * MathF.Sin(2 * MathF.PI * frequency * i / 44100);
        }
        logger.Debug("Rendered example phrase {PhraseHash:x16} for track {TrackNo}", phrase.hash, trackNo);
        return Task.FromResult(result);
    }

    public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => null!;

    public UExpressionDescriptor[] GetSuggestedExpressions(
        USinger singer,
        URenderSettings renderSettings) => Array.Empty<UExpressionDescriptor>();

    public override string ToString() => name;
}

/// <summary>
/// Demonstrates renderer-owned source analysis. Real engines would write their
/// spectral model or frequency map instead of this small descriptive sidecar.
/// </summary>
public sealed class ExampleAnalysisProvider : IRendererAnalysisProvider {
    readonly RendererAnalysisService analysis;

    public ExampleAnalysisProvider(RendererAnalysisService analysis) {
        this.analysis = analysis;
    }

    public Task<IReadOnlyList<RendererAnalysisResult>> GenerateAsync(
            IReadOnlyList<RendererAnalysisRequest> requests,
            IProgress<int> progress,
            CancellationToken cancellation) {
        var results = new List<RendererAnalysisResult>(requests.Count);
        for (var i = 0; i < requests.Count; i++) {
            var request = requests[i];
            cancellation.ThrowIfCancellationRequested();
            try {
                File.WriteAllText(request.OutputFile,
                    $"source={Path.GetFileName(request.SourceFile)}\n", Encoding.UTF8);
                results.Add(new(request, RendererAnalysisOutcome.Generated));
            } catch (Exception error) when (error is not OperationCanceledException) {
                results.Add(new(request, RendererAnalysisOutcome.Failed, error.Message));
            }
            progress.Report(i + 1);
        }
        return Task.FromResult<IReadOnlyList<RendererAnalysisResult>>(results);
    }

    public ValueTask<RendererAnalysisState> ValidateAsync(
            RendererAnalysisRequest request, CancellationToken cancellation) {
        cancellation.ThrowIfCancellationRequested();
        return ValueTask.FromResult(analysis.GetBasicState(request.Format, request.SourceFile));
    }
}
