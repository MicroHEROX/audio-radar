using System.Numerics;

namespace AudioRadar.Dsp;

internal static class Fft
{
    /// <summary>
    /// In-place radix-2 Cooley-Tukey FFT. Buffer length must be a power of two.
    /// </summary>
    public static void Transform(Complex[] buffer)
    {
        var n = buffer.Length;
        if (n <= 1)
        {
            return;
        }

        if ((n & (n - 1)) != 0)
        {
            throw new ArgumentException("FFT length must be a power of two.", nameof(buffer));
        }

        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }

            j ^= bit;
            if (i < j)
            {
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
            }
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2.0 * Math.PI / length;
            var wLength = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var i = 0; i < n; i += length)
            {
                var w = Complex.One;
                var half = length >> 1;
                for (var j = 0; j < half; j++)
                {
                    var even = buffer[i + j];
                    var odd = buffer[i + j + half] * w;
                    buffer[i + j] = even + odd;
                    buffer[i + j + half] = even - odd;
                    w *= wLength;
                }
            }
        }
    }
}
