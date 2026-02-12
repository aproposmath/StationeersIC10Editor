namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.Linq;

using Assets.Scripts;
using Assets.Scripts.Networking;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.UI;

using BepInEx.Configuration;

using Cysharp.Threading.Tasks;

using ImGuiNET;

using UnityEngine;

using static Settings;
using static Utils;

public class ConfirmWindow
{
    public string Message;
    public bool IsOpen = true;

    public Action OnConfirm = delegate { };

    public ConfirmWindow(string message)
    {
        ImGui.OpenPopup("Confirm");
        Message = message;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        ImGui.CloseCurrentPopup();
    }

    public void Confirm()
    {
        OnConfirm?.Invoke();
        IsOpen = false;
        ImGui.CloseCurrentPopup();
    }

    public void Draw()
    {
        bool open = true;
        if (
            ImGui.BeginPopupModal(
                "Confirm",
                ref open,
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize
            )
        )
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ICodeFormatter.ColorWarning);
            ImGui.Text(Message);
            ImGui.PopStyleColor();
            ImGui.Text("");
            ImGui.Text("Press Escape to cancel, Enter to confirm.");
            ImGui.Separator();

            if (ImGui.Button("Cancel", Scale * new Vector2(100, 0)))
                Close();
            ImGui.SameLine();

            var pos = ImGui.GetCursorPos();
            var space =
                ImGui.GetContentRegionAvail().x
                - Scale * 100
                - Scale * ImGui.GetStyle().FramePadding.x
                - ImGui.GetStyle().ItemSpacing.x
                - Scale * 10;

            ImGui.SetCursorPos(new Vector2(pos.x + space, pos.y));

            if (ImGui.Button("Confirm", Scale * new Vector2(100, 0)))
                Confirm();

            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                Close();

            if (ImGui.IsKeyPressed(ImGuiKey.Enter))
                Confirm();
            ImGui.EndPopup();
        }
    }
}

public class EditorState
{
    public string Code;
    public StyledText FormattedText;
    public TextPosition CaretPos;
    public double Timestamp;
    public bool Mergeable;
}

public enum MoveToken
{
    Char,
    Line,
    WordBeginning,
    WordEnd,
}

public struct MoveAction
{
    public MoveToken Token;
    public bool Forward;
    public uint Amount;

    public int Direction => Forward ? 1 : -1;
    public int SignedAmount => (int)(Direction * Amount);

    public MoveAction(MoveToken token = MoveToken.Char, bool forward = true, uint amount = 0)
    {
        Token = token;
        Forward = forward;
        Amount = amount;
    }
}

public static class Utils
{
    public static bool IsWordChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '-';
    }
}

public static class Settings
{
    public static bool VimEnabled => IC10EditorPlugin.VimBindings.Value;
    public static bool EnforceLineLengthLimit => IC10EditorPlugin.EnforceLineLengthLimit.Value;
    public static bool EnforceLineLimit => IC10EditorPlugin.EnforceLineLimit.Value;
    public static bool EnforceByteLimit => IC10EditorPlugin.EnforceByteLimit.Value;
    public static bool PauseOnOpen => IC10EditorPlugin.PauseOnOpen.Value;
    public static float TooltipDelay => IC10EditorPlugin.TooltipDelay.Value;
    public static float Scale => Mathf.Clamp(IC10EditorPlugin.ScaleFactor.Value, 0.25f, 5.0f);
    public static bool EnableAutoComplete => IC10EditorPlugin.EnableAutoComplete.Value;
    public static int LineSpacingOffset => IC10EditorPlugin.LineSpacingOffset.Value;
    public static bool CollapseOnGameWindow => IC10EditorPlugin.CollapseOnGameWindow.Value;
    public static bool RelativeLineNumbers => IC10EditorPlugin.RelativeLineNumbers.Value;
    public static bool RestoreSelectedHousing => IC10EditorPlugin.RestoreSelectedHousing.Value;

    public static Vector2 buttonSize => Scale * new Vector2(85, 0);
    public static Vector2 smallButtonSize => Scale * new Vector2(50, 0);

    public const string LimitExceededMessage = "Size limit exceeded: cannot save or export.";

    private static float _lastScale = -1.0f;
    private static float _lastLineSpacingOffset = -1.0f;
    private static float _charWidth = 0.0f;
    private static float _lineHeight = 0.0f;
    private static float _lineSpacing = 0.0f;
    public static float CharWidth => _charWidth;
    public static float LineHeight => _lineHeight;
    public static float LineSpacing => _lineSpacing;

    public static void UpdateTextSize()
    {
        if (Scale == _lastScale && LineSpacingOffset == _lastLineSpacingOffset)
            return;

        string s =
            "MMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n";

        var size = ImGui.CalcTextSize(s);

        _lineHeight = Mathf.Ceil(size.y / 100.0f + LineSpacingOffset);
        _charWidth = size.x / 100.0f;

        _lineSpacing =
            ImGui.GetTextLineHeightWithSpacing()
            - ImGui.GetTextLineHeight()
            + LineSpacingOffset;
        _lastScale = Scale;
        _lastLineSpacingOffset = LineSpacingOffset;
    }
}

public class Editor
{
    public object Target;
    public ProgrammableChipMotherboard PCM => Target as ProgrammableChipMotherboard;
    public InstructionData InstructionData => Target as InstructionData;
    bool IsMotherboard => PCM != null;
    public bool EnforceLineLengthLimit => Settings.EnforceLineLengthLimit && IsMotherboard;
    public bool EnforceLineLimit => Settings.EnforceLineLimit && IsMotherboard;
    public bool EnforceByteLimit => Settings.EnforceByteLimit && IsMotherboard;

    public bool LimitExceeded => (EnforceLineLimit && Lines.Count > 128) || (EnforceByteLimit && Code.Length > 4096) || (EnforceLineLengthLimit && Lines.Any(line => line.Text.Length > 90));

    public bool HaveSelection => (bool)Selection;
    public KeyHandler KeyHandler;
    public bool IsReadOnly = false;

    public TextPosition _caretPos;

    public int ScrollToCaret = 0;
    protected double _timeLastAction = 0.0;
    protected bool _isCodeChanged = false;

    public double TimeLastAction => _timeLastAction;
    public KeyMode KeyMode => KeyHandler.Mode;

    public LinkedList<EditorState> UndoList;
    public LinkedList<EditorState> RedoList;
    public ICodeFormatter CodeFormatter;
    public string Code => CodeFormatter.RawText;
    public List<StyledLine> Lines => CodeFormatter.Lines;
    public string CommandStatus = "";

    public EditorTab ParentTab = null;

    public ConfirmWindow _confirmWindow = null;

    public string FileName
    {
        get
        {
            if (IsMotherboard)
                return $"motherboard_id_{PCM.ReferenceId}";
            else if (InstructionData != null)
                return InstructionData.Title.Trim().Replace(' ', '_').Replace('/', '_');
            else
                return "Untitled";
        }
    }

    public Editor(KeyHandler keyHandler, object target = null)
    {
        Target = target;
        KeyHandler = keyHandler;
        CodeFormatter = CodeFormatters.GetFormatter();
        CodeFormatter.Editor = this;
        UndoList = new LinkedList<EditorState>();
        RedoList = new LinkedList<EditorState>();
        var code = "";
        CodeFormatter.ResetCode(code);
        CaretPos = new TextPosition(0, 0);
    }

    public TextPosition CaretPos
    {
        get { return _caretPos; }
        set
        {
            _caretPos = value;
            if (_caretPos.Line < 0)
                _caretPos.Line = 0;
            if (_caretPos.Line >= Lines.Count)
                _caretPos.Line = Lines.Count - 1;
            if (_caretPos.Col < 0)
                _caretPos.Col = 0;
            if (Lines.Count > 0 && _caretPos.Col > Lines[_caretPos.Line].Length)
                _caretPos.Col = Lines[_caretPos.Line].Length;
            ScrollToCaret += 1;
            _timeLastAction = ImGui.GetTime();
        }
    }

    public int CaretLine
    {
        get { return _caretPos.Line; }
        set { CaretPos = new TextPosition(value, _caretPos.Col); }
    }

    public int CaretCol
    {
        get { return CaretPos.Col; }
        set { CaretPos = new TextPosition(_caretPos.Line, value); }
    }

    public TextRange Selection;

