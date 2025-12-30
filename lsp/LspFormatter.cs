using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

using StationeersIC10Editor;

namespace ImGuiEditor.LSP;

public class LSPFormatter : ICodeFormatter
{
    protected VersionedTextDocumentIdentifier Identifier;
    protected LspClient LspClient;
    protected List<TextDocumentContentChangeEvent> _changes = new List<TextDocumentContentChangeEvent>();

    protected bool _isOpen = false;

    public int Version
    {
        get { return Identifier.version; }
        set { Identifier.version = value; }
    }

    public LSPFormatter() : base()
    {
    }

    public int IncrementVersion()
    {
        Identifier.version += 1;
        return Identifier.version;
    }

    public void UpdateDiagnostics(PublishDiagnosticsParams p)
    {
        foreach (var diag in p.diagnostics)
        {
            if (diag.severity > 1)
                continue;

            var start = diag.range.start;
            var end = diag.range.end;
            var line = start.line;
            var column = start.character;
            var length = end.character - start.character;
            if (end.line > start.line)
                length = Lines[line].Text.Length - start.character;
            if (line < Lines.Count)
            {
                var token = Lines[line].GetTokenAt(column);
                if (token == null)
                    token = Lines[line].GetTokenAt(column - 1);

                if (token == null)
                {
                    L.Debug($"ERROR: No token Adding error token at {line},{column}: {diag.message}");
                }
                else
                {
                    token.Error = StyledText.ErrorText(diag.message);
                    token.Style.Color = ICodeFormatter.ColorError;
                }
            }
        }
    }

    public static double MatchingScore(string input)
    {
        return 1.0;
    }

    public virtual void SubmitChanges()
    {
        L.Debug("Code changed, submitting changes to LSP");
        if (LspClient != null && _changes.Count > 0 && LspClient.IsInitialized)
        {
            L.Debug($"Submitting {_changes.Count} changes to LSP (version {Identifier.version})");
            if (!_isOpen)
            {
                L.Debug("Opening document in LSP");
                LspClient.OpenDocument(Identifier.uri, "ic10", Editor.Code);
                _isOpen = true;
            }
            else
            {
                L.Debug("Sending changes to LSP");
                // LspClient.ChangeDocument(Identifier, _changes.ToArray());
                LspClient.ChangeDocumentFull(Identifier, Editor.Code);
            }

            L.Debug("Changes submitted, request tokens");
            // request semantic tokens update
            UpdateTokens();
            _changes.Clear();
        }
    }

    public override void UpdateAutocomplete()
    {
        _autocomplete = null;
        _autocompleteInsertText = null;
        UpdateAutocompleteAsync();
    }

    TextPosition _lastCompletePos = new TextPosition(-1, -1);
    int _lastCompleteVersion = -1;

    public async void UpdateAutocompleteAsync()
    {
        if (Version != _lastCompleteVersion || CaretPos != _lastCompletePos)
        {
            _lastCompleteVersion = Version;
            _lastCompletePos = CaretPos;
        }
        else
        {
            L.Debug("Skipping autocomplete request, position and version unchanged");
            return;
        }
        int versionBefore = Version;
        var p = new TextPosition(Editor.CaretLine, Editor.CaretCol - 1);
        var currentWord = "";
        if (p.Col < 0)
            p.Col = 0;
        else if (Editor[p] != '.')
            currentWord = Editor.GetCode(Editor.GetWordAt(p));

        if (Editor[p] == '(' || Editor[p] == ',')
        {
            L.Debug($"Requesting signature help at {p.Line},{p.Col}");
            var signatureHelps = await LspClient.RequestSignatureHelp(Identifier.uri, new Position(Editor.CaretPos));

            if (signatureHelps != null && signatureHelps.Count > 0)
            {
                var text = new StyledText();
                var signatureHelp = signatureHelps[0];
                text.AddWrapped(signatureHelp.label, 60, new Style());
                text.AddWrapped(signatureHelp.documentation.value, 60, new Style());
                _autocomplete = text;
                return;
            }
        }

        L.Debug($"Requesting completions at {p.Line},{p.Col} for current word '{currentWord}'");

        var completions = await LspClient.RequestCompletions(Identifier.uri, new Position(Editor.CaretPos));
        if (versionBefore != Version || completions == null || completions.Count == 0)
            return;

        completions.RemoveAll(item => !item.label.StartsWith(currentWord) || item.label.StartsWith("_"));
        if (completions.Count == 0)
            return;

        completions.Sort((a, b) => { return a.sortText.CompareTo(b.sortText); });

        string commonPrefix = null;

        L.Debug($"Received {completions.Count} completions from LSP");
        StyledText items = new StyledText();
        var colorMap = ColorTheme.Default.Colors;
        foreach (var item in completions)
        {
            if (commonPrefix == null)
                commonPrefix = item.label;
            else
            {
                int minLength = Math.Min(commonPrefix.Length, item.label.Length);
                int i = 0;
                for (; i < minLength; i++)
                    if (commonPrefix[i] != item.label[i])
                        break;
                commonPrefix = commonPrefix.Substring(0, i);
            }
            var sline = new StyledLine(item.label);
            sline.Add(new Token(0, item.label, colorMap[(int)item.kind]));
            items.Add(sline); //new StyledLine($"{item.label}, kind: {item.kind}, detail: {item.detail}"));
            if (items.Count >= 20)
            {
                items.Add(new StyledLine($"... and {completions.Count - items.Count} more"));
                break;
            }
        }
        L.Debug($"Common prefix: '{commonPrefix}'");
        if (items.Count > 0 && (items.Count > 1 || items[0].Text != currentWord))
        {
            _autocomplete = items;
            _autocompleteInsertText = commonPrefix != null && commonPrefix.Length > currentWord.Length ? commonPrefix.Substring(currentWord.Length) : null;
        }
    }

