using LanguageServer.Parameters.TextDocument;


namespace Leaf.LSP.Server;
public class TextDocumentChangedEventArgs : EventArgs
{
    private readonly TextDocumentItem _document;

    public TextDocumentChangedEventArgs(TextDocumentItem document)
    {
        _document = document;
    }

    public TextDocumentItem Document => _document;
}