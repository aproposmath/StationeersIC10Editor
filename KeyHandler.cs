namespace StationeersIC10Editor;

using System;

using Assets.Scripts;
using Assets.Scripts.Objects;
using Assets.Scripts.UI;

using ImGuiNET;

using UnityEngine;

using static Settings;

public enum KeyMode
{
    None,
    Insert,
    VimNormal,
    VimVisual
}

public class VimCommand
{
    public string Command = "";
    public char Movement = '\0';
    public string Argument = "";
    private uint _count = 0;
    private KeyHandler Handler;
    public string Input = "";
    public bool RecordInput = false;

    public uint Count
    {
        get => _count == 0 ? 1 : _count;
        set => _count = value;
    }

    public VimCommand(KeyHandler handler)
    {
        Handler = handler;
    }

    public VimCommand Clone()
    {
        var cmd = new VimCommand(Handler);
        cmd.Command = Command;
        cmd.Movement = Movement;
        cmd.Argument = Argument;
        cmd._count = _count;
        return cmd;
    }

    public bool IsEmpty => Command == "" && Movement == '\0' && _count == 0 && Argument == "";
    public bool IsFind => Command == "" && (Movement == 'f' || Movement == 't');
    public bool IsSearch => Command == "*" || Command == "/" || Command == "?" || Command == "#";
    public bool IsMovement => Command == "" && Movement != '\0' && _movements.Contains(Movement.ToString());
    public bool IsLineCommand => Command.Length == 1 && _lineCommands.Contains(Command);

    public void AddChar(char c)
    {
        if (Command == "r" || Movement == 'f' || Movement == 't' || IsLineCommand)
            Argument += c.ToString();
        else if (char.IsDigit(c) && (_count > 0 || c != '0'))
            Count = _count * 10 + (uint)(c - '0');
        else if (Command.Length == 1 && _twoCharCommands.Contains($"{Command}{c} "))
            Command += c;
        else if (_movements.Contains($"{c}"))
            Movement = c;
        else
            Command += c;

        if (!IsValid)
            Reset();
    }

    public void Reset()
    {
        Command = "";
        Count = 0;
        Movement = '\0';
        Argument = "";
    }

    public override string ToString()
    {
        var sCount = _count > 0 ? Count.ToString() : "";
        var sMovement = Movement != '\0' ? Movement.ToString() : "";
        return $"{Command}{sCount}{sMovement}{Argument}";
    }

    private static readonly string _movements = "fthjklwb0$G";
    private static readonly string _immediateSingleCharCommands = "*#~aiuCDxIAOoJpPx" + _movements.Substring(2);
    private static readonly string _singleCharCommands = "cdry";
    private static readonly string _twoCharCommands = "dd yy cc gg gf << >> ";
    private static readonly string _lineCommands = "/:?"; // commands that read argument until Enter is pressed
    private static readonly string _visualCommands = "dcyxJ<> dd cc yy JJ << >>";

    private static readonly string _validFirstChars = _immediateSingleCharCommands + _singleCharCommands + "gft:<>";

    public bool IsValid
    {
        get
        {
            if (IsLineCommand)
                return true;

            if (Command.Length == 0)
                return true;

            if (Command.Length > 2)
                return false;

            if (Command.Length == 1)
                return _validFirstChars.Contains(Command);

            return _twoCharCommands.Contains(Command + " ");
        }
    }

    public bool IsComplete(KeyMode mode, TextRange selection)
    {
        if (mode == KeyMode.VimVisual || (bool)selection)
        {
            string cmd = ToString();
            if (Command == cmd && cmd.Length == 1 && _visualCommands.Contains(cmd))
            {
                if (cmd == "x")
                    Command = "d";
                Command = cmd + cmd;
                return true;
            }
        }
        if (Command == "gf")
            return true;

        if (IsLineCommand)
            return Argument.EndsWith("\n");

        if (Movement != '\0')
            return (Movement != 'f' && Movement != 't') || Argument.Length == 1;

        if (Command.Length == 1)
            if (_immediateSingleCharCommands.Contains(Command))
                return true;
            else
                return _singleCharCommands.Contains(Command) && Argument.Length == 1;

        if (Command.Length == 2)
            return _twoCharCommands.Contains(Command + " ");

        return false;
    }

