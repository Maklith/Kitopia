using System;
using OpenCvSharp;

namespace Core.Utils;

public static class ImageStitcher
{
   
    public static Mat StitchImages(Mat previous, Mat current)
    {
        if (previous.Empty() || current.Empty() || previous.Cols != current.Cols)
            return null;

        int topOffset = (int)(current.Rows * 0.6);
        if (topOffset < 50) topOffset = 50; // Minimum offset
        if (topOffset >= current.Rows / 2) topOffset = 0; // Fallback if image small

        int templateHeight = 100;
        if (topOffset + templateHeight > current.Rows)
            templateHeight = current.Rows - topOffset;

        if (templateHeight < 10) return null; 

        using var template = current[topOffset, topOffset + templateHeight, 0, current.Cols];
        
        int searchStartY = previous.Rows - current.Rows;
        if (searchStartY < 0) searchStartY = 0;

        // Try to match with a bottom offset (to skip fixed bottom bars) first, then try without offset
        int[] bottomOffsets = [0, (int)System.Math.Min(150, current.Height*0.1)];

        foreach (var bottomOffset in bottomOffsets)
        {
            int searchEndY = previous.Rows - bottomOffset;

            if (searchEndY <= searchStartY + templateHeight) 
                continue;

            using var searchRegion = previous[searchStartY, searchEndY, 0, previous.Cols];

            using var result = new Mat();
            Cv2.MatchTemplate(searchRegion, template, result, TemplateMatchModes.CCoeffNormed);

            double minVal, maxVal;
            Point minLoc, maxLoc;
            Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);
            if (maxVal < 0.6)
                continue;
        
            int matchYInPrevious = searchStartY + maxLoc.Y; 
            int currentTopMatchInPrevious = matchYInPrevious - topOffset;
       
            int overlapHeight = previous.Rows - currentTopMatchInPrevious;
            
            if (overlapHeight <= 0 || overlapHeight > current.Rows) 
                continue;
        
            int newHeight = previous.Rows + (current.Rows - overlapHeight);

            if (newHeight <= previous.Rows)
            {
               
                if (maxVal > 0.9) return null;
                continue;
            }

            var stitched = new Mat(newHeight, previous.Cols, previous.Type());

            previous.CopyTo(stitched[0, previous.Rows, 0, previous.Cols]);
        
            current.CopyTo(stitched[currentTopMatchInPrevious, newHeight, 0, current.Cols]);

            return stitched;
        }

        return null;
    }
}