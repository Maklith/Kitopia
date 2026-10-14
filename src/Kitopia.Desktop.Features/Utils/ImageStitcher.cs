using OpenCvSharp;

namespace Kitopia.Desktop.Features.Utils;

public static class ImageStitcher
{
    private const double MatchThreshold = 0.88;
    private const int MinimumNewContentHeight = 4;
    private const int MinimumImageHeight = 64;
    private const int MinimumTemplateHeight = 32;

    /// <summary>
    /// Appends the non-overlapping portion of <paramref name="current"/> to <paramref name="previous"/>.
    /// </summary>
    /// <remarks>
    /// The search is deliberately restricted to the last viewport in <paramref name="previous"/>. Searching the
    /// complete accumulator can match an older, repeated section of a document after several scroll operations.
    /// </remarks>
    public static Mat? StitchImages(Mat previous, Mat current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (previous.Empty() || current.Empty() || previous.Cols != current.Cols ||
            previous.Rows < MinimumImageHeight || current.Rows < MinimumImageHeight)
        {
            return null;
        }

        using var previousGray = ConvertToGrayscale(previous);
        using var currentGray = ConvertToGrayscale(current);

        int templateHeight = Math.Clamp(current.Rows / 10, MinimumTemplateHeight, 96);
        int templateWidth = Math.Max(1, current.Cols * 4 / 5);
        int templateX = (current.Cols - templateWidth) / 2;
        if (templateHeight >= current.Rows)
        {
            return null;
        }

        // The new viewport can only overlap the viewport that produced the final portion of the accumulator.
        int searchStartY = Math.Max(0, previous.Rows - current.Rows);
        int searchHeight = previous.Rows - searchStartY;
        using var searchRegion = new Mat(previousGray, new Rect(0, searchStartY, previous.Cols, searchHeight));

        var candidates = new List<MatchCandidate>();
        foreach (int sampleY in GetSampleRows(current.Rows, templateHeight))
        {
            using var template = new Mat(currentGray, new Rect(templateX, sampleY, templateWidth, templateHeight));
            if (GetStandardDeviation(template) < 8.0)
            {
                continue;
            }

            using var result = new Mat();
            Cv2.MatchTemplate(searchRegion, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double score, out _, out Point location);
            if (score < MatchThreshold)
            {
                continue;
            }

            int candidateOffset = searchStartY + location.Y - sampleY;
            int newContentHeight = candidateOffset + current.Rows - previous.Rows;
            if (newContentHeight < MinimumNewContentHeight || newContentHeight >= current.Rows - templateHeight)
            {
                continue;
            }

            candidates.Add(new MatchCandidate(candidateOffset, score));
        }

        if (!TryGetConsensusOffset(candidates, out int offset))
        {
            return null;
        }

        int stitchedHeight = offset + current.Rows;
        if (stitchedHeight <= previous.Rows)
        {
            return null;
        }

        var stitched = new Mat(stitchedHeight, previous.Cols, previous.Type());
        previous.CopyTo(stitched[new Rect(0, 0, previous.Cols, previous.Rows)]);
        using var newContent = new Mat(current, new Rect(0, previous.Rows - offset, current.Cols, stitchedHeight - previous.Rows));
        newContent.CopyTo(stitched[new Rect(0, previous.Rows, previous.Cols, stitchedHeight - previous.Rows)]);
        return stitched;
    }

    private static Mat ConvertToGrayscale(Mat source)
    {
        var gray = new Mat();
        switch (source.Channels())
        {
            case 1:
                source.CopyTo(gray);
                break;
            case 3:
                Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
                break;
            case 4:
                Cv2.CvtColor(source, gray, ColorConversionCodes.BGRA2GRAY);
                break;
            default:
                gray.Dispose();
                throw new ArgumentException($"Unsupported image channel count: {source.Channels()}.", nameof(source));
        }

        return gray;
    }

    private static IEnumerable<int> GetSampleRows(int imageHeight, int templateHeight)
    {
        // These lie in the leading half of the incoming viewport, which is the overlap after a wheel scroll.
        int maximumStart = imageHeight - templateHeight;
        int[] sampleRows = [
            imageHeight / 16,
            imageHeight / 6,
            imageHeight / 4,
            imageHeight / 3,
            imageHeight * 5 / 12
        ];

        return sampleRows.Select(row => Math.Clamp(row, 0, maximumStart)).Distinct();
    }

    private static double GetStandardDeviation(Mat image)
    {
        using var mean = new Mat();
        using var standardDeviation = new Mat();
        Cv2.MeanStdDev(image, mean, standardDeviation);
        return standardDeviation.Get<double>(0);
    }

    private static bool TryGetConsensusOffset(IReadOnlyList<MatchCandidate> candidates, out int offset)
    {
        const int offsetTolerance = 2;
        const int requiredVotes = 2;

        offset = 0;
        if (candidates.Count < requiredVotes)
        {
            return false;
        }

        MatchCandidate? bestCandidate = null;
        int bestVoteCount = 0;
        double bestScore = 0;

        foreach (MatchCandidate candidate in candidates)
        {
            List<MatchCandidate> cluster = candidates
                .Where(other => Math.Abs(other.Offset - candidate.Offset) <= offsetTolerance)
                .ToList();
            int voteCount = cluster.Count;
            double score = cluster.Sum(match => match.Score);
            if (voteCount > bestVoteCount || voteCount == bestVoteCount && score > bestScore)
            {
                bestCandidate = candidate;
                bestVoteCount = voteCount;
                bestScore = score;
            }
        }

        if (bestCandidate is null || bestVoteCount < requiredVotes)
        {
            return false;
        }

        offset = bestCandidate.Value.Offset;
        return true;
    }

    private readonly record struct MatchCandidate(int Offset, double Score);
}
