namespace CardValueOverlay.Modeling.Simulation;

/// <summary>
/// Small, fast, deterministic pseudo-random generator used inside the deck simulator.
/// Replaces <see cref="System.Random"/> on the hot search path: the simulator reseeds a
/// generator per search branch for reproducibility, and <see cref="System.Random"/>'s
/// ~232-byte state array makes that allocation-heavy. This class holds a single 64-bit
/// state word (SplitMix64), so construction and generation are cheap.
///
/// Determinism is preserved (same seed always yields the same stream), but the produced
/// sequence differs from <see cref="System.Random"/>; simulation samples therefore differ
/// from the legacy generator. Aggregate expected values converge with run count.
/// </summary>
internal sealed class FastRandom
{
    private FastRandomState _state;

    public FastRandom(int seed)
    {
        _state = new FastRandomState(seed);
    }

    public int Next()
    {
        return _state.Next();
    }

    public int Next(int maxValue)
    {
        return _state.Next(maxValue);
    }
}

internal struct FastRandomState
{
    private ulong _state;

    public FastRandomState(int seed)
    {
        // Mix the (possibly small or zero) seed into a well-distributed 64-bit state.
        _state = unchecked(((ulong)(uint)seed * 0x9E3779B97F4A7C15UL) + 0xD1B54A32D192ED03UL);
    }

    public int Next()
    {
        // Non-negative int in [0, int.MaxValue]; top 31 bits of the SplitMix64 output.
        return (int)(NextUInt64() >> 33);
    }

    public int Next(int maxValue)
    {
        if (maxValue <= 0)
        {
            return 0;
        }

        // Lemire's unbiased bounded reduction over 32 bits.
        ulong product = (ulong)(uint)(NextUInt64() >> 32) * (uint)maxValue;
        return (int)(product >> 32);
    }

    private ulong NextUInt64()
    {
        unchecked
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
