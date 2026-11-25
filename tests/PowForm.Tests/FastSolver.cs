using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace PowForm.Tests;

/// <summary>
/// What a spammer who writes a native solver does: no strings, no allocation per hash, and every
/// core. Used only in the measurements, as the honest limit of the technique.
/// </summary>
public static class FastSolver
{
    public static int? Solve(string salt, string challengeHex, int maxNumber, bool allCores)
    {
        var target = Convert.FromHexString(challengeHex);
        var prefix = Encoding.UTF8.GetBytes(salt);
        var found = -1;

        // One thread walks 0..maxNumber in order; with all cores, each takes blocks of 4096.
        const int Block = 4096;
        var blocks = (maxNumber / Block) + 1;
        var options = new ParallelOptions { MaxDegreeOfParallelism = allCores ? Environment.ProcessorCount : 1 };
        Parallel.For(0, blocks, options, (block, state) =>
        {
            Span<byte> buffer = stackalloc byte[prefix.Length + 12];
            Span<byte> hash = stackalloc byte[32];
            prefix.CopyTo(buffer);
            var from = block * Block;
            var to = Math.Min(from + Block - 1, maxNumber);
            for (var number = from; number <= to; number++)
            {
                if (Volatile.Read(ref found) >= 0)
                {
                    state.Stop();
                    return;
                }

                Utf8Formatter.TryFormat(number, buffer[prefix.Length..], out var written);
                SHA256.HashData(buffer[..(prefix.Length + written)], hash);
                if (hash.SequenceEqual(target))
                {
                    Interlocked.Exchange(ref found, number);
                    state.Stop();
                    return;
                }
            }
        });

        return found >= 0 ? found : null;
    }
}
