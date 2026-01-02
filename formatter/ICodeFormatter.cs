namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

using ImGuiNET;

using UnityEngine;

using static Settings;

public abstract class ICodeFormatter
{
    public const uint ColorDefault = 0xFFFFFFFF;
    public static uint ColorError = ColorFromHTML("#ff0000");
    public static uint ColorWarning = ColorFromHTML("#ff8f00");
    public static uint ColorComment = ColorFromHTML("#808080");
    public static uint ColorLineNumber = ColorFromHTML("#808080");
    public static uint ColorSelection = ColorFromHTML("#1a44b0ff");
    public static uint ColorNumber = ColorFromHTML("#20b2aa");
    public static float LineNumberOffset = 5.3f;

    public static Style DefaultStyle = new Style
    {
        Color = ColorDefault,
        Background = 0
    };

    public StyledText Lines = new StyledText();
    public string RawText => Lines.RawText;
    public StyledLine CurrentLine
    {
        get
        {
            if (_lastCaretPos.Line >= 0 && _lastCaretPos.Line < Lines.Count)
                return Lines[_lastCaretPos.Line];
            return null;
        }
    }
    public string Name = "";

    protected StyledText _status = null;
    protected StyledText _autocomplete = null;
    protected string _autocompleteInsertText = null;
    protected StyledText _tooltip = null;
    protected TextPosition _lastCaretPos = new TextPosition(-1, -1);
    protected Vector2 _lastMousePos = new Vector2(-1, -1);
    public Editor Editor;

    public Vector2 MousePos => _lastMousePos;
    public TextPosition CaretPos => _lastCaretPos;

    public StyledText Status => _status;
    public StyledText Tooltip => _tooltip;

    public Action OnCodeChanged = () => { };
    public Action OnCaretMoved = () => { };

    public virtual StyledLine ParseLine(string line)
    {
        var sline = new StyledLine(line);
        sline.Add(new Token(0, line, DefaultStyle));
        return sline;
    }

    public ICodeFormatter()
    {
        OnCodeChanged += () =>
        {
            _status = null;
            _autocomplete = null;
            _tooltip = null;
            UpdateStatus();
            if (NeedsUpdateAutocomplete())
                UpdateAutocomplete();
        };

        OnCaretMoved += () =>
        {
            _status = null;
            _autocomplete = null;
            UpdateStatus();
            if (NeedsUpdateAutocomplete())
                UpdateAutocomplete();
        };
    }

    public static uint ColorFromHTML(string htmlColor)
    {
        if (string.IsNullOrWhiteSpace(htmlColor))
            return ColorDefault;

        if (htmlColor.StartsWith("0x"))
            htmlColor = htmlColor.Substring(2);
        if (!htmlColor.StartsWith("#"))
        {
            // Simple Color Map fallback
            switch (htmlColor.ToLower())
            {
                case "blue":
                    return 0xFFFF0000; // ImGui is ABGR/RGBA depending on backend, keeping logic same as before
                case "red":
                    return 0xFF0000FF;
                case "green":
                    return 0xFF00FF00;
                case "white":
                    return 0xFFFFFFFF;
                case "black":
                    return 0xFF000000;
                case "yellow":
                    return 0xFF00FFFF;
                case "orange":
                    return 0xFF2B66FF;
                case "purple":
                    return 0xFF800080;
                case "gray":
                case "grey":
                    return 0xFF808080;
                default:
                    return ColorDefault;
            }
        }
        htmlColor = htmlColor.TrimStart('#');
        if (
            uint.TryParse(
                htmlColor,
                System.Globalization.NumberStyles.HexNumber,
                null,
                out uint rgb
            )
        )
        {
            byte a = 0xFF;
            if (htmlColor.Length > 6)
            {
                a = (byte)(rgb & 0xFF);
                rgb = rgb >> 8;
            }
            byte r = (byte)((rgb >> 16) & 0xFF);
            byte g = (byte)((rgb >> 8) & 0xFF);
            byte b = (byte)(rgb & 0xFF);
            return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
        }
        return ColorDefault;
    }

    public virtual void ReplaceLine(int index, string newLine)
    {
        RemoveLine(index);
        InsertLine(index, newLine);
    }

    public virtual void AppendLine(string line)
    {
        Lines.Add(ParseLine(line));
    }

    public virtual void InsertLine(int index, string line)
    {
        Lines.Insert(index, ParseLine(line));
    }

    public virtual void RemoveLine(int index)
    {
        Lines.RemoveAt(index);
    }

