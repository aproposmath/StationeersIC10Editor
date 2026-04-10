#nullable enable

using Newtonsoft.Json;

namespace ImGuiEditor.LSP;

public class TextDocumentIdentifier
{
    public string uri { get; set; } = string.Empty;
}

public class DidOpenTextDocumentParams
{
    public TextDocumentItem textDocument { get; set; } = new();
}

public class TextDocumentItem
{
    public string uri { get; set; } = string.Empty;
    public string languageId { get; set; } = string.Empty;
    public int version { get; set; }
    public string text { get; set; } = string.Empty;
}

public class DidChangeTextDocumentParams
{
    public VersionedTextDocumentIdentifier textDocument { get; set; } = new();
    public TextDocumentContentChangeEvent[] contentChanges { get; set; } = [];
}

public class VersionedTextDocumentIdentifier : TextDocumentIdentifier
{
    public int version { get; set; }
}

public class Range
{
    public Position start { get; set; } = new();
    public Position end { get; set; } = new();
}

public class Position
{
    public Position()
    {
        line = 0;
        character = 0;
    }
    public Position(StationeersIC10Editor.TextPosition p)
    {
        line = p.Line;
        character = p.Col;
    }
    public int line { get; set; }
    public int character { get; set; }
}

public class TextDocumentContentChangeEvent
{
    public string text { get; set; } = string.Empty;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public Range? range { get; set; }
}

public class DidCloseTextDocumentParams
{
    public TextDocumentIdentifier textDocument { get; set; } = new();
}


public class SemanticTokensLegend
{
    public string[] tokenTypes { get; set; } = [];
    public string[] tokenModifiers { get; set; } = [];
}

public class SemanticTokensParams
{
    public TextDocumentIdentifier textDocument { get; set; } = new();
}

public class SemanticTokens
{
    public string resultId { get; set; } = string.Empty;
    public int[] data { get; set; } = [];
}

public class Diagnostic
{
    public Range range { get; set; } = new();
    public int severity { get; set; }
    public string code { get; set; } = string.Empty;
    public string source { get; set; } = string.Empty;
    public string message { get; set; } = string.Empty;
}

public class PublishDiagnosticsParams
{
    public TextDocumentIdentifier textDocument { get; set; } = new();
    public Diagnostic[] diagnostics { get; set; } = [];
}


public class DocumentDiagnosticParams
{
    public TextDocumentIdentifier textDocument { get; set; } = new();
}

public class Documentation
{
    public string kind { get; set; } = string.Empty;
    public string value { get; set; } = string.Empty;
}

public class CompletionItem
{
    public string label { get; set; } = string.Empty;
    public int kind { get; set; }
    public string detail { get; set; } = string.Empty;
    public string sortText { get; set; } = string.Empty;
    public Documentation documentation { get; set; } = new();
}

public class CompletionList
{
    public bool isIncomplete { get; set; }
    public CompletionItem[] items { get; set; } = [];
}

public class ParameterInformation
{
    public string label { get; set; } = string.Empty;
    public Documentation documentation { get; set; } = new();
}

public class SignatureHelp
{
    public string label { get; set; } = string.Empty;
    public Documentation documentation { get; set; } = new();
    public ParameterInformation[] parameters { get; set; } = [];
}

public class SignatureHelpList
{
    public SignatureHelp[] signatures { get; set; } = [];
    public int activeSignature { get; set; }
    public int activeParameter { get; set; }
}