    public async void UpdateTokens()
    {
        var tokens = await LspClient.RequestSemanticTokens(Identifier.uri);
        // L.Debug($"Received {tokens.Count} semantic tokens from LSP");

        for (var i = 0; i < tokens.Count; i++)
        {
            var semToken = tokens[i];
            var line = semToken.Line;
            if (line < Lines.Count)
            {
                var token = Lines[line].GetTokenAt(semToken.Column);
                if (token != null)
                    token.Style.Color = semToken.Style.Color;
                else
                    L.Debug($"No token found at {line},{semToken.Column} to update");

            }
        }

        // var pos = _lastCaretPos;
        // var hover = await LspClient.SendRequestAsync("textDocument/hover",
        //         new
        //         {
        //             textDocument = new { uri = Identifier.uri },
        //             position = new { line = pos.Line, character = pos.Col }
        //         });
        // L.Debug($"Hover info at {pos.Line},{pos.Col}: {hover}");
        // var diag = await LspClient.RequestDiagnostics(Identifier);
        // L.Debug($"Received {diag} diagnostics from manual request");
    }

    public override void ReplaceLine(int index, string newLine)
    {
        IncrementVersion();
        _changes.Add(new TextDocumentContentChangeEvent
        {
            range = new Range
            {
                start = new Position { line = index, character = 0 },
                end = new Position { line = index, character = Lines[index].Text.Length }
            },
            text = newLine
        });
        Lines[index] = ParseLine(newLine);
    }

    public override void AppendLine(string line)
    {
        IncrementVersion();
        var iLine = Lines.Count > 0 ? Lines.Count - 1 : 0;
        var iChar = Lines.Count > 0 ? Lines[iLine].Text.Length : 0;
        _changes.Add(new TextDocumentContentChangeEvent
        {
            range = new Range
            {
                start = new Position { line = iLine, character = iChar },
                end = new Position { line = iLine, character = iChar }
            },
            text = Lines.Count > 0 ? "\n" + line : line,
        });
        Lines.Add(ParseLine(line));
    }

    public override void InsertLine(int index, string line)
    {
        IncrementVersion();
        var pos = new Position { line = index - 1, character = Lines[index - 1].Text.Length };
        _changes.Add(new TextDocumentContentChangeEvent
        {
            range = new Range { start = pos, end = pos },
            text = "\n" + line
        });
        Lines.Insert(index, ParseLine(line));
    }

    public override void RemoveLine(int index)
    {
        IncrementVersion();
        _changes.Add(new TextDocumentContentChangeEvent
        {
            range = new Range
            {
                start = new Position { line = index, character = 0 },
                end = new Position { line = index + 1, character = 0 }
            },
            text = ""
        });
        Lines.RemoveAt(index);
    }

