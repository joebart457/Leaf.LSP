using LanguageServer.Client;
using LanguageServer.Parameters.General;
using LanguageServer.Parameters.TextDocument;
using LanguageServer.Parameters.Workspace;
using LanguageServer.Parameters;
using LanguageServer;
using Range = LanguageServer.Parameters.Range;
using Leaf.Language.TypedExpressions;
using Leaf.Language.Core.Models;
using Leaf.Language.Core.Constants;
using Leaf.Language.Api.Models;
using Tokenizer.Core.Models;
using Location = LanguageServer.Parameters.Location;
using Leaf.Language.Api;

namespace Leaf.LSP.Server;

public class LeafLanguageServer : ServiceConnection
{
    private Uri? _workerSpaceRoot;
    private int _maxNumberOfProblems = 1000;
    private TextDocumentManager _documents;

    public LeafLanguageServer(Stream input, Stream output)
        : base(input, output)
    {
        _documents = new TextDocumentManager();
        _documents.Changed += Documents_Changed;
    }

    private void Documents_Changed(object? sender, TextDocumentChangedEventArgs e)
    {
        ValidateTextDocument(e.Document);
    }
    protected override Result<InitializeResult, ResponseError<InitializeErrorData>> Initialize(InitializeParams @params)
    {
        _workerSpaceRoot = @params.rootUri;
        var result = new InitializeResult
        {
            capabilities = new ServerCapabilities
            {
                textDocumentSync = TextDocumentSyncKind.Full,
                completionProvider = new CompletionOptions
                {
                    resolveProvider = false
                },
                definitionProvider = true,
                typeDefinitionProvider = true,
                foldingRangeProvider = false,
                documentFormattingProvider = false,
                hoverProvider = true,
                referencesProvider = true,

            }
        };
        return Result<InitializeResult, ResponseError<InitializeErrorData>>.Success(result);
    }

    protected override void DidOpenTextDocument(DidOpenTextDocumentParams @params)
    {
        _documents.Add(@params.textDocument);
        Logger.Instance.Log($"{@params.textDocument.uri} opened.");
    }

    protected override void DidChangeTextDocument(DidChangeTextDocumentParams @params)
    {
        _documents.Change(@params.textDocument.uri, @params.textDocument.version, @params.contentChanges);
        Logger.Instance.Log($"{@params.textDocument.uri} changed.");
    }

    protected override void DidCloseTextDocument(DidCloseTextDocumentParams @params)
    {
        _documents.Remove(@params.textDocument.uri);
        Logger.Instance.Log($"{@params.textDocument.uri} closed.");
    }

    protected override void DidChangeConfiguration(DidChangeConfigurationParams @params)
    {
        _maxNumberOfProblems = @params?.settings?.languageServerExample?.maxNumberOfProblems ?? _maxNumberOfProblems;
        Logger.Instance.Log($"maxNumberOfProblems is set to {_maxNumberOfProblems}.");
        foreach (var document in _documents.All)
        {
            ValidateTextDocument(document);
        }
    }

