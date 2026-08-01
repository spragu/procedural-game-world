using System.Runtime.CompilerServices;

namespace ProceduralWorld.Core.Noise;

/// <summary>
/// Deterministic 2D simplex-style gradient noise with a seeded permutation table.
/// Produces values in roughly [-1, 1].
/// </summary>
public sealed class GradientNoise
{
    private const float F2 = 0.366025403f; // 0.5 * (sqrt(3) - 1)
    private const float G2 = 0.211324865f; // (3 - sqrt(3)) / 6

    private static readonly float[,] Grad2 =
    {
        { 1, 1 }, { -1, 1 }, { 1, -1 }, { -1, -1 },
        { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 },
    };

    private readonly byte[] _perm = new byte[512];

    public GradientNoise(int seed)
    {
        var p = new byte[256];
        for (int i = 0; i < 256; i++) p[i] = (byte)i;

        // Fisher-Yates with a deterministic PRNG so a seed always yields the same field.
        var rng = new SplitMix64((ulong)seed * 0x9E3779B97F4A7C15UL + 0xDEADBEEFUL);
        for (int i = 255; i > 0; i--)
        {
            int j = (int)(rng.NextUInt() % (uint)(i + 1));
            (p[i], p[j]) = (p[j], p[i]);
        }

        for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
    }

    public float Sample(float x, float y)
    {
        float s = (x + y) * F2;
        int i = FastFloor(x + s);
        int j = FastFloor(y + s);

        float t = (i + j) * G2;
        float x0 = x - (i - t);
        float y0 = y - (j - t);

        int i1, j1;
        if (x0 > y0) { i1 = 1; j1 = 0; } else { i1 = 0; j1 = 1; }

        float x1 = x0 - i1 + G2;
        float y1 = y0 - j1 + G2;
        float x2 = x0 - 1.0f + 2.0f * G2;
        float y2 = y0 - 1.0f + 2.0f * G2;

        int ii = i & 255;
        int jj = j & 255;

        float n = 0f;
        n += Corner(x0, y0, _perm[ii + _perm[jj]] & 7);
        n += Corner(x1, y1, _perm[ii + i1 + _perm[jj + j1]] & 7);
        n += Corner(x2, y2, _perm[ii + 1 + _perm[jj + 1]] & 7);

        return 70.0f * n;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Corner(float x, float y, int gi)
    {
        float t = 0.5f - x * x - y * y;
        if (t < 0f) return 0f;
        t *= t;
        return t * t * (Grad2[gi, 0] * x + Grad2[gi, 1] * y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FastFloor(float v) => v >= 0 ? (int)v : (int)v - 1;
}

/// <summary>Small, fast, deterministic PRNG used for reproducible worlds.</summary>
public struct SplitMix64(ulong state)
{
    private ulong _state = state;

    public ulong NextULong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public uint NextUInt() => (uint)(NextULong() >> 32);

    /// <summary>Uniform float in [0, 1).</summary>
    public float NextFloat() => (NextULong() >> 40) * (1.0f / 16777216.0f);

    public float Range(float min, float max) => min + NextFloat() * (max - min);
}