    public string CurrentLine
    {
        get { return Lines[CaretLine].Text; }
        set
        {
            if (IsReadOnly || value == Lines[CaretLine].Text)
                return;

            ReplaceLine(CaretLine, value);
        }
    }
    public EditorState State
    {
        get
        {
            return new EditorState
            {
                Code = Code,
                FormattedText = CodeFormatter.Lines,
                CaretPos = CaretPos,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
        }
        set
        {
            if (IsReadOnly)
                return;
            CaretPos = value.CaretPos;
            CodeFormatter.ResetCode(value.Code);
            _isCodeChanged = true;
        }
    }

    public Vector2 _textAreaOrigin,
        _textAreaSize;
    public float _scrollY = 0.0f;

    public bool IsMouseInsideTextArea()
    {
        Vector2 mousePos = ImGui.GetMousePos();
        float px = _textAreaOrigin.x;
        float py = _textAreaOrigin.y + ImGui.GetStyle().FramePadding.y;
        return mousePos.x >= px
            && mousePos.x <= px + _textAreaSize.x - ImGui.GetStyle().ScrollbarSize
            && mousePos.y >= py
            && mousePos.y <= py + _textAreaSize.y;
    }

    public TextPosition GetTextPositionFromMouse(bool clampToTextArea = true)
    {
        Vector2 mousePos = ImGui.GetMousePos();

        int line =
            (int)((mousePos.y + LineSpacing - _firstLineY) / LineHeight) + _firstLineIndex;
        int column = (int)(
            (mousePos.x - _textAreaOrigin.x) / CharWidth - ICodeFormatter.LineNumberOffset - 0.5f
        );

        if (!clampToTextArea && (line < 0 || line >= Lines.Count || column < 0))
        {
            return new TextPosition(-1, -1);
        }

        line = Mathf.Clamp(line, 0, Lines.Count - 1);
        column = Mathf.Clamp(column, 0, Lines[line].Length);

        return new TextPosition(line, column);
    }

    public Vector2 _caretPixelPos;
    private int _firstLineIndex = -1;
    private float _firstLineY = -1.0f;



    public void PushUndoState(bool merge = true)
    {
        while (UndoList.Count > 100)
            UndoList.RemoveLast();

        var state = State;
        state.Mergeable = merge;

        if (string.IsNullOrEmpty(state.Code))
            return;

        if (UndoList.Count > 0)
        {
            var first = UndoList.First.Value;
            if (state.Code == first.Code)
            {
                first.CaretPos = state.CaretPos;
                first.FormattedText = state.FormattedText;
                return;
            }

            // merge with previous state if within 500ms or same code
            // merging does not happen accross "large" changes (e.g. paste, cut, delete selection etc.)
            if (merge && first.Mergeable && state.Timestamp < first.Timestamp + 500)
            {
                L.Debug($" Merging undo state, time diff {state.Timestamp - first.Timestamp}");
                UndoList.RemoveFirst();
            }
        }

        UndoList.AddFirst(state);
    }

    public void Undo()
    {
        if (!IsReadOnly && UndoList.Count > 0)
        {
            RedoList.AddFirst(State);
            State = UndoList.First.Value;
            UndoList.RemoveFirst();
        }
    }

    public void Redo()
    {
        if (!IsReadOnly && RedoList.Count > 0)
        {
            UndoList.AddFirst(State);
            State = RedoList.First.Value;
            RedoList.RemoveFirst();
        }
    }

    public void RemoveLine(int lineIndex)
    {
        if (IsReadOnly || lineIndex < 0 || lineIndex >= Lines.Count)
            return;

        if (CodeFormatter.Lines.Count == 1)
            ReplaceLine(0, "");
        else
            CodeFormatter.RemoveLine(lineIndex);
        _isCodeChanged = true;
    }

    public void ReplaceLine(int lineIndex, string newLine)
    {
        if (IsReadOnly || lineIndex < 0 || lineIndex >= Lines.Count)
            return;

        CodeFormatter.ReplaceLine(lineIndex, newLine);
        _isCodeChanged = true;
    }

    public void InsertLine(int lineIndex, string newLine)
    {
        if (IsReadOnly || lineIndex < 0 || lineIndex > Lines.Count)
            return;

        CodeFormatter.InsertLine(lineIndex, newLine);
        _isCodeChanged = true;
    }

    public bool IsWordBeginning(TextPosition pos)
    {
        if (pos.Col == 0)
            return true;

        var leftPos = new TextPosition(pos.Line, pos.Col - 1);
        return !IsWordChar(this[leftPos]) && IsWordChar(this[pos]);
    }

    public bool IsWordEnd(TextPosition pos)
    {
        if (pos.Col == 0)
            return false;

        if (pos.Line >= Lines.Count)
            return true;

        if (pos.Col >= Lines[pos.Line].Length)
            return true;

        var leftPos = new TextPosition(pos.Line, pos.Col - 1);

        return IsWordChar(this[leftPos]) && !IsWordChar(this[pos]);
    }

    public TextPosition FindWordBeginning(TextPosition pos, bool forward)
    {
        int dir = forward ? 1 : -1;
        pos.Col += dir;
        pos = WrapPos(pos);
        while (!IsWordBeginning(pos))
        {
            pos.Col += dir;
            pos = WrapPos(pos);
            if (pos.Line == Lines.Count - 1 && pos.Col == Lines[pos.Line].Length)
                break;
        }
        return pos;
    }

    public TextPosition FindWordEnd(TextPosition pos, bool forward)
    {
        int dir = forward ? 1 : -1;
        pos.Col += dir;
        pos = WrapPos(pos);
        while (!IsWordEnd(pos))
        {
            pos.Col++;
            pos = WrapPos(pos);
            if (pos.Line == 0 && pos.Col == 0)
                break;
        }
        return pos;
    }

    public TextPosition WrapPos(TextPosition pos)
    {
        if (pos.Col < 0 && pos.Line > 0)
        {
            pos.Line--;
            pos.Col = Lines[pos.Line].Length;
        }
        if (pos.Col > Lines[pos.Line].Length && pos.Line < Lines.Count)
        {
            pos.Col = 0;
            pos.Line++;
        }

        if (pos.Line < 0)
            pos.Line = 0;
        if (pos.Line >= Lines.Count)
            pos.Line = Lines.Count - 1;

        if (pos.Col < 0)
            pos.Col = 0;
        if (pos.Col > Lines[pos.Line].Length)
            pos.Col = Lines[pos.Line].Length;

        return pos;
    }

    public TextPosition MoveLines(TextPosition pos, int amount)
    {
        pos.Line += amount;
        pos.Line = Math.Max(0, Math.Min(pos.Line, Lines.Count - 1));
        return pos;
    }

    public TextPosition MoveChars(TextPosition startPos, int amount)
    {
        int dir = amount >= 0 ? 1 : -1;
        amount = Math.Abs(amount);
        TextPosition pos = startPos;
        for (int i = 0; i < amount; i++)
        {
            pos.Col += dir;
            pos = WrapPos(pos);
        }

        return pos;
    }

    public TextPosition FindWhitespace(TextPosition pos, bool forward = true)
    {
        // Move to the next whitespace or next line if there is none in this line
        string line = Lines[pos.Line].Text;
        int dir = forward ? 1 : -1;
        while (pos.Col < line.Length && pos.Col >= 0 && !char.IsWhiteSpace(line[pos.Col]))
        {
            pos.Col += dir;
            if (pos.Col < 0)
                return WrapPos(pos);
        }

        return pos;
    }

    public TextPosition FindNonWhitespace(TextPosition pos, bool forward = true)
    {
        if (!char.IsWhiteSpace(this[pos]))
            return pos;

        int dir = forward ? 1 : -1;
        string line = Lines[pos.Line].Text;

        while (pos.Col < line.Length && pos.Col >= 0 && char.IsWhiteSpace(this[pos]))
            pos.Col += dir;

        pos = WrapPos(pos);
        return pos;
    }

    public TextPosition FindNextWord(TextPosition startPos, bool forward = true)
    {
        TextPosition pos = startPos;
        if (char.IsWhiteSpace(this[pos]))
            return FindNonWhitespace(pos, forward);

        pos = FindWhitespace(pos, forward);
        return FindNonWhitespace(pos, forward);
    }

    public TextPosition FindString(
        TextPosition startPos,
        string searchTerm,
        bool forward = true
    )
    {
        if (forward)
            return FindStringForward(startPos, searchTerm);
        else
            return FindStringBackward(startPos, searchTerm);
    }

    public TextPosition FindStringForward(TextPosition startPos, string searchTerm)
    {
        int lineIndex = startPos.Line;
        if(lineIndex < 0 || lineIndex >= Lines.Count)
            return new TextPosition(-1, -1);

        int colIndex = startPos.Col + 1;

        while (lineIndex < Lines.Count)
        {
            string line = Lines[lineIndex].Text;
            int foundIndex = line.IndexOf(searchTerm, colIndex, StringComparison.Ordinal);
            if (foundIndex != -1)
                return new TextPosition(lineIndex, foundIndex);

            lineIndex++;
            colIndex = 0;
        }

        return new TextPosition(-1, -1);
    }

    private TextPosition FindStringBackward(TextPosition startPos, string searchTerm)
    {
        int lineIndex = startPos.Line;
        int colIndex = startPos.Col - 1;

        while (lineIndex >= 0)
        {
            string line = Lines[lineIndex].Text;

            if (colIndex >= line.Length)
                colIndex = line.Length - 1;
            if (colIndex < 0)
            {
                lineIndex--;
                if (lineIndex >= 0)
                    colIndex = Lines[lineIndex].Length - 1;
                continue;
            }

            int foundIndex = line.LastIndexOf(searchTerm, colIndex, StringComparison.Ordinal);

            if (foundIndex != -1)
                return new TextPosition(lineIndex, foundIndex);

            lineIndex--;
            if (lineIndex >= 0)
                colIndex = Lines[lineIndex].Length - 1;
        }

        return new TextPosition(-1, -1);
    }

    public char this[TextPosition pos]
    {
        get
        {
            var line = Lines[pos.Line].Text;
            if (pos.Col == line.Length)
                return '\n';
            return line[pos.Col];
        }
    }

    public void CaretToEndOfLine()
    {
        CaretCol = Lines[CaretLine].Length;
    }

    public void CaretToStartOfLine()
    {
        CaretCol = 0;
    }

    public void CaretUp(int numLines = 1)
    {
        MoveCaret(0, -numLines, true);
    }

    public void CaretDown(int numLines = 1)
    {
        MoveCaret(0, numLines, true);
    }

    public void CaretLeft(int numCols = 1)
    {
        MoveCaret(-numCols, 0, true);
    }

    public void CaretRight(int numCols = 1)
    {
        MoveCaret(numCols, 0, true);
    }

    public void MoveCaret(
        int horizontal = 0,
        int vertical = 0,
        bool isRelative = true,
        bool isSelecting = false
    )
    {
        Selection.Reset();
        TextPosition newPos = CaretPos;
        if (isRelative)
        {
            newPos.Line += vertical;
            newPos.Col += horizontal;
        }
        else
        {
            newPos.Line = vertical;
            newPos.Col = horizontal;
        }

        if (newPos.Line < 0)
            newPos.Line = 0;

        if (newPos.Line >= Lines.Count)
            newPos.Line = Lines.Count - 1;

        if (newPos.Col < 0)
            newPos.Col = 0;

        if (newPos.Col > Lines[newPos.Line].Length)
            newPos.Col = Lines[newPos.Line].Length;

        if (CaretPos == newPos)
            return;

        if (isSelecting)
            Selection.End = newPos;
        else
            Selection.Reset();

        CaretPos = newPos;
    }

    public void SelectAll()
    {
        Selection.Start = new TextPosition(0, 0);
        Selection.End = new TextPosition(Lines.Count - 1, Lines[Lines.Count - 1].Length);
    }

    public void Cut()
    {
        if (IsReadOnly || !HaveSelection)
            return;
        GameManager.Clipboard = SelectedCode;
        DeleteSelectedCode();
    }

    public void CopyRange(TextRange range)
    {
        string code = GetCode(range);
        if (code != null)
        {
            GameManager.Clipboard = code;
        }
    }

    public void Copy()
    {
        CopyRange(Selection.Sorted());
    }

    public void Paste()
    {
        if (IsReadOnly)
            return;
        if (!DeleteSelectedCode())
            PushUndoState(false);
        Insert(GameManager.Clipboard);
    }

    public string GetCode(TextRange range)
    {
        if (!(bool)range)
            return "";
        range = range.Sorted();
        if (range.Start == range.End)
            return "";
        var start = range.Start;
        var end = range.End;
        var suffix = "";

        if (end.Col > Lines[end.Line].Length)
        {
            end.Col = Lines[end.Line].Length;
            suffix = "\n";
        }

        if (start.Line == end.Line)
            return Lines[start.Line].Text.Substring(start.Col, end.Col - start.Col) + suffix;

        string code = Lines[start.Line].Text.Substring(start.Col);
        for (int i = start.Line + 1; i < end.Line; i++)
            code += '\n' + Lines[i].Text;

        code += '\n' + Lines[end.Line].Text.Substring(0, end.Col);
        return code + suffix;
    }

    public string SelectedCode => GetCode(Selection.Sorted());

    public TextPosition Clamp(TextPosition pos)
    {
        if (pos.Line < 0)
        {
            pos.Line = 0;
            pos.Col = 0;
        }
        else if (pos.Line >= Lines.Count)
        {
            pos.Line = Lines.Count - 1;
            pos.Col = Lines[pos.Line].Length;
        }
        else if (pos.Col < 0)
            pos.Col = 0;
        else if (pos.Col > Lines[pos.Line].Length)
            pos.Col = Lines[pos.Line].Length;
        return pos;
    }

    public TextRange Clamp(TextRange range)
    {
        range.Start = Clamp(range.Start);
        range.End = Clamp(range.End);
        return range;
    }

    public bool DeleteRange(TextRange range, bool pushUndo = true)
    {
        if (IsReadOnly || !(bool)range)
            return false;

        range = range.Sorted();
        bool removeLast = range.End.Col > Lines[range.End.Line].Length;
        range = Clamp(range);

        if (pushUndo)
            PushUndoState(false);

        var start = range.Start;
        var end = range.End;

        if (start.Line == end.Line)
        {
            if (start.Col == 0 && removeLast)
                RemoveLine(start.Line);
            else
            {
                string line = Lines[start.Line].Text;
                string newLine =
                    line.Substring(0, start.Col)
                    + line.Substring(end.Col, line.Length - end.Col);
                ReplaceLine(start.Line, newLine);
            }
        }
        else
        {
            string firstLine = Lines[start.Line].Text;
            string lastLine = Lines[end.Line].Text;
            string newFirstLine = firstLine.Substring(0, start.Col);
            string newLastLine = lastLine.Substring(end.Col, lastLine.Length - end.Col);
            ReplaceLine(start.Line, newFirstLine + newLastLine);

            for (int i = end.Line; i > start.Line; i--)
            {
                CodeFormatter.RemoveLine(i);
            }
            if (removeLast)
                RemoveLine(start.Line);
        }

        CaretPos = start;
        Selection.Reset();
        return true;
    }

    public bool DeleteSelectedCode()
    {
        if (IsReadOnly || DeleteRange(Selection))
        {
            Selection.Reset();
            _isCodeChanged = true;
            return true;
        }

        return false;
    }

    public TextRange GetWordAt(TextPosition pos)
    {
        if (Lines[pos.Line].Length == 0)
            return new TextRange(pos, pos);
        bool isWordChar = IsWordChar(this[pos]);
        bool IsWordBeginning =
            pos.Col == 0
            || (isWordChar && !IsWordChar(this[new TextPosition(pos.Line, pos.Col - 1)]));

        var startPos = IsWordBeginning ? pos : FindWordBeginning(pos, !isWordChar);
        var endPos = FindWordEnd(pos, isWordChar);

        return new TextRange(startPos, endPos);
    }

    public void ClearCode(bool pushUndo = true)
    {
        if (IsReadOnly)
            return;
        if (pushUndo)
            PushUndoState(false);
        CaretPos = new TextPosition(0, 0);
        CodeFormatter.ResetCode(string.Empty);
        Selection.Reset();
    }

    public void Insert(string code)
    {
        if (IsReadOnly)
            return;
        Insert(code, CaretPos);
    }

    public void Insert(string code, TextPosition pos)
    {
        if (IsReadOnly)
            return;
        code = code.Replace("\r", string.Empty);
        if (string.IsNullOrEmpty(code))
            return;
        var newLines = new List<string>(code.Split('\n'));
        if (newLines.Count == 0)
            return;

        bool atCaret = pos == CaretPos;

        string line = Lines[pos.Line].Text;

        // CodeFormatter.RemoveLine(CaretLine);
        string beforeCaret = line.Substring(0, pos.Col);
        string afterCaret = line.Substring(pos.Col, line.Length - pos.Col);

        if (newLines.Count == 1)
        {
            ReplaceLine(pos.Line, beforeCaret + newLines[0] + afterCaret);
            if (atCaret)
                CaretCol = beforeCaret.Length + newLines[0].Length;
            return;
        }
        ReplaceLine(pos.Line, beforeCaret + newLines[0]);
        newLines.RemoveAt(0);
        int newCaretCol = newLines[newLines.Count - 1].Length;
        newLines[newLines.Count - 1] += afterCaret;

        for (var j = 0; j < newLines.Count; j++)
            CodeFormatter.InsertLine(pos.Line + 1 + j, newLines[j]);

        if (atCaret)
            CaretPos = Clamp(new TextPosition(CaretLine + newLines.Count, newCaretCol));

        _isCodeChanged = true;
    }

    public TextPosition Move(TextPosition startPos, MoveAction action)
    {
        if (action.Amount == 0)
            return startPos;

        if (action.Token == MoveToken.Char)
            return MoveChars(startPos, action.SignedAmount);

        if (action.Token == MoveToken.Line)
        {
            var newLine = startPos.Line + action.SignedAmount;
            if (newLine < 0)
                newLine = 0;
            if (newLine >= Lines.Count)
                newLine = Lines.Count - 1;
            return new TextPosition(newLine, startPos.Col);
        }

        if (action.Token == MoveToken.WordBeginning)
        {
            var pos = startPos;
            for (int i = 0; i < action.Amount; i++)
                pos = FindWordBeginning(pos, action.Forward);
            return pos;
        }
        if (action.Token == MoveToken.WordEnd)
        {
            var pos = startPos;
            for (int i = 0; i < action.Amount; i++)
                pos = FindWordEnd(pos, action.Forward);
            return pos;
        }

        throw new NotImplementedException($"Move not implemented for token {action.Token}");
    }

    public void ResetCode(string code, bool pushUndo = true)
    {
        code = code.Replace("\r", string.Empty);
        ClearCode(pushUndo);
        var lines = code.Split('\n');
        if (pushUndo)
        {
            var formatter = CodeFormatters.GetFormatterByMatching(code);
            if (typeof(ICodeFormatter) != CodeFormatter.GetType())
            {
                CodeFormatter = formatter;
                CodeFormatter.Editor = this;
            }
        }
        CodeFormatter.ResetCode(code);
        CaretPos = new TextPosition(0, 0);
        _isCodeChanged = true;
    }

    public string Save()
    {
        if (PCM)
        {
            if (LimitExceeded)
            {
                return LimitExceededMessage;
            }
            var code = CodeFormatter.Compile();
            PCM.InputFinished(code);
            return "Saved to Motherboard";
        }
        if (InstructionData != null)
        {
            _confirmWindow = new ConfirmWindow(
                $"Are you sure to overwrite the code in Library '{InstructionData.Title}'?"
            );
            _confirmWindow.OnConfirm = () =>
            {
                InstructionData.Instructions = Code;
                InstructionData.SaveToFile(InstructionData.DirectoryPath);
                KeyHandler.CommandStatus = $"Library '{InstructionData.Title}' saved.";
            };
            return "";
        }
        return "Error: No target to save to.";
    }

    public void Update()
    {
        if (_isCodeChanged)
        {
            CodeFormatter.OnCodeChanged();
            _isCodeChanged = false;
        }
        CodeFormatter.Update(CaretPos, ImGui.GetMousePos(), GetTextPositionFromMouse(false));
    }

    public bool HasFocus => KeyHandler.Editor == this;

    public unsafe void Draw(Vector2 pos, Vector2 size, string id)
    {
        var padding = ImGui.GetStyle().FramePadding;
        _textAreaSize = size;
        float scrollHeight = size.y;
        ImGui.BeginChild(id, size, true);
        _textAreaOrigin = pos;
        _textAreaSize = size;
        var posPrev = ImGui.GetCursorScreenPos();

        var linePos = _textAreaOrigin;
        linePos.x += 4.8f * CharWidth;
        ImGui
            .GetWindowDrawList()
            .AddLine(
                linePos,
                new Vector2(linePos.x, linePos.y + LineHeight * (Lines.Count + 0.3f)),
                ICodeFormatter.ColorLineNumber,
                1.5f
            );

        ImGuiListClipperPtr clipper = new ImGuiListClipperPtr(
            ImGuiNative.ImGuiListClipper_ImGuiListClipper()
            );

        clipper.Begin(Lines.Count);

        Vector2 mousePos = ImGui.GetMousePos();

        if (ScrollToCaret > 0)
        {
            float lineHeight = LineHeight;
            float lineSpacing = ImGui.GetStyle().ItemSpacing.y;

            float pageHeight = (Lines.Count * lineHeight) - ImGui.GetScrollMaxY();
            float scrollY = ImGui.GetScrollY();
            float viewTop = _scrollY;
            float viewBottom = _scrollY + pageHeight;

            float caretTop = CaretLine * lineHeight;
            float caretBottom = caretTop + lineHeight;

            if (caretTop < viewTop)
            {
                scrollY = caretTop;
            }
            else if (caretBottom > viewBottom)
            {
                scrollY = caretBottom - pageHeight + lineSpacing;
            }

            ImGui.SetScrollY(Math.Min(scrollY, ImGui.GetScrollMaxY()));
            ScrollToCaret -= 1;
        }

        _scrollY = ImGui.GetScrollY();

        _firstLineIndex = -1;

        var selection = Selection.Sorted();

        while (clipper.Step())
        {
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var ppos = ImGui.GetCursorScreenPos();
                CodeFormatter.DrawLine(i, selection);

                if (i == CaretLine && HasFocus)
                {
                    _caretPixelPos = ppos;
                    _caretPixelPos.x +=
                        CharWidth * (CaretCol + ICodeFormatter.LineNumberOffset);
                    DrawCaret(_caretPixelPos);
                }
                if (_firstLineIndex == -1)
                {
                    _firstLineIndex = i;
                    _firstLineY = ppos.y;
                }
                ppos.y += LineHeight;
                ImGui.SetCursorScreenPos(ppos);
            }
        }

        if (EnableAutoComplete)
        {
            var completePos = _caretPixelPos + new Vector2(0, 1.5f * LineHeight);
            ImGui.SetCursorScreenPos(_caretPixelPos);
            CodeFormatter.DrawAutocomplete(completePos);
        }

        clipper.End();

        CodeFormatter.AfterDrawLines(_textAreaOrigin, _textAreaSize);

        ImGui.EndChild();
        ImGui.SetCursorScreenPos(posPrev);
    }

