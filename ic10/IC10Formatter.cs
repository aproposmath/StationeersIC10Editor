namespace StationeersIC10Editor.IC10;

// todo: proper tooltips(check column)
//     update on line called too often(and throws away data if there are no semantic tokens)
//     -> relevant in tooltips

using System;
using System.Collections.Generic;

using Assets.Scripts.Objects;

using ImGuiNET;

using UnityEngine;

public class IC10CodeFormatter : StaticFormatter
{
    private Dictionary<string, DataType> types = new Dictionary<string, DataType>();
    private Dictionary<string, int> defines = new Dictionary<string, int>();
    private Dictionary<string, int> regAliases = new Dictionary<string, int>();
    private Dictionary<string, int> devAliases = new Dictionary<string, int>();
    private Dictionary<string, int> labels = new Dictionary<string, int>();
    private HashSet<string> _tokensToUpdate = new HashSet<string>();

    public static double MatchingScore(string input)
    {
        // Simple heuristic: count occurrences of IC10-specific keywords
        int score = 0;
        var lines = input.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var firstWord = line.TrimStart().Split(' ')[0];
            if (firstWord.EndsWith(":") || IC10Utils.Instructions.ContainsKey(firstWord))
                score++;
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

        var mix = (uint c) => {
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
    public static uint ColorBasicEnum = ColorFromHTML("#20b2aa");
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
        line.UpdateTokenColors(types);
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
                dt = IC10Utils.Types[txt];
            else if (txt.EndsWith(":"))
                dt = DataType.Label;
            else if (types.TryGetValue(txt, out DataType type))
                dt = type;
            else if (double.TryParse(txt, out _))
            {
                dt = DataType.Number;
                if (int.TryParse(txt, out int hash))
                {
                    var thing = Prefab.Find<Thing>(hash);
                    if (thing != null)
                    {
                        var tooltip = new StyledText();
                        var ttLine = new StyledLine(thing.PrefabName);
                        ttLine.Add(new Token(0, thing.PrefabName, ColorFromHTML("#00ff00")));
                        tooltip.Add(ttLine);
                        t.Tooltip = tooltip;
                    }
                }
            }
            else if (txt.StartsWith("$"))
                dt = DataType.Number;
            else if (
                IC10Line.IsHashExpression(txt)
                || IC10Line.IsStringExpression(txt)
                || IC10Line.IsBinaryExpression(txt)
            )
                dt = DataType.Number;
            else if (IC10Line.IsDeviceChannel(txt))
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

        line.UpdateTokenColors(types);
    }

    public void UpdateJumpTarget()
    {
        LineStyles.Clear();
        var line = Lines[Editor.CaretPos.Line] as IC10Line;
        if (line == null || !line.IsInstruction || line.NumCodeTokens < 2)
            return;

        var instruction = line[0].Text;
        if (!instruction.StartsWith("j") && !instruction.StartsWith("b"))
            return;

        bool isRelative = instruction.StartsWith("br") || instruction == "jr";

        var operand = line[1];

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

        LineStyles[targetLine] = new Style(ColorLabel);
    }

    public void UpdateDataType(string newToken, bool defer = true)
    {
        if (newToken != null)
        {
            L.Debug($"UpdateDataType: scheduling update for token {newToken}, defer={defer}");
            _tokensToUpdate.Add(newToken);
        }
        if (defer || _tokensToUpdate.Count == 0)
            return;

        bool needsUpdate = false;

        foreach (var token in _tokensToUpdate)
        {
            int count = 0;
            DataType type = DataType.Unknown;
            if (defines.ContainsKey(token))
            {
                count += defines[token];
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
                count += labels[token];
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
            types[token] = type;
        }

        // In an efficient implementation, we would re-parse only affected lines here
        if (needsUpdate)
            foreach (IC10Line line in Lines)
                line.UpdateTokenColors(types, _tokensToUpdate);

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
            UpdateDict(labels, TrimToken(line[0].Text), DataType.Label, add);
        else if (line.IsNumAlias)
            UpdateDict(regAliases, line[1].Text, DataType.Number, add);
        else if (line.IsDevAlias)
            UpdateDict(devAliases, line[1].Text, DataType.Device, add);
        else if (line.IsDefine)
            UpdateDict(defines, line[1].Text, DataType.Number, add);
    }

    private void UpdateDict(Dictionary<string, int> dict, string key, DataType type, bool add)
    {
        L.Debug($"UpdateDict: {(add ? "adding" : "removing")} key {key} of type {type}");
        if (add)
        {
            if (!dict.ContainsKey(key))
                dict[key] = 0;

            dict[key]++;
        }
        else
        {
            if (!dict.ContainsKey(key))
            {
                L.Warning($"RemoveDictEntry: trying to remove non-existing key {key} from dictionary");
                return;
            }
            L.Debug($"RemoveDictEntry: removing key {key} from dictionary {dict[key]}");
            dict[key]--;
            if (dict[key] == 0)
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
        var index = line.IndexOf(token);
        if (index > 0 && !line.IsInstruction)
            return;

        IC10.ArgType argType = DataType.Instruction;
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

    private bool[] _registerUsage = new bool[16];

    private void UpdateRegisterUsage()
    {
        for (int i = 0; i < 16; i++)
            _registerUsage[i] = false;

        foreach (var line in Lines)
            foreach (var token in line)
                if (IC10Utils.Registers.Contains(token.Text) && token.Text.StartsWith("r") && token.Text != "ra")
                {
                    var reg = token.Text;
                    while (reg.StartsWith("rr") && reg.Length > 2)
                        reg = reg.Substring(1);
                    if (int.TryParse(reg.Substring(1), out int regNum))
                    {
                        if (regNum >= 0 && regNum < 16)
                            _registerUsage[regNum] = true;
                        else L.Warning($"Register number out of range: {reg}");
                    }
                    else L.Warning($"Failed to parse register number: {reg}");
                    _registerUsage[int.Parse(reg.Substring(1))] = true;
                }
    }

    public override void DrawStatus(Vector2 pos)
    {
        base.DrawStatus(pos);
        DrawRegisterUsage();
    }


    public void DrawRegisterUsage()
    {

        var drawList = ImGui.GetWindowDrawList();

        Vector2 startPos = ImGui.GetCursorScreenPos();
        Vector2 windowSize = ImGui.GetWindowSize();

        Vector2 rectSize = new Vector2(9, 9) * Settings.Scale;
        float spacing = 4.0f * Settings.Scale;

        startPos.x = ImGui.GetWindowPos().x + ImGui.GetWindowWidth() - 3 * Settings.buttonSize.x - ImGui.GetStyle().FramePadding.x * 3 - ImGui.GetStyle().ItemSpacing.x;
        startPos.y += 8.0f;

        uint colorUsed = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 0.0f, 1.0f));
        uint colorFree = ImGui.GetColorU32(new Vector4(0.0f, 1.0f, 0.0f, 1.0f));

        float x0 = startPos.x;

        for (int i = 0; i < 16; i++)
        {
            uint color = _registerUsage[i] ? colorUsed : colorFree;
            int xShift = i + i / 4; // add extra space every 4 registers
            startPos.x = x0 + xShift * (rectSize.x + spacing);
            drawList.AddCircleFilled(startPos + rectSize / 2, rectSize.x / 2, color, 12);
        }
    }
}
