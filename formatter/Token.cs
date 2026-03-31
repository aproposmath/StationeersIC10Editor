namespace StationeersIC10Editor;

using System.Collections.Generic;

using ImGuiNET;

using UnityEngine;

using static Settings;

/// <summary>
/// Represents a lightweight pointer to a range in the source buffer.
/// Does NOT hold the text content itself.
/// </summary>
public struct SemanticToken
{
    public int Line; // Line index (0-based)
    public int Column; // Start column index (0-based)
    public int Length; // Length of the token
    public uint Type; // Semantic Type ID (mapped to DataType usually)
    public Style Style;

    // Metadata for tooltips/errors, managed via dictionary or side-channel in a real LSP,
    // but stored here for simplicity in this hybrid approach.
    public string Data;
    public bool IsError;

    public void SetError(string errorMessage)
    {
        IsError = true;
        Data = errorMessage;
    }

    public SemanticToken(
        int line,
        int column,
        int length,
        uint type,
        Style style = new Style(),
        string data = null,
        bool isError = false
    )
    {
        Line = line;
        Column = column;
        Length = length;
        Type = type;
        Style = style;
        Data = data;
        IsError = isError;
    }

    public uint Color
    {
        get => Style.Color;
        set => Style.Color = value;
    }
    public uint Background
    {
        get => Style.Background;
        set => Style.Background = value;
    }
}

public class Token
{
    public int Column;
    public string Text;
    public Style Style;
    public int Length => Text.Length;

    public uint Type = 0;
    public StyledText Tooltip = null;
    public StyledText Error = null;

    public bool IsError => Error != null;

    public Token(int column, string text, Style style = new Style(), uint type = 0)
    {
        Column = column;
        Text = text;
        Style = style;
        Type = type;
    }

    public uint Color => Error != null ? ICodeFormatter.ColorError : Style.Color;
    public uint Background => Style.Background;
}

public class StyledLine : List<Token>
{
    protected string _content = "";
    public Style Style = new Style();

    public string Text
    {
        get => _content;
        set => _content = value ?? string.Empty;
    }

    public int Length => _content.Length;

    public StyledLine() : base()
    {
        _content = string.Empty;
    }

    public StyledLine(string text, List<SemanticToken> tokens = null)
        : base()
    {
        _content = text ?? string.Empty;

        if (tokens != null)
            Update(tokens);
    }

    public void Update(List<SemanticToken> tokens)
    {
        tokens.Sort((a, b) => a.Column.CompareTo(b.Column));
        Clear();

        int column = 0;
        int len = _content.Length;

        foreach (var token in tokens)
        {
            if (column >= len)
                break;

            if (token.Column >= len)
                break;

            if (token.Column + token.Length > len)
                continue;

            // Add plain text before token
            if (token.Column > column)
            {
                string plainText = _content.Substring(column, token.Column - column);
                Add(new Token(column, plainText, ICodeFormatter.DefaultStyle));
                column = token.Column;
            }

            string tokenText = _content.Substring(token.Column, token.Length);
            Add(new Token(token.Column, tokenText, token.Style));
            column = token.Column + token.Length;
        }

        if (column < len)
        {
            string plainText = _content.Substring(column, len - column);
            Add(new Token(column, plainText, ICodeFormatter.DefaultStyle));
        }
    }

    // Helper to find a token at a specific column
    public Token GetTokenAt(int column)
    {
        // Simple linear search. For very long lines, binary search could be used if tokens are sorted.
        foreach (var token in this)
        {
            if (column >= token.Column && column < token.Column + token.Length)
                return token;
        }
        return null;
    }

    public void DrawBackground(Vector2 pos, int lineIndex)
    {
        var list = ImGui.GetWindowDrawList();
        if (this.Count == 0)
            return;
        foreach (var token in this)
        {
            if (token.Background != 0)
            {
                Vector2 start = new Vector2(
                    pos.x + CharWidth * token.Column,
                    pos.y
                );
                Vector2 end = new Vector2(
                    pos.x + CharWidth * (token.Column + token.Text.Length),
                    pos.y + ImGui.GetTextLineHeightWithSpacing()
                );
                list.AddRectFilled(start, end, token.Background);
            }
        }
    }

    public void DrawText(Vector2 pos, int lineIndex)
    {
        var list = ImGui.GetWindowDrawList();
        if (this.Count == 0)
        {
            if (!string.IsNullOrEmpty(_content))
                list.AddText(pos, ICodeFormatter.ColorDefault, _content);
            return;
        }
        foreach (var token in this)
        {
            list.AddText(
                new Vector2(pos.x + CharWidth * token.Column, pos.y),
                token.Color,
                token.Text
            );

        }
    }

    public void Draw(Vector2 pos, int lineIndex)
    {
        DrawBackground(pos, lineIndex);
        DrawText(pos, lineIndex);
    }
}


public class StyledText : List<StyledLine>
{

    public StyledText() : base() { }
    public StyledText(string text) : base()
    {

        foreach (var line in text.Split('\n'))
            Add(new StyledLine(line));
    }

    public float Width
    {
        get
        {
            int width = 0;
            foreach (var line in this)
                if (line.Length > width)
                    width = line.Length;
            return width * CharWidth;
        }
    }

    public float Height => Count * LineHeight;

    public string RawText
    {
        get { return string.Join("\n", this.ConvertAll(line => line.Text)); }
    }

    public Token GetTokenAtPosition(TextPosition pos)
    {
        if (pos.Line < 0 || pos.Line >= Count)
            return null;
        return this[pos.Line].GetTokenAt(pos.Col);
    }

    public void Draw(Vector2 pos)
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].Draw(pos, i);
            pos.y += LineHeight;
        }
    }

    public void AddLine(string text, Style style)
    {
        var line = new StyledLine(text);
        line.Add(new Token(0, text, style));
        Add(line);
    }

    public static List<string> WrapText(string text, int maxLen)
    {
        var lines = new List<string>();
        string currentLine = "";
        foreach (var word in text.Split(' '))
        {
            if (currentLine.Length + word.Length + 1 > maxLen)
            {
                lines.Add(currentLine);
                currentLine = "";
            }
            if (currentLine.Length > 0)
                currentLine += " ";
            currentLine += word;
        }
        if (currentLine.Length > 0)
            lines.Add(currentLine);
        return lines;
    }

    public void AddWrapped(string text, int width, Style style)
    {
        if (style.Color == 0)
            style = ICodeFormatter.DefaultStyle;
        foreach (var line in WrapText(text, width))
            AddLine(line, style);
    }

    public static StyledText ErrorText(string message)
    {
        var text = new StyledText();
        var line = new StyledLine(message);
        var errorStyle = new Style(ICodeFormatter.ColorError);
        line.Add(new Token(0, message, errorStyle));
        text.Add(line);
        return text;
    }
}
