using System.Text;
using System.Text.Json;
using Kitopia.Desktop.Features.Search.Semantic;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
var tokenizer = Path.GetExtension(args[0]) == ".txt"
    ? BertWordPieceTokenizer.LoadVocabulary(args[0])
    : BertWordPieceTokenizer.Load(args[0]);
var maximumTokens = int.Parse(args[1]);
var texts = JsonSerializer.Deserialize<string[]>(Console.In.ReadToEnd())
            ?? throw new InvalidDataException("Expected an array of texts.");
Console.Write(JsonSerializer.Serialize(texts.Select(text => tokenizer.Encode(text, maximumTokens))));