    private void ValidateTextDocument(TextDocumentItem document)
    {
        var diagnostics = new List<Diagnostic>();

        var lines = document.text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var problems = 0;

        var programContext = GetContext(document.text);
        foreach (var error in programContext.ValidationErrors)
        {
            if (problems >= _maxNumberOfProblems) break;
            diagnostics.Add(new Diagnostic()
            {
                severity = DiagnosticSeverity.Warning,
                range = new Range
                {
                    start = new Position { line = error.Item1.Start.Line, character = error.Item1.Start.Column },
                    end = new Position { line = error.Item1.End.Line, character = error.Item1.End.Column }
                },
                message = error.Item2,
                source = "Language Information Engine"
            });
            problems++;
        }


        Proxy.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            uri = document.uri,
            diagnostics = diagnostics.ToArray()
        });
    }

    protected override void DidChangeWatchedFiles(DidChangeWatchedFilesParams @params)
    {
        Logger.Instance.Log("We received an file change event");
    }


    protected override Result<CompletionResult, ResponseError> Completion(CompletionParams @params)
    {
        var programContext = GetContext(@params.textDocument.uri);
        var token = programContext.GetTokenAt((int)@params.position.line, (int)@params.position.character);
        var functionContext = programContext.GetFunctionContext((int)@params.position.line, (int)@params.position.character);
        var expressionContext = functionContext?.GetExpressionContext((int)@params.position.line, (int)@params.position.character);

        var suggestions = new List<CompletionItem>();
        if (functionContext != null)
        {
            if (expressionContext?.Expression is TypedGetExpression ge && ge.Instance.TypeInfo.IsValidNormalPtr && ge.Instance.TypeInfo.GenericTypeArgument is StructTypeInfo structTypeInfo)
            {
                suggestions.AddRange(structTypeInfo.Fields.Select(x => new CompletionItem()
                {
                    label = x.Name.Lexeme,
                    detail = x.TypeInfo.ToString(),
                    documentation = $"type {structTypeInfo.Name.Lexeme}",
                }));
            }
            var function = functionContext.FunctionDefinition;
            suggestions.AddRange(functionContext.FunctionDefinition.Parameters.Select(p => new CompletionItem()
            {
                label = p.Name.Lexeme,
                detail = p.TypeInfo.ToString(),
                documentation = $"{function.FunctionName.Lexeme}:{function.ReturnType}{(function.Parameters.Any() ? " " : "")}{string.Join(" ", function.Parameters.Select(x => $"(param {x.Name.Lexeme} {x.TypeInfo})"))}",
            }));
            suggestions.AddRange(function.ExtractLocalVariableExpressions().Select(p => new CompletionItem()
            {
                label = p.Identifier.Lexeme,
                detail = p.VariableType.ToString(),
                documentation = "local",
            }));
        }
        suggestions.AddRange(programContext.FunctionDefinitions.Select(x => new CompletionItem()
        {
            label = x.FunctionName.Lexeme,
            detail = x.GetFunctionPointerType().ToString(),
            documentation = "function",
        }));
        suggestions.AddRange(programContext.GenericFunctionDefinitions.Select(x => new CompletionItem()
        {
            label = x.FunctionName.Lexeme,
            detail = x.GetDecoratedFunctionIdentifier(),
            documentation = "function",
        }));
        suggestions.AddRange(programContext.ImportedFunctionDefinitions.Select(x => new CompletionItem()
        {
            label = x.FunctionName.Lexeme,
            detail = x.GetFunctionPointerType().ToString(),
            documentation = "importedfunction",
        }));
        suggestions.AddRange(programContext.UserDefinedTypes.Select(x => new CompletionItem()
        {
            label = x.Name.Lexeme,
            detail = x.ToString(),
            documentation = "function",
        }));
        if (token != null) suggestions = suggestions.Where(x => x.label.StartsWith(token.Lexeme, StringComparison.InvariantCultureIgnoreCase)).ToList();
        var array = suggestions.ToArray();
        return Result<CompletionResult, ResponseError>.Success(array);
    }

    protected override Result<Location[], ResponseError> FindReferences(ReferenceParams @params)
    {
        var programContext = GetContext(@params.textDocument.uri);
        var token = programContext.GetTokenAt((int)@params.position.line, (int)@params.position.character);
        if (token != null && programContext.References.TryGetValue(token, out var references))
        {
            var locations = references.Select(x => CreateLocationFromToken(@params.textDocument.uri, x)).ToArray();
            return Result<Location[], ResponseError>.Success(locations);
        }
        else return Result<Location[], ResponseError>.Success(Array.Empty<Location>());

    }

    protected override Result<LocationSingleOrArray, ResponseError> GotoDefinition(TextDocumentPositionParams @params)
    {
        var programContext = GetContext(@params.textDocument.uri);
        var token = programContext.GetTokenAt((int)@params.position.line, (int)@params.position.character);
        if (token?.Type == ReclassifiedTokenTypes.Type) return GotoTypeDefinition(@params);
        if (token?.Type == ReclassifiedTokenTypes.Parameter) return GotoParameterDefinition(programContext, token, @params);
        if (token?.Type == ReclassifiedTokenTypes.Variable) return GotoLocalVariableDefinition(programContext, token, @params);
        if (token?.Type == ReclassifiedTokenTypes.Function) return GotoFunctionDefinition(programContext, token, @params);
        if (token?.Type == ReclassifiedTokenTypes.ImportedFunction) return GotoImportedFunctionDefinition(programContext, token, @params);
        if (token?.Type == ReclassifiedTokenTypes.TypeField) return GotoFieldDefinition(programContext, token, @params);
        if (token?.Type == ReclassifiedTokenTypes.ImportLibrary) return GotoImportLibraryDefinition(programContext, token, @params);

        return Result<LocationSingleOrArray, ResponseError>.Error(new() { message = $"Unable to go to definition at ({@params.position.line}, col. {@params.position.character}). Token: {token}." });
    }

    protected override Result<LocationSingleOrArray, ResponseError> GotoTypeDefinition(TextDocumentPositionParams @params)
    {
        var programContext = GetContext(@params.textDocument.uri);
        var token = programContext.GetTokenAt((int)@params.position.line, (int)@params.position.character);
        var typeToken = programContext.UserDefinedTypes.Find(x => x.Name.Lexeme == token?.Lexeme)?.Name;
        if (typeToken == null)
            typeToken = programContext.GenericTypeDefinitions.Find(x => x.TypeName.Lexeme == token?.Lexeme)?.TypeName;
        if (typeToken != null)
        {
            return GenerateLocationFromToken(@params.textDocument.uri, typeToken);
        }
        return Result<LocationSingleOrArray, ResponseError>.Error(new() { message = $"Unable to go to definition at ({@params.position.line}, col. {@params.position.character}). Token: {token} Type token: {typeToken}" });
    }

    protected Result<LocationSingleOrArray, ResponseError> GotoFunctionDefinition(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        var functionToken = programContext.FunctionDefinitions.Find(x => x.FunctionName.Lexeme == token?.Lexeme)?.FunctionName;
        if (functionToken == null)
            functionToken = programContext.GenericFunctionDefinitions.Find(x => x.FunctionName.Lexeme == token?.Lexeme)?.FunctionName;
        if (functionToken != null)
        {
            return GenerateLocationFromToken(@params.textDocument.uri, functionToken);
        }
        return Result<LocationSingleOrArray, ResponseError>.Error(new() { message = $"Unable to go to function definition at ({@params.position.line}, col. {@params.position.character}). Token: {token} function token: {functionToken}" });
    }

    protected Result<LocationSingleOrArray, ResponseError> GotoImportedFunctionDefinition(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        var functionToken = programContext.ImportedFunctionDefinitions.Find(x => x.FunctionName.Lexeme == token?.Lexeme)?.FunctionName;
        if (functionToken != null)
        {
            return GenerateLocationFromToken(@params.textDocument.uri, functionToken);
        }
        return Result<LocationSingleOrArray, ResponseError>.Error(new() { message = $"Unable to go to function definition at ({@params.position.line}, col. {@params.position.character}). Token: {token} function token: {functionToken}" });
    }

    protected Result<LocationSingleOrArray, ResponseError> GotoParameterDefinition(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        var functionContext = programContext.GetFunctionContext((int)@params.position.line, (int)@params.position.character);
        if (functionContext != null)
        {
            var parameter = functionContext.GetParameter(token);
            if (parameter != null) return GenerateLocationFromToken(@params.textDocument.uri, parameter.Name);
        }
        var genericFunctionContext = programContext.GetGenericFunctionContext((int)@params.position.line, (int)@params.position.character);
        if (genericFunctionContext != null)
        {
            var parameter = genericFunctionContext.GetParameter(token);
            if (parameter != null) return GenerateLocationFromToken(@params.textDocument.uri, parameter.Name);
        }
        return Result<LocationSingleOrArray, ResponseError>.Error(new() { message = $"Unable to go to symbol definition at ({@params.position.line}, col. {@params.position.character}). Token: {token}" });
    }

    protected Result<LocationSingleOrArray, ResponseError> GotoLocalVariableDefinition(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        var functionContext = programContext.GetFunctionContext((int)@params.position.line, (int)@params.position.character);
        var localVariableExpression = functionContext?.GetLocalVariableExpression(token);

        if (localVariableExpression != null)
        {
            return GenerateLocationFromToken(@params.textDocument.uri, localVariableExpression.Identifier);
        }
        var genericFunctionContext = programContext.GetGenericFunctionContext((int)@params.position.line, (int)@params.position.character);
        var localVariableExpr = genericFunctionContext?.GetLocalVariableExpression(token);

        if (localVariableExpr != null)
        {
            return GenerateLocationFromToken(@params.textDocument.uri, localVariableExpr.Identifier);
        }
        return Result<LocationSingleOrArray, ResponseError>.Error(new() { message = $"Unable to go to variable definition at ({@params.position.line}, col. {@params.position.character}). Token: {token} Context: {functionContext?.FunctionDefinition.FunctionName}" });
    }

    protected Result<LocationSingleOrArray, ResponseError> GotoFieldDefinition(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        var functionContext = programContext.GetFunctionContext((int)@params.position.line, (int)@params.position.character);
        var expressionContext = functionContext?.GetExpressionContext((int)@params.position.line, (int)@params.position.character);

        if (expressionContext?.Expression is TypedGetExpression getExpression && getExpression.Instance.TypeInfo.GenericTypeArgument is StructTypeInfo structTypeInfo)
        {
            var foundField = structTypeInfo.Fields.Find(x => getExpression.TargetField.Lexeme == x.Name.Lexeme);
            if (foundField != null) return GenerateLocationFromToken(@params.textDocument.uri, foundField.Name);
        }
        return Result<LocationSingleOrArray, ResponseError>.Error(new() { message = $"Unable to go to field definition at ({@params.position.line}, col. {@params.position.character}). Token: {token}" });
    }

    protected Result<LocationSingleOrArray, ResponseError> GotoImportLibraryDefinition(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        var importLibraryDefinition = programContext.ImportLibraryDefinitions.Find(x => x.LibraryAlias.Lexeme == token.Lexeme);

        if (importLibraryDefinition != null)
        {
            return GenerateLocationFromToken(@params.textDocument.uri, importLibraryDefinition.LibraryAlias);
        }
        return Result<LocationSingleOrArray, ResponseError>.Error(new() { message = $"Unable to go to field definition at ({@params.position.line}, col. {@params.position.character}). Token: {token}" });
    }

    private Result<LocationSingleOrArray, ResponseError> GenerateLocationFromToken(Uri documentUri, Token token)
    {
        return Result<LocationSingleOrArray, ResponseError>.Success(
                new LocationSingleOrArray(
                new Location()
                {
                    range = new Range()
                    {
                        start = new Position() { line = token.Start.Line, character = token.Start.Column },
                        end = new Position() { line = token.End.Line, character = token.End.Column },
                    },
                    uri = documentUri,
                }));
    }

    private Location CreateLocationFromToken(Uri documentUri, Token token)
    {
        return new Location()
        {
            range = new Range()
            {
                start = new Position() { line = token.Start.Line, character = token.Start.Column },
                end = new Position() { line = token.End.Line, character = token.End.Column },
            },
            uri = documentUri,
        };
    }

    protected override Result<Hover, ResponseError> Hover(TextDocumentPositionParams @params)
    {
        var programContext = GetContext(@params.textDocument.uri);

        var token = programContext.GetTokenAt((int)@params.position.line, (int)@params.position.character);
        if (token?.Type == ReclassifiedTokenTypes.Type) return HoverTypeSymbol(programContext, token, @params);
        if (token?.Type == ReclassifiedTokenTypes.Parameter) return HoverParameter(programContext, token, @params);
        if (token?.Type == ReclassifiedTokenTypes.Variable) return HoverVariable(programContext, token, @params);
        if (token?.Type == ReclassifiedTokenTypes.Function) return HoverFunction(programContext, token, @params);
        if (token?.Type == ReclassifiedTokenTypes.ImportedFunction) return HoverImportedFunction(programContext, token, @params);
        if (token?.Type == ReclassifiedTokenTypes.TypeField) return HoverTypeField(programContext, @params);
        if (token?.Type == ReclassifiedTokenTypes.ImportLibrary) return HoverImportLibrary(programContext, token, @params);
        if (token?.Type == TokenTypes.IntrinsicType) return HoverIntrinsicType(programContext, token, @params);
        if (token?.Type == TokenTypes.Return) return HoverReturn(programContext, token, @params);
        return HoverExpression(programContext, @params);
    }

    protected Result<Hover, ResponseError> HoverTypeSymbol(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        var userDefinedType = programContext.UserDefinedTypes.Find(x => x.Name.Lexeme == token?.Lexeme);
        if (userDefinedType != null)
        {
            return Result<Hover, ResponseError>.Success(new Hover()
            {
                contents = new(CreateMarkupLanguageContent($"type {userDefinedType}")),
            });
        }
        var genericTypeDefinition = programContext.GenericTypeDefinitions.Find(x => x.TypeName.Lexeme == token?.Lexeme);
        if (genericTypeDefinition != null)
        {

            return Result<Hover, ResponseError>.Success(new Hover()
            {
                contents = new(CreateMarkupLanguageContent($"type {genericTypeDefinition.TypeName.Lexeme}[{string.Join(", ", genericTypeDefinition.GenericTypeParameters.Select(x => $"gen {x.TypeName.Lexeme}"))}]")),
            });
        }
        return Result<Hover, ResponseError>.Error(new() { message = $"No information found for type symbol." });
    }
    protected Result<Hover, ResponseError> HoverParameter(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        var functionContext = programContext.GetFunctionContext((int)@params.position.line, (int)@params.position.character);
        if (functionContext != null)
        {
            var parameter = functionContext.GetParameter(token);
            if (parameter != null)
                return Result<Hover, ResponseError>.Success(new Hover()
                {
                    contents = new(CreateMarkupLanguageContent(parameter.TypeInfo.ToString())),
                });
        }
        var genericFunctionContext = programContext.GetGenericFunctionContext((int)@params.position.line, (int)@params.position.character);
        if (genericFunctionContext != null)
        {
            var parameter = genericFunctionContext.GetParameter(token);
            if (parameter != null)
                return Result<Hover, ResponseError>.Success(new Hover()
                {
                    contents = new(CreateMarkupLanguageContent(parameter.TypeSymbol.ToString())),
                });
        }
        return Result<Hover, ResponseError>.Error(new() { message = $"No information found for parameter." });
    }
    protected Result<Hover, ResponseError> HoverFunction(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        var function = programContext.FunctionDefinitions.Find(x => x.FunctionName.Lexeme == token?.Lexeme);

        if (function != null)
        {
            return Result<Hover, ResponseError>.Success(new Hover()
            {
                contents = new(CreateMarkupLanguageContent($"{function.FunctionName.Lexeme}:{function.ReturnType}{(function.Parameters.Any() ? " " : "")}{string.Join(" ", function.Parameters.Select(x => $"(param {x.Name.Lexeme} {x.TypeInfo})"))}")),
            });
        }
        var genericFunction = programContext.GenericFunctionDefinitions.Find(x => x.FunctionName.Lexeme == token.Lexeme);
        if (genericFunction != null)
        {
            return Result<Hover, ResponseError>.Success(new Hover()
            {
                contents = new(CreateMarkupLanguageContent($"{genericFunction.FunctionName.Lexeme}[{string.Join(",", genericFunction.GenericTypeParameters.Select(x => x.ToString()))}]:{genericFunction.ReturnType}{(genericFunction.Parameters.Any() ? " " : "")}{string.Join(" ", genericFunction.Parameters.Select(x => $"(param {x.Name.Lexeme} {x.TypeSymbol})"))}")),
            });
        }
        return Result<Hover, ResponseError>.Error(new() { message = $"No information found for function." });
    }

    protected Result<Hover, ResponseError> HoverExpression(ProgramContext programContext, TextDocumentPositionParams @params)
    {
        var functionContext = programContext.GetFunctionContext((int)@params.position.line, (int)@params.position.character);
        var expressionContext = functionContext?.GetExpressionContext((int)@params.position.line, (int)@params.position.character);
        if (expressionContext != null)
        {

            return Result<Hover, ResponseError>.Success(new Hover()
            {
                contents = new(CreateMarkupLanguageContent(expressionContext.Expression.TypeInfo.ToString())),
            });
        }
        return Result<Hover, ResponseError>.Success(new());
    }

    protected Result<Hover, ResponseError> HoverTypeField(ProgramContext programContext, TextDocumentPositionParams @params)
    {
        var functionContext = programContext.GetFunctionContext((int)@params.position.line, (int)@params.position.character);
        var expressionContext = functionContext?.GetExpressionContext((int)@params.position.line, (int)@params.position.character);
        if (expressionContext != null)
        {
            return Result<Hover, ResponseError>.Success(new Hover()
            {
                contents = new(CreateMarkupLanguageContent(expressionContext.Expression.TypeInfo.ToString())),
            });
        }
        return Result<Hover, ResponseError>.Error(new() { message = $"No information found for function." });
    }

    protected Result<Hover, ResponseError> HoverImportLibrary(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        var importLibraryDefinition = programContext.ImportLibraryDefinitions.Find(x => x.LibraryAlias.Lexeme == token.Lexeme);
        if (importLibraryDefinition != null)
        {
            return Result<Hover, ResponseError>.Success(new Hover()
            {
                contents = new(CreateMarkupLanguageContent($"'{importLibraryDefinition.LibraryPath.Lexeme}'")),
            });
        }
        return Result<Hover, ResponseError>.Error(new() { message = $"No information found for function." });
    }

    protected Result<Hover, ResponseError> HoverIntrinsicType(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        return Result<Hover, ResponseError>.Success(new Hover()
        {
            contents = new(CreateMarkupLanguageContent(token.Lexeme.ToString())),
        });
    }

    protected Result<Hover, ResponseError> HoverReturn(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        var functionContext = programContext.GetFunctionContext((int)@params.position.line, (int)@params.position.character);
        if (functionContext != null)
        {

            return Result<Hover, ResponseError>.Success(new Hover()
            {
                contents = new(CreateMarkupLanguageContent($"{functionContext.FunctionDefinition.FunctionName.Lexeme}:{functionContext.FunctionDefinition.ReturnType}{(functionContext.FunctionDefinition.Parameters.Any() ? " " : "")}{string.Join(" ", functionContext.FunctionDefinition.Parameters.Select(x => $"(param {x.Name.Lexeme} {x.TypeInfo})"))}")),
            });
        }
        return Result<Hover, ResponseError>.Error(new() { message = $"No information found for function." });
    }

    protected Result<Hover, ResponseError> HoverImportedFunction(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        var function = programContext.ImportedFunctionDefinitions.Find(x => x.FunctionName.Lexeme == token?.Lexeme);
        if (function != null)
        {
            return Result<Hover, ResponseError>.Success(new Hover()
            {
                contents = new(CreateMarkupLanguageContent($"{function.LibraryAlias.Lexeme} {function.CallingConvention.ToString().ToLower()} {function.FunctionName.Lexeme}:{function.ReturnType}{(function.Parameters.Any() ? " " : "")}{string.Join(" ", function.Parameters.Select(x => $"(param {x.Name.Lexeme} {x.TypeInfo})"))}"))
            });
        }
        return Result<Hover, ResponseError>.Error(new() { message = $"No information found for imported function." });
    }
    protected Result<Hover, ResponseError> HoverVariable(ProgramContext programContext, Token token, TextDocumentPositionParams @params)
    {
        var functionContext = programContext.GetFunctionContext((int)@params.position.line, (int)@params.position.character);
        var localVariableExpression = functionContext?.GetLocalVariableExpression(token);

        if (localVariableExpression != null)
        {
            return Result<Hover, ResponseError>.Success(new Hover()
            {
                contents = new(CreateMarkupLanguageContent(localVariableExpression.VariableType.ToString()))
            });
        }
        return Result<Hover, ResponseError>.Error(new() { message = $"No information found for variable." });
    }

    private MarkupContent CreateMarkupLanguageContent(string code)
    {
        var content = $"```leaf\r\n{code}\r\n```";
        return new MarkupContent()
        {
            kind = MarkupKind.Markdown,
            value = content,
        };
    }

    protected override Result<CompletionItem, ResponseError> ResolveCompletionItem(CompletionItem @params)
    {
        //retrn
        //if (@params.data == 1)
        //{
        //    @params.detail = "TypeScript details";
        //    @params.documentation = "TypeScript documentation";
        //}
        //else if (@params.data == 2)
        //{
        //    @params.detail = "JavaScript details";
        //    @params.documentation = "JavaScript documentation";
        //}
        return Result<CompletionItem, ResponseError>.Success(@params);
    }

    protected override VoidResult<ResponseError> Shutdown()
    {
        Logger.Instance.Log("Language Server is about to shutdown.");
        // WORKAROUND: Language Server does not receive an exit notification.
        Task.Delay(1000).ContinueWith(_ => Environment.Exit(0));
        return VoidResult<ResponseError>.Success();
    }

    private ProgramContext GetContext(Uri documentUri)
    {
        var document = _documents.All.FirstOrDefault(x => x.uri == documentUri);
        if (document == null) return new ProgramContext();
        return GetContext(document.text);
    }

    private ProgramContext GetContext(string text)
    {
        var engine = new LanguageInformationResolver();
        return engine.ResolveText(text);
    }
}