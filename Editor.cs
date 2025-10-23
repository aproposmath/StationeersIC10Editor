namespace StationeersIC10Editor
{
    using System;
    using System.Collections.Generic;
    using Assets.Scripts;
    using Assets.Scripts.Objects.Electrical;
    using Assets.Scripts.Objects.Motherboards;
    using Assets.Scripts.UI;
    using ImGuiNET;
    using UnityEngine;

    public struct EditorState
    {
        public string Code;
        public TextPosition CaretPos;
        public double Timestamp;
    }

    public class IC10Editor
    {
        public static bool UseNativeEditor = false;

        private ProgrammableChipMotherboard _pcm;
        private string Title = "IC10 Editor";

        public IC10Editor(ProgrammableChipMotherboard pcm)
        {
            CodeFormatter = new IC10CodeFormatter();
            UndoList = new LinkedList<EditorState>();
            RedoList = new LinkedList<EditorState>();
            Lines = new List<string>();
            Lines.Add("");
            CaretPos = new TextPosition(0, 0);
            L.Info("Creating IC10 Editor");
            _pcm = pcm;
        }

        public ICodeFormatter CodeFormatter;

        public List<string> Lines;

        public string Code => string.Join("\n", Lines);

        public EditorState State
        {
            get
            {
                return new EditorState { Code = Code, CaretPos = CaretPos, Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            }

            set
            {
                ResetCode(value.Code, false);
                CaretPos = value.CaretPos;
            }
        }

        public LinkedList<EditorState> UndoList;
        public LinkedList<EditorState> RedoList;
        public bool ScrollToCaret = false;

        public void PushUndoState()
        {
            var state = State;
            if (UndoList.Count > 0)
            {
                // merge with previous state if within 500ms or same code
                var first = UndoList.First.Value;
                if (state.Timestamp < first.Timestamp + 500 || state.Code == first.Code)
                    UndoList.RemoveFirst();
            }

            UndoList.AddFirst(State);
            while (UndoList.Count > 100)
            {
                UndoList.RemoveLast();
            }
        }

        public void Undo()
        {
            if (UndoList.Count > 0)
            {
                RedoList.AddFirst(State);
                State = UndoList.First.Value;
                UndoList.RemoveFirst();
            }
        }

        public void Redo()
        {
            if (RedoList.Count > 0)
            {
                UndoList.AddFirst(State);
                State = RedoList.First.Value;
                RedoList.RemoveFirst();
            }
        }

        public void ReplaceLine(int lineIndex, string newLine)
        {
            if (lineIndex < 0 || lineIndex >= Lines.Count)
                return;

            CodeFormatter.ReplaceLine(Lines[lineIndex], newLine);
            Lines[lineIndex] = newLine;
        }

        public string CurrentLine
        {
            get
            {
                return Lines[CaretLine];
            }

            set
            {
                if (value == Lines[CaretLine])
                    return;

                ReplaceLine(CaretLine, value);
            }
        }

        private bool Show = false;
        private double blinkStartTime = 0.0;

        public void SwitchToNativeEditor()
        {
            UseNativeEditor = true;
            L.Info("Switching to native IC10 editor");
            Show = false;

            // localPosition was set to -10000,-10000,0 to hide the native editor, so set it back to 0,0,0 to show it
            InputSourceCode.Instance.Window.localPosition = new Vector3(0, 0, 0);
            KeyManager.RemoveInputState("ic10editorinputstate");
            InputSourceCode.Paste(Code);
        }

        public void HideWindow()
        {
            L.Info("Hiding IC10 Editor window");
            Show = false;
            KeyManager.RemoveInputState("ic10editorinputstate");
            if (InputWindow.InputState == InputPanelState.Waiting)
                InputWindow.CancelInput();
            if (WorldManager.IsGamePaused)
                InputSourceCode.Instance.PauseGameToggle(false);
            InputSourceCode.Instance.ButtonInputCancel();
        }

        public void ShowWindow()
        {
            L.Info("Showing IC10 Editor window");
            Show = true;
            L.Info($"Current code {Code}");
            KeyManager.SetInputState("ic10editorinputstate", KeyInputState.Typing);
            if (!WorldManager.IsGamePaused)
                InputSourceCode.Instance.PauseGameToggle(true);

            InputSourceCode.Instance.RectTransform.localPosition = new Vector3(-10000, -10000, 0);
        }

        public bool IsInitialized = false;
        public TextPosition _caretPos;

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
                if (_caretPos.Col > Lines[_caretPos.Line].Length)
                    _caretPos.Col = Lines[_caretPos.Line].Length;
                ScrollToCaret = true;
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
            bool isSelecting = false)
        {
            ScrollToCaret = true;
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
            blinkStartTime = ImGui.GetTime();
        }

        public void SelectAll()
        {
            Selection.Start = new TextPosition(0, 0);
            Selection.End = new TextPosition(Lines.Count - 1, Lines[Lines.Count - 1].Length);
        }

        public void Cut()
        {
            if (!HaveSelection)
                return;
            GameManager.Clipboard = SelectedCode;
            DeleteSelectedCode();
        }

        public void Copy()
        {
            string code = SelectedCode;
            if (code != null)
            {
                GameManager.Clipboard = code;
            }
        }

        public void Paste()
        {
            if (!DeleteSelectedCode())
                PushUndoState();
            Insert(GameManager.Clipboard.Replace("\r", string.Empty));
        }

        public void SetTitle(string title)
        {
            Title = title;
        }

        public void ClearCode(bool pushUndo = true)
        {
            if (pushUndo)
                PushUndoState();
            Lines.Clear();
            Lines.Add(string.Empty);
            CodeFormatter.ResetCode(string.Empty);
            CaretLine = 0;
            CaretCol = 0;
            Selection.Reset();
        }

        public void Insert(string code)
        {
            var newLines = new List<string>(code.Split('\n'));
            if (newLines.Count == 0)
                return;

            CodeFormatter.RemoveLine(CurrentLine);
            string beforeCaret = CurrentLine.Substring(0, CaretCol);
            string afterCaret = CurrentLine.Substring(CaretCol, CurrentLine.Length - CaretCol);
            CurrentLine = beforeCaret + newLines[0];
            newLines.RemoveAt(0);
            int newCaretCol = newLines[newLines.Count - 1].Length;
            newLines[newLines.Count - 1] += afterCaret;
            Lines.InsertRange(CaretLine + 1, newLines);
            foreach (var line in newLines)
                CodeFormatter.AddLine(line);

            CaretPos = new TextPosition(
                CaretLine + newLines.Count,
                newCaretCol);
        }

        public bool HaveSelection => (bool)Selection;

        public string GetCode(TextRange range)
        {
            var start = range.Start;
            var end = range.End;

            if (start.Line == end.Line)
                return Lines[start.Line].Substring(start.Col, end.Col - start.Col);

            string code = Lines[start.Line].Substring(start.Col);
            for (int i = start.Line + 1; i < end.Line; i++)
                code += '\n' + Lines[i];

            code += '\n' + Lines[end.Line].Substring(0, end.Col);
            return code;
        }

        public string SelectedCode => GetCode(Selection.Sorted());

        public bool DeleteRange(TextRange range)
        {
            if (!(bool)range)
                return false;

            range = range.Sorted();

            PushUndoState();

            var start = range.Start;
            var end = range.End;
            if (start.Line == end.Line)
            {
                string line = Lines[start.Line];
                string newLine =
                    line.Substring(0, start.Col) + line.Substring(end.Col, line.Length - end.Col);
                ReplaceLine(start.Line, newLine);
            }
            else
            {
                string firstLine = Lines[start.Line];
                string lastLine = Lines[end.Line];
                string newFirstLine = firstLine.Substring(0, start.Col);
                string newLastLine = lastLine.Substring(end.Col, lastLine.Length - end.Col);
                ReplaceLine(start.Line, newFirstLine + newLastLine);

                for (int i = end.Line; i > start.Line; i--)
                {
                    CodeFormatter.RemoveLine(Lines[i]);
                    Lines.RemoveAt(i);
                }
            }

            CaretPos = start;
            return true;
        }

        public bool DeleteSelectedCode()
        {
            if (DeleteRange(Selection))
            {
                Selection.Reset();
                return true;
            }

            return false;
        }

        public void HandleInput()
        {
            foreach (ImGuiKey key in Enum.GetValues(typeof(ImGuiKey)))
            {
                if (key != ImGuiKey.COUNT && ImGui.IsKeyPressed(key))
                    L.Info($"Imgui Pressed: {key}");
            }
            var io = ImGui.GetIO();
            io.ConfigWindowsMoveFromTitleBarOnly = true;
            bool ctrlDown = io.KeyCtrl || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shiftDown = io.KeyShift || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                // these combos are not captured by ImGui for some reason, so handle them via Unity Input
                if (Input.GetKeyDown(KeyCode.S))
                    Confirm();
                if (Input.GetKeyDown(KeyCode.E))
                    Export();
            }

            if (ctrlDown)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.V))
                    Paste();
                if (ImGui.IsKeyPressed(ImGuiKey.A))
                    SelectAll();
                if (ImGui.IsKeyPressed(ImGuiKey.C))
                    Copy();
                if (ImGui.IsKeyPressed(ImGuiKey.X))
                    Cut();
                if (ImGui.IsKeyPressed(ImGuiKey.Z))
                    Undo();
                if (ImGui.IsKeyPressed(ImGuiKey.Y))
                    Redo();

                for (int i = 0; i < io.InputQueueCharacters.Size; i++)
                {
                    var ic = io.InputQueueCharacters[i];
                    char c = (char)ic;
                    L.Info($"Ignoring input char while Ctrl is held down: {c} (code {ic})");
                }
            }
            else
            {
                // if (ImGui.IsKeyPressed(ImGuiKey.Escape)) CurrentLine += "<esc>";
                if (ImGui.IsKeyPressed(ImGuiKey.Backspace))
                {
                    if (DeleteSelectedCode())
                        return;

                    PushUndoState();
                    if (CurrentLine.Length > 0 && CaretCol > 0)
                    {
                        CurrentLine = CurrentLine.Remove(CaretCol - 1, 1);
                        CaretCol--;
                    }
                    else if (CaretCol == 0 && CaretLine > 0)
                    {
                        // Merge with previous line
                        int prevLineLength = Lines[CaretLine - 1].Length;
                        CurrentLine = Lines[CaretLine - 1] + CurrentLine;
                        Lines.RemoveAt(CaretLine - 1);
                        CodeFormatter.RemoveLine(Lines[CaretLine - 1]);
                        CaretLine--;
                        CaretCol = prevLineLength;
                    }
                }
                if (ImGui.IsKeyPressed(ImGuiKey.Delete))
                {
                    if (DeleteSelectedCode())
                        return;

                    PushUndoState();
                    if (CaretCol < CurrentLine.Length)
                        CurrentLine = CurrentLine.Remove(CaretCol, 1);
                    else if (CaretCol == CurrentLine.Length && CaretLine < Lines.Count - 1)
                    {
                        // Merge with next line
                        CurrentLine = CurrentLine + Lines[CaretLine + 1];
                        Lines.RemoveAt(CaretLine + 1);
                        CodeFormatter.RemoveLine(Lines[CaretLine]);
                    }
                }

                if (ImGui.IsKeyPressed(ImGuiKey.Enter))
                {
                    PushUndoState();
                    string newLine = CurrentLine.Substring(CaretCol);
                    CurrentLine = CurrentLine.Substring(0, CaretCol);
                    Lines.Insert(CaretLine + 1, newLine);
                    MoveCaret(0, CaretLine + 1, false);
                }

                if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow))
                    CaretLeft();
                if (ImGui.IsKeyPressed(ImGuiKey.RightArrow))
                    CaretRight();
                if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
                    CaretUp();
                if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
                    CaretDown();
                if (ImGui.IsKeyPressed(ImGuiKey.Home))
                    CaretToStartOfLine();
                if (ImGui.IsKeyPressed(ImGuiKey.End))
                    CaretToEndOfLine();
                if (ImGui.IsKeyPressed(ImGuiKey.PageUp))
                    CaretUp(20);
                if (ImGui.IsKeyPressed(ImGuiKey.PageDown))
                    CaretDown(20);

                string input = string.Empty;
                for (int i = 0; i < io.InputQueueCharacters.Size; i++)
                {
                    char c = (char)io.InputQueueCharacters[i];
                    input += c;
                }

                if (input.Length > 0)
                {
                    if (!DeleteSelectedCode())
                    {
                        PushUndoState();
                    }

                    CurrentLine = CurrentLine.Insert(CaretCol, input);
                    CaretCol += input.Length;
                }
            }
        }

        private Vector2 buttonSize = new Vector2(85, 0);
        private Vector2 smallButtonSize = new Vector2(50, 0);

        public void ShowHelpWindow(HelpMode mode)
        {
            foreach (var window in InputSourceCode.Instance.HelpWindows)
                if (window.HelpMode == mode)
                    window.ToggleVisibility();
        }

        private bool _helpWindowVisible = false;

        public void DrawHeader()
        {
            // rounded buttons style
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5.0f);

            if (ImGui.Button("Library", buttonSize))
                ShowHelpWindow(HelpMode.Instructions);

            ImGui.SameLine();

            if (ImGui.Button("Clear", buttonSize))
                ClearCode();

            ImGui.SameLine();

            if (ImGui.Button("Copy", buttonSize))
                GameManager.Clipboard = Code;

            ImGui.SameLine();

            if (ImGui.Button("Paste", buttonSize))
            {
                ClearCode();
                Insert(GameManager.Clipboard);
            }

            ImGui.SameLine();

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 2 * ImGui.GetStyle().ItemSpacing.x);

            if (ImGui.Button("Help", buttonSize))
                _helpWindowVisible = !_helpWindowVisible;

            ImGui.SameLine();

            if (ImGui.Button("Native", buttonSize))
                SwitchToNativeEditor();

            ImGui.SameLine();

            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 3 * smallButtonSize.x - buttonSize.x - ImGui.GetStyle().FramePadding.x * 3 - ImGui.GetStyle().ItemSpacing.x * 3);


            if (ImGui.Button("s(x)", smallButtonSize))
                ShowHelpWindow(HelpMode.SlotVariables);

            ImGui.SameLine();

            if (ImGui.Button("x", smallButtonSize))
                ShowHelpWindow(HelpMode.Variables);

            ImGui.SameLine();

            if (ImGui.Button("f", smallButtonSize))
                ShowHelpWindow(HelpMode.Functions);

            ImGui.SameLine();

            bool isPaused = WorldManager.IsGamePaused;
            if (ImGui.Button(isPaused ? "Resume" : "Pause", buttonSize))
                InputSourceCode.Instance.PauseGameToggle(!isPaused);

            ImGui.PopStyleVar();
        }

        public void DrawFooter()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5.0f);
            ImGui.Text(
                $"Lines: {Lines.Count}  Caret: ({CaretLine},{CaretCol})  Undo: {UndoList.Count}");

            ImGui.SameLine();

            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 3 * buttonSize.x - ImGui.GetStyle().FramePadding.x * 3 - ImGui.GetStyle().ItemSpacing.x);
            if (ImGui.Button("Cancel", buttonSize))
                HideWindow();

            ImGui.SameLine();
            if (ImGui.Button("Export", buttonSize))
                Export();

            ImGui.SameLine();
            if (ImGui.Button("Confirm", buttonSize))
                Confirm();

            ImGui.PopStyleVar();
        }

        public void Confirm()
        {
            L.Info("Confirming IC10 Editor changes");
            _pcm.InputFinished(Code);
            HideWindow();
        }

        public void Export()
        {
            Confirm();
            _pcm.Export();
        }

        private bool MouseIsInsideTextArea(Vector2 mousePos, Vector2 textOrigin, Vector2 availSize)
        {
            L.Info($"MousePos: {mousePos}, TextOrigin: {textOrigin}, AvailSize: {availSize}");
            return mousePos.x >= textOrigin.x
                && mousePos.x <= textOrigin.x + availSize.x
                && mousePos.y >= textOrigin.y
                && mousePos.y <= textOrigin.y + availSize.y;
        }

        private Vector2 caretPixelPos;

        public unsafe void DrawCodeArea()
        {
            Vector2 availSize = ImGui.GetContentRegionAvail();
            float statusLineHeight = ImGui.GetTextLineHeightWithSpacing() * 2;
            float scrollHeight =
                availSize.y - statusLineHeight - (ImGui.GetStyle().FramePadding.y * 2);

            ImGui.BeginChild("ScrollRegion", new Vector2(0, scrollHeight), true);

            ImGuiListClipperPtr clipper = new ImGuiListClipperPtr(
                ImGuiNative.ImGuiListClipper_ImGuiListClipper());

            clipper.Begin(Lines.Count);

            Vector2 textAreaOrigin = ImGui.GetCursorScreenPos(); // Store this before drawing lines
            Vector2 mousePos = ImGui.GetMousePos();

            if (ImGui.IsMouseClicked(0)) // Left click
            {
                // if (MouseIsInsideTextArea(mousePos, textAreaOrigin, availSize))

                float mx = mousePos.x;
                float my = mousePos.y;
                float tx = textAreaOrigin.x;
                float ty = textAreaOrigin.y + ImGui.GetScrollY();
                float tw = availSize.x;
                float th = scrollHeight;
                if (mx >= tx && mx <= tx + tw && my >= ty && my <= ty + th)
                {
                    CaretPos = GetTextPositionFromMouse(mousePos, textAreaOrigin);
                    Selection.Start = CaretPos;
                    Selection.End.Reset();
                }
                else
                    Selection.Reset();
            }

            if ((bool)Selection.Start && (ImGui.IsMouseDown(0) || ImGui.IsMouseReleased(0)))
            {
                Selection.End = GetTextPositionFromMouse(mousePos, textAreaOrigin);
            }

            if (ScrollToCaret)
            {
                float lineHeight = ImGui.GetTextLineHeightWithSpacing();
                float lineSpacing = ImGui.GetStyle().ItemSpacing.y;

                float pageHeight = (Lines.Count * lineHeight) - ImGui.GetScrollMaxY();
                float scrollY = ImGui.GetScrollY();
                float viewTop = scrollY;
                float viewBottom = scrollY + pageHeight;

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
                ScrollToCaret = false;
            }

            caretPixelPos = textAreaOrigin;

            var selection = Selection.Sorted();
            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    if (i == CaretLine)
                    {
                        caretPixelPos = ImGui.GetCursorScreenPos();
                        caretPixelPos.x += ImGui.CalcTextSize("M").x * (CaretCol + ICodeFormatter.LineNumberOffset);
                        DrawCaret(caretPixelPos);
                    }

                    CodeFormatter.DrawLine(i, Lines[i], selection);
                    ImGui.NewLine();
                }
            }

            clipper.End();
            ImGui.EndChild();

        }

        public void DrawCaret(Vector2 pos)
        {
            bool blinkOn = ((int)((ImGui.GetTime() - blinkStartTime) * 2) % 2) == 0;

            if (blinkOn)
            {
                var drawList = ImGui.GetWindowDrawList();
                var lineHeight = ImGui.GetTextLineHeight();

                // Draw a vertical line as the cursor
                drawList.AddLine(
                    pos,
                    new Vector2(pos.x, pos.y + lineHeight),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)),
                    1.5f);
            }
        }

        public void Draw()
        {
            if (!Show) return;

            // make sure the native editor is hidden
            InputSourceCode.Instance.Window.localPosition = new Vector3(-10000, -10000, 0);

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.1f, 1.0f));

            ImGui.Begin(Title);

            if (!IsInitialized)
            {
                var displaySize = ImGui.GetIO().DisplaySize;
                var windowSize = new Vector2(
                    Math.Min(1200, displaySize.x - 100),
                    displaySize.y - 100);
                var windowPos = 0.5f * (displaySize - windowSize);

                ImGui.SetNextWindowSize(windowSize);
                ImGui.SetNextWindowPos(windowPos);
                IsInitialized = true;
            }

            HandleInput();

            DrawHeader();
            DrawCodeArea();
            DrawFooter();

            ImGui.End();
            ImGui.PopStyleColor();

            CodeFormatter.DrawTooltip(Lines[CaretLine], CaretPos, caretPixelPos);

            DrawHelpWindow();
        }

        public void DrawHelpWindow()
        {
            if (_helpWindowVisible)
            {
                ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
                ImGui.Begin("IC10 Editor Help", ref _helpWindowVisible);

                ImGui.TextWrapped(
                    "This is the IC10 Editor. It allows you to edit the source code of IC10 programs with syntax highlighting, undo/redo, and other features.");

                ImGui.Separator();

                ImGui.TextWrapped(
                    "Keyboard Shortcuts:\n" +
                    "\n" +
                    "- Ctrl+S:       Save and confirm changes\n" +
                    "- Ctrl+E:       Save + export code to ic chip\n" +
                    "- Ctrl+Z:       Undo\n" +
                    "- Ctrl+Y:       Redo\n" +
                    "- Ctrl+C:       Copy selected code\n" +
                    "- Ctrl+V:       Paste code from clipboard\n" +
                    "- Ctrl+A:       Select all code\n" +
                    "- Ctrl+X:       Cut selected code\n" +
                    "- Arrow Keys:   Move caret\n" +
                    "- Home/End:     Move caret to start/end of line\n" +
                    "- Page Up/Down: Move caret up/down by 20 lines");

                ImGui.End();
            }
        }

        public void ResetCode(string code, bool pushUndo = true)
        {
            ClearCode(pushUndo);
            Lines.Clear();
            var lines = code.Split('\n');
            foreach (var line in lines)
            {
                Lines.Add(line);
                CodeFormatter.AddLine(line);
            }
            CaretPos = new TextPosition(0, 0);
        }


        private TextPosition GetTextPositionFromMouse(Vector2 mousePos, Vector2 origin)
        {
            float charWidth = ImGui.CalcTextSize("M").x;
            float lineHeight = ImGui.GetTextLineHeightWithSpacing();

            int line = (int)((mousePos.y - origin.y) / lineHeight);
            int column =
                (int)((mousePos.x - origin.x) / charWidth) - ICodeFormatter.LineNumberOffset;

            line = Mathf.Clamp(line, 0, Lines.Count - 1);
            string lineText = Lines[line];
            column = Mathf.Clamp(column, 0, lineText.Length);

            return new TextPosition(line, column);
        }
    }

}
