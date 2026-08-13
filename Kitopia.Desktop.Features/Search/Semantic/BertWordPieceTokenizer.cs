using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kitopia.Desktop.Features.Search.Semantic;

/// <summary>
/// Minimal BERT tokenizer for the WordPiece tokenizer.json bundled with bge-small-zh-v1.5.
/// </summary>
internal sealed class BertWordPieceTokenizer
{
    private const int MaximumWordLength = 100;

    private readonly Dictionary<string, int> _vocabulary;
    private readonly int _classificationTokenId;
    private readonly int _separatorTokenId;
    private readonly int _unknownTokenId;
    private readonly int _paddingTokenId;
    private readonly WordPieceTrieNode _initialWordPieces = new();
    private readonly WordPieceTrieNode _continuationWordPieces = new();

    private BertWordPieceTokenizer(Dictionary<string, int> vocabulary)
    {
        _vocabulary = vocabulary;
        _classificationTokenId = GetTokenId("[CLS]");
        _separatorTokenId = GetTokenId("[SEP]");
        _unknownTokenId = GetTokenId("[UNK]");
        _paddingTokenId = GetTokenId("[PAD]");
        foreach (var (wordPiece, tokenId) in vocabulary)
        {
            AddWordPiece(wordPiece, tokenId);
        }
    }

    public long PaddingTokenId => _paddingTokenId;

    public static BertWordPieceTokenizer Load(string tokenizerPath)
    {
        using var stream = File.OpenRead(tokenizerPath);
        using var document = JsonDocument.Parse(stream);
        var model = document.RootElement.GetProperty("model");
        if (!string.Equals(model.GetProperty("type").GetString(), "WordPiece", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Only Hugging Face WordPiece tokenizer.json files are supported.");
        }

        var vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in model.GetProperty("vocab").EnumerateObject())
        {
            vocabulary[item.Name] = item.Value.GetInt32();
        }

        return new BertWordPieceTokenizer(vocabulary);
    }

    /// <summary>
    /// Loads the line-based BERT vocabulary distributed with Chinese-CLIP.
    /// </summary>
    public static BertWordPieceTokenizer LoadVocabulary(string vocabularyPath)
    {
        var vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
        var index = 0;
        foreach (var line in File.ReadLines(vocabularyPath))
        {
            vocabulary[line.Trim()] = index++;
        }

        return new BertWordPieceTokenizer(vocabulary);
    }