    public TextRange CaretAfterMove(Editor ed)
    {
        var startPos = ed.CaretPos;
        var pos = ed.CaretPos;
        string cmd = Movement != '\0' ? $"{Movement}" : Command;
        switch (cmd)
        {
            case "dd":
            case "yy":
            case "<<":
            case ">>":
                startPos.Col = 0;
                pos.Line = startPos.Line + (int)Count - 1;
                pos = ed.Clamp(pos);
                pos.Col = ed.Lines[pos.Line].Length + 1;
                return new TextRange(startPos, pos);
            case "j":
                pos = ed.Move(pos, new MoveAction(MoveToken.Line, true, Count));
                break;
            case "J":
                pos = ed.Move(pos, new MoveAction(MoveToken.Line, true, Count));
                pos.Line = Math.Min(pos.Line, ed.Lines.Count - 1);
                pos.Col = ed.Lines[pos.Line].Length;
                startPos.Col = 0;
                break;
            case "k":
                pos = ed.Move(pos, new MoveAction(MoveToken.Line, false, Count));
                break;
            case "h":
                pos = ed.Move(pos, new MoveAction(MoveToken.Char, false, Count));
                break;
            case "l":
            case "a":
                pos = ed.Move(pos, new MoveAction(MoveToken.Char, true, Count));
                break;
            case "x":
            case "r":
                pos.Col += (int)Count;
                pos.Col = Math.Min(pos.Col, ed.Lines[pos.Line].Length);
                break;
            case "w":
                pos = ed.Move(pos, new MoveAction(MoveToken.WordBeginning, true, Count));
                break;
            case "b":
                pos = ed.Move(pos, new MoveAction(MoveToken.WordBeginning, false, Count));
                break;
            case "0":
                pos = new TextPosition(pos.Line, 0);
                break;
            case "$":
                pos = new TextPosition(pos.Line, ed.Lines[pos.Line].Length - 1);
                break;
            case "C":
            case "D":
                var newLine = pos.Line + (int)Count - 1;
                pos = new TextPosition(newLine, ed.Lines[newLine].Length);
                break;
            case "I":
                var col = ed.CurrentLine.Length - ed.CurrentLine.TrimStart().Length;
                pos = new TextPosition(pos.Line, col);
                break;
            case "A":
                pos = new TextPosition(pos.Line, ed.Lines[pos.Line].Length);
                break;
            case "f":
            case "t":
                {
                    var line = ed.Lines[pos.Line].Text;
                    var newCol = pos.Col;
                    var apply = true;
                    for (int i = 0; i < Count; i++)
                    {
                        var index = line.IndexOf(Argument, pos.Col + 1);
                        if (index == -1)
                        {
                            apply = false;
                            break;
                        }
                        newCol = index;
                        if (!string.IsNullOrEmpty(Command))
                            newCol += 1;
                        if (cmd == "t")
                            newCol -= 1;
                    }
                    if (apply)
                        pos.Col = newCol;
                }
                break;
            case "G":
                pos.Line = ed.Lines.Count - 1;
                pos.Col = ed.Lines[pos.Line].Length + 1;
                break;
        }

        return ed.Clamp(new TextRange(startPos, pos));
    }