    public virtual void ResetCode(string code)
    {
        var lines = code.Split('\n');
        Lines.Clear();
        L.Debug($"Resetting code with {lines.Length} lines");
        foreach (var line in lines)
            AppendLine(line);
        OnCodeChanged();
    }

    public virtual void AfterDrawLines(Vector2 pos, Vector2 size)
    {
        // // Example: Draw a button at the top-right corner to toggle read-only mode
        // float width = 60;
        // ImGui.SetCursorScreenPos(
        //     pos + new Vector2(
        //         size.x - width - 10,
        //         0
        //     )
        // );
        //
        // string label = Editor.IsReadOnly ? "RO" : "RW";
        //
        // if (ImGui.Button(label, new Vector2(width, 0)))
        //     Editor.IsReadOnly = !Editor.IsReadOnly;
    }

    public Dictionary<int, Style> LineStyles = new Dictionary<int, Style>();

    public virtual void DrawLine(int lineIndex, TextRange selection, bool drawLineNumber = true)
    {
        Vector2 pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        StyledLine line = Lines[lineIndex];

        Style lineStyle = line.Style;

        if (LineStyles.TryGetValue(lineIndex, out Style style))
            lineStyle = style;

        if (lineStyle.Color > 0)
        {
            float offset = 4.8f * CharWidth - ImGui.GetStyle().WindowPadding.x;
            if (style.Color > 0)
            {
                drawList.AddLine(
                    pos + new Vector2(
                        offset,
                        0
                    ),
                    new Vector2(
                        pos.x + offset,
                        pos.y + LineHeight
                    ),
                    style.Color,
                    5.0f
                );
            }
        }


        if (drawLineNumber)
        {
            int lineNumber = lineIndex;
            if (RelativeLineNumbers && lineNumber != CaretPos.Line)
                lineNumber = Math.Abs(lineNumber - CaretPos.Line);
            drawList.AddText(
                pos,
                ICodeFormatter.ColorLineNumber,
                lineNumber.ToString().PadLeft(3) + "."
            );
            pos.x += ICodeFormatter.LineNumberOffset * CharWidth;
        }

        if (lineStyle.Background > 0)
        {
            drawList.AddRectFilled(
                new Vector2(
                    pos.x,
                    pos.y
                ),
                new Vector2(
                    pos.x + ImGui.GetContentRegionAvail().x,
                    pos.y + LineHeight
                ),
                style.Background
            );
        }

        int selectionMin = -1,
            selectionMax = -1;
        // Calculate Selection Rect
        if (selection)
        {
            float lineHeight = ImGui.GetTextLineHeightWithSpacing();
            if (selection.Start.Line <= lineIndex && selection.End.Line >= lineIndex)
            {
                selectionMin = lineIndex == selection.Start.Line ? selection.Start.Col : 0;
                selectionMax = lineIndex == selection.End.Line ? selection.End.Col : line.Length;

                selectionMin = Mathf.Clamp(selectionMin, 0, line.Length);
                selectionMax = Mathf.Clamp(selectionMax, 0, line.Length);

                Vector2 selStart = new Vector2(pos.x + (CharWidth * selectionMin), pos.y);
                Vector2 selEnd = new Vector2(
                    pos.x + (CharWidth * selectionMax),
                    pos.y + lineHeight
                );

                drawList.AddRectFilled(selStart, selEnd, ICodeFormatter.ColorSelection);
            }
        }

        // Draw the actual line content using its SemanticTokens
        line.Draw(pos, lineIndex);
    }

    public virtual string Compile() => RawText;

    public virtual void UpdateTooltip(TextPosition mouseTextPos)
    {
        _tooltip = null;
        if (!(bool)mouseTextPos)
            return;

        var token = Lines.GetTokenAtPosition(mouseTextPos);
        if (token == null)
            return;

        _tooltip = token.Tooltip;
        if (token.Error != null)
        {
            _tooltip = token.Error;
        }
    }

    public virtual void UpdateStatus()
    {
        _status = null;
        if (!(bool)CaretPos)
            return;

        var token = Lines.GetTokenAtPosition(CaretPos);
        if (token == null)
            return;

        _status = token.Error;
    }

    public virtual bool NeedsUpdateAutocomplete()
    {
        if (!Settings.EnableAutoComplete)
            return false;

        if (Editor.KeyMode != KeyMode.Insert)
            return false;

        var caret = Editor.CaretPos;

        if (caret.Col == 0)
            return false;

        if (caret.Line >= Lines.Count)
            return false;

        if (!Editor.IsWordEnd(caret) && caret.Col < Lines[caret.Line].Length)
            return false;

        return true;
    }