    public long[] Encode(string text, int maximumTokens)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTokens, 2);

        var tokenIds = new List<long>(Math.Min(maximumTokens, 32)) { _classificationTokenId };
        var tokens = new BasicTokenEnumerator(text.AsSpan());
        while (tokens.MoveNext(out var token))
        {
            var wordPieceCount = CountWordPieces(token);
            if (tokenIds.Count + wordPieceCount >= maximumTokens)
            {
                tokenIds.Add(_separatorTokenId);
                return tokenIds.ToArray();
            }

            AppendWordPieces(token, tokenIds);
        }

        tokenIds.Add(_separatorTokenId);
        return tokenIds.ToArray();
    }

    public int CountTokens(string text)
    {
        return CountTokens(text.AsSpan());
    }

    public int CountTokens(ReadOnlySpan<char> text)
    {
        var tokenCount = 0;
        var tokens = new BasicTokenEnumerator(text);
        while (tokens.MoveNext(out var token))
        {
            tokenCount += CountWordPieces(token);
        }

        return tokenCount;
    }

    public string GetFingerprint()
    {
        using var stream = new MemoryStream();
        foreach (var item in _vocabulary.OrderBy(entry => entry.Value))
        {
            var bytes = Encoding.UTF8.GetBytes(item.Key);
            stream.Write(bytes);
            stream.WriteByte(0);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private int GetTokenId(string token)
    {
        return _vocabulary.TryGetValue(token, out var tokenId)
            ? tokenId
            : throw new InvalidDataException($"The BERT tokenizer is missing the {token} token.");
    }

    private int CountWordPieces(ReadOnlySpan<char> token)
    {
        if (token.Length > MaximumWordLength)
        {
            return 1;
        }

        var count = 0;
        for (var start = 0; start < token.Length;)
        {
            var node = start == 0 ? _initialWordPieces : _continuationWordPieces;
            var matchedTokenId = node.TokenId;
            var matchedEnd = matchedTokenId >= 0 ? start : -1;
            for (var current = start; current < token.Length; current++)
            {
                if (!node.Children.TryGetValue(token[current], out node))
                {
                    break;
                }

                if (node.TokenId >= 0)
                {
                    matchedTokenId = node.TokenId;
                    matchedEnd = current + 1;
                }
            }

            if (matchedTokenId < 0)
            {
                return 1;
            }

            count++;
            start = matchedEnd;
        }

        return count;
    }

    private void AppendWordPieces(ReadOnlySpan<char> token, List<long> tokenIds)
    {
        if (token.Length > MaximumWordLength)
        {
            tokenIds.Add(_unknownTokenId);
            return;
        }

        for (var start = 0; start < token.Length;)
        {
            var node = start == 0 ? _initialWordPieces : _continuationWordPieces;
            var matchedTokenId = node.TokenId;
            var matchedEnd = matchedTokenId >= 0 ? start : -1;
            for (var current = start; current < token.Length; current++)
            {
                if (!node.Children.TryGetValue(token[current], out node))
                {
                    break;
                }

                if (node.TokenId >= 0)
                {
                    matchedTokenId = node.TokenId;
                    matchedEnd = current + 1;
                }
            }

            if (matchedTokenId < 0)
            {
                tokenIds.Add(_unknownTokenId);
                return;
            }

            tokenIds.Add(matchedTokenId);
            start = matchedEnd;
        }
    }

    private void AddWordPiece(string wordPiece, int tokenId)
    {
        var characters = wordPiece.AsSpan();
        var node = _initialWordPieces;
        if (characters.StartsWith("##", StringComparison.Ordinal))
        {
            characters = characters[2..];
            node = _continuationWordPieces;
        }

        foreach (var character in characters)
        {
            if (!node.Children.TryGetValue(character, out var child))
            {
                child = new WordPieceTrieNode();
                node.Children.Add(character, child);
            }

            node = child;
        }

        node.TokenId = tokenId;
    }

    private ref struct BasicTokenEnumerator
    {
        private readonly ReadOnlySpan<char> _value;
        private int _index;

        public BasicTokenEnumerator(ReadOnlySpan<char> value)
        {
            _value = value;
            _index = 0;
        }

        public bool MoveNext(out ReadOnlySpan<char> token)
        {
            while (_index < _value.Length)
            {
                var character = _value[_index];
                if (IsIgnored(character) || char.IsWhiteSpace(character))
                {
                    _index++;
                    continue;
                }

                if (IsChineseCharacter(character) || IsPunctuation(character))
                {
                    token = _value.Slice(_index++, 1);
                    return true;
                }

                var start = _index++;
                while (_index < _value.Length)
                {
                    character = _value[_index];
                    if (IsIgnored(character) || char.IsWhiteSpace(character)
                                             || IsChineseCharacter(character) || IsPunctuation(character))
                    {
                        break;
                    }

                    _index++;
                }

                token = _value.Slice(start, _index - start);
                return true;
            }

            token = default;
            return false;
        }
    }

    private static bool IsIgnored(char character) =>
        character is '\0' or '\uFFFD' || (char.IsControl(character) && character is not '\t' and not '\n' and not '\r');

    private static bool IsChineseCharacter(char character)
    {
        return character is >= '\u4E00' and <= '\u9FFF'
            or >= '\u3400' and <= '\u4DBF'
            or >= '\uF900' and <= '\uFAFF'
            or >= '\u3040' and <= '\u30FF';
    }

    private static bool IsPunctuation(char character)
    {
        if (character is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~')
        {
            return true;
        }

        return char.GetUnicodeCategory(character) is UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation;
    }

    private sealed class WordPieceTrieNode
    {
        public Dictionary<char, WordPieceTrieNode> Children { get; } = new();
        public int TokenId { get; set; } = -1;
    }
}