    public void DrawCaret(Vector2 pos)
    {

        if (KeyHandler.Editor != this)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var height = LineHeight;
        if (LineSpacingOffset < 0)
        {
            height = height - LineSpacingOffset;
            pos.y -= LineSpacingOffset / 2;
        }

        if (KeyHandler.Mode == KeyMode.Insert)
        {
            bool blinkOn = ((int)((ImGui.GetTime() - TimeLastAction) * 2) % 2) == 0;
            if (blinkOn)
            {
                drawList.AddLine(
                    pos,
                    new Vector2(pos.x, pos.y + height),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)),
                    1.5f
                );
            }
        }
        else
        {
            // Draw a block cursor
            drawList.AddRect(
                new Vector2(pos.x - 1, pos.y - 1),
                new Vector2(pos.x + CharWidth, pos.y + height),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.7f, 1.0f))
            );
            drawList.AddRect(
                new Vector2(pos.x - 2, pos.y - 2),
                new Vector2(pos.x + 1 + CharWidth, pos.y + height + 1),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 1.0f))
            );
        }
    }

    public void DrawTooltip()
    {
        if (HasFocus && IsMouseInsideTextArea())
        {
            ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]);
            if (KeyHandler.IsMouseIdle(TooltipDelay / 1000.0f))
            {
                var pos = GetTextPositionFromMouse(false);
                if (pos.Col >= 0)
                    CodeFormatter.DrawTooltip(ImGui.GetMousePos());
            }
            ImGui.PopFont();
        }
    }
}

public class EditorTab
{
    public List<Editor> Editors;
    public string Title;
    public EditorWindow ParentWindow;

    public EditorTab(EditorWindow window, Editor editor, string title)
    {
        ParentWindow = window;
        editor.ParentTab = this;
        Editors = new List<Editor> { editor };
        Title = title;
    }

    public int AddEditor(Editor editor)
    {
        editor.ParentTab = this;
        Editors.Add(editor);
        return Editors.Count - 1;
    }

    public Editor this[int index]
    {
        get { return Editors[index]; }
    }

    public void Save()
    {
        Editors[0].Save();
    }

    public void ClearExtraEditors()
    {
        while (Editors.Count > 1)
            Editors.RemoveAt(Editors.Count - 1);
    }

    public void Draw(float availHeight)
    {
        ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]);
        int n = Editors.Count;
        var p0 = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        var spacing = ImGui.GetStyle().ItemSpacing.x;
        avail.y = availHeight;
        avail.x = avail.x / n - spacing * (n - 1) / n;
        for (int i = 0; i < n; i++)
        {
            var editor = Editors[i];
            editor.Update();
            editor.Draw(p0, avail, $"##editorpane{i}");
            if (i < n - 1)
                ImGui.SameLine();
            p0.x += avail.x + spacing;
        }
        ImGui.PopFont();
        ImGui.SetCursorScreenPos(new Vector2(p0.x, p0.y + avail.y + ImGui.GetStyle().ItemSpacing.y));
    }
}

public class EditorWindow
{
    public KeyMode KeyMode;
    public static bool UseNativeEditor = false;
    KeyHandler KeyHandler;

    public List<EditorTab> Tabs = new List<EditorTab>();

    private int _activeTabIndex = 0;
    private int _activeEditorIndex = 0;
    public EditorTab ActiveTab => Tabs[_activeTabIndex];
    public Editor ActiveEditor
    {
        get
        {
            if (_activeEditorIndex < 0 || _activeEditorIndex >= ActiveTab.Editors.Count)
                _activeEditorIndex = 0;
            return ActiveTab[_activeEditorIndex];
        }
    }

    public void SetActiveEditor(int editorIndex)
    {
        _activeEditorIndex = Mathf.Clamp(editorIndex, 0, ActiveTab.Editors.Count - 1);
    }

    public EditorTab MotherboardTab => Tabs[0];

    public List<StyledLine> Lines => ActiveEditor.Lines;
    public string Code => ActiveEditor.Code;
    public int CaretLine => ActiveEditor.CaretLine;
    public int CaretCol => ActiveEditor.CaretCol;
    public TextPosition CaretPos => ActiveEditor.CaretPos;

    public TextRange Selection => ActiveEditor.Selection;

    bool LimitExceeded => ActiveTab[0].LimitExceeded;
    string CommandStatus => ActiveTab[0].CommandStatus;

    private string Title = "IC10 Editor";

    public EditorWindow(ProgrammableChipMotherboard pcm)
    {
        KeyHandler = new KeyHandler(this);
        Tabs.Add(new EditorTab(this, new Editor(KeyHandler, pcm), "Motherboard"));
    }

    private bool Show = false;

    private List<InstructionData> _libraryCodes = new List<InstructionData>();

    List<InstructionData> LibraryCodes => _libraryCodes;
    HashSet<string> _libraryTitles;
    private List<InstructionData> _librarySearchResults = new List<InstructionData>();

    private bool _librarySearchVisible = false;
    private string _librarySearchText = "";
    public bool IsLibrarySearchVisible => _librarySearchVisible;

    private bool _librarySearchJustOpened = false;
    private int _librarySelectedIndex = -1;

    public async UniTask PublishLibrary(InstructionData data)
    {
        try
        {
            await data.PublishToWorkshop();
        }
        finally
        {
            await LoadLibraries();
        }
    }

    public async UniTask LoadLibraries()
    {
        var items = await NetworkManager.GetLocalAndWorkshopItems(
            SteamTransport.WorkshopType.ICCode
        );

        var libs = new List<InstructionData>();
        var titles = new HashSet<string>();

        foreach (var item in items)
        {
            InstructionData data = InstructionData.GetFromFile(item.FilePathFullName);
            data.ItemWrapper = item;
            libs.Add(data);
            titles.Add(data.Title);
        }

        await UniTask.SwitchToMainThread();
        _libraryCodes = libs;
        _libraryTitles = titles;

        if (_librarySearchVisible)
            PerformLibrarySearch(_librarySearchText);
    }

    public void ShowLibrarySearch()
    {
        _librarySearchVisible = true;
        _librarySearchJustOpened = true;

        ImGui.OpenPopup("Library Search");

        LoadLibraries().Forget();
    }

    Editor _previewEditor = null;
    ConfirmWindow _confirmDeleteLibWindow = null;
    public void DrawLibrarySearchWindow()
    {

        if (!_librarySearchVisible)
            return;

        bool open = true;
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2.0f);
        ImGui.SetNextWindowSize(new Vector2(1300, 800), ImGuiCond.FirstUseEver);
        if (
            ImGui.Begin("Library Search", ref open)
        )
        {
            if (_librarySearchJustOpened)
            {
                ImGui.SetKeyboardFocusHere();
                _librarySearchJustOpened = false;
            }

            ImGui.Text("Search libraries:");
            ImGui.SameLine();
            string oldSearchText = _librarySearchText;
            ImGui.InputText(
                "##LibrarySearch",
                ref _librarySearchText,
                256,
                ImGuiInputTextFlags.EnterReturnsTrue
            );

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text(
                    "Load first matching entry with Enter key, or any entry with double-click."
                );
                ImGui.EndTooltip();
            }

            if (oldSearchText != _librarySearchText)
                PerformLibrarySearch(_librarySearchText);

            // Search if Enter pressed or text changed
            if (
                (ImGui.IsItemDeactivatedAfterEdit()
                || ImGui.IsItemFocused()) && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
            )
            {
                if (_librarySearchResults.Count > 0)
                    LoadLibraryEntry(_librarySearchResults[0]);
            }

            ImGui.SameLine();

            bool libExists = _libraryTitles.Contains(_librarySearchText) || string.IsNullOrWhiteSpace(_librarySearchText);

