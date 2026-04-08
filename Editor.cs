// todo: ocnfirom window capure keyinput
namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

using static ImGuiUtils;
using static Settings;
using static Utils;

public class ConfirmWindow
{
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
        ImGui.OpenPopup(title);
        Message = message;
        IsOpen = true;
        InputPrompt = inputPrompt;
        UserInput = "";
        _justOpened = true;
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
    public static bool EnableVersionControl => IC10EditorPlugin.EnableVersionControl.Value && FossilInstaller.IsFossilExeValid;

    public static Vector2 buttonSize => Scale * new Vector2(85, 30);
    public static Vector2 smallButtonSize => Scale * new Vector2(50, 30);

    public const string LimitExceededMessage = "Size limit exceeded: cannot save or export.";

    private static float _lastScale = -1.0f;
    private static float _lastLineSpacingOffset = -1.0f;
    private static float _charWidth = 0.0f;
    private static float _lineHeight = 0.0f;
    private static float _lineSpacing = 0.0f;
    public static float CharWidth => _charWidth;
    public static float LineHeight => _lineHeight;
    public static float LineSpacing => _lineSpacing;
    public static float LineHeightWithSpacing => _lineHeight + _lineSpacing;

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

    private static Vector2 _lastMousePos = new Vector2(0, 0);
    private static double _lastMouseMoveTime = 0.0;

    public static bool ShowTooltip = false;
    public static void Update()
    {
        var mousePos = ImGui.GetMousePos();
        var time = ImGui.GetTime();
        if (mousePos != _lastMousePos)
        {
            _lastMousePos = mousePos;
            _lastMouseMoveTime = time;
        }
        ShowTooltip = time - _lastMouseMoveTime > TooltipDelay / 1000.0f;
    }
}

public class Editor
{
    public object Target;
    public ProgrammableChipMotherboard PCM => Target as ProgrammableChipMotherboard;
    public VersionedLibrary Library => Target as VersionedLibrary;
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
    public KeyMode KeyMode => KeyHandler?.Mode ?? KeyMode.None;

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

