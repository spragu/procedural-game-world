namespace ProceduralWorld.Core.Noise;

/// <summary>
/// Fractal Brownian motion layered on top of <see cref="GradientNoise"/>.
/// Supports standard and ridged fractals plus domain warping, which is what makes
/// coastlines and biome borders look organic instead of blobby.
/// </summary>
public sealed class FractalNoise
{
    private readonly GradientNoise[] _octaves;
    private readonly float[] _amplitudes;
    private readonly float _normalization;

    public float Frequency { get; }
    public float Lacunarity { get; }
    public float Gain { get; }

    public FractalNoise(int seed, int octaves = 5, float frequency = 1f, float lacunarity = 2.02f, float gain = 0.5f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(octaves, 1);

        Frequency = frequency;
        Lacunarity = lacunarity;
        Gain = gain;

        _octaves = new GradientNoise[octaves];
        _amplitudes = new float[octaves];

        float amp = 1f;
        float total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            // Offset each octave's seed so layers are uncorrelated.
            _octaves[i] = new GradientNoise(seed + i * 7919);
            _amplitudes[i] = amp;
            total += amp;
            amp *= gain;
        }

        _normalization = total > 0f ? 1f / total : 1f;
    }

    /// <summary>Classic fBm in [-1, 1].</summary>
    public float Sample(float x, float y)
    {
        float freq = Frequency;
        float sum = 0f;

        for (int i = 0; i < _octaves.Length; i++)
        {
            sum += _octaves[i].Sample(x * freq, y * freq) * _amplitudes[i];
            freq *= Lacunarity;
        }

        return sum * _normalization;
    }

    /// <summary>fBm remapped to [0, 1].</summary>
    public float Sample01(float x, float y) => Sample(x, y) * 0.5f + 0.5f;

    /// <summary>
    /// Ridged multifractal in [0, 1]. Great for mountain spines and cliff lines.
    /// </summary>
    public float Ridged(float x, float y)
    {
        float freq = Frequency;
        float sum = 0f;
        float weight = 1f;

        for (int i = 0; i < _octaves.Length; i++)
        {
            float n = 1f - MathF.Abs(_octaves[i].Sample(x * freq, y * freq));
            n *= n;
            n *= weight;
            weight = Math.Clamp(n * 2f, 0f, 1f);

            sum += n * _amplitudes[i];
            freq *= Lacunarity;
        }

        return Math.Clamp(sum * _normalization, 0f, 1f);
    }

    /// <summary>
    /// Billowy fBm in [0, 1] - rounded, cloud-like. Used for moisture fields.
    /// </summary>
    public float Billow(float x, float y)
    {
        float freq = Frequency;
        float sum = 0f;

        for (int i = 0; i < _octaves.Length; i++)
        {
            sum += (MathF.Abs(_octaves[i].Sample(x * freq, y * freq)) * 2f - 1f) * _amplitudes[i];
            freq *= Lacunarity;
        }

        return Math.Clamp(sum * _normalization * 0.5f + 0.5f, 0f, 1f);
    }

    /// <summary>
    /// Warps the sample position by another noise field before sampling. This is the
    /// single biggest visual win for natural-looking coastlines and biome edges.
    /// </summary>
    public float Warped(float x, float y, FractalNoise warpX, FractalNoise warpY, float strength)
    {
        float wx = x + warpX.Sample(x, y) * strength;
        float wy = y + warpY.Sample(x, y) * strength;
        return Sample(wx, wy);
    }
}