    public override void ResetCode(string code)
    {
        IncrementVersion();
        _changes.Add(new TextDocumentContentChangeEvent { text = code });
        var lines = code.Split('\n');
        Lines.Clear();
        foreach (var line in lines)
            Lines.Add(ParseLine(line));
        OnCodeChanged();
    }
    // public void Dispose()
    // {
    // L.Debug("Disposing LSP Formatter and closing document");
    // LspClient.CloseDocument(Identifier.uri);
    // }
}


public class IC10LSPFormatter : LSPFormatter
{
    public StationeersIC10Editor.IC10.IC10CodeFormatter StaticFormatter;
    public IC10LSPFormatter() : base()
    {
        Identifier = new VersionedTextDocumentIdentifier { uri = null, version = 1 };
        var lspexe = Path.Combine(BepInEx.Paths.CachePath, "ic10lsp.exe");
        var startinfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = lspexe,
            Arguments = "",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        StaticFormatter = new StationeersIC10Editor.IC10.IC10CodeFormatter();
        LspClient = new LspClientStdio(startinfo);
        LspClient.OnDiagnostics += UpdateDiagnostics;
        LspClient.OnInitialized += () => { SubmitChanges(); };
        if (LspClient.IsInitialized)
            SubmitChanges();
        OnCodeChanged += () => { SubmitChanges(); };
    }

    public override void SubmitChanges()
    {
        if (Identifier.uri == null)
        {
            string filename = Editor.FileName + ".ic10";
            Identifier.uri = $"memory://{filename}";
        }
        base.SubmitChanges();
        // AsyncDiag().Forget();
    }

    public async UniTask AsyncDiag()
    {
        var diag = await LspClient.RequestDiagnostics(Identifier);
        L.Debug($"Received {diag} diagnostics from manual request");
    }

    public override StyledLine ParseLine(string line)
    {
        return StaticFormatter.ParseLine(line);
    }

    private TextPosition _lastHoverTextPos = new TextPosition(-1, -1);
    private StyledText _lastHoverInfo = null;
    private TextPosition _reqestedHoverPos = new TextPosition(-1, -1);

    public async UniTaskVoid GetHoverInfo(TextPosition pos)
    {
        if (pos.Line < 0 || pos.Col < 0)
            return;

        if (pos == _lastHoverTextPos || pos == _reqestedHoverPos)
            return;

        if(!_isOpen)
            return;

        _reqestedHoverPos = pos;

        await Task.Delay(300);

        if (_reqestedHoverPos != pos)
            return;

        _tooltip = null;

        var hover = await LspClient.SendRequestAsync("textDocument/hover",
            new
            {
                textDocument = new { uri = Identifier.uri },
                position = new { line = pos.Line, character = pos.Col }
            });

        if (hover == null || !hover.HasValues || hover["contents"] == null)
            return;

        var content = hover["contents"];
        StyledText tooltip = new StyledText();

        var addText = (string text) =>
        {
            if(text.Contains("**Interactive Actions:**"))
                return;
            foreach (var line in text.Split('\n'))
                tooltip.AddWrapped(line, 80, ICodeFormatter.DefaultStyle);
        };
        if (content.Type == Newtonsoft.Json.Linq.JTokenType.Array)
        {
            foreach (var item in content)
            {
                if (item.Type == Newtonsoft.Json.Linq.JTokenType.Object && item["value"] != null)
                {
                    addText(item["value"].ToString());
                }
                else if (item.Type == Newtonsoft.Json.Linq.JTokenType.String)
                {
                    addText(item.ToString());
                }
            }
        }
        else if (content.Type == Newtonsoft.Json.Linq.JTokenType.Object && content["value"] != null)
        {
            addText(content["value"].ToString());
        }
        else if (content.Type == Newtonsoft.Json.Linq.JTokenType.String)
        {
            addText(content.ToString());
        }

        if (tooltip.Count == 0)
            return;

        _tooltip = tooltip;
    }

    public override void UpdateTooltip(TextPosition mouseTextPos)
    {
        if (_lastHoverTextPos != mouseTextPos)
        {
            _tooltip = null;
            GetHoverInfo(mouseTextPos).Forget();
        }
    }

    public override void Dispose()
    {
        L.Debug("Disposing IC10 LSP Formatter and closing document");
        LspClient.CloseDocument(Identifier.uri);
        LspClient.Dispose();
        base.Dispose();
    }
}