    public string Execute(Editor editor, KeyMode mode)
    {
        TextRange range;
        if (mode == KeyMode.VimVisual)
        {
            if (!_visualCommands.Contains(Command))
                return null;
            range = CaretAfterMove(editor);
            editor.CaretPos = range.End;
            editor.KeyHandler.UpdateVisualSelection();
            if (!IsMovement)
                range = editor.Selection.Sorted();
        }
        else
        {
            if (editor.HaveSelection)
                range = editor.Clamp(editor.Selection.Sorted());
            else
                range = CaretAfterMove(editor);
        }

        string status = null;
        var nLines = range.End.Line - range.Start.Line + 1;
        if (range.Start.Col > 0 || range.End.Col <= editor.Lines[range.End.Line].Length)
        {
            // if we copy partial lines, adjust the range end to not include a newline char
            if (!IsMovement && range.End.Col == 0 && range.End.Line > range.Start.Line)
            {
                range.End.Line -= 1;
                range.End.Col = editor.Lines[range.End.Line].Length;
            }
        }
        var sLines = $"{nLines} " + (nLines > 1 ? "lines" : "line");

        switch (Command)
        {
            case "":
            case "h":
            case "j":
            case "k":
            case "l":
            case "w":
            case "b":
            case "0":
            case "G":
                editor.CaretPos = range.End;
                break;
            case "d":
            case "D":
            case "x":
            case "dd":
                editor.CopyRange(range);
                editor.DeleteRange(range);
                if (nLines > 0)
                    status = $"Deleted {sLines}";
                break;
            case "r":
                string oldCode = editor.GetCode(range);
                editor.DeleteRange(range);
                editor.Insert(Argument.PadRight(oldCode.Length, Argument[0]));
                editor.CaretPos = new TextPosition(editor.CaretLine, editor.CaretCol - 1);
                break;
            case "y":
            case "yy":
                editor.CopyRange(range);
                if (nLines > 0)
                    status = $"Yanked {sLines}";
                break;
            case "c":
            case "C":
                if (Command == "c" && Movement == 'w' && range.End.Col > 0)
                    range.End.Col--;
                Handler.InsertMode(true);
                editor.DeleteRange(range, false);
                editor.CaretPos = range.Sorted().Start;
                break;
            case "O":
                Handler.InsertMode(true);
                editor.CaretPos = new TextPosition(editor.CaretLine, 0);
                editor.Insert("\n");
                editor.CaretPos = new TextPosition(editor.CaretLine - 1, 0);
                break;
            case "o":
                Handler.InsertMode(true);
                bool move = editor.CaretLine < editor.Lines.Count - 1;
                editor.CaretPos = editor.Clamp(new TextPosition(editor.CaretLine + 1, 0));
                editor.Insert("\n");
                if (move)
                    editor.CaretPos = new TextPosition(editor.CaretLine - 1, 0);
                break;
            case "u":
                editor.Undo();
                break;
            case "i":
            case "a":
                editor.CaretPos = range.End;
                Handler.InsertMode(true);
                break;
            case "I":
            case "A":
                editor.CaretPos = range.End;
                Handler.InsertMode(true);
                break;
            case "J":
                if (range.End.Line > range.Start.Line)
                {
                    var newCode = editor.GetCode(range).Replace("\n", " ");
                    Handler.OnKeyPressed($"J - Join lines, range={range}, newCode='{newCode}', oldCode='{editor.GetCode(range)}'");
                    editor.DeleteRange(range);
                    editor.Insert(newCode);
                }
                break;
            case "gg":
                editor.CaretLine = (int)_count;
                break;
            case "P":
            case "p":
                editor.PushUndoState(false);
                var linesBefore = editor.Lines.Count;
                var code = GameManager.Clipboard.Replace("\r", "");
                bool insertLines = code.EndsWith("\n");
                if (insertLines && Command == "p")
                    code = "\n" + code.Substring(0, code.Length - 1);
                for (int i = 0; i < (int)Count; i++)
                {
                    if (insertLines)
                    {
                        TextPosition insertPos = new TextPosition(editor.CaretLine, editor.CurrentLine.Length);
                        if (Command == "P")
                            insertPos.Col = 0;
                        editor.Insert(code, insertPos);
                    }
                    else
                    {
                        if (Command == "p")
                            editor.CaretCol += 1;
                        editor.Insert(code);
                    }
                }
                nLines = editor.Lines.Count - linesBefore;
                status = $"Pasted {nLines} line" + (nLines > 1 ? "s" : "");
                break;
            case "<<":
            case ">>":
                editor.PushUndoState(false);
                for (int line = range.Start.Line; line <= range.End.Line; line++)
                {
                    var currentLine = editor.Lines[line].Text;
                    if (Command == "<<" && currentLine.StartsWith("  "))
                        editor.ReplaceLine(line, currentLine.Substring(2));
                    else if (Command == ">>")
                        editor.ReplaceLine(line, "  " + currentLine);
                }
                break;
            case "gf":
                Handler.OpenStationPedia(editor.CaretPos);
                break;
            case "~":
                editor.PushUndoState(false);
                var lineText = editor.Lines[editor.CaretLine].Text;
                for (int col = editor.CaretCol; col < Math.Min(editor.CaretCol + (int)Count, lineText.Length); col++)
                {
                    char c = lineText[col];
                    char newC = char.IsUpper(c) ? char.ToLower(c) : char.ToUpper(c);
                    lineText = lineText.Remove(col, 1).Insert(col, newC.ToString());
                }
                editor.ReplaceLine(editor.CaretLine, lineText);
                editor.CaretCol += (int)Count;
                break;
            case ":":
                foreach (char c in Argument)
                {
                    switch (c)
                    {
                        case 'q':
                            Handler.Window.HideWindow();
                            break;
                        case 'w':
                            status = editor.Save();
                            break;
                    }
                }
                break;
            case "*":
            case "#":
            case "/":
            case "?":
                {
                    bool forward = Command == "*" || Command == "/";

                    if (Command == "*" || Command == "#")
                    {
                        Argument = editor.GetCode(editor.GetWordAt(editor.CaretPos)) + "\n";
                        Command = forward ? "/" : "?";
                    }

                    var searchTerm = Argument.TrimEnd('\n');
                    var pos = editor.FindString(editor.CaretPos, searchTerm, forward);
                    if (!(bool)pos)
                    {
                        // try searching from the beginning/end
                        var startPos = forward ? new TextPosition(0, 0) : new TextPosition(editor.Lines.Count - 1, editor.Lines[editor.Lines.Count - 1].Length);
                        pos = editor.FindString(startPos, searchTerm, forward);
                    }

                    if ((bool)pos)
                    {
                        editor.CaretPos = pos;
                        status = $"Found '{searchTerm}'";
                    }
                    else
                    {
                        status = $"'{searchTerm}' not found";
                    }
                }
                break;
        }

        if (mode == KeyMode.VimVisual && Command.Length > 0 && _visualCommands.Contains(Command))
        {
            editor.CaretPos = range.Start;
            Count = (uint)nLines;
        }
        editor.KeyHandler.UpdateVisualSelection();

        if (editor.KeyHandler.Mode == KeyMode.Insert && Input.Length > 0)
        {
            editor.CurrentLine = editor.CurrentLine.Insert(editor.CaretCol, Input);
            editor.CaretCol += Input.Length;
            editor.KeyHandler.Mode = KeyMode.VimNormal;
        }
        return status;
    }
}

