using System;
using System.Collections.Generic;

namespace StationeersIC10Editor;

public class SimpleMarkdownFormatter : StaticFormatter
{
    public SimpleMarkdownFormatter()
        : base("", "", "<!--", null, true)
    { }

    public override List<Token> TokenizeLine(string lineText)
    {
        var tokens = new List<Token>();
        if (string.IsNullOrEmpty(lineText))
            return tokens;

        var trimmed = lineText.TrimStart();

        var color = ColorDefault;
        if (trimmed.StartsWith("####"))
            color = ColorFromHTML("#D4D4D4");
        else if (trimmed.StartsWith("###"))
            color = ColorFromHTML("#9F6FB5");
        else if (trimmed.StartsWith("##"))
            color = ColorFromHTML("#4FB8A8");
        else if (trimmed.StartsWith("#"))
            color = ColorFromHTML("#6FAFEF");
        else if (trimmed.StartsWith("- [x]") || trimmed.StartsWith("* [x]"))
            color = ColorFromHTML("#7FBF6A");
        else if (trimmed.StartsWith("- [ ]") || trimmed.StartsWith("* [ ]"))
            color = ColorFromHTML("#E6DB74");
        else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || IsOrderedList(trimmed))
            color = ColorFromHTML("#B8C6D9");

        tokens.Add(new Token(0, lineText, color));
        return tokens;
    }

    public override StyledLine ParseLine(string lineText)
    {
        var styledLine = new StyledLine();
        styledLine.Text = lineText;

        var tokens = TokenizeLine(lineText);
        styledLine.AddRange(tokens);

        return styledLine;
    }

    public static bool IsOrderedList(string line)
    {
        int i = 0;
        while (i < line.Length && char.IsDigit(line[i]))
            i++;

        // must have at least one digit and then ". "
        return i > 0 &&
               i < line.Length - 1 &&
               line[i] == '.' &&
               line[i + 1] == ' ';
    }

    public static double MatchingScore(string input)
    {
        double score = 0;
        var lines = input.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
            return 0;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            // Headers
            if (trimmed.StartsWith("##"))
            {
                score += 1;
                continue;
            }

            // Task lists
            if (trimmed.StartsWith("- [x]") || trimmed.StartsWith("* [x]") ||
                trimmed.StartsWith("- [ ]") || trimmed.StartsWith("* [ ]"))
            {
                score += 1;
                continue;
            }

            // Bullet / unordered lists
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "))
            {
                score += 0.7;
                continue;
            }

            if (IsOrderedList(trimmed))
            {
                score += 1;
                continue;
            }
        }

        return score / lines.Length;
    }
}
