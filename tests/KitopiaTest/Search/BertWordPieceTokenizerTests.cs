using Kitopia.Desktop.Features.Search.Semantic;

namespace KitopiaTest.Search;

[TestClass]
public sealed class BertWordPieceTokenizerTests
{
    [TestMethod]
    [Timeout(2000)]
    public void LoadVocabulary_UnicodeWhitespaceTokens_UnknownWordTerminates()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, ["[PAD]", "[CLS]", "[SEP]", "[UNK]", "\u2028", "##\u2028", "known"]);
            var tokenizer = BertWordPieceTokenizer.LoadVocabulary(path);

            CollectionAssert.AreEqual(new long[] { 1, 3, 2 }, tokenizer.Encode("unknown", 52));
            CollectionAssert.AreEqual(new long[] { 1, 6, 2 }, tokenizer.Encode("known", 52));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