public class KeyHandler
{
    public Action<string> OnKeyPressed = delegate { };

    public EditorWindow Window;
    public Editor Editor => Window.ActiveEditor;
    public bool DoRecordInput { get; set; }

    public KeyMode Mode
    {
        get
        {
            return Window.KeyMode;
        }
        set
        {
            Window.KeyMode = value;
        }
    }

    private bool _isSelecting = false;
    private static double _timeLastMouseMove = 0.0;
    private static Vector2 _lastMousePos = new Vector2(0, 0);

    private VimCommand CurrentCommand;
    private VimCommand LastCommand;
    private VimCommand LastFindCommand;
    private VimCommand LastSearchCommand;

    TextPosition CaretPos
    {
        get => Editor.CaretPos;
        set => Editor.CaretPos = value;
    }

    string CurrentLine
    {
        get => Editor.CurrentLine;
        set => Editor.CurrentLine = value;
    }

    int CaretCol
    {
        get => Editor.CaretCol;
        set => Editor.CaretCol = value;
    }

    int CaretLine
    {
        get => Editor.CaretLine;
        set => Editor.CaretLine = value;
    }

    public KeyHandler(EditorWindow window)
    {
        Window = window;
        OnKeyPressed = (s) =>
        {
            CommandStatus = "";
        };

        CurrentCommand = new VimCommand(this);
        LastCommand = new VimCommand(this);
        LastFindCommand = new VimCommand(this);
        LastSearchCommand = new VimCommand(this);
    }