            if (libExists)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ICodeFormatter.ColorFromHTML("gray"));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ICodeFormatter.ColorFromHTML("gray"));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, ICodeFormatter.ColorFromHTML("gray"));
            }
            if (ImGui.Button("New") && !libExists)
            {
                InputSourceCode.Paste(MotherboardTab[0].Code);
                InputSourceCode.Instance.SaveNewWithName(_librarySearchText, "");
                LoadLibraries().Forget();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                if (string.IsNullOrWhiteSpace(_librarySearchText))
                    ImGui.Text("Please enter a valid library name to the search field to create a new entry.");
                else if (libExists)
                    ImGui.Text($"Library \"{_librarySearchText}\" already exists.");
                else
                    ImGui.Text("Creates a new library entry using the name in the search box and the current code.");
                ImGui.EndTooltip();
            }


            if (libExists)
            {
                ImGui.PopStyleColor();
                ImGui.PopStyleColor();
                ImGui.PopStyleColor();
            }

            ImGui.Separator();

            // Show results
            if (_librarySearchResults.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No results found.");
            }
            else
            {
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
                ImGui.BeginChild("LibrarySearchResults", new Vector2(500, 600), true);
                for (int i = 0; i < _librarySearchResults.Count; i++)
                {
                    var lib = _librarySearchResults[i];

                    var entryLabel = "";
                    if (lib.WorkshopFileHandle != 0)
                        entryLabel = $"{lib.Title} by {lib.Author} (workshop)";
                    else
                        entryLabel = $"{lib.Title} by {lib.Author} (local)";
                    if (ImGui.Selectable(entryLabel, _librarySelectedIndex == i) || _librarySearchResults.Count == 1)
                    {
                        _librarySelectedIndex = i;

                        if (_previewEditor == null)
                        {
                            _previewEditor = new Editor(KeyHandler, lib);
                            _previewEditor.IsReadOnly = true;
                        }
                        _previewEditor.ResetCode(lib?.Instructions ?? "", false);
                    }

                    // Double-click to load
                    if (
                        ImGui.IsItemHovered()
                        && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)
                    )
                    {
                        LoadLibraryEntry(lib);
                    }
                }
                ImGui.EndChild();

                ImGui.SameLine();
                ImGui.BeginChild("LibrarySearchPreview", new Vector2(700, 600), true);

                if (
                    _librarySelectedIndex >= 0
                    && _librarySelectedIndex < _librarySearchResults.Count
                )
                {
                    var lib = _librarySearchResults[_librarySelectedIndex];
                    ImGui.PushItemWidth(300);
                    ImGui.Text($"Title:  {lib.Title}");
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(330);
                    if (ImGui.Button("Publish", buttonSize))
                    {
                        lib.PublishToWorkshop().Forget();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Delete", buttonSize))
                    {
                        _confirmDeleteLibWindow = new ConfirmWindow(
                            $"Are you sure to delete the library '{lib.Title}'?"
                        );
                        _confirmDeleteLibWindow.OnConfirm = () =>
                        {
                            InputSourceCode.DeleteInstruction(lib.DirectoryPath.Name);
                            LoadLibraries().Forget();
                            _librarySelectedIndex = -1;
                            _previewEditor = null;
                            _confirmDeleteLibWindow = null;
                        };
                    }

                    if (_confirmDeleteLibWindow != null)
                    {
                        if (!_confirmDeleteLibWindow.IsOpen)
                            _confirmDeleteLibWindow = null;
                        else
                            _confirmDeleteLibWindow.Draw();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Delete the library");
                        ImGui.EndTooltip();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Copy", buttonSize))
                        GameManager.Clipboard = lib.Instructions;

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Copy code to clipboard");
                        ImGui.EndTooltip();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Load", buttonSize))
                        LoadLibraryEntry(lib);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Load this library into the editor");
                        ImGui.EndTooltip();
                    }
                    ImGui.Text($"Author: {lib.Author}");
                    var date = DateTime.FromFileTimeUtc(lib.DateTime);
                    ImGui.Text($"Date:   {date.ToLocalTime()}");
                    ImGui.TextWrapped($"Description:");
                    ImGui.PopItemWidth();
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(605);
                    if (ImGui.Button("Save", buttonSize))
                        lib.SaveToFile(lib.DirectoryPath);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Save desription");
                        ImGui.EndTooltip();
                    }
                    ImGui.InputTextMultiline(
                        "##LibraryDescriptionEdit",
                        ref lib.Description,
                        1024,
                        new Vector2(675, 60)
                    );
                    ImGui.Separator();

                    float heightAvailable = ImGui.GetContentRegionAvail().y - 10;

                    _previewEditor.Update();
                    ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0);
                    _previewEditor.Draw(
                        ImGui.GetCursorScreenPos(),
                        new Vector2(670, heightAvailable),
                        "##LibraryPreviewEditor"
                    );
                    ImGui.PopStyleVar();
                }
                ImGui.EndChild();

                ImGui.PopStyleVar();
            }

            ImGui.Separator();
            if (ImGui.Button("Close"))
                _librarySearchVisible = false;
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                _librarySearchVisible = false;

            ImGui.End();
        }
        ImGui.PopStyleVar();

        if (!open)
            _librarySearchVisible = false;
    }

    private void PerformLibrarySearch(string query)
    {
        _librarySearchResults.Clear();

        // if (string.IsNullOrWhiteSpace(query))
        // return;

        string q = query.Trim().ToLowerInvariant();

        foreach (var lib in _libraryCodes)
        {
            if (
                string.IsNullOrEmpty(q)
                || lib.Title.ToLowerInvariant().Contains(q)
                || lib.Instructions.ToLowerInvariant().Contains(q)
            )
            {
                _librarySearchResults.Add(lib);
            }
        }
    }

    private void LoadLibraryEntry(InstructionData lib)
    {
        if (lib == null)
            return;

        var editor = new Editor(KeyHandler, lib);
        editor.ResetCode(lib.Instructions);
        Tabs.Add(new EditorTab(this, editor, lib.Title));
        _activeTabIndex = Tabs.Count - 1;
        _librarySearchVisible = false;
    }

    public void SwitchToNativeEditor()
    {
        UseNativeEditor = true;
        Show = false;

        // localPosition was set to -10000,-10000,0 to hide the native editor, so set it back to 0,0,0 to show it
        InputSourceCode.Instance.Window.localPosition = new Vector3(0, 0, 0);
        KeyManager.RemoveInputState("ic10editorinputstate");
        InputSourceCode.Paste(MotherboardTab[0].Code);
    }

    public void Confirm()
    {
        if (LimitExceeded)
        {
            ActiveTab[0].CommandStatus = LimitExceededMessage;
            return;
        }
        ActiveTab.Save();
        if (!IsMotherboard)
            LoadLibraries().Forget();
        HideWindow();
    }

    public void Export()
    {
        if (IsMotherboard)
        {
            if (LimitExceeded)
            {
                ActiveTab[0].CommandStatus = LimitExceededMessage;
                return;
            }
            Confirm();
            MotherboardTab[0].PCM.Export();
        }
        else
        {
            // Apply code to motherboard tab
            MotherboardTab[0].ResetCode(ActiveEditor.Code);
            _activeTabIndex = 0;
        }
    }

    public void HideWindow()
    {
        if (Show == false)
            return;

        Show = false;
        KeyManager.RemoveInputState("ic10editorinputstate");
        if (InputWindow.InputState == InputPanelState.Waiting)
            InputWindow.CancelInput();
        if (WorldManager.IsGamePaused)
            InputSourceCode.Instance.PauseGameToggle(false);
        InputSourceCode.Instance.ButtonInputCancel();

        // This fixes following behavior:
        // 1. Open IC10 editor while alt key is pressed (e.g. laptop)
        // 2. Close IC10 editor with cancel button
        // -> Right click action on any tool not working until alt key is pressed again
        InputMouse.SetMouseControl(false);
    }

    public void ShowWindow()
    {
        Show = true;
        KeyHandler._isClosing = false;
        KeyManager.SetInputState("ic10editorinputstate", KeyInputState.Typing);

        if (VimEnabled)
            KeyHandler.Mode = KeyMode.VimNormal;

        if (!WorldManager.IsGamePaused && PauseOnOpen)
            InputSourceCode.Instance.PauseGameToggle(true);

        InputSourceCode.Instance.RectTransform.localPosition = new Vector3(-10000, -10000, 0);
    }

    public bool IsInitialized = false;

    public void SetTitle(string title)
    {
        Title = title;
    }

    public void HandleInput(bool hasFocus)
    {
        KeyHandler.HandleInput(hasFocus);
    }

    public void ShowNativeWindow(HelpMode mode)
    {
        foreach (var window in InputSourceCode.Instance.HelpWindows)
            if (window.HelpMode == mode)
                window.ToggleVisibility();
    }

    private bool _helpWindowVisible = false;
    private bool _debugWindowVisible = false;

    public void DrawHeader()
    {
        // rounded buttons style
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5.0f);

        if (ImGui.Button($"Library", buttonSize))
            ShowLibrarySearch();

        ImGui.SameLine();

        if (ImGui.Button("Clear", buttonSize))
            ActiveTab[0].ClearCode();

        ImGui.SameLine();

        if (ImGui.Button("Copy", buttonSize))
            GameManager.Clipboard = Code;

        ImGui.SameLine();

        if (ImGui.Button("Paste", buttonSize))
        {
            ActiveTab[0].ClearCode();
            ActiveTab[0].Insert(GameManager.Clipboard);
        }

        ImGui.SameLine();

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 2 * ImGui.GetStyle().ItemSpacing.x);

        if (ImGui.Button("?", smallButtonSize))
            _helpWindowVisible = !_helpWindowVisible;

        ImGui.SameLine();

        float comboWidth = 130;

        ImGui.SetCursorPosX(
            ImGui.GetWindowWidth()
                - comboWidth
                - 3 * smallButtonSize.x
                - buttonSize.x
                - ImGui.GetStyle().FramePadding.x * 4
                - ImGui.GetStyle().ItemSpacing.x * 3
        );

        var formatters = CodeFormatters.FormatterNames;
        var formatter = ActiveTab[0].CodeFormatter;

        ImGui.PushItemWidth(comboWidth);
        if (ImGui.BeginCombo("##CodeFormat", formatter.Name))
        {
            foreach (var fmt in formatters)
            {
                bool isSelected = fmt == formatter.Name;
                if (ImGui.Selectable(fmt, isSelected))
                {
                    var code = ActiveTab[0].Code;
                    ActiveTab[0].CodeFormatter = CodeFormatters.GetFormatter(fmt);
                    ActiveTab[0].CodeFormatter.Editor = ActiveTab[0];
                    ActiveTab[0].CodeFormatter.ResetCode(code);
                    ActiveTab.ClearExtraEditors();
                }
                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();

        ImGui.SameLine();

        if (ImGui.Button("s(x)", smallButtonSize))
            ShowNativeWindow(HelpMode.SlotVariables);

        ImGui.SameLine();

        if (ImGui.Button("x", smallButtonSize))
            ShowNativeWindow(HelpMode.Variables);

        ImGui.SameLine();

        if (ImGui.Button("f", smallButtonSize))
            ShowNativeWindow(HelpMode.Functions);

        ImGui.SameLine();

        bool isPaused = WorldManager.IsGamePaused;
        if (ImGui.Button(isPaused ? "Resume" : "Pause", buttonSize))
            InputSourceCode.Instance.PauseGameToggle(!isPaused);

        ImGui.PopStyleVar();
    }

    private static uint _colorGood = ICodeFormatter.ColorFromHTML("green");
    private static uint _colorWarning = ICodeFormatter.ColorFromHTML("orange");
    private static uint _colorBad = ICodeFormatter.ColorFromHTML("red");
    private static uint _colorDefault = ICodeFormatter.ColorFromHTML("white");

    public bool IsMotherboard => ActiveTab[0].PCM != null;
    public bool EnforceLineLimit => IsMotherboard && Settings.EnforceLineLimit;
    public bool EnforceByteLimit => IsMotherboard && Settings.EnforceByteLimit;

    public float FooterHeight => 2 * ImGui.GetTextLineHeightWithSpacing() + 2 * ImGui.GetStyle().FramePadding.y;

    public void DrawFooter()
    {
        ImGui.SetCursorPosX(ImGui.GetStyle().FramePadding.x);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5.0f);
        ImGui.Text($"{CaretLine,3}/{CaretCol,2},");
        ImGui.SameLine();

        var pos = ImGui.GetCursorScreenPos();
        var px0 = ImGui.GetCursorPosX();
        var psx0 = pos.x;
        var code = Code;

        var drawList = ImGui.GetWindowDrawList();
        var drawLimit = (bool enforce, int n, int limit, string unit) =>
        {
            uint color = _colorDefault;
            var sValue = $" {n.ToString().PadLeft(2, ' ')}";
            if (enforce)
            {
                sValue += $"/{limit}";
                if (n < limit * 0.9f)
                    color = _colorGood;
                else if (n <= limit)
                    color = _colorWarning;
                else
                    color = _colorBad;
            }
            drawList.AddText(pos, color, sValue);
            pos.x += sValue.Length * CharWidth;
            drawList.AddText(pos, _colorDefault, $" {unit}");
            pos.x += (unit.Length + 1) * CharWidth;
        };

        drawLimit(EnforceLineLimit, Lines.Count, 128, "lines,");
        drawLimit(EnforceLineLengthLimit, Lines.Max(line => line.Text.Length), 90, "chars,");
        drawLimit(EnforceByteLimit, code.Length + Lines.Count - 1, 4096, "bytes");
        pos.x += 4 * CharWidth;

        ImGui.SetCursorPosX(px0 + pos.x - psx0);

        ActiveTab[0].CodeFormatter.DrawButtons();

        ImGui.SameLine();

        ImGui.SetCursorPosX(
            ImGui.GetWindowWidth()
                - 3 * buttonSize.x
                - ImGui.GetStyle().FramePadding.x * 4
                - ImGui.GetStyle().ItemSpacing.x
        );
        if (ImGui.Button("Cancel", buttonSize))
            HideWindow();

        ImGui.SameLine();

        bool limitExceeded = LimitExceeded;
        if (limitExceeded)
        {
            ImGui.PushItemFlag(ImGuiItemFlags.Disabled, true);
            ImGui.PushStyleColor(ImGuiCol.Button, ICodeFormatter.ColorFromHTML("gray"));
        }

        if (ImGui.Button(IsMotherboard ? "Export" : "To MB", buttonSize))
            Export();

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(
                IsMotherboard
                    ? "Close the editor and export the code to IC10 Chip."
                    : "Apply code to the Motherboard tab"
            );
            ImGui.EndTooltip();
        }

        ImGui.SameLine();
        if (IsMotherboard)
        {
            if (ImGui.Button("Confirm", buttonSize))
                Confirm();
        }
        else
        {
            if (ImGui.Button("Save", buttonSize))
                KeyHandler.CommandStatus = ActiveTab[0].Save();
        }

        if (limitExceeded)
        {
            ImGui.PopStyleColor();
            ImGui.PopItemFlag();
        }

        KeyHandler.DrawStatus();
        ActiveTab[0].CodeFormatter.DrawStatus(ImGui.GetCursorScreenPos());

        ImGui.PopStyleVar();
    }

    private bool _hasFocus = false;
    private int _openGameWindows = 0;
    private bool _didGameWindowOpen = false;
    private bool _didGameWindowClose = false;

    public bool HasFocus => _hasFocus && !_librarySearchVisible;

    public void CalcDidGameWindowOpen()
    {
        int count = 0;
        count += Stationpedia.Instance.IsVisible ? 1 : 0;

        foreach (var window in InputSourceCode.Instance.HelpWindows)
            count += window.IsVisible ? 1 : 0;

        _didGameWindowOpen = count > 0 && _openGameWindows == 0;
        _didGameWindowClose = count == 0 && _openGameWindows > 0;
        _openGameWindows = count;
    }

    private Queue<double> _renderTimes = new();
    private System.Diagnostics.Stopwatch _renderStopwatch;

    public void Draw()
    {
        if (!Show)
            return;

        if (_debugWindowVisible)
            _renderStopwatch = System.Diagnostics.Stopwatch.StartNew();

        // make sure the native editor is hidden
        InputSourceCode.Instance.Window.localPosition = new Vector3(-10000, -10000, 0);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
        var io = ImGui.GetIO();
        ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]);

        if (!IsInitialized)
        {
            var displaySize = ImGui.GetIO().DisplaySize;
            var windowSize = new Vector2(
                Math.Min(1200, displaySize.x - 100),
                displaySize.y - 100
            );
            var windowPos = 0.5f * (displaySize - windowSize);

            windowPos.x = Mathf.Round(windowPos.x);
            windowPos.y = Mathf.Round(windowPos.y);

            windowPos = Scale * windowPos;

            ImGui.SetNextWindowSize(windowSize);
            ImGui.SetNextWindowPos(windowPos);
            IsInitialized = true;
        }

        CalcDidGameWindowOpen();
        if (CollapseOnGameWindow)
        {
            if (_didGameWindowOpen)
                ImGui.SetNextWindowCollapsed(true);
            if (_didGameWindowClose)
                ImGui.SetNextWindowCollapsed(false);
        }

        ImGui.Begin(Title + "###IC10EditorWindow", ImGuiWindowFlags.NoSavedSettings);
        ImGui.GetStyle().Colors[(int)ImGuiCol.Tab] = new Vector4(0.2f, 0.2f, 0.2f, 1.0f);

        ImGui.SetWindowFontScale(Scale);
        UpdateTextSize();
        DrawHeader();

        if (HasFocus)
            HandleInput(_hasFocus);

        _hasFocus = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

        if (ImGui.BeginTabBar("EditorTabs"))
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                var tab = Tabs[i];
                bool isOpen = _activeTabIndex == i;
                if (
                    ImGui.BeginTabItem(
                        $"{tab.Title} ###{i}",
                        isOpen ? ImGuiTabItemFlags.SetSelected : 0
                    )
                )
                {
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
                        CloseTab(i);
                    tab.Draw(ImGui.GetContentRegionAvail().y - FooterHeight);
                    ImGui.EndTabItem();
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    _activeTabIndex = i;
                if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
                    CloseTab(i);
            }
        }
        ImGui.EndTabBar();

        DrawFooter();
        DrawLibrarySearchWindow();

        if (ActiveTab[0]._confirmWindow != null)
            ActiveTab[0]._confirmWindow.Draw();

        ImGui.End();
        ImGui.PopStyleColor();

        if (!IsLibrarySearchVisible)
            foreach (var editor in ActiveTab.Editors)
                editor.DrawTooltip();

        DrawHelpWindow();
        DrawDebugWindow();

        ImGui.PopFont();
    }

    public void CloseTab(int index = -1)
    {
        index = index == -1 ? _activeTabIndex : index;
        if (index <= 0 || index >= Tabs.Count)
            return;
        if (Tabs.Count <= 1)
            return;
        Tabs.RemoveAt(index);
        if (_activeTabIndex >= Tabs.Count)
            _activeTabIndex = Tabs.Count - 1;
    }

    public void PreviousTab()
    {
        _activeTabIndex = (_activeTabIndex - 1 + Tabs.Count) % Tabs.Count;
    }

    public void NextTab()
    {
        _activeTabIndex = (_activeTabIndex + 1) % Tabs.Count;
    }

    public void SetTab(int index)
    {
        if (index < 0 || index >= Tabs.Count)
        {
            L.Warning($"SetTab: index {index} out of range");
            return;
        }
        _activeTabIndex = index;
    }

    private void DrawBoolOption(string label, ConfigEntry<bool> entry)
    {
        var value = entry.Value;
        if (ImGui.Checkbox(label, ref value))
            entry.BoxedValue = value;
    }

    private void DrawFloatOption(string label, ConfigEntry<float> entry, float min, float max)
    {
        var value = entry.Value;
        ImGui.PushItemWidth(ImGui.CalcTextSize("000000.00").x);
        if (ImGui.InputFloat(label, ref value))
            entry.BoxedValue = value;
        ImGui.PopItemWidth();
        ImGui.SameLine();
        if (ImGui.Button("Reset"))
        {
            entry.BoxedValue = 1.0f;
            value = 1.0f;
        }
    }

    private void DrawFloatOption(string label, ConfigEntry<float> entry)
    {
        var value = entry.Value;
        ImGui.PushItemWidth(ImGui.CalcTextSize("000000.00").x);
        if (ImGui.InputFloat(label, ref value))
            entry.BoxedValue = value;
        ImGui.PopItemWidth();
    }

    private void DrawIntOption(string label, ConfigEntry<int> entry)
    {
        var value = entry.Value;
        ImGui.PushItemWidth(ImGui.CalcTextSize("000000.00").x);
        if (ImGui.InputInt(label, ref value, -20, 20))
            entry.BoxedValue = value;
        ImGui.PopItemWidth();
    }

    public void DrawHelpWindow()
    {
        if (!_helpWindowVisible)
            return;
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
        ImGui.SetNextWindowSize(Scale * new Vector2(600, 400), ImGuiCond.FirstUseEver);
        ImGui.Begin(
            "IC10 Editor Help",
            ref _helpWindowVisible,
            ImGuiWindowFlags.NoSavedSettings
        );
        ImGui.SetWindowFontScale(Mathf.Clamp(Scale, 0.5f, 5.0f));

        ImGui.TextWrapped(
            "This is the IC10 Editor. It allows you to edit the source code of IC10 programs with syntax highlighting, undo/redo, and other features."
        );

        if (ImGui.Button("Native", buttonSize))
            SwitchToNativeEditor();

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Switch to the native Stationeers IC10 editor.");
            ImGui.EndTooltip();
        }

        ImGui.Separator();
        ImGui.Text("\nConfiguration:");
        DrawBoolOption("Pause Game on Open", IC10EditorPlugin.PauseOnOpen);
        DrawBoolOption("Collapse when other window is open", IC10EditorPlugin.CollapseOnGameWindow);
        DrawBoolOption("Enforce 90 Characters per Line Limit", IC10EditorPlugin.EnforceLineLengthLimit);
        DrawBoolOption("Enforce 128 Lines Limit", IC10EditorPlugin.EnforceLineLimit);
        DrawBoolOption("Enforce 4096 Bytes Limit", IC10EditorPlugin.EnforceByteLimit);
        DrawFloatOption("UI Scaling", IC10EditorPlugin.ScaleFactor, 0.25f, 5.0f);
        DrawIntOption("Line Spacing Offset", IC10EditorPlugin.LineSpacingOffset);
        DrawFloatOption("Toolitp delay (ms)", IC10EditorPlugin.TooltipDelay);
        DrawBoolOption("VIM bindings", IC10EditorPlugin.VimBindings);
        DrawBoolOption(
            "Auto Completion (insert with Tab key)",
            IC10EditorPlugin.EnableAutoComplete
        );
        DrawBoolOption("Relative line numbers", IC10EditorPlugin.RelativeLineNumbers);
        DrawBoolOption("Apply patch to keep selected IC10 in computer (Experimental)", IC10EditorPlugin.RestoreSelectedHousing);
        ImGui.Checkbox("Show debug window", ref _debugWindowVisible);

        ImGui.Separator();
        ImGui.NewLine();
        ImGui.Text("Colors");
        ImGui.SameLine();
        if (ImGui.Button("Reload"))
        {
            IC10EditorPlugin.Instance.Config.Reload();
            IC10EditorPlugin.LoadColorConfig();
        }

        ImGui.TextWrapped(
                "Color settings can be changed in two ways\n"
                + "- BepInEx/config/aproposmath-stationeers-ic10-editor.cfg\n"
                + "\tedit the file and then click the reload button above\n"
                + "- in the Stationeers Launchpad config menu on game startup\n"
                + "\n"
                );

        ImGui.Separator();

        ImGui.TextWrapped(
            "\nKeyboard Shortcuts:\n"
                + "\n"
                + "Arrow Keys            Move caret\n"
                + "Home/End              Move caret to start/end of line\n"
                + "Page Up/Down          Move caret up/down by 20 lines\n"
                + "Shift+Arrow           Select text while moving caret\n"
                + "Tab                   Autocomplete/Indent\n"
                + "Ctrl + Q              Quit (no confirm, see note below)\n"
                + "Ctrl + W              Close tab (only for library code tabs)\n"
                + "Ctrl + S              Save\n"
                + "Ctrl + E              Motherboard: Save + export code to ic chip\n"
                + "Ctrl + E              Library Tab: Apply code to Motherboard tab\n"
                + "Ctrl + Z              Undo\n"
                + "Ctrl + Y              Redo\n"
                + "Ctrl + C              Copy selected code\n"
                + "Ctrl + V              Paste code from clipboard\n"
                + "Ctrl + A              Select all code\n"
                + "Ctrl + X              Cut selected code\n"
                + "Ctrl + Arrow          Move caret by word\n"
                + "Ctrl + Click          Open Stationpedia page of word at cursor\n"
                + "Ctrl + Space          Next tab\n"
                + "Ctrl + Shift + Space  Previous tab\n"
                + "Ctrl + Number         Switch to tab <Number>\n\n"
        );

        ImGui.Separator();

        ImGui.TextWrapped(
            "\nNotes:\n"
                + "\n"
                + "Closing the editor via Ctrl+Q key or Cancel button will not ask for confirmation, BUT you can always reopen the editor and Undo (Ctrl+Z) to get the state before cancelling.\n"
        );

        ImGui.Separator();

        ImGui.TextWrapped(
            "\nVIM Mode - Supported Commands:\n"
                + "\n"
                + "Movements (with optional number prefix):\n"
                + "h j, k, l, w, b, 0, $, gg, G, *, #, <C-u>, <C-d>\n\n"
                + "Editing (with optional number and movement or search):\n"
                + "i I a A c C d D dd o O x y yy p ~ << >> u <C-r>\n\n"
                + "Search:\n"
                + "f t gf\n\n"
                + "Other:\n"
                + ". ; n N :w :wq :q\n\n"
                + "Notes:\n"
                + "'gf' opens Stationpedia page of hash/name at cursor\n\n"
                + "'.'  is not working for commands that switch to insert mode\n\n"
        );

        ImGui.Separator();
        ImGui.End();
        ImGui.PopStyleColor();
    }

    public void DrawDebugWindow()
    {
        if (!_debugWindowVisible)
            return;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
        ImGui.SetNextWindowSize(Scale * new Vector2(600, 400), ImGuiCond.FirstUseEver);
        ImGui.Begin(
            "IC10 Debug Window",
            ref _debugWindowVisible,
            ImGuiWindowFlags.NoSavedSettings
        );
        ImGui.SetWindowFontScale(Mathf.Clamp(Scale, 0.5f, 5.0f));

        double avgRenderTime = 0.0;
        double maxRenderTime = 0.0;

        foreach (var time in _renderTimes)
        {
            avgRenderTime += time;
            if (time > maxRenderTime)
                maxRenderTime = time;
        }
        if (_renderTimes.Count > 0)
            avgRenderTime /= _renderTimes.Count;

        avgRenderTime = (avgRenderTime * 1000000.0);
        maxRenderTime = (maxRenderTime * 1000000.0);

        var e = ActiveEditor;

        ImGui.Text($"Render Time: {avgRenderTime:F0} us avg, {maxRenderTime:F0} us max");
        ImGui.Text($"ScrollY: {e._scrollY:F2}");
        ImGui.Text(
            $"Textpos: {e._textAreaOrigin.x:F2}, {e._textAreaOrigin.y:F2}, {e._textAreaOrigin.y + e._scrollY:F2}"
        );
        ImGui.Text($"Textsize: {e._textAreaSize.x:F2}, {e._textAreaSize.y:F2}");
        ImGui.Text($"CaretPixelPos: {e._caretPixelPos.x:F2}, {e._caretPixelPos.y:F2}");
        ImGui.Text($"MousePos: {ImGui.GetMousePos().x:F2}, {ImGui.GetMousePos().y:F2}");
        ImGui.Text(
            $"Mouse relative to text area: {ImGui.GetMousePos().x - e._textAreaOrigin.x:F2}, {ImGui.GetMousePos().y - (e._textAreaOrigin.y + e._scrollY):F2}"
        );
        ImGui.Text($"LineNumberOffset: {ICodeFormatter.LineNumberOffset}");
        ImGui.Text($"Mouse caret pos: {e.GetTextPositionFromMouse(false)}");
        var mousePos = ImGui.GetMousePos();
        float c1 = (mousePos.x - e._textAreaOrigin.x + ImGui.GetStyle().FramePadding.x) / CharWidth;
        float c2 = c1 - ICodeFormatter.LineNumberOffset;
        ImGui.Text($"Mouse caret col: {c1}, {c2}");
        ImGui.Text(
            $"Mouse line: {(ImGui.GetMousePos().y - e._textAreaOrigin.y) / LineHeight:F2}"
        );
        ImGui.Text($"CaretPixelPos: {e._caretPixelPos.x:F2}, {e._caretPixelPos.y:F2}");
        ImGui.Text($"Font w/h: {CharWidth:F2}, {LineHeight:F2}");

        if (_renderStopwatch != null)
        {
            double seconds = _renderStopwatch.Elapsed.TotalSeconds;
            _renderTimes.Enqueue(seconds);
            while (_renderTimes.Count > 100)
                _renderTimes.Dequeue();
        }

        ImGui.Separator();
        ImGui.End();
        ImGui.PopStyleColor();
    }

}
