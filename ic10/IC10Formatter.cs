namespace StationeersIC10Editor.IC10;

using System;
using System.Collections.Generic;

using Assets.Scripts.Objects;

using ImGuiNET;

using UnityEngine;

public class DefinedIdentifier(string value)
{
    public int Count = 0;
    public string Value = value;
}

public class IC10CodeFormatter : StaticFormatter
{
    private Dictionary<string, DataType> types = new Dictionary<string, DataType>();
    private Dictionary<string, DefinedIdentifier> defines = [];
    private Dictionary<string, DefinedIdentifier> regAliases = [];
    private Dictionary<string, DefinedIdentifier> devAliases = [];
    private Dictionary<string, DefinedIdentifier> labels = [];
    private HashSet<string> _tokensToUpdate = new HashSet<string>();
    private bool _showRegisterUsage = false;

    public Editor _MinifyEditor = null;
    public Editor MinifyEditor
    {
        get
        {
            if (_MinifyEditor == null)
            {
                var tab = Editor.ParentTab;
                tab.ClearExtraEditors();
                _MinifyEditor = new Editor(Editor.KeyHandler);
                _MinifyEditor.IsReadOnly = true;
                tab.AddEditor(_MinifyEditor);
                _MinifyEditor.CodeFormatter = new IC10CodeFormatter();
                _MinifyEditor.CodeFormatter.Editor = _MinifyEditor;
            }
            return _MinifyEditor;
        }
    }

    public ICodeFormatter MinifyFormatter => MinifyEditor.CodeFormatter;

