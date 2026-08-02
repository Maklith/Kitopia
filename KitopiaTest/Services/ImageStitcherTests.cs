using Kitopia.Desktop.Features.Utils;
using OpenCvSharp;

namespace KitopiaTest.Services;

[TestClass]
public class ImageStitcherTests
{
    [TestMethod]
    public void StitchImages_OverlappingBgraFrames_AppendsOnlyNewRows()
    {
        const int width = 320;
        const int viewportHeight = 360;
        const int scrollDistance = 140;

        using var document = new Mat(viewportHeight + scrollDistance * 2, width, MatType.CV_8UC4);
        Cv2.Randu(document, Scalar.All(0), Scalar.All(255));
        using var firstFrame = new Mat(document, new Rect(0, 0, width, viewportHeight)).Clone();
        using var secondFrame = new Mat(document, new Rect(0, scrollDistance, width, viewportHeight)).Clone();
        using var firstStitch = ImageStitcher.StitchImages(firstFrame, secondFrame);

        Assert.IsNotNull(firstStitch);
        Assert.AreEqual(viewportHeight + scrollDistance, firstStitch.Rows);
        AssertFramesEqual(document, firstStitch);

        using var thirdFrame = new Mat(document, new Rect(0, scrollDistance * 2, width, viewportHeight)).Clone();
        using var secondStitch = ImageStitcher.StitchImages(firstStitch, thirdFrame);

        Assert.IsNotNull(secondStitch);
        Assert.AreEqual(document.Rows, secondStitch.Rows);
        AssertFramesEqual(document, secondStitch);
    }

    [TestMethod]
    public void StitchImages_UnchangedFrame_ReturnsNull()
    {
        using var frame = new Mat(360, 320, MatType.CV_8UC4);
        Cv2.Randu(frame, Scalar.All(0), Scalar.All(255));
        using var identicalFrame = frame.Clone();

        using var result = ImageStitcher.StitchImages(frame, identicalFrame);

        Assert.IsNull(result);
    }

    private static void AssertFramesEqual(Mat expected, Mat actual)
    {
        using var expectedRegion = new Mat(expected, new Rect(0, 0, actual.Cols, actual.Rows));
        Assert.AreEqual(0, Cv2.Norm(expectedRegion, actual, NormTypes.L1));
    }
}
