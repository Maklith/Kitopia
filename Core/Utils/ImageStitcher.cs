using System;
using OpenCvSharp;

namespace Core.Utils;

public static class ImageStitcher
{
   
    public static Mat StitchImages(Mat previous, Mat current)
    {
        if (previous.Empty() || current.Empty() || previous.Cols != current.Cols)
            return null;

        int topOffset = (int)(current.Rows * 0.15);
        if (topOffset < 50) topOffset = 50; // Minimum offset
        if (topOffset >= current.Rows / 2) topOffset = 0; // Fallback if image small

        int templateHeight = 100;
        if (topOffset + templateHeight > current.Rows)
            templateHeight = current.Rows - topOffset;

        if (templateHeight < 10) return null; 

        using var template = current[topOffset, topOffset + templateHeight, 0, current.Cols];
        
        int searchStartY = previous.Rows - current.Rows;
        if (searchStartY < 0) searchStartY = 0;
        
        if (searchStartY >= previous.Rows) return null;

        using var searchRegion = previous[searchStartY, previous.Rows, 0, previous.Cols];

        
        using var result = new Mat();
        Cv2.MatchTemplate(searchRegion, template, result, TemplateMatchModes.CCoeffNormed);

        double minVal, maxVal;
        Point minLoc, maxLoc;
        Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);

        if (maxVal < 0.8)
            return null;
        
        int matchYInPrevious = searchStartY + maxLoc.Y; 
        int currentTopMatchInPrevious = matchYInPrevious - topOffset;
       
        int overlapHeight = previous.Rows - currentTopMatchInPrevious;

        if (overlapHeight <= 0 || overlapHeight >= current.Rows) 
            return null;
        
        int newHeight = previous.Rows + (current.Rows - overlapHeight);
        var stitched = new Mat(newHeight, previous.Cols, previous.Type());

        previous.CopyTo(stitched[0, previous.Rows, 0, previous.Cols]);
        
        var newPart = current[overlapHeight, current.Rows, 0, current.Cols];
        newPart.CopyTo(stitched[previous.Rows, newHeight, 0, current.Cols]);

        return stitched;
    }
}