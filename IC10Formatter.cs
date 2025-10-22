namespace StationeersIC10Editor
{
    using System;
    using UI.Tooltips;
    using System.Text.RegularExpressions;
    using Assets.Scripts.Util;
    using System.Text;
    using System.Collections.Generic;
    using Assets.Scripts;
    using Assets.Scripts.Objects.Electrical;
    using Assets.Scripts.Objects.Motherboards;
    using Assets.Scripts.UI;
    using ImGuiNET;
    using UnityEngine;

    public abstract class ICodeFormatter
    {
        public static uint ColorError = ColorFromHTML("#ff0000");
        public static uint ColorComment = ColorFromHTML("#808080");
        public static uint ColorLineNumber = ColorFromHTML("#808080");
        public static uint ColorDefault = ColorFromHTML("#ffffff");
        public static uint ColorSelection = ColorFromHTML("#3388ff80");
        public static uint ColorNumber = ColorFromHTML("#20b2aa");

        public const int LineNumberOffset = 5;

        public abstract void ResetCode(string code);
        public abstract void RemoveLine(string line);
        public abstract void AddLine(string line);
        public abstract uint GetColor(string token);
        public abstract void DrawTooltip(string line, TextPosition caret, Vector2 pos);

        public static uint ColorFromHTML(string htmlColor)
        {
            if (string.IsNullOrWhiteSpace(htmlColor))
            {
                L.Warning("ColorFromHTML: empty color string");
                return ColorDefault;
            }
            if (htmlColor.StartsWith("0x"))
                htmlColor = htmlColor.Substring(2);
            if (!htmlColor.StartsWith("#"))
            {
                if (htmlColor == "blue")
                    return 0xFF0000FF;
                if (htmlColor == "red")
                    return 0xFFFF0000;
                if (htmlColor == "green")
                    return 0xFF00FF00;
                if (htmlColor == "white")
                    return 0xFFFFFFFF;
                if (htmlColor == "black")
                    return 0xFF000000;
                if (htmlColor == "yellow")
                    return 0xFFFFFF00;
                if (htmlColor == "orange")
                    return 0xFFFFA500;
                if (htmlColor == "purple")
                    return 0xFF800080;
                if (htmlColor == "gray" || htmlColor == "grey")
                    return 0xFF808080;

                L.Warning($"ColorFromHTML - unknown color: {htmlColor}");
                return ColorDefault;
            }
            htmlColor = htmlColor.TrimStart('#');
            uint rgb = uint.Parse(htmlColor, System.Globalization.NumberStyles.HexNumber);
            byte a = 0xFF;

            if (rgb > 0xFFFFFF)
            {
                a = (byte)(rgb & 0xFF);
                rgb = rgb >> 8;
            }

            byte r = (byte)((rgb >> 16) & 0xFF);
            byte g = (byte)((rgb >> 8) & 0xFF);
            byte b = (byte)(rgb & 0xFF);

            return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
        }

        public void ReplaceLine(string oldLine, string newLine)
        {
            RemoveLine(oldLine);
            AddLine(newLine);
        }

        public string TrimToken(string token)
        {
            return token.TrimEnd(':');
        }

        public static List<string> Tokenize(string text, bool keepWhitespace = false)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(text))
                return tokens;

            int i = 0;
            while (i < text.Length)
            {
                int start = i;
                bool isWhitespace = char.IsWhiteSpace(text[i]);
                i++;
                while (i < text.Length && char.IsWhiteSpace(text[i]) == isWhitespace)
                    i++;

                if (keepWhitespace || !isWhitespace)
                    tokens.Add(text.Substring(start, i - start));
            }

            return tokens;
        }

        public void DrawLine(int lineIndex, string line, TextRange selection = default)
        {
            float charWidth = ImGui.CalcTextSize("M").x;
            var codeComment = line.Split('#');
            string code = codeComment[0];
            var tokens = Tokenize(code, true);
            Vector2 pos = ImGui.GetCursorScreenPos();
            ImGui
                .GetWindowDrawList()
                .AddText(pos, ColorLineNumber, lineIndex.ToString().PadLeft(3) + ".");
            pos.x += LineNumberOffset * charWidth;

            int selectionMin = -1,
                selectionMax = -1;

            if (selection)
            {
                float lineHeight = ImGui.GetTextLineHeightWithSpacing();
                if (selection.Start.Line <= lineIndex && selection.End.Line >= lineIndex)
                {
                    selectionMin = lineIndex == selection.Start.Line ? selection.Start.Col : 0;
                    selectionMax =
                        lineIndex == selection.End.Line ? selection.End.Col : line.Length;

                    selectionMin = Mathf.Clamp(selectionMin, 0, line.Length);
                    selectionMax = Mathf.Clamp(selectionMax, 0, line.Length);

                    Vector2 selStart = new Vector2(pos.x + (charWidth * selectionMin), pos.y);
                    Vector2 selEnd = new Vector2(
                        pos.x + (charWidth * selectionMax),
                        pos.y + lineHeight);

                    ImGui
                        .GetWindowDrawList()
                        .AddRectFilled(selStart, selEnd, ICodeFormatter.ColorSelection);
                }
            }

            foreach (var token in tokens)
            {
                if (!string.IsNullOrWhiteSpace(token))
                    ImGui.GetWindowDrawList().AddText(pos, GetColor(token), token);

                pos.x += charWidth * token.Length;
            }

            if (codeComment.Length > 1)
            {
                string token = "#" + codeComment[1];
                ImGui.GetWindowDrawList().AddText(pos, ColorComment, token);
            }
        }
    }

    public class IC10CodeFormatter : ICodeFormatter
    {
        public static uint ColorInstruction = ColorFromHTML("#ffff00");

        // todo: recognize data type of tokens
        public static uint ColorDevice = ColorFromHTML("#00ff00");
        public static uint ColorLogicType = ColorFromHTML("#ff8000");
        public static uint ColorRegister = ColorFromHTML("#0080ff");

        public static uint ColorDefine = ColorNumber;
        public static uint ColorAlias = ColorFromHTML("#4d4dcc");
        public static uint ColorLabel = ColorFromHTML("#800080");

        private HashSet<string> errors = new HashSet<string>(); // tokens that are marked as errors (used as alias and define for instance)

        private Dictionary<string, int> defines = new Dictionary<string, int>();
        private Dictionary<string, int> aliases = new Dictionary<string, int>();
        private Dictionary<string, int> labels = new Dictionary<string, int>();
        private Dictionary<string, ScriptCommand> instructions = new Dictionary<string, ScriptCommand>();
        private HashSet<string> logicTypes = new HashSet<string>();
        private HashSet<string> registers = new HashSet<string>();
        private HashSet<string> devices = new HashSet<string>();

        public IC10CodeFormatter()
        {
            L.Info("IC10CodeFormatter - Constructor");
            foreach (ScriptCommand cmd in EnumCollections.ScriptCommands.Values)
            {
                string cmdName = Enum.GetName(typeof(ScriptCommand), cmd);
                instructions[cmdName] = cmd;
            }

            foreach (LogicType lt in EnumCollections.LogicTypes.Values)
            {
                string ltName = Enum.GetName(typeof(LogicType), lt);
                logicTypes.Add(ltName);
            }
            logicTypes.Add("Average");
            logicTypes.Add("Sum");
            logicTypes.Add("Min");
            logicTypes.Add("Max");

            for (int i = 0; i < 16; i++)
                registers.Add($"r{i}");

            registers.Add("sp");
            registers.Add("ra");

            for (int i = 0; i < 6; i++)
                devices.Add($"d{i}");

            devices.Add("db");
        }

        public override void ResetCode(string code)
        {
            L.Info("IC10CodeFormatter - Reset");
            defines.Clear();
            aliases.Clear();
            labels.Clear();
            errors.Clear();

            var lines = code.Split('\n');
            foreach (var line in lines)
                AddLine(line);
        }

        public void CalcIsError(string token)
        {
            int count = 0;
            if (defines.ContainsKey(token))
                count += defines[token];

            if (aliases.ContainsKey(token))
                count += 1; // multiple aliaes are allowed, thus only count as 1

            if (labels.ContainsKey(token))
                count += labels[token];

            if (instructions.ContainsKey(token))
                count += 1;

            if (count > 1)
                errors.Add(token);
            else
                errors.Remove(token);
        }

        private void AddDictEntry(Dictionary<string, int> dict, string key)
        {
            if (!dict.ContainsKey(key))
                dict[key] = 0;

            dict[key]++;
            CalcIsError(key);
        }

        private void RemoveDictEntry(Dictionary<string, int> dict, string key)
        {
            dict[key]--;
            if (dict[key] == 0)
                dict.Remove(key);

            CalcIsError(key);
        }

        public override void AddLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            var tokens = Tokenize(line);

            if (tokens.Count > 1)
            {
                if (tokens[0] == "define")
                    AddDictEntry(defines, tokens[1]);

                if (tokens[0] == "alias")
                    AddDictEntry(aliases, tokens[1]);
            }

            if (tokens[0].EndsWith(":"))
                AddDictEntry(labels, tokens[0].Substring(0, tokens[0].Length - 1));
        }

        public override void RemoveLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            var tokens = Tokenize(line);

            if (tokens.Count > 1)
            {
                if (tokens[0] == "define")
                    RemoveDictEntry(defines, tokens[1]);

                if (tokens[0] == "alias")
                    RemoveDictEntry(aliases, tokens[1]);
            }

            if (tokens[0].EndsWith(":"))
                RemoveDictEntry(labels, tokens[0].Substring(0, tokens[0].Length - 1));
        }

        public override uint GetColor(string token)
        {
            if (double.TryParse(token, out double number))
            {
                return ColorNumber;
            }

            token = TrimToken(token);
            if (errors.Contains(token))
                return ColorError;
            else if (defines.ContainsKey(token))
                return ColorDefine;
            else if (aliases.ContainsKey(token))
                return ColorAlias;
            else if (labels.ContainsKey(token))
                return ColorLabel;
            else if (instructions.ContainsKey(token))
                return ColorInstruction;
            else if (devices.Contains(token))
                return ColorDevice;
            else if (logicTypes.Contains(token))
                return ColorLogicType;
            else if (registers.Contains(token))
                return ColorRegister;
            else
                return ColorDefault;
        }

        public struct ColoredTextSegment
        {
            public uint Color;
            public string Text;

            public ColoredTextSegment(string color, string text)
            {
                Color = ColorFromHTML(color);
                Text = text;
            }
        }

        public static List<ColoredTextSegment> ParseAndDrawColoredText(string input)
        {
            var result = new List<ColoredTextSegment>();
            var regex = new Regex(@"<color=(.*?)>(.*?)</color>", RegexOptions.Singleline);
            int lastIndex = 0;

            foreach (Match match in regex.Matches(input))
            {
                if (match.Index > lastIndex)
                {
                    string rawText = input.Substring(lastIndex, match.Index - lastIndex);
                    result.Add(new ColoredTextSegment("#ffffff", rawText));
                }

                string color = match.Groups[1].Value;
                string text = match.Groups[2].Value;
                result.Add(new ColoredTextSegment(color, text));

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < input.Length)
                result.Add(new ColoredTextSegment("#ffffff", input.Substring(lastIndex)));

            Vector2 pos = ImGui.GetCursorScreenPos();
            foreach (var segment in result)
            {
                ImGui.GetWindowDrawList().AddText(
                    pos,
                    segment.Color,
                    segment.Text);
                pos.x += ImGui.CalcTextSize(segment.Text).x;
            }

            ImGui.NewLine();

            return result;
        }

        public override void DrawTooltip(string line, TextPosition caret, Vector2 pos)
        {
            var col = caret.Col;
            if (col == 0)
                return;
            var charBefore = line[col - 1];
            var tokensBefore = ICodeFormatter.Tokenize(line.Substring(0, col), true);
            if (charBefore == ' ' && tokensBefore.Count > 0)
            {
                var instruction = tokensBefore[0];
                if (instructions.ContainsKey(instruction))
                {
                    pos += new Vector2(30, 40);

                    ImGui.SetNextWindowSize(new Vector2(500, 0), ImGuiCond.Once);
                    ImGui.SetNextWindowPos(pos, ImGuiCond.Always);

                    if (ImGui.Begin("##IC10EditorTooltip",
                        ImGuiWindowFlags.NoNav
                        // | ImGuiWindowFlags.NoFocusOnAppearing
                        | ImGuiWindowFlags.NoTitleBar
                        | ImGuiWindowFlags.NoScrollbar
                        | ImGuiWindowFlags.NoSavedSettings
                        | ImGuiWindowFlags.NoMove
                        | ImGuiWindowFlags.NoCollapse
                        | ImGuiWindowFlags.NoResize
                        ))
                    {

                        ScriptCommand scriptCommand = instructions[instruction];
                        String example = ProgrammableChip.GetCommandExample(scriptCommand);
                        String description = ProgrammableChip.GetCommandDescription(scriptCommand);

                        ParseAndDrawColoredText($"Instruction: <color=yellow>{instruction}</color>");
                        ImGui.Separator();
                        ParseAndDrawColoredText($"    {example}");
                        ImGui.NewLine();
                        // ImGui.Text("Description: ");
                        ImGui.TextWrapped(description);
                        ImGui.End();
                    }
                }
            }
        }
    }
}