    public virtual void UpdateAutocomplete()
    {
        _autocomplete = null;
    }

    public virtual void PerformAutocomplete()
    {
        if (_autocompleteInsertText == null)
            return;

        var newLine = CurrentLine.Text;
        newLine = newLine.Insert(
            _lastCaretPos.Col,
            _autocompleteInsertText
        );

        Editor.ReplaceLine(_lastCaretPos.Line, newLine);
        Editor.CaretPos = new TextPosition(
            Editor.CaretPos.Line,
            Editor.CaretPos.Col + _autocompleteInsertText.Length
        );
    }

    public virtual void Update(TextPosition caretPos, Vector2 mousePos, TextPosition mouseTextPos)
    {
        bool hasMouseMoved = !mousePos.Equals(_lastMousePos);
        bool hasCaretMoved = !caretPos.Equals(_lastCaretPos);

        _lastCaretPos = caretPos;
        _lastMousePos = mousePos;

        if (hasMouseMoved)
            UpdateTooltip(mouseTextPos);
        if (hasCaretMoved)
            OnCaretMoved();
    }

    public virtual void DrawStatus(Vector2 pos)
    {
        if (Status != null)
            Status.Draw(pos);
    }

    public virtual void DrawTooltip(Vector2 pos)
    {
        if (Tooltip == null || Tooltip.Count == 0)
            return;
        ImGui.SetNextWindowSize(
            new Vector2(Tooltip.Width, Tooltip.Height) + 2 * ImGui.GetStyle().WindowPadding
        );
        ImGui.BeginTooltip();
        Tooltip.Draw(ImGui.GetCursorScreenPos());
        ImGui.EndTooltip();
    }

    public virtual void DrawAutocomplete(Vector2 pos)
    {
        if (_autocomplete != null)
        {
            var completeSize = new Vector2(
                _autocomplete.Width + 2 * ImGui.GetStyle().WindowPadding.x,
                _autocomplete.Height + 2 * ImGui.GetStyle().WindowPadding.y
            );

            float bottomSize = ImGui.GetContentRegionAvail().y - LineHeight - 5.0f + ImGui.GetScrollY();
            if (bottomSize < completeSize.y)
            {
                pos.y -= completeSize.y - bottomSize;
                pos.x += CharWidth * 2;
            }
            var list = ImGui.GetWindowDrawList();
            list.PushClipRectFullScreen();
            list.AddRectFilled(pos, pos + completeSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.2f, 0.9f)), 5.0f);
            _autocomplete.Draw(pos + ImGui.GetStyle().WindowPadding);
            list.PopClipRect();
        }
    }

    public static string EncodeB64(string source)
    {
        if (string.IsNullOrEmpty(source))
            return "";

        byte[] bytes = Encoding.UTF8.GetBytes(source);

        using (var memoryStream = new MemoryStream())
        {
            using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
                gzipStream.Write(bytes, 0, bytes.Length);
            return Convert.ToBase64String(memoryStream.ToArray());
        }
    }

    public static string DecodeB64(string source)
    {
        if (string.IsNullOrEmpty(source))
            return "";

        byte[] compressedBytes = Convert.FromBase64String(source);

        using (var memoryStream = new MemoryStream(compressedBytes))
        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
        using (var outputStream = new MemoryStream())
        {
            gzipStream.CopyTo(outputStream);

            return Encoding.UTF8.GetString(outputStream.ToArray());
        }
    }

    public static string EncodeSource(string source, string tag, int wrapLength = 90)
    {
        if (!string.IsNullOrEmpty(tag))
            tag += ": ";

        var result = new StringBuilder();

        var encoded = tag + EncodeB64(source);
        int i = 0;
        while (i < encoded.Length)
        {
            var chunk = encoded.Substring(i, Math.Min(90, encoded.Length - i));
            result.Append("\n# " + chunk);
            i += chunk.Length;
        }
        return result.ToString();
    }

    public static string ExtractEncodedSource(string code, string tag)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(tag))
        {
            L.Debug("No code or tag provided for extraction.");
            return "";
        }

        tag = "# " + tag + ":";

        if (!code.Contains(tag))
        {
            L.Debug("Tag not found in code for extraction.");
            return "";
        }

        try
        {
            var parts = code.Split(
                new string[] { tag },
                System.StringSplitOptions.None
            );

            if (parts.Length >= 2)
                return DecodeB64(parts[1].Replace("\n# ", "").Trim());
        }
        catch (Exception e)
        {
            L.Info($"Exception during extraction: {e}");
        }

        L.Debug("No encoded source found after tag for extraction.");
        return "";
    }
}