    public string Save(bool doCommit = false)
    {
        doCommit = doCommit && EnableVersionControl;
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
        if (Library != null)
        {
            var noChanges = Library.Data.Instructions == Code;
            if (doCommit)
                noChanges = noChanges && (Library.State != FileState.Untracked) && (Library.State != FileState.Modified);
            if (noChanges)
                return "No changes to " + (doCommit ? "commit" : "save");
            Library.Data.Instructions = Code;
            Library.Data.SaveToFile(Library.Data.DirectoryPath);
            LibrariesWindow.LoadLibraries().Forget();
            var msg = $"Library '{Library.Data.Title}' saved.";
            if (doCommit)
            {
                _confirmWindow = new ConfirmWindow($"Commit {Library.Data.Title}", null, "Message");
                _confirmWindow.OnConfirm = () =>
                {
                    try
                    {
                        FossilVCS.AddAndCommit([Library.Data.DirectoryPath.Name], _confirmWindow.UserInput).Forget();
                        Library.State = FileState.Unchanged;
                        msg = $"Version saved: {_confirmWindow.UserInput}";
                    }
                    catch (Exception ex)
                    {
                        msg = $"Failed to commit: {ex.Message}";
                        L.Error(ex.Message);
                        L.Error(ex.StackTrace);
                    }
                };
            }
            return msg;
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

    public bool HasFocus => KeyHandler?.Editor == this;

    public unsafe void Draw(Vector2 pos, Vector2 size, string id)
    {
        var _f = new ScopedFont(ImGui.GetIO().Fonts.Fonts[0]);
        _textAreaSize = size;
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

        var clipper = new ImGuiListClipperPtr(
            ImGuiNative.ImGuiListClipper_ImGuiListClipper()
            );

        clipper.Begin(Lines.Count);

        if (ScrollToCaret > 0)
        {
            var lineHeight = LineHeight;
            var lineSpacing = ImGui.GetStyle().ItemSpacing.y;

            var pageHeight = (Lines.Count * lineHeight) - ImGui.GetScrollMaxY();
            var scrollY = ImGui.GetScrollY();
            var viewTop = _scrollY;
            var viewBottom = _scrollY + pageHeight;

            var caretTop = CaretLine * lineHeight;
            var caretBottom = caretTop + lineHeight;

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
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
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
    public InstructionData Library;
    public FileHistoryWindow VersionWindow;

    public string Title => Library?.Title ?? "Motherboard";

    public EditorTab(EditorWindow window, Editor editor, VersionedLibrary lib)
    {
        Library = lib?.Data;
        ParentWindow = window;
        editor.ParentTab = this;
        Editors = new List<Editor> { editor };
        VersionWindow = new FileHistoryWindow(lib);
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
        using var _ = new ScopedFont(ImGui.GetIO().Fonts.Fonts[0]);
        var n = Editors.Count;
        var p0 = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        var spacing = ImGui.GetStyle().ItemSpacing.x;
        avail.y = availHeight;
        avail.x = avail.x / n - spacing * (n - 1) / n;
        for (var i = 0; i < n; i++)
        {
            var editor = Editors[i];
            editor.Update();
            editor.Draw(p0, avail, $"##editorpane{i}");
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

    private string Title = "IC10 Editor";

    public EditorWindow(ProgrammableChipMotherboard pcm)
    {
        KeyHandler = new KeyHandler(this);
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
            LibrariesWindow.LoadLibraries().Forget();
        HideWindow();
    }

    public void Export()
    {
        if (LimitExceeded)
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
        MotherboardTab.Save();
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
            LibrariesWindow.Open();

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
            ActiveTab.VersionWindow.Open();

        ImGui.SameLine();

        using (new ScopedFont(UI.ImGuiUi.ImguiHelper.GetFont(1)))
        {
            ImGui.SetWindowFontScale(1.4f);
            if (Button("⟲", smallButtonSize, "Undo (Ctrl+Z)", ActiveEditor.UndoList.Count == 0))
                ActiveEditor.Undo();

            ImGui.SameLine();

            if (Button("⟳", smallButtonSize, "Redo (Ctrl+Y)", ActiveEditor.RedoList.Count == 0))
                ActiveEditor.Undo();

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
        var pauseLabel = isPaused ? "Resume" : "Pause";
        if (Button(pauseLabel, buttonSize, $"{pauseLabel} Game"))
            InputSourceCode.Instance.PauseGameToggle(!isPaused);
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

        ImGui.Text($"{CaretLine,3}/{CaretCol,2},");
        ImGui.SameLine();

        var pos = ImGui.GetCursorScreenPos();
        var px0 = ImGui.GetCursorPosX();
        var psx0 = pos.x;
        var code = Code;

        var drawList = ImGui.GetWindowDrawList();
        void drawLimit(bool enforce, int n, int limit, string unit)
        {
            var color = _colorDefault;
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
        }

        drawLimit(EnforceLineLimit, Lines.Count, 128, "lines,");
        drawLimit(EnforceLineLengthLimit, Lines.Max(line => line.Text.Length), 90, "chars,");
        drawLimit(EnforceByteLimit, code.Length + Lines.Count - 1, 4096, "bytes");
        pos.x += 4 * CharWidth;

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

        if (Button("Export", buttonSize, "Export to IC10 chip and close editor (Ctrl+E)", LimitExceeded))
            Export();

        ImGui.SameLine();

        if (IsMotherboard)
        {
            if (Button("Confirm", buttonSize, "Save to Motherboard and quit editor (Ctrl+S)"))
                Confirm();
        }
        else if (Button("Save", buttonSize, "Save to Library (Ctrl+S)"))
            KeyHandler.CommandStatus = ActiveTab[0].Save();

        KeyHandler.DrawStatus();
        ActiveTab[0].CodeFormatter.DrawStatus(ImGui.GetCursorScreenPos());
    }

    private bool _hasFocus = false;
    private int _openGameWindows = 0;
    private bool _didGameWindowOpen = false;
    private bool _didGameWindowClose = false;

    // public bool HasFocus => _hasFocus && !LibrariesWindow.IsOpen && !(ActiveEditor._confirmWindow?.IsOpen ?? false) && !(ActiveTab.VersionWindow?.IsOpen ?? false);
    public bool HasFocus => _hasFocus && !(ActiveEditor._confirmWindow?.IsOpen ?? false);

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

    public void Draw()
    {
        if (!Show)
            return;

        using var _fscale = new ScopedFontScale(Scale);

        LibrariesWindow.Window = this;

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

        CalcDidGameWindowOpen();
        if (CollapseOnGameWindow)
        {
            if (_didGameWindowOpen)
                ImGui.SetNextWindowCollapsed(true);
            if (_didGameWindowClose)
                ImGui.SetNextWindowCollapsed(false);
        }

        ImGui.Begin(Title);
        ImGui.GetStyle().Colors[(int)ImGuiCol.Tab] = new Vector4(0.2f, 0.2f, 0.2f, 1.0f);

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

        if (HasFocus)
            foreach (var editor in ActiveTab.Editors)
                editor.DrawTooltip();

        if (ActiveTab[0]._confirmWindow != null)
            ActiveTab[0]._confirmWindow.Draw();

        ImGui.End();
        ImGui.PopStyleColor();

        LibrariesWindow.Draw();

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