    public static double MatchingScore(string input)
    {
        // Simple heuristic: count occurrences of IC10-specific keywords
        double score = 0;
        var lines = input.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var firstWord = line.TrimStart().Split(' ')[0];
            if (firstWord.EndsWith(":") || IC10Utils.Instructions.ContainsKey(firstWord))
                score++;
            else if (firstWord.StartsWith("#"))
                score += 0.5f;
        }
        L.Debug($"IC10CodeFormatter MatchingScore: {score} for input with {lines.Length} lines = {(double)score / lines.Length}");
        return 1.0 * score / lines.Length;
    }

    public IC10CodeFormatter() : base(
           TokenSeparators: " \t",
           StringDelimiters: "\"",
           CommentPrefix: "#",
           KeepWhitespaces: false)
    {
        OnCodeChanged += () =>
        {
            UpdateDataType(null, defer: false);
            UpdateRegisterUsage();
            Minify();
        };
        OnCaretMoved += () => UpdateJumpTarget();
    }

    public string TrimToken(string token)
    {
        return token.TrimEnd(':');
    }

    public static uint GetColor(DataType type, string text)
    {
        if (type == DataType.Color)
            return text == "Color.White" ? ColorFromHTML("black") : ColorFromHTML("white");
        switch (type)
        {
            case DataType.Number:
                return ColorNumber;
            case DataType.Device:
                return ColorDevice;
            case DataType.Register:
                return ColorRegister;
            case DataType.LogicType:
            case DataType.LogicSlotType:
            case DataType.BatchMode:
                return ColorLogicType;
            case DataType.Instruction:
            case DataType.Define:
            case DataType.Alias:
                return ColorInstruction;
            case DataType.Label:
                return ColorLabel;
            case DataType.Comment:
                return ColorComment;
            case DataType.Unknown:
                return ColorError;
            case DataType.BasicEnum:
                return ColorBasicEnum;
            default:
                return ColorDefault;
        }
    }

    public static uint Darken(uint color, float factor)
    {
        if (color == 0xffffffff)
            return color;

        uint a = (color >> 24) & 0xFF;
        uint r = (color >> 16) & 0xFF;
        uint g = (color >> 8) & 0xFF;
        uint b = color & 0xFF;

        var mix = (uint c) =>
        {
            return (uint)(c * factor);
        };

        r = mix(r);
        g = mix(g);
        b = mix(b);

        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    public static uint GetBackgroundColor(DataType type, string text)
    {
        if (type != DataType.Color)
            return 0;
        return IC10Utils.Colors.TryGetValue(text, out uint color) ? Darken(color, 0.7f) : 0;
    }

    public static Style GetStyle(DataType type, string text)
    {
        return new Style(GetColor(type, text), GetBackgroundColor(type, text));
    }

    public static uint ColorInstruction = ColorFromHTML("#ffff00");
    public static uint ColorDevice = ColorFromHTML("#00ff00");
    public static uint ColorLogicType = ColorFromHTML("#ff8000");
    public static uint ColorRegister = ColorFromHTML("#0080ff");
    public static uint ColorBasicEnum = ColorNumber;
    public static uint ColorDefine = ColorNumber;
    public static uint ColorAlias = ColorFromHTML("#4d4dcc");
    public static uint ColorLabel = ColorFromHTML("#A128C1");

    public static int FindNextWhitespace(string text, int startIndex)
    {
        bool haveQuote = false;
        while (startIndex < text.Length && (!char.IsWhiteSpace(text[startIndex]) || haveQuote))
        {
            if (text[startIndex] == '\"')
                haveQuote = !haveQuote;
            startIndex++;
        }
        return startIndex;
    }

    public static int FindNextNonWhitespace(string text, int startIndex)
    {
        while (startIndex < text.Length && char.IsWhiteSpace(text[startIndex]))
            startIndex++;
        return startIndex;
    }

    public override StyledLine ParseLine(string text)
    {
        var line = TParseLine<IC10Line>(text);

        IdentifyTypesAndAddTokens(line);
        return line;
    }

    public void IdentifyTypesAndAddTokens(IC10Line line)
    {
        if (line.Count == 0)
            return;

        var isInstructionLine = false;

        for (int i = 0; i < line.NumCodeTokens; i++)
        {
            var t = line[i];
            t.Tooltip = null;
            t.Error = null;
            string txt = t.Text;
            ArgType dt = DataType.Unknown;
            string error = null;

            if (IC10Utils.IsBuiltin(txt))
            {
                dt = IC10Utils.Types[txt];
                if (IC10Utils.IsHashExpression(txt))
                {
                    // dt = DataType.Number;
                    t.Tooltip =
                    [
                        StyledLine.FromString("Ctrl+Click to open Stationpedia", ColorFromHTML("#A0A0A0")),
                    ];
                }
            }
            else if (txt.EndsWith(":"))
                dt = DataType.Label;
            else if (types.TryGetValue(txt, out DataType type))
                dt = type;
            else if (IC10Utils.TryParseNumber(txt, out _))
            {
                dt = DataType.Number;
                if (!txt.StartsWith("HASH"))
                {
                    string prefabName = IC10Utils.GetLogicablePrefabName(txt);
                    if (prefabName != null)
                    {
                        t.Tooltip =
                        [
                            StyledLine.FromString(prefabName, ColorFromHTML("#00ff00")),
                            StyledLine.FromString(""),
                            StyledLine.FromString("Ctrl+Click to open Stationpedia", ColorFromHTML("#A0A0A0")),
                        ];
                    }
                }
            }
            else if (
                IC10Utils.IsHashExpression(txt) ||
                IC10Utils.IsStringExpression(txt)
            )
                dt = DataType.Number;
            else if (IsDeviceNetwork(txt))
                dt = DataType.Device;
            else
            {
                dt = DataType.Unknown;
                error = "Unknown identifier";
            }

            if (dt.Has(DataType.Instruction))
                t.Tooltip = IC10Utils.Instructions[txt].Tooltip;

            if (i == 0)
            {
                ArgType validFirstType = DataType.Instruction | DataType.Label | DataType.Alias | DataType.Define;
                isInstructionLine = dt.Has(DataType.Instruction);
                if (!validFirstType.Has(dt))
                    error = $"Unknown instruction '{txt}'";
            }

            else if (isInstructionLine)
            {
                var opcode = IC10Utils.Instructions[line[0].Text];
                int argIndex = i - 1;
                if (argIndex < opcode.ArgumentTypes.Count)
                {
                    var expected = opcode.ArgumentTypes[argIndex];
                    var compat = expected.Compat;

                    if (!compat.Has(dt))
                    {
                        error =
                            $"Invalid argument type {dt.Description}, expected {expected.Description}";
                        dt = DataType.Unknown;
                    }
                    else if (line.IsBatchInstruction && argIndex == line.DeviceHashArgumentIndex)
                    {
                        string h = line[i].Text;
                        if (IC10Utils.GetLogicablePrefabName(h) == null)
                        {
                            error = $"Invalid device hash {h}";
                            dt = DataType.Unknown;
                        }
                    }

                    dt = compat.CommonType(dt);
                }
                else
                {
                    error = "Too many arguments";
                }
            }

            var concreteType = dt.ToDataType();
            t.Type = (uint)concreteType;
            t.Style = new Style(error != null ? ColorError : GetColor(concreteType, txt),
             GetBackgroundColor(concreteType, txt));
            if (error != null)
                t.Error = StyledText.ErrorText(error);

            // line.Add(t);
        }

        UpdateTokens(line);
    }

    public void SetDefinedValueTooltip(Token token)
    {
        var name = token.Text;
        defines.TryGetValue(name, out var identifier);
        if (identifier == null) regAliases.TryGetValue(name, out identifier);
        if (identifier == null) devAliases.TryGetValue(name, out identifier);
        if (identifier != null)
        {
            token.Tooltip =
            [
                StyledLine.FromString($"{identifier.Value}", GetColor((DataType)token.Type, name))
            ];
        }
    }

    public void UpdateTokens(StyledLine line, HashSet<string> changedNames = null)
    {
        for (int i = 0; i < line.Count; i++)
        {
            var t = line[i];
            string text = t.Text;
            bool isUnknown = false;

            // Re-resolve type if it was an identifier
            if (types.TryGetValue(text, out DataType newType))
            {
                isUnknown = newType == DataType.Unknown;
                if (!isUnknown)
                {
                    t.Error = null;
                    t.Type = (uint)newType;
                    t.Style = new Style(GetColor(newType, text), GetBackgroundColor(newType, text));
                    SetDefinedValueTooltip(t);
                }
            }
            else if (changedNames != null && changedNames.Contains(text))
                isUnknown = true;

            if (isUnknown)
            {
                t.Error = StyledText.ErrorText("Undefined identifier");
                t.Type = (uint)DataType.Unknown;
                t.Style = new Style(GetColor(DataType.Unknown, text), GetBackgroundColor(DataType.Unknown, text));
                t.Tooltip = null;
            }
        }
    }

    public bool IsDeviceNetwork(string text)
    {
        if (!text.Contains(":"))
            return false;

        var parts = text.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        if (!Int32.TryParse(parts[1], out int networkId) || networkId < 0)
            return false;

        var type = IC10Utils.GetType(parts[0]);
        if (type.Has(DataType.Device) || type.Has(DataType.Alias) || type.Has(DataType.Register))
            return true;

        if (devAliases.ContainsKey(parts[0]))
            return true;

        if (regAliases.ContainsKey(parts[0]))
            return true;

        return false;
    }

    public void UpdateJumpTarget()
    {
        var style = new Style(ColorLabel);

        foreach (var i in new List<int>(LineStyles.Keys))
        {
            var lineStyle = LineStyles[i];
            if (lineStyle.Equals(style))
                LineStyles.Remove(i);
            else if (lineStyle.Color == style.Color)
                LineStyles[i] = new Style { Background = lineStyle.Background };
        }

        if (Editor.CaretPos.Line < 0 || Editor.CaretPos.Line >= Lines.Count)
            return;
        var line = Lines[Editor.CaretPos.Line] as IC10Line;
        if (line == null || !line.IsInstruction || line.NumCodeTokens < 2 || !line.IsJump)
            return;

        bool isRelative = line.IsRelativeJump;

        var operand = line[line.NumCodeTokens - 1];

        int targetLine = -1;
        if (int.TryParse(operand.Text, out int lineNum))
            targetLine = lineNum;
        else if (!operand.IsError && labels.ContainsKey(TrimToken(operand.Text)))
        {
            // find line with label
            string label = TrimToken(operand.Text);
            for (int i = 0; i < Lines.Count; i++)
            {
                var l = Lines[i] as IC10Line;
                if (l != null && l.IsLabel && TrimToken(l[0].Text) == label)
                {
                    targetLine = i;
                    break;
                }
            }
        }
        else return;

        if (isRelative)
            targetLine += Editor.CaretPos.Line;

        if (LineStyles.ContainsKey(targetLine))
            LineStyles[targetLine] = new Style(ColorLabel, LineStyles[targetLine].Background);
        else
            LineStyles[targetLine] = style;
    }

    public void UpdateDataType(string newToken, bool defer = true)
    {
        if (newToken != null)
        {
            // L.Debug($"UpdateDataType: scheduling update for token {newToken}, defer={defer}");
            _tokensToUpdate.Add(newToken);
        }
        if (defer || _tokensToUpdate.Count == 0)
            return;

        bool needsUpdate = false;
        bool needsFullUpdate = false;

        foreach (var token in _tokensToUpdate)
        {
            int count = 0;
            DataType type = DataType.Unknown;
            if (defines.ContainsKey(token))
            {
                count += defines[token].Count;
                type = DataType.Number;
            }
            if (devAliases.ContainsKey(token))
            {
                count++;
                type = DataType.Device;
            }
            if (regAliases.ContainsKey(token))
            {
                count++;
                type = DataType.Register;
            }
            if (labels.ContainsKey(TrimToken(token)))
            {
                count += labels[token].Count;
                type = DataType.Label;
            }
            if (IC10Utils.Instructions.ContainsKey(token))
            {
                count++;
                type = DataType.Instruction;
            }

            if (count > 1)
                type = DataType.Unknown;

            needsUpdate |= !types.ContainsKey(token) || types[token] != type;
            needsFullUpdate |= count == 0 && IC10Utils.IsBuiltin(token);

            if (count > 0)
                types[token] = type;
        }

        // In an efficient implementation, we would re-parse only affected lines here
        if (needsFullUpdate)
        {
            L.Debug("UpdateDataType: performing full update of all lines");
            foreach (IC10Line line in Lines)
                IdentifyTypesAndAddTokens(line);
        }
        else if (needsUpdate)
            foreach (IC10Line line in Lines)
                UpdateTokens(line, _tokensToUpdate);

        _tokensToUpdate.Clear();
    }

    public override void ResetCode(string code)
    {
        types.Clear();
        defines.Clear();
        regAliases.Clear();
        devAliases.Clear();
        labels.Clear();
        base.ResetCode(code);
    }

    public override void InsertLine(int index, string line)
    {
        base.InsertLine(index, line);
        TrackAliases(Lines[index] as IC10Line, true);
    }

    public override void AppendLine(string line)
    {
        base.AppendLine(line);
        TrackAliases(Lines[Lines.Count - 1] as IC10Line, true);
    }

    public override void RemoveLine(int index)
    {
        if (index < 0 || index >= Lines.Count)
            return;
        string lineText = Lines[index].Text;
        var line = Lines[index] as IC10Line;
        base.RemoveLine(index);
        TrackAliases(line, false);
    }

    private void TrackAliases(IC10Line line, bool add)
    {
        if (line.NumCodeTokens == 0)
            return;

        if (line.IsLabel)
            UpdateDict(labels, TrimToken(line[0].Text), DataType.Label, add, TrimToken(line[0].Text));
        else if (line.IsNumAlias)
            UpdateDict(regAliases, line[1].Text, DataType.Number, add, TrimToken(line[2].Text));
        else if (line.IsDevAlias)
            UpdateDict(devAliases, line[1].Text, DataType.Device, add, TrimToken(line[2].Text));
        else if (line.IsDefine)
            UpdateDict(defines, line[1].Text, DataType.Number, add, TrimToken(line[2].Text));
    }

    private void UpdateDict(Dictionary<string, DefinedIdentifier> dict, string key, DataType type, bool add, string value)
    {
        // L.Debug($"UpdateDict: {(add ? "adding" : "removing")} key {key} of type {type}");
        if (add)
        {
            if (!dict.ContainsKey(key))
                dict[key] = new DefinedIdentifier(value);
            else
                dict[key].Value = value;

            dict[key].Count++;
        }
        else
        {
            if (!dict.ContainsKey(key))
            {
                L.Warning($"RemoveDictEntry: trying to remove non-existing key {key} from dictionary");
                return;
            }
            L.Debug($"RemoveDictEntry: removing key {key} from dictionary {dict[key]}");
            dict[key].Count--;
            if (dict[key].Count == 0)
            {
                dict.Remove(key);
                types.Remove(key);
            }
        }

        UpdateDataType(key);
    }

    public override void UpdateAutocomplete()
    {
        _autocomplete = null;
        _autocompleteInsertText = null;

        var caret = Editor.CaretPos;

        if (char.IsWhiteSpace(Editor[caret]))
            caret.Col--;

        var token = Lines.GetTokenAtPosition(caret);
        if (token == null)
            return;

        var line = Lines[caret.Line] as IC10Line;

        if (line.NumCodeTokens == 0)
            return;

        var index = line.IndexOf(token);
        if (index > 0 && !line.IsInstruction && line[0].Text != "define")
            return;

        ArgType argType = DataType.Instruction;
        if (index == 0)
            argType.Add(DataType.Define, DataType.Alias);

        var text = line[index].Text;

        if (index > 0)
        {
            var opcode = IC10Utils.Instructions[line[0].Text];
            if (index - 1 >= opcode.ArgumentTypes.Count)
                return;
            argType = opcode.ArgumentTypes[index - 1].Compat;
        }

        var suggestionsSet = new HashSet<string>();

        foreach (var entry in IC10Utils.Types)
            if (!entry.Key.StartsWith("rr") && !entry.Key.StartsWith("dr"))
                if (argType.Has(entry.Value) && entry.Key.StartsWith(text))
                    suggestionsSet.Add(entry.Key);

        foreach (var entry in types)
            if (argType.Has(entry.Value) && entry.Key.StartsWith(text))
                suggestionsSet.Add(entry.Key);

        var n = suggestionsSet.Count;
        L.Debug($"Found {n} autocomplete suggestions for token '{text}' of type {argType}");
        if (n == 0)
            return;

        var suggestions = new List<string>();
        foreach (var s in suggestionsSet)
        {
            L.Debug($"Adding suggestion: {s}");
            suggestions.Add(s);
        }

        if (n == 1 && suggestions[0] == text)
            return;

        string commonPrefix = null;

        _autocomplete = new StyledText();
        for (var iLine = 0; iLine < suggestions.Count; iLine++)
        {
            var suggestion = suggestions[iLine];
            var type = types.ContainsKey(suggestion) ? types[suggestion] : DataType.Unknown;
            if (type == DataType.Unknown && IC10Utils.Types.ContainsKey(suggestion))
                type = IC10Utils.Types[suggestion].ToDataType();
            var tok = new Token(0, suggestion, GetStyle(type, suggestion), (uint)type);
            var l = new StyledLine(suggestion);
            l.Add(tok);
            _autocomplete.Add(l);

            var rest = suggestion.Substring(text.Length);

            if (commonPrefix == null)
                commonPrefix = rest;
            else
            {
                if (commonPrefix.Length == 0)
                    continue;

                int len = Math.Min(commonPrefix.Length, rest.Length);
                int i = 0;
                for (; i < len; i++)
                    if (commonPrefix[i] != rest[i])
                        break;

                commonPrefix = commonPrefix.Substring(0, i);
            }
        }

        if (n == 1)
            commonPrefix += " ";

        if (commonPrefix.Length > 0)
            _autocompleteInsertText = commonPrefix;

        if (_autocomplete.Count > 15)
        {
            var trimmed = new StyledText();
            for (int i = 0; i < 15; i++)
                trimmed.Add(_autocomplete[i]);
            var moreLine = new StyledLine($"... and {_autocomplete.Count - 15} more");
            moreLine.Add(new Token(0, $"... and {_autocomplete.Count - 15} more", ColorFromHTML("#888888")));
            trimmed.Add(moreLine);
            _autocomplete = trimmed;
        }
    }

    private int[] _registerUsage = new int[18];
    private int[] _registerUsageAlias = new int[18];

    private void UpdateRegisterUsage()
    {
        for (int i = 0; i < 18; i++)
        {
            _registerUsage[i] = 0;
            _registerUsageAlias[i] = 0;
        }

        foreach (IC10Line line in Lines)
            foreach (var token in line)
                if (IC10Utils.Registers.Contains(token.Text))
                {
                    var reg = token.Text;
                    while (reg.StartsWith("rr") && reg.Length > 2)
                        reg = reg.Substring(1);
                    var regNum = -1;
                    if (reg == "sp")
                        regNum = 16;
                    else if (reg == "ra")
                        regNum = 17;
                    else if (int.TryParse(reg.Substring(1), out int parsed))
                        regNum = parsed;
                    else L.Warning($"Failed to parse register number: {reg}");
                    if (regNum != -1)
                    {
                        if (regNum >= 0 && regNum < 18)
                        {
                            if (line.IsAlias)
                                _registerUsageAlias[regNum]++;
                            else
                                _registerUsage[regNum]++;
                        }
                        else L.Warning($"Register number out of range: {reg}");
                    }
                }
    }

    public override void DrawStatus(Vector2 pos)
    {
        base.DrawStatus(pos);
        DrawRegisterUsage();
        if (!Settings.MinifyEnabled && Editor.ParentTab.Editors.Count > 1)
        {
            Editor.ParentTab.ClearExtraEditors();
            _MinifyEditor = null;
            UpdateCodeSize();
        }
        if (!Editor.IsReadOnly && Settings.MinifyEnabled && Editor.ParentTab.Editors.Count == 1)
            Minify();
    }

    private bool IsMinifyActive => Settings.MinifyEnabled && _MinifyEditor != null;

    public void Minify()
    {
        if (Editor == null || Editor.ParentTab == null || Editor.IsReadOnly) return;
        if (!Settings.MinifyEnabled) return;
        MinifyEditor.ResetCode(IC10Utils.Minify(Lines));
        CodeSize = MinifyFormatter.CodeSize;
    }

    const string _minifyTooltip = "Show minified version of the code, which will be used on 'Export'.\n\nAppend '# KEEP' to define/alias/labels lines to keep them.";

    public override void DrawButtons()
    {
        bool minify = Settings.MinifyEnabled;
        if (ImGuiUtils.Checkbox("Minify", ref minify, _minifyTooltip))
        {
            IC10EditorPlugin.Minify.Value = minify;
            if (minify)
                Minify();
            else
            {
                Editor.ParentTab.ClearExtraEditors();
                _MinifyEditor = null;
            }
            UpdateCodeSize();
        }
        ImGui.SameLine();
        ImGuiUtils.Checkbox("Registers", ref _showRegisterUsage, "Show register usage");
    }


    public void DrawRegisterUsage()
    {
        if (!_showRegisterUsage) return;
        var drawList = ImGui.GetWindowDrawList();

        var startPos = ImGui.GetCursorScreenPos();

        var width = Settings.CharWidth * 7.0f;
        var height = (18 + 4 * 0.5f) * Settings.LineHeightWithSpacing;

        startPos.x = ImGui.GetWindowPos().x + ImGui.GetWindowWidth() - width - ImGui.GetStyle().FramePadding.x - ImGui.GetStyle().ItemSpacing.x * 5;
        startPos.y -= 2.0f * Settings.buttonSize.y + height;

        var colorUsed4 = new Vector4(1.0f, 1.0f, 0.0f, 1.0f);
        var colorFree4 = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
        var colorWarn4 = new Vector4(1.0f, 0.5f, 0.0f, 1.0f);
        var colorUsed = ImGui.GetColorU32(colorUsed4);
        var colorFree = ImGui.GetColorU32(colorFree4);
        var colorWarn = ImGui.GetColorU32(colorWarn4);


        var mousePos = ImGui.GetMousePos();
        if (Settings.ShowTooltip && mousePos.x >= startPos.x && mousePos.x <= startPos.x + width && mousePos.y >= startPos.y && mousePos.y <= startPos.y + height)
        {
            ImGui.BeginTooltip();
            ImGui.Text($"Register Usage (direct and as alias)");
            ImGui.Text($"Colors:");
            ImGui.TextColored(colorFree4, "  Free");
            ImGui.TextColored(colorUsed4, "  Used: direct or alias");
            ImGui.TextColored(colorWarn4, "  Warn: direct + alias or multiple aliases");
            ImGui.EndTooltip();
        }

        for (var i = 0; i < 18; i++)
        {
            var numAlias = _registerUsageAlias[i];
            var numUse = _registerUsage[i];
            var color = numUse + numAlias > 0 ? colorUsed : colorFree;
            if (numAlias > 1 || numAlias > 0 && numUse > 0)
                color = colorWarn;

            var shift = new Vector2(Settings.CharWidth, 0);
            var strNumUse = numUse > 0 ? $"{numUse}".PadLeft(2) : " .";
            var strNumAlias = numAlias > 0 ? $"{numAlias}".PadLeft(2) : " .";
            var strReg = $"r{i}";
            if (i == 16) strReg = "sp";
            if (i == 17) strReg = "ra";
            drawList.AddText(startPos, color, strReg);
            drawList.AddText(startPos + 3 * shift, color, strNumUse);
            drawList.AddText(startPos + 5 * shift, color, strNumAlias);
            startPos.y += Settings.LineHeightWithSpacing * (i % 4 == 3 ? 1.5f : 1);
        }
    }

    public override void UpdateCodeSize()
    {
        if (IsMinifyActive)
            CodeSize = MinifyEditor.CodeFormatter.CodeSize;
        else
            base.UpdateCodeSize();
    }

    public override string Compile()
    {
        if (Settings.MinifyEnabled)
            return IC10Utils.Minify(Lines);
        return base.Compile();
    }
}