    TextPosition Move(TextPosition pos, MoveAction action)
    {
        return Editor.Clamp(Editor.Move(pos, action));
    }

    public static bool IsMouseIdle(double idleTime)
    {
        return (ImGui.GetTime() - _timeLastMouseMove) >= idleTime;
    }

    public void InsertMode(bool doRecordInput = false)
    {
        if (Mode == KeyMode.Insert)
            return;
        Editor.PushUndoState(false);
        Mode = KeyMode.Insert;
        DoRecordInput = doRecordInput;
    }

    public string CommandStatus = "";

    public void DrawStatus()
    {
        if (VimEnabled)
        {
            String status = $"Mode: {Mode} ";
            ImGui.Text(status);
            ImGui.SameLine();
        }
        ImGui.Text($"{CommandStatus}");
        ImGui.SameLine();
    }

    public void OpenStationPedia(TextPosition pos)
    {
        var name = Editor.GetCode(Editor.GetWordAt(pos));

        if (Int32.TryParse(name, out var hash))
        {
            var thing = Prefab.Find<Thing>(hash);
            if (thing == null) return;
            name = thing.PrefabName;
        }

        name = "Thing" + name;

        Stationpedia._linkIdLookup.TryGetValue(name, out var page);
        if (page != null)
            Stationpedia.Instance.OpenPageByKey(page.Key);
    }

    public void HandleMouse(bool ctrlDown, bool shiftDown, bool altDown)
    {
        var mousePos = ImGui.GetMousePos();
        if (mousePos != _lastMousePos)
        {
            _timeLastMouseMove = ImGui.GetTime();
            _lastMousePos = mousePos;
        }

        foreach (var editor in Window.ActiveTab.Editors)
        {
            if (editor.IsMouseInsideTextArea())
            {
                var index = Window.ActiveTab.Editors.IndexOf(editor);
                if (ImGui.IsMouseDown(0))
                    Window.SetActiveEditor(index);
                if (ctrlDown)
                {
                    if (ImGui.IsMouseReleased(0))
                    {
                        OnKeyPressed("Ctrl+Click");
                        OpenStationPedia(editor.GetTextPositionFromMouse());
                    }
                }
                else
                {
                    if (ImGui.IsMouseDoubleClicked(0))
                    {
                        OnKeyPressed("DoubleClick");
                        _isSelecting = false;
                        var clickPos = editor.GetTextPositionFromMouse();
                        var range = editor.GetWordAt(clickPos);

                        editor.Selection.Start = range.Start;
                        editor.Selection.End = range.End;
                        CaretPos = range.End;
                        InsertMode();
                    }
                    else if (ImGui.IsMouseClicked(0)) // Left click
                    {
                        OnKeyPressed("Click");
                        _isSelecting = true;
                        var clickPos = editor.GetTextPositionFromMouse();
                        CaretPos = clickPos;
                        editor.Selection.Start = clickPos;
                        editor.Selection.End.Reset();
                    }
                    else if (_isSelecting)
                        editor.Selection.End = editor.GetTextPositionFromMouse();

                    if (ImGui.IsMouseReleased(0))
                        _isSelecting = false;

                }
            }
        }
    }

    public bool _isClosing = false;

    public void HandleCommon(bool ctrlDown, bool shiftDown, bool altDown)
    {
        if (Input.GetKeyUp(KeyCode.Q) && _isClosing)
        {
            Window.HideWindow();
            return;
        }
        var io = ImGui.GetIO();
        var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (ctrlDown && !altDown)
        {
            if (Input.GetKeyDown(KeyCode.S))
                CommandStatus = Editor.Save(shift);
            if (shiftDown)
            {
                if (Input.GetKeyDown(KeyCode.Space))
                    Window.PreviousTab();
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.E))
                    Window.Export();
                if (Input.GetKeyDown(KeyCode.Space))
                    Window.NextTab();
                if (Input.GetKeyDown(KeyCode.H) && !Window.IsMotherboard)
                    Window.ActiveTab.VersionWindow.Open();
                if (Input.GetKeyDown(KeyCode.Q))
                    _isClosing = true;
                if (Input.GetKeyDown(KeyCode.L))
                    LibraryWindow.Open();
                if (Input.GetKeyDown(KeyCode.W))
                    Window.CloseTab();
                for (var i = 0; i <= 9; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha0 + i))
                        Window.SetTab((i + 9) % 10);

