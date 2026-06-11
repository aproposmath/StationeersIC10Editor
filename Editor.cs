namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Assets.Scripts;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.UI;

using Cysharp.Threading.Tasks;

using ImGuiNET;

using UnityEngine;

using static ImGuiUtils;
using static Settings;
using static Utils;

public class ConfirmWindow
{
    public static int NumWindowsOpen = 0;
    public string Title;
    public string Message;
    public bool IsOpen = true;
    public string InputPrompt = null;
    public string UserInput = "";

    public Action OnConfirm = delegate { };
    private bool _justOpened = true;

    public ConfirmWindow(string title, string message, string inputPrompt = null)
    {
        Title = title;
        Message = message;
        IsOpen = true;
        InputPrompt = inputPrompt;
        UserInput = "";
        _justOpened = true;
        NumWindowsOpen++;
    }

    public void Close()
    {
        IsOpen = false;
        ImGui.CloseCurrentPopup();
        NumWindowsOpen--;
    }

    public void Confirm()
    {
        OnConfirm?.Invoke();
        IsOpen = false;
        ImGui.CloseCurrentPopup();
        NumWindowsOpen--;
    }

    public void Draw()
    {
        if (_justOpened)
            ImGui.OpenPopup(Title);
        var open = true;
        if (
            ImGui.BeginPopupModal(
                Title,
                ref open,
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize
            )
        )
        {
            using (new ScopedStyleColor(ImGuiCol.Text, ICodeFormatter.ColorWarning))
                if (!string.IsNullOrEmpty(Message))
                    ImGui.Text(Message);
            if (string.IsNullOrEmpty(InputPrompt) == false)
            {
                ImGui.Text(InputPrompt);
                ImGui.SameLine();
                if (_justOpened)
                    ImGui.SetKeyboardFocusHere(0);
                ImGui.InputText("##user_input", ref UserInput, 256, ImGuiInputTextFlags.EnterReturnsTrue);
            }
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

            if (ImGui.Button("OK", Scale * new Vector2(100, 0)))
                Confirm();

            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                Close();

            if (ImGui.IsKeyPressed(ImGuiKey.Enter))
                Confirm();
            ImGui.EndPopup();
        }
        _justOpened = false;
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

public class Editor
{
    public object Target;
    public ProgrammableChipMotherboard PCM => Target as ProgrammableChipMotherboard;
    public VersionedScript Library => Target as VersionedScript;
    public bool IsMotherboard => PCM != null;

    public bool LimitExceeded => IsMotherboard && (EnforceLineLimit && CodeSize.NumLines > 128) || (EnforceByteLimit && CodeSize.NumBytes > 4096) || (EnforceLineLengthLimit && CodeSize.MaxLineLength > 90);
    public bool ExportLimitExceeded => (Settings.EnforceLineLimit && CodeSize.NumLines > 128) || (Settings.EnforceByteLimit && CodeSize.NumBytes > 4096) || (Settings.EnforceLineLengthLimit && CodeSize.MaxLineLength > 90);

    public bool HaveSelection => (bool)Selection;
    public KeyHandler KeyHandler;
    public bool IsReadOnly = false;

    public TextPosition _caretPos;

    public static float LineNumberOffset = 5.0f;
    public int ScrollToCaret = 0;
    protected double _timeLastAction = 0.0;
    protected bool _isCodeChanged = false;

    public double TimeLastAction => _timeLastAction;
    public KeyMode KeyMode => KeyHandler?.Mode ?? KeyMode.None;

    public LinkedList<EditorState> UndoList;
    public LinkedList<EditorState> RedoList;
    public ICodeFormatter CodeFormatter;
    public string Code => CodeFormatter.RawText;
    public List<StyledLine> Lines => CodeFormatter.Lines;
    public CodeSize CodeSize => CodeFormatter.CodeSize;
    public string CommandStatus = "";

    public EditorTab ParentTab = null;

    public ConfirmWindow _confirmWindow = null;

    public string FileName
    {
        get
        {
            if (IsMotherboard)
                return $"motherboard_id_{PCM.ReferenceId}";
            else if (Library != null)
                return Library.Data.Title.Trim().Replace(' ', '_').Replace('/', '_');
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
    public float _scrollX = 0.0f;
    public float _scrollY = 0.0f;
    private bool _hasHorizontalScrollbar = false;
    private bool _hasVerticalScrollbar = false;

    public bool IsMouseInsideTextArea()
    {
        Vector2 mousePos = ImGui.GetMousePos();
        float px = _textAreaOrigin.x;
        float py = _textAreaOrigin.y + ImGui.GetStyle().FramePadding.y;
        float maxX = px + _textAreaSize.x - (_hasVerticalScrollbar ? ImGui.GetStyle().ScrollbarSize : 0.0f);
        float maxY = py + _textAreaSize.y - (_hasHorizontalScrollbar ? ImGui.GetStyle().ScrollbarSize : 0.0f);
        return mousePos.x >= px
            && mousePos.x <= maxX
            && mousePos.y >= py
            && mousePos.y <= maxY;
    }

    public TextPosition GetTextPositionFromMouse(bool clampToTextArea = true)
    {
        Vector2 mousePos = ImGui.GetMousePos();

        int line =
            (int)((mousePos.y + LineSpacing - _firstLineY) / LineHeight) + _firstLineIndex;
        int column = (int)(
            (mousePos.x - _textAreaOrigin.x + _scrollX) / CharWidth
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
        if (lineIndex < 0 || lineIndex >= Lines.Count)
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
        var formatter = CodeFormatters.GetFormatterByMatching(code);
        if (typeof(ICodeFormatter) != CodeFormatter.GetType())
        {
            CodeFormatter = formatter;
            CodeFormatter.Editor = this;
        }
        CodeFormatter.ResetCode(code);
        CaretPos = new TextPosition(0, 0);
        _isCodeChanged = true;
    }

    public string Save(bool doCommit = false)
    {
        if (PCM)
        {
            if (LimitExceeded)
                return LimitExceededMessage;
            PCM.InputFinished(CodeFormatter.Compile());
            return "Saved to Motherboard";
        }
        if (Library != null)
        {
            var noChanges = Library.Data.Instructions == Code;
            if (doCommit)
                noChanges = noChanges && (Library.State == FileState.Unchanged);
            if (noChanges)
                return "No changes to " + (doCommit ? "commit" : "save") + $" state: {Library.State}";
            Library.Data.Instructions = Code;
            Library.Save();
            LibraryWindow.NeedsReload(Library);
            if (doCommit)
            {
                _confirmWindow = new ConfirmWindow($"Commit {Library.Data.Title}", null, "Message");
                _confirmWindow.OnConfirm = () => CommitAsync(Library, _confirmWindow.UserInput).Forget();
            }
            return $"Library '{Library.Data.Title}' saved.";
        }
        return "Error: No target to save to.";
    }

    public async UniTask CommitAsync(VersionedScript script, string message)
    {
        try
        {
            await LibraryWindow.CommitAsync(script, message);
            KeyHandler.CommandStatus = $"Version saved: {message}";
        }
        catch (Exception ex)
        {
            KeyHandler.CommandStatus = $"Failed to commit: {ex.Message}";
            L.Error(ex.Message);
            L.Error(ex.StackTrace);
        }
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

    public bool HasFocus => KeyHandler?.Editor == this;

    string lastId = null;
    string _lineNumbersId = null;

    private static List<string> _lineNumbersBuffer = [.. Enumerable.Range(-1000, 10001).Select(lineNumber => lineNumber.ToString().PadLeft(3) + ".")];

    public void DrawLineNumbers(Vector2 pos, Vector2 size, string id)
    {
        if (id != lastId)
        {
            lastId = id;
            _lineNumbersId = id + "_LineNumbers";
        }

        ImGui.SetNextWindowContentSize(size);
        ImGui.SetCursorScreenPos(pos);
        ImGui.BeginChild(_lineNumbersId, size, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings);

        var yEnd = pos.y + size.y;

        var drawList = ImGui.GetWindowDrawList();

        var linePos = pos + (LineNumberOffset - 0.8f) * CharWidth * Vector2.right;
        drawList.AddLine(
            linePos,
            linePos + size.y * Vector2.up,
            ICodeFormatter.ColorLineNumber,
            1.5f
        );

        pos.y = _firstLineY;
        var lineNumber = _firstLineIndex;
        while (pos.y < yEnd)
        {
            if (lineNumber == -1)
                break;
            drawList.AddText(
                pos,
                ICodeFormatter.ColorLineNumber,
                lineNumber > -1000 && lineNumber < 10000 ? _lineNumbersBuffer[lineNumber + 1000] : lineNumber.ToString() + "."
            );
            lineNumber++;
            pos.y += LineHeight;
        }
        ImGui.EndChild();
    }

    public unsafe void Draw(Vector2 pos, Vector2 size, string id)
    {
        using var _cbs = new ScopedStyleVar(ImGuiStyleVar.ChildBorderSize, 0);
        var _f = new ScopedFont(ImGui.GetIO().Fonts.Fonts[0]);
        var maxLineLength = Lines.Count > 0 ? Lines.Max(line => line.Length) : 0;
        var contentSize = new Vector2(CharWidth * (LineNumberOffset + maxLineLength + 1), LineHeight * Lines.Count);

        var lineNumbersWidth = CharWidth * LineNumberOffset;
        var lineNumbersOffset = new Vector2(lineNumbersWidth, 0);

        _textAreaOrigin = pos + lineNumbersOffset;
        _textAreaSize = size - lineNumbersOffset;

        ImGui.SetNextWindowContentSize(contentSize);
        ImGui.SetCursorScreenPos(_textAreaOrigin);
        ImGui.BeginChild(id, _textAreaSize, true, ImGuiWindowFlags.HorizontalScrollbar);

        var posPrev = ImGui.GetCursorScreenPos();

        var clipper = new ImGuiListClipperPtr(
            ImGuiNative.ImGuiListClipper_ImGuiListClipper()
            );

        clipper.Begin(Lines.Count);


        if (ScrollToCaret > 0)
        {
            var viewSize = contentSize - new Vector2(ImGui.GetScrollMaxX(), ImGui.GetScrollMaxY());

            var scrollX = ImGui.GetScrollX();
            var scrollY = ImGui.GetScrollY();

            static float adjustScroll(float caretStart, float caretSize, float viewStart, float viewSize1)
            {
                var caretEnd = caretStart + caretSize;
                var viewEnd = viewStart + viewSize1;
                if (caretStart < viewStart)
                    return caretStart - caretSize;
                else if (caretEnd > viewEnd)
                    return caretEnd - viewSize1 + caretSize;
                return viewStart;
            }

            scrollX = adjustScroll(CharWidth * CaretCol, CharWidth, scrollX, viewSize.x);
            scrollY = adjustScroll(CaretLine * LineHeight, LineHeight, scrollY, viewSize.y);

            ImGui.SetScrollX(Mathf.Clamp(scrollX, 0.0f, ImGui.GetScrollMaxX()));
            ImGui.SetScrollY(Mathf.Clamp(scrollY, 0.0f, ImGui.GetScrollMaxY()));
            ScrollToCaret -= 1;
        }

        _scrollX = ImGui.GetScrollX();
        _scrollY = ImGui.GetScrollY();
        _hasHorizontalScrollbar = ImGui.GetScrollMaxX() > 0.0f;
        _hasVerticalScrollbar = ImGui.GetScrollMaxY() > 0.0f;

        _firstLineIndex = -1;

        var selection = Selection.Sorted();

        while (clipper.Step())
        {
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var ppos = ImGui.GetCursorScreenPos();
                // ImGui.SetCursorScreenPos(ppos + CharWidth * Vector2.right);
                CodeFormatter.DrawLine(i, selection);

                if (i == CaretLine && HasFocus)
                {
                    _caretPixelPos = ppos + CharWidth * CaretCol * Vector2.right;
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

        DrawLineNumbers(pos, new Vector2(lineNumbersWidth, size.y), id);

        ImGui.SetCursorScreenPos(posPrev);
    }

    public void DrawCaret(Vector2 pos)
    {

        if (KeyHandler?.Editor != this)
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
            if (((int)((ImGui.GetTime() - TimeLastAction) * 2) % 2) == 0)
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
                new Vector2(pos.x + 1, pos.y + 1),
                new Vector2(pos.x + CharWidth, pos.y + height),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.7f, 1.0f))
            );
            drawList.AddRect(
                new Vector2(pos.x, pos.y),
                new Vector2(pos.x - 1 + CharWidth, pos.y + height - 1),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 1.0f))
            );
        }
    }

    public void DrawTooltip()
    {
        if (HasFocus && IsMouseInsideTextArea())
        {
            using var _ = new ScopedFont(ImGui.GetIO().Fonts.Fonts[0]);
            if (KeyHandler.IsMouseIdle(TooltipDelay / 1000.0f))
            {
                var pos = GetTextPositionFromMouse(false);
                if (pos.Col >= 0)
                    CodeFormatter.DrawTooltip(ImGui.GetMousePos());
            }
        }
    }
}

public class EditorTab
{
    public List<Editor> Editors;
    public EditorWindow ParentWindow;
    public string FilePath;
    public VersionedScript Script => FilePath != null && LibraryWindow.VersionedScriptsByPath.ContainsKey(FilePath) ? LibraryWindow.VersionedScriptsByPath[FilePath] : null;
    public FileHistoryWindow VersionWindow;

    public string Title => Script?.Title ?? "Motherboard";

    public EditorTab(EditorWindow window, Editor editor, string filePath = null)
    {
        FilePath = filePath;
        ParentWindow = window;
        editor.ParentTab = this;
        Editors = new List<Editor> { editor };
        VersionWindow = null;
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

    public void OpenVersionWindow()
    {
        if (Script == null)
            return;

        if (VersionWindow == null)
            VersionWindow = new FileHistoryWindow(Script);

        VersionWindow.Open();
    }

    private List<string> _editorIds = new List<string>();

    public void Draw(float availHeight)
    {
        using var _ = new ScopedFont(ImGui.GetIO().Fonts.Fonts[0]);
        var n = Editors.Count;
        var p0 = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        var spacing = ImGui.GetStyle().ItemSpacing.x;
        avail.y = availHeight;
        avail.x = avail.x / n - spacing * (n - 1) / n;
        for (var i = 0; i < n; i++)
        {
            if (_editorIds.Count <= i)
                _editorIds.Add($"##editorpane{_editorIds.Count}");
            var editor = Editors[i];
            editor.Update();
            editor.Draw(p0, avail, _editorIds[i]);
            if (i < n - 1)
                ImGui.SameLine();
            p0.x += avail.x + spacing;
        }
        ImGui.SetCursorScreenPos(new Vector2(p0.x, p0.y + avail.y + ImGui.GetStyle().ItemSpacing.y));
        VersionWindow?.Draw();
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
    bool ExportLimitExceeded => ActiveTab[0].ExportLimitExceeded;

    private string Title = "IC10 Editor";

    public EditorWindow(ProgrammableChipMotherboard pcm)
    {
        KeyHandler = new KeyHandler(this) { Mode = VimEnabled ? KeyMode.VimNormal : KeyMode.Insert };
        Tabs.Add(new EditorTab(this, new Editor(KeyHandler, pcm), null));
    }

    private bool Show = false;


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
            LibraryWindow.LoadScripts().Forget();
        HideWindow();
    }

    public void Export()
    {
        if (ExportLimitExceeded)
        {
            ActiveTab[0].CommandStatus = LimitExceededMessage;
            return;
        }

        if (IsMotherboard)
        {
            Confirm();
            MotherboardTab[0].PCM.Export();
            return;
        }

        ActiveTab.Save();
        MotherboardTab[0].ResetCode(ActiveEditor.Code);
        MotherboardTab[0].Save();
        MotherboardTab[0].PCM.Export();
        HideWindow();
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

    public bool HasFileVCS => !IsMotherboard && ActiveTab[0].Library.State != FileState.Workshop;
    public bool IsFileReadonly => !IsMotherboard && ActiveTab[0].Library.State == FileState.Workshop;

    public void DrawHeader()
    {
        if (Button($"Library", buttonSize, "Load File from Library (Ctrl+L)"))
            LibraryWindow.Open();

        ImGui.SameLine();

        if (Button("Clear", buttonSize, "Clear Code"))
            ActiveTab[0].ClearCode();

        ImGui.SameLine();

        if (Button("Copy", buttonSize, "Copy Code to clipboard"))
            GameManager.Clipboard = Code;

        ImGui.SameLine();

        if (Button("Paste", buttonSize, "Paste Code from clipboard"))
        {
            ActiveTab[0].ClearCode();
            ActiveTab[0].Insert(GameManager.Clipboard);
        }

        ImGui.SameLine();

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 2 * ImGui.GetStyle().ItemSpacing.x);

        if (Button("?", smallButtonSize, "Help/Configuration Menu"))
            HelpWindow.IsOpen = !HelpWindow.IsOpen;

        ImGui.SameLine();

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 2 * ImGui.GetStyle().ItemSpacing.x);

        if (Button("History", buttonSize, "Version History (Ctrl+H)", !HasFileVCS))
            ActiveTab.OpenVersionWindow();

        ImGui.SameLine();

        using (new ScopedFont(UI.ImGuiUi.ImguiHelper.GetFont(1)))
        {
            ImGui.SetWindowFontScale(1.4f);
            if (Button("⟲", smallButtonSize, "Undo (Ctrl+Z)", ActiveEditor.UndoList.Count == 0))
                ActiveEditor.Undo();

            ImGui.SameLine();

            if (Button("⟳", smallButtonSize, "Redo (Ctrl+Y)", ActiveEditor.RedoList.Count == 0))
                ActiveEditor.Redo();

            ImGui.SameLine();
            ImGui.SetWindowFontScale(1.0f);
        }

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
                var isSelected = fmt == formatter.Name;
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

        if (Button("s(x)", smallButtonSize, "Slot Variables"))
            ShowNativeWindow(HelpMode.SlotVariables);

        ImGui.SameLine();

        if (Button("x", smallButtonSize, "Variables"))
            ShowNativeWindow(HelpMode.Variables);

        ImGui.SameLine();

        if (Button("f", smallButtonSize, "Functions"))
            ShowNativeWindow(HelpMode.Functions);

        ImGui.SameLine();

        var isPaused = WorldManager.IsGamePaused;
        var labelIndex = isPaused ? 1 : 0;
        if (Button(_pauseLabels[labelIndex], buttonSize, _pauseTooltips[labelIndex]))
            InputSourceCode.Instance.PauseGameToggle(!isPaused);
    }

    private static readonly string[] _pauseLabels = ["Pause", "Resume"];
    private static readonly string[] _pauseTooltips = ["Resume Game", "Pause Game"];

    private static uint _colorGood = ICodeFormatter.ColorFromHTML("green");
    private static uint _colorWarning = ICodeFormatter.ColorFromHTML("orange");
    private static uint _colorBad = ICodeFormatter.ColorFromHTML("red");
    private static uint _colorDefault = ICodeFormatter.ColorFromHTML("white");

    public bool IsMotherboard => ActiveTab[0].PCM != null;

    public float FooterHeight => 2 * ImGui.GetTextLineHeightWithSpacing() + 2 * ImGui.GetStyle().FramePadding.y;

    private TextPosition _lastCaretPos = new TextPosition(-1, -1);
    private string _caretPosString = null;
    private CodeSize _lastCodeSize = new CodeSize { NumLines = -1, MaxLineLength = -1, NumBytes = -1 };
    private StyledLine _limitsLine = null;
    private int _lastEnforce = -1;

    public void DrawFooter()
    {
        ImGui.SetCursorPosX(ImGui.GetStyle().FramePadding.x);

        if (_caretPosString == null || CaretPos != _lastCaretPos)
        {
            _caretPosString = $"{CaretLine,3}/{CaretCol,2},";
            _lastCaretPos = CaretPos;
        }

        int enforceFlags = (Settings.EnforceLineLimit ? 1 : 0) + (Settings.EnforceLineLengthLimit ? 2 : 0) + (Settings.EnforceByteLimit ? 4 : 0);
        if (_lastCodeSize != ActiveTab[0].CodeSize || enforceFlags != _lastEnforce)
        {
            _lastEnforce = enforceFlags;
            _lastCodeSize = ActiveTab[0].CodeSize;
            _limitsLine = new StyledLine();
            int charPos = 0;
            void drawLimit(bool enforce, int n, int limit, string unit)
            {
                var color = _colorDefault;
                var sValue = $"{n,2}";
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
                _limitsLine.Add(new Token(charPos, sValue, new Style(color)));
                charPos += sValue.Length + 1;
                var unitString = $"{unit}";
                _limitsLine.Add(new Token(charPos, unitString, new Style(color)));
                charPos += unitString.Length + 1;
            }

            var size = ActiveTab[0].CodeSize;

            drawLimit(EnforceLineLimit, size.NumLines, 128, "lines,");
            drawLimit(EnforceLineLengthLimit, size.MaxLineLength, 90, "chars,");
            drawLimit(EnforceByteLimit, size.NumBytes, 4096, "bytes");
        }

        ImGui.Text(_caretPosString);
        ImGui.SameLine();

        var pos = ImGui.GetCursorScreenPos();
        var px0 = ImGui.GetCursorPosX();
        var psx0 = pos.x;

        var drawList = ImGui.GetWindowDrawList();
        _limitsLine.Draw(pos, 0);
        pos.x += (4 + _limitsLine.Last().Column + _limitsLine.Last().Length) * CharWidth;

        ImGui.SetCursorPosX(px0 + pos.x - psx0);

        ActiveTab[0].CodeFormatter.DrawButtons();

        ImGui.SameLine();

        ImGui.SetCursorPosX(
            ImGui.GetWindowWidth()
                - 4 * buttonSize.x
                - ImGui.GetStyle().FramePadding.x * 5
                - ImGui.GetStyle().ItemSpacing.x
        );

        if (Button("Cancel", buttonSize, "Close Editor (Ctrl+Q)"))
            HideWindow();

        ImGui.SameLine();

        if (Button("Commit", buttonSize, "Add version to History (Ctrl+Shift+S)", !HasFileVCS))
            ActiveTab[0].Save(true);

        ImGui.SameLine();

        if (Button("Export", buttonSize, "Export to IC10 chip and close editor (Ctrl+E)", ExportLimitExceeded))
            Export();

        ImGui.SameLine();

        if (IsMotherboard)
        {
            if (Button("Confirm", buttonSize, "Save to Motherboard and quit editor (Ctrl+S)", LimitExceeded))
                Confirm();
        }
        else if (Button("Save", buttonSize, "Save to Library (Ctrl+S)"))
            KeyHandler.CommandStatus = ActiveTab[0].Save();

        KeyHandler.DrawStatus();
        ActiveTab[0].CodeFormatter.DrawStatus(ImGui.GetCursorScreenPos());
    }

    private bool _hasFocus = false;

    // public bool HasFocus => _hasFocus && !LibrariesWindow.IsOpen && !(ActiveEditor._confirmWindow?.IsOpen ?? false) && !(ActiveTab.VersionWindow?.IsOpen ?? false);
    public bool HasFocus => _hasFocus && ConfirmWindow.NumWindowsOpen == 0;

    public void Draw()
    {
        if (!Show)
            return;

        using var _fscale = new ScopedFontScale(Scale);

        LibraryWindow.Window = this;

        if (DebugWindow.IsOpen)
            DebugWindow.RenderStopwatch = Stopwatch.StartNew();

        using var _fr = new ScopedStyleVar(ImGuiStyleVar.FrameRounding, 3.0f);
        // make sure the native editor is hidden
        InputSourceCode.Instance.Window.localPosition = new Vector3(-10000, -10000, 0);

        var _ = new ScopedStyleColor(ImGuiCol.WindowBg, ICodeFormatter.ColorFromVector4(0.1f, 0.1f, 0.1f, 1.0f));
        var _f = new ScopedFont(ImGui.GetIO().Fonts.Fonts[0]);

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

        if (CollapseOnGameWindow)
            SetImGuiWindowCollapsed();

        ImGui.Begin(Title);
        ImGui.GetStyle().Colors[(int)ImGuiCol.Tab] = new Vector4(0.2f, 0.2f, 0.2f, 1.0f);

        UpdateTextSize();
        DrawHeader();

        if (HasFocus)
            HandleInput(_hasFocus);

        _hasFocus = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.IsWindowCollapsed();

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

        if (HasFocus)
            foreach (var editor in ActiveTab.Editors)
                editor.DrawTooltip();

        if (ActiveTab[0]._confirmWindow != null)
            ActiveTab[0]._confirmWindow.Draw();

        ImGui.End();
        ImGui.PopStyleColor();

        LibraryWindow.Draw();

        HelpWindow.Draw();
        DebugWindow.Draw();
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

}
