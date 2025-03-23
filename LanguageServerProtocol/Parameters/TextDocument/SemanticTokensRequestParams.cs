using LanguageServer.Parameters;


namespace LanguageServerProtocol.Parameters.TextDocument;
public class SemanticTokensRequestParams
{
    /// <summary>
    /// The text document.
    /// </summary>
    /// <seealso>Spec 3.10.0</seealso>
    public TextDocumentIdentifier textDocument { get; set; }
}