                if (ImGui.IsKeyPressed(ImGuiKey.V))
                {
                    Editor.Paste();
                    OnKeyPressed("Ctrl+V - Paste");
                }
                if (ImGui.IsKeyPressed(ImGuiKey.A))
                {
                    Editor.SelectAll();
                    OnKeyPressed("Ctrl+A - Select All");
                }
                if (ImGui.IsKeyPressed(ImGuiKey.C))
                {
                    Editor.Copy();
                    OnKeyPressed("Ctrl+C - Copy");
                }
                if (ImGui.IsKeyPressed(ImGuiKey.X))
                {
                    Editor.Cut();
                    OnKeyPressed("Ctrl+X - Cut");
                }
                if (ImGui.IsKeyPressed(ImGuiKey.Z))
                {
                    Editor.Undo();
                    OnKeyPressed("Ctrl+Z - Undo");
                }
                if (ImGui.IsKeyPressed(ImGuiKey.Y))
                {
                    Editor.Redo();
                    OnKeyPressed("Ctrl+Y - Redo");
                }
            }
        }

        // check for move actions
        var newPos = new TextPosition(-1, -1);

        var arrowMoveToken = ctrlDown ? MoveToken.WordBeginning : MoveToken.Char;

        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow))
            newPos = Move(CaretPos, new MoveAction(arrowMoveToken, false, 1));
        if (ImGui.IsKeyPressed(ImGuiKey.RightArrow))
            newPos = Move(CaretPos, new MoveAction(arrowMoveToken, true, 1));
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
            newPos = Move(CaretPos, new MoveAction(MoveToken.Line, false, 1));
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
            newPos = Move(CaretPos, new MoveAction(MoveToken.Line, true, 1));
        if (ImGui.IsKeyPressed(ImGuiKey.Home))
            newPos = new TextPosition(CaretPos.Line, 0);
        if (ImGui.IsKeyPressed(ImGuiKey.End))
            newPos = new TextPosition(CaretPos.Line, Editor.Lines[CaretPos.Line].Length);
        if (ImGui.IsKeyPressed(ImGuiKey.PageUp))
            newPos = Move(CaretPos, new MoveAction(MoveToken.Line, false, 20));
        if (ImGui.IsKeyPressed(ImGuiKey.PageDown))
            newPos = Move(CaretPos, new MoveAction(MoveToken.Line, true, 20));

        if ((bool)newPos)
        {
            if (shiftDown)
            {
                OnKeyPressed("Shift+Movement - Selecting");
                if (!(bool)Editor.Selection.Start)
                    Editor.Selection.Start = CaretPos;
                Editor.Selection.End = newPos;
            }
            else
            {
                OnKeyPressed("Movement");
                Editor.Selection.Reset();
            }

            CaretPos = newPos;
        }
    }

    public void HandleInsertMode()
    {
        var io = ImGui.GetIO();

        if (VimEnabled && ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            OnKeyPressed("Escape - to Normal mode");
            Mode = KeyMode.VimNormal;
            DoRecordInput = false;
            CurrentCommand.Reset();
            if (CaretCol > 0)
                CaretCol--;
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Backspace))
        {
            OnKeyPressed("Backspace");
            if (Editor.DeleteSelectedCode())
                return;

            Editor.PushUndoState();
            if (CurrentLine.Length > 0 && CaretCol > 0)
            {
                CurrentLine = CurrentLine.Remove(CaretCol - 1, 1);
                CaretCol--;
            }
            else if (CaretCol == 0 && CaretLine > 0)
            {
                // Merge with previous line
                int prevLineLength = Editor.Lines[CaretLine - 1].Length;
                CurrentLine = Editor.Lines[CaretLine - 1].Text + CurrentLine;
                Editor.RemoveLine(CaretLine - 1);
                CaretLine--;
                CaretCol = prevLineLength;
            }
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Delete))
        {
            OnKeyPressed("Del");
            if (Editor.DeleteSelectedCode())
                return;

            Editor.PushUndoState();
            if (CaretCol < CurrentLine.Length)
                CurrentLine = CurrentLine.Remove(CaretCol, 1);
            else if (CaretCol == CurrentLine.Length && CaretLine < Editor.Lines.Count - 1)
            {
                // Merge with next line
                CurrentLine = CurrentLine + Editor.Lines[CaretLine + 1].Text;
                Editor.RemoveLine(CaretLine + 1);
            }
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
        {
            OnKeyPressed("Enter");
            if (!VimEnabled)
                Editor.PushUndoState(false);
            string newLine = CurrentLine.Substring(CaretCol);
            CurrentLine = CurrentLine.Substring(0, CaretCol);
            Editor.InsertLine(CaretLine + 1, newLine);
            CaretPos = new TextPosition(CaretLine + 1, 0);
            Editor.ScrollToCaret += 1; // we need to scroll to caret in the next two frames, one for updating the scroll area, one to actually scroll there
        }

        string input = string.Empty;
        for (int i = 0; i < io.InputQueueCharacters.Size; i++)
        {
            char c = (char)io.InputQueueCharacters[i];
            if (c == '\t')
            {
                string lineStart = CurrentLine.Substring(0, CaretCol);
                if (string.IsNullOrWhiteSpace(lineStart))
                    input += "  ";
                else
                    Editor.CodeFormatter.PerformAutocomplete();

            }
            else
                input += c;
        }

        if (input.Length > 0)
        {
            OnKeyPressed($"{input}");
            if (!Editor.DeleteSelectedCode())
                Editor.PushUndoState();

            CurrentLine = CurrentLine.Insert(CaretCol, input);
            CaretCol += input.Length;
            if (DoRecordInput)
                LastCommand.Input += input;
        }
    }

    public void CheckCommand()
    {
        CommandStatus = CurrentCommand.ToString();
        if (CurrentCommand.IsComplete(Mode, Editor.Selection))
        {
            if (Mode == KeyMode.VimVisual && !CurrentCommand.IsMovement && CurrentCommand.Movement != '\0')
            {
                Editor.Selection.Reset();
                CurrentCommand.Reset();
                return;
            }
            CommandStatus = CurrentCommand.Execute(Editor, Mode);
            if (CurrentCommand.IsFind)
                LastFindCommand = CurrentCommand.Clone();
            else if (CurrentCommand.IsSearch)
                LastSearchCommand = CurrentCommand.Clone();
            else if (!CurrentCommand.IsMovement && CurrentCommand.Command != "u")
            {
                if (Mode == KeyMode.VimVisual)
                    Mode = KeyMode.VimNormal;
                LastCommand = CurrentCommand.Clone();
            }
            if (Mode != KeyMode.VimVisual)
                Editor.Selection.Reset();
            CurrentCommand.Reset();
        }
    }

    public void UpdateVisualSelection()
    {
        if (Mode == KeyMode.VimVisual)
        {
            Editor.Selection.End = CaretPos;
            var n0 = Editor.Selection.Start.Line;
            var n1 = Editor.Selection.End.Line;
            if (n0 < n1)
            {
                Editor.Selection.Start.Col = 0;
                Editor.Selection.End.Col = Editor.Lines[n1].Length + 1;
            }
            else
            {
                Editor.Selection.End.Col = 0;
                Editor.Selection.Start.Col = Editor.Lines[n0].Length + 1;
            }
        }
    }

    public void HandleVimNormalVisualMode(bool ctrlDown, bool shiftDown, bool altDown)
    {
        var io = ImGui.GetIO();

        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // these combos are not captured by ImGui for some reason, so handle them via Unity Input
            if (Input.GetKeyDown(KeyCode.U))
            {
                OnKeyPressed("Ctrl+U");
                CaretPos = Move(CaretPos, new MoveAction(MoveToken.Line, false, 20));
                UpdateVisualSelection();
            }
            if (Input.GetKeyDown(KeyCode.D))
            {
                OnKeyPressed("Ctrl+D");
                CaretPos = Move(CaretPos, new MoveAction(MoveToken.Line, true, 20));
                UpdateVisualSelection();
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                OnKeyPressed("Ctrl+R");
                Editor.Redo();
            }


        }

        if (shiftDown && !ctrlDown && Input.GetKeyDown(KeyCode.V))
        {
            OnKeyPressed("Shift+V - to Visual Line mode");
            DoRecordInput = false;
            Mode = KeyMode.VimVisual;
            Editor.Selection.Start = new TextPosition(CaretLine, 0);
            Editor.Selection.End = new TextPosition(CaretLine + 1, 0);
            CurrentCommand.Reset();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CurrentCommand.Reset();
            Mode = KeyMode.VimNormal;
            DoRecordInput = false;
        }

        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            if (CurrentCommand.Command == ":")
                CurrentCommand.Argument = CurrentCommand.Argument.Length > 0 ? CurrentCommand.Argument.Substring(0, CurrentCommand.Argument.Length - 1) : "";
            else
                CurrentCommand.Reset();
        }

        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (CurrentCommand.IsLineCommand)
                CurrentCommand.AddChar('\n');
            else
            {
                CurrentCommand.Reset();
                CurrentCommand.AddChar('j');
            }

            CheckCommand();
        }

        for (int iChar = 0; iChar < io.InputQueueCharacters.Size; iChar++)
        {
            char c = (char)io.InputQueueCharacters[iChar];

            if (ctrlDown)
            {
                if (c >= '0' && c <= '9')
                {
                    int num = (int)(c - '0');
                    Window.SetTab(num);
                }
                continue;
            }

            OnKeyPressed($"{c}");

            if (CurrentCommand.IsEmpty)
            {
                if (c == '.' && Mode == KeyMode.VimNormal)
                {
                    if (LastCommand.IsComplete(Mode, Editor.Selection))
                        CommandStatus = LastCommand.Execute(Editor, Mode);
                    continue;
                }

                if (c == ';')
                {
                    if (LastFindCommand.IsComplete(Mode, Editor.Selection))
                        CommandStatus = LastFindCommand.Execute(Editor, Mode);
                    continue;
                }

                if (c == 'n')
                {
                    if (LastSearchCommand.IsComplete(Mode, Editor.Selection))
                        CommandStatus = LastSearchCommand.Execute(Editor, Mode);
                    continue;
                }
                if (c == 'N')
                {
                    if (LastSearchCommand.IsComplete(Mode, Editor.Selection))
                    {
                        var cmd = LastSearchCommand.Clone();
                        char s = cmd.Command[0];
                        switch (s)
                        {
                            case '/':
                                cmd.Command = "?";
                                break;
                            case '?':
                                cmd.Command = "/";
                                break;
                            case '*':
                                cmd.Command = "#";
                                break;
                            case '#':
                                cmd.Command = "*";
                                break;
                            default:
                                CommandStatus = "Cannot reverse last search command";
                                continue;
                        }
                        CommandStatus = cmd.Execute(Editor, Mode);
                    }
                    continue;
                }
            }

            CurrentCommand.AddChar(c);
            CheckCommand();
        }
    }

    public void HandleInput(bool hasFocus)
    {
        var io = ImGui.GetIO();
        io.ConfigWindowsMoveFromTitleBarOnly = true;
        bool ctrlDown = io.KeyCtrl || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shiftDown = io.KeyShift || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool altDown = io.KeyAlt || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr);

        HandleMouse(ctrlDown, shiftDown, altDown);

        if (!hasFocus)
            return;

        HandleCommon(ctrlDown, shiftDown, altDown);

        if (Mode == KeyMode.Insert)
            HandleInsertMode();
        if (Mode == KeyMode.VimNormal || Mode == KeyMode.VimVisual)
            HandleVimNormalVisualMode(ctrlDown, shiftDown, altDown);
    }
}
