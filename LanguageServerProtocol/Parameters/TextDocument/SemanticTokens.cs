
namespace LanguageServerProtocol.Parameters.TextDocument;

public class SemanticTokens
{
    public string? resultId { get; set; }
    public uint[] data { get; set; } = new uint[0];
}