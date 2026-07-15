namespace Kitopia.Desktop.Features.Utils.ImageTools;

public class GaussianBlur1
{
    public static byte[] GaussianBlur(byte[] data, int width, int height, int radial)
    {
        var i1 = width * height;
        var _alpha = new int[i1];
        var _red = new int[i1];
        var _green = new int[i1];
        var _blue = new int[i1];
        for (var j = 0; j < i1; j++)
        {
            _alpha[j] = data[j * 4 + 3];
            _red[j] = data[j * 4 + 2];
            _green[j] = data[j * 4 + 1];
            _blue[j] = data[j * 4];
        }

        var newAlpha = new int[i1];
        var newRed = new int[i1];
        var newGreen = new int[i1];
        var newBlue = new int[i1];
        var dest = new byte[i1 * 4];

        Parallel.Invoke(
            () => gaussBlur_4(_alpha, newAlpha, radial, width, height),
            () => gaussBlur_4(_red, newRed, radial, width, height),
            () => gaussBlur_4(_green, newGreen, radial, width, height),
            () => gaussBlur_4(_blue, newBlue, radial, width, height));

        for (var i = 0; i < i1; i++)
        {
            if (newAlpha[i] > 255) newAlpha[i] = 255;
            if (newRed[i] > 255) newRed[i] = 255;
            if (newGreen[i] > 255) newGreen[i] = 255;
            if (newBlue[i] > 255) newBlue[i] = 255;

            if (newAlpha[i] < 0) newAlpha[i] = 0;
            if (newRed[i] < 0) newRed[i] = 0;
            if (newGreen[i] < 0) newGreen[i] = 0;
            if (newBlue[i] < 0) newBlue[i] = 0;
            dest[i * 4 + 3] = (byte)newAlpha[i];
            dest[i * 4 + 2] = (byte)newRed[i];
            dest[i * 4 + 1] = (byte)newGreen[i];
            dest[i * 4] = (byte)newBlue[i];
        }

        newAlpha = null;
        newRed = null;
        newGreen = null;
        newBlue = null;
        return dest;
    }


    private static void gaussBlur_4(int[] source, int[] dest, int r, int _width, int _height)
    {
        var bxs = boxesForGauss(r, 3);
        boxBlur_4(source, dest, _width, _height, (bxs[0] - 1) / 2);
        boxBlur_4(dest, source, _width, _height, (bxs[1] - 1) / 2);
        boxBlur_4(source, dest, _width, _height, (bxs[2] - 1) / 2);
    }

    private static int[] boxesForGauss(int sigma, int n)
    {
        var wIdeal = System.Math.Sqrt(12 * sigma * sigma / n + 1);
        var wl = (int)System.Math.Floor(wIdeal);
        if (wl % 2 == 0) wl--;
        var wu = wl + 2;

        var mIdeal = (double)(12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4 * wl - 4);
        var m = System.Math.Round(mIdeal);

        var sizes = new List<int>();
        for (var i = 0; i < n; i++) sizes.Add(i < m ? wl : wu);
        return sizes.ToArray();
    }

    private static void boxBlur_4(int[] source, int[] dest, int w, int h, int r)
    {
        for (var i = 0; i < source.Length; i++) dest[i] = source[i];
        boxBlurH_4(dest, source, w, h, r);
        boxBlurT_4(source, dest, w, h, r);
    }

    private static void boxBlurH_4(int[] source, int[] dest, int w, int h, int r)
    {
        var iar = (double)1 / (r + r + 1);
        Parallel.For(0, h, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var ti = i * w;
            var li = ti;
            var ri = ti + r;
            var fv = source[ti];
            var lv = source[ti + w - 1];
            var val = (r + 1) * fv;
            for (var j = 0; j < r; j++) val += source[ti + j];
            for (var j = 0; j <= r; j++)
            {
                val += source[ri++] - fv;
                dest[ti++] = (int)System.Math.Round(val * iar);
            }

            for (var j = r + 1; j < w - r; j++)
            {
                val += source[ri++] - dest[li++];
                dest[ti++] = (int)System.Math.Round(val * iar);
            }

            for (var j = w - r; j < w; j++)
            {
                val += lv - source[li++];
                dest[ti++] = (int)System.Math.Round(val * iar);
            }
        });
    }

    private static void boxBlurT_4(int[] source, int[] dest, int w, int h, int r)
    {
        var iar = (double)1 / (r + r + 1);
        Parallel.For(0, w, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var ti = i;
            var li = ti;
            var ri = ti + r * w;
            var fv = source[ti];
            var lv = source[ti + w * (h - 1)];
            var val = (r + 1) * fv;
            for (var j = 0; j < r; j++) val += source[ti + j * w];
            for (var j = 0; j <= r; j++)
            {
                val += source[ri] - fv;
                dest[ti] = (int)System.Math.Round(val * iar);
                ri += w;
                ti += w;
            }

            for (var j = r + 1; j < h - r; j++)
            {
                val += source[ri] - source[li];
                dest[ti] = (int)System.Math.Round(val * iar);
                li += w;
                ri += w;
                ti += w;
            }

            for (var j = h - r; j < h; j++)
            {
                val += lv - source[li];
                dest[ti] = (int)System.Math.Round(val * iar);
                li += w;
                ti += w;
            }
        });
    }
}