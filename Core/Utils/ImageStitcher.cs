using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace Core.Utils;

public static class ImageStitcher
{
    /// <summary>
    /// Stitches two images vertically using a robust multi-template matching strategy.
    /// </summary>
    /// <param name="previous">The top image.</param>
    /// <param name="current">The bottom image to be appended.</param>
    /// <returns>A new Mat containing the stitched result, or null if stitching failed.</returns>
    public static Mat? StitchImages(Mat previous, Mat current)
    {
        // 1. Basic Validation
        if (previous.Empty() || current.Empty() || previous.Cols != current.Cols)
            return null;

        // If images are too small, fallback or fail
        if (previous.Rows < 50 || current.Rows < 50)
            return null;

        // 2. Define Search Parameters
        // Search in bottom 70% of previous (increased from 50% for faster scrolling)
        int searchHeight = (int)(previous.Rows * 0.7); 
        int searchStartY = previous.Rows - searchHeight;
        
        int templateH = 40; // Thin horizontal strips
        int templateW = (int)(current.Cols * 0.7); // 70% width, centered to avoid scrollbars/edges
        int templateX = (current.Cols - templateW) / 2;

        if (searchHeight < templateH * 2) return null;

        // Candidates for offsets. Key = offset (dy), Value = Sum of Confidence Scores
        // We use Score instead of just count to break ties better
        Dictionary<int, double> offsetScores = new Dictionary<int, double>();
        Dictionary<int, int> offsetCounts = new Dictionary<int, int>();
        
        // Test 5 vertical positions (approx 5%, 15%, 25%, 35%, 45%)
        // We stay in the top half of 'current' because that's where the overlap usually starts.
        HashSet<int> sampleRowsSet = new HashSet<int>();
        for (double p = 0.05; p <= 0.45; p += 0.1)
        {
            sampleRowsSet.Add((int)(current.Rows * p));
        }
        
        List<int> validSamples = sampleRowsSet
            .Where(y => y >= 0 && y + templateH < current.Rows)
            .ToList();
        
        if (validSamples.Count == 0) return null;

        Rect searchRect = new Rect(0, searchStartY, previous.Cols, searchHeight);
        using Mat searchRegion = new Mat(previous, searchRect);

        int validTemplatesProcessed = 0;

        foreach (int y in validSamples)
        {
            Rect templateRect = new Rect(templateX, y, templateW, templateH);
            using Mat template = new Mat(current, templateRect);
            
            // Validate template info (std dev check to avoid flat areas)
            using Mat mean = new Mat();
            using Mat stdDev = new Mat();
            Cv2.MeanStdDev(template, mean, stdDev);
            // Increased threshold slightly to avoid weak noise
            if (stdDev.Get<double>(0) < 10.0) 
                continue; 

            validTemplatesProcessed++;

            using Mat result = new Mat();
            Cv2.MatchTemplate(searchRegion, template, result, TemplateMatchModes.CCoeffNormed);

            double minVal, maxVal;
            Point minLoc, maxLoc;
            Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);

            // Threshold: 0.75 for voting
            if (maxVal > 0.75) 
            {
                // Global Y in previous = searchStartY + maxLoc.Y
                // Offset = MatchYGlobal - TemplateYInCurrent
                int matchYGlobal = searchStartY + maxLoc.Y;
                int offset = matchYGlobal - y;
                
                // Cluster logic (+/- 2 pixels)
                bool foundCluster = false;
                List<int> keys = offsetScores.Keys.ToList();
                foreach (int k in keys)
                {
                    if (System.Math.Abs(k - offset) <= 2)
                    {
                        offsetScores[k] += maxVal;
                        offsetCounts[k]++;
                        foundCluster = true;
                        break;
                    }
                }
                
                if (!foundCluster)
                {
                    offsetScores[offset] = maxVal;
                    offsetCounts[offset] = 1;
                }
            }
        }

        // 3. Analyze Votes
        int bestOffset = -1;
        double maxScore = 0;
        int maxCount = 0;

        foreach (var kvp in offsetScores)
        {
            if (kvp.Value > maxScore)
            {
                maxScore = kvp.Value;
                maxCount = offsetCounts[kvp.Key];
                bestOffset = kvp.Key;
            }
        }

        // 4. Decision Logic
        if (bestOffset == -1) return null; // No matches

        // Consensus Rules:
        // 1. If we have > 2 valid templates, we ideally want at least 2 votes.
        // 2. If we only have 1 vote (either because only 1 template was valid, or others failed),
        //    we need strict confidence (> 0.85) to accept it.
        
        bool accepted = false;
        
        if (validTemplatesProcessed <= 1)
        {
            // Only 1 template was even tried/valid. Trust if score (which is just maxVal) is high.
            if (maxScore > 0.85) accepted = true;
        }
        else
        {
            // We tried multiple templates.
            if (maxCount >= 2)
            {
                // Consensus found!
                accepted = true;
            }
            else
            {
                // No consensus (e.g. 5 templates, all gave different offsets or failed).
                // Check if the single winner is VERY strong (e.g. > 0.9)
                if (maxScore > 0.9) accepted = true;
            }
        }
        
        if (!accepted) return null;

        // 5. Construct Result
        int finalHeight = bestOffset + current.Rows;
        
        // Strict jitter check: if new content < 1px, ignore.
        // Note: bestOffset = topOfCurrentInPrev.
        // If bestOffset + current.Rows <= previous.Rows, it means current ends BEFORE previous ends.
        if (finalHeight <= previous.Rows) 
            return null;

        Mat stitched = new Mat(finalHeight, previous.Cols, previous.Type());

        // Draw Previous
        Rect prevRect = new Rect(0, 0, previous.Cols, previous.Rows);
        previous.CopyTo(stitched[prevRect]);

        // Draw Current (Overwriting the overlap)
        int destY = bestOffset;
        int srcY = 0;
        
        if (destY < 0)
        {
            srcY = -destY;
            destY = 0;
        }

        int copyHeight = current.Rows - srcY;
        if (copyHeight > 0)
        {
            Rect src = new Rect(0, srcY, current.Cols, copyHeight);
            Rect dst = new Rect(0, destY, current.Cols, copyHeight);
            
            // Safety clips
            if (dst.Bottom > stitched.Rows) dst.Height = stitched.Rows - dst.Y;
            if (src.Bottom > current.Rows) src.Height = current.Rows - src.Y;
            
            if (dst.Height > 0 && src.Height > 0)
            {
                using Mat part = new Mat(current, src);
                part.CopyTo(stitched[dst]);
            }
        }
        
        return stitched;
    }
}
