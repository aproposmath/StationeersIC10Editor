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
        private ProgrammableChipMotherboard _pcm;
        private string Title = "IC10 Editor";

        public IC10Editor(ProgrammableChipMotherboard pcm)
        {
            CodeFormatter = new IC10CodeFormatter();
            UndoList = new LinkedList<EditorState>();
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
                ClearCode();
                Insert(value.Code);
                CaretPos = value.CaretPos;
            }
        }

        public LinkedList<EditorState> UndoList;
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
                State = UndoList.First.Value;
                UndoList.RemoveFirst();
            }
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
                {
                    return;
                }

                CodeFormatter.ReplaceLine(CurrentLine, value);
                Lines[CaretLine] = value;
            }
        }

        private bool Show = false;
        private double blinkStartTime = 0.0;

        public void HideWindow()
        {
            L.Info("Hiding IC10 Editor window");
            Show = false;
            KeyManager.RemoveInputState("ic10editorinputstate");
            CursorManager.SetCursor(false);
            InputWindow.InputState = InputPanelState.None;
            InputSourceCode.InputState = InputPanelState.None;
            InputMouse.SetMouseControl(false);
            if (WorldManager.IsGamePaused)
                InputSourceCode.Instance.PauseGameToggle(false);
        }

        public void ShowWindow()
        {
            L.Info("Showing IC10 Editor window");
            Show = true;
            L.Info($"Current code {Code}");
            KeyManager.SetInputState("ic10editorinputstate", KeyInputState.Typing);
            CursorManager.SetCursor(true);
            // InputSourceCode.InputState = InputPanelState.Waiting;
            // InputWindow.InputState = InputPanelState.Waiting;
            InputMouse.SetMouseControl(true);
            if (!WorldManager.IsGamePaused)
                InputSourceCode.Instance.PauseGameToggle(true);

            // InputSourceCode.Instance.SetActive(false);
            // PanelInputSourceCode.Instance.gameObject.SetActive(false);
            InputSourceCode.Instance.CodeInputWindow.SetVisible(false);
        }

        public bool IsInitialized = false;
        public TextPosition CaretPos;

        public int CaretLine
        {
            get { return CaretPos.Line; }
            set { CaretPos.Line = value; }
        }

        public int CaretCol
        {
            get { return CaretPos.Col; }
            set { CaretPos.Col = value; }
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
            GameManager.Clipboard = string.Join("\n", Lines);
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
            DeleteSelectedCode();
            Insert(GameManager.Clipboard.Replace("\r", string.Empty));
        }

        public void SetTitle(string title)
        {
            Title = title;
        }
        public void SetSourceCode(string code)
        {
            L.Info("Setting IC10 Editor source code");
            ClearCode();
            Insert(code);
            CaretPos = new TextPosition(0, 0);
            L.Info($"Current code {Code}");
            L.Info($"Caret position {CaretPos.Line},{CaretPos.Col}");
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

            CurrentLine += newLines[0];
            newLines.RemoveAt(0);
            Lines.InsertRange(CaretLine, newLines);
            foreach (var line in newLines)
            {
                CodeFormatter.AddLine(line);
            }

            MoveCaret(0, newLines.Count, true);
        }

        public void Export()
        {
            _pcm.InputFinished(Code);
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
            {
                return false;
            }

            PushUndoState();

            var start = range.Start;
            var end = range.End;
            if (start.Line == end.Line)
            {
                string line = Lines[start.Line];
                string newLine =
                    line.Substring(0, start.Col) + line.Substring(end.Col, line.Length - end.Col);
                CurrentLine = newLine;
            }
            else
            {
                string firstLine = Lines[start.Line];
                string lastLine = Lines[end.Line];
                string newFirstLine = firstLine.Substring(0, start.Col);
                string newLastLine = lastLine.Substring(end.Col, lastLine.Length - end.Col);
                CurrentLine = newFirstLine + newLastLine;

                for (int i = end.Line; i > start.Line; i--)
                {
                    CodeFormatter.RemoveLine(Lines[i]);
                    Lines.RemoveAt(i);
                }
            }

            CaretLine = start.Line;
            CaretCol = start.Col;
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
            var io = ImGui.GetIO();
            io.ConfigWindowsMoveFromTitleBarOnly = true;
            bool ctrlDown = io.KeyCtrl;
            bool shiftDown = io.KeyShift;
            if (ctrlDown)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.V))
                    Paste();
                if (ImGui.IsKeyPressed(ImGuiKey.A))
                    SelectAll();
                if (ImGui.IsKeyPressed(ImGuiKey.C))
                    Copy();

                if (ImGui.IsKeyPressed(ImGuiKey.X))
                {
                    GameManager.Clipboard = SelectedCode;
                    DeleteSelectedCode();
                }

                if (ImGui.IsKeyPressed(ImGuiKey.Z))
                    Undo();
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
                {
                    // InputSourceCode.Instance.Initialize();
                    // L.Info($"Showing help window for mode {mode}");
                    // var transform = window.GameObject.transform;
                    // var parent = transform.parent;
                    // L.Info($"New parent : {transform.parent}");
                    // window.SetActive(true);
                    // InputSourceCode.Instance.SetVisible(true);
                    // window.SetVisible(true);
                    window.ToggleVisibility();
                    // parent.gameObject.SetActive(true);
                    // L.Info($"Window active self: {window.GameObject.activeSelf}");
                    // L.Info($"Window active in hierarchy : {window.GameObject.activeInHierarchy}");
                }
        }

        public void DrawHeader()
        {
            // rounded buttons style
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5.0f);

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

            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 3 * smallButtonSize.x - buttonSize.x - ImGui.GetStyle().FramePadding.x * 2 - ImGui.GetStyle().ItemSpacing.x * 3);

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

            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 2 * buttonSize.x - ImGui.GetStyle().FramePadding.x * 2 - ImGui.GetStyle().ItemSpacing.x);
            if (ImGui.Button("Cancel", buttonSize))
            {
                L.Info("Cancelling IC10 Editor changes");
                HideWindow();
            }

            ImGui.SameLine();
            if (ImGui.Button("Confirm", buttonSize))
            {
                L.Info("Confirming IC10 Editor changes");
                _pcm.SetSourceCode(Code);
                HideWindow();
            }
            ImGui.PopStyleVar();
        }

        private bool MouseIsInsideTextArea(Vector2 mousePos, Vector2 textOrigin, Vector2 availSize)
        {
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
                if (MouseIsInsideTextArea(mousePos, textAreaOrigin, availSize))
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

                float scrollY = ImGui.GetScrollY();
                float pageHeight = (Lines.Count * lineHeight) - ImGui.GetScrollMaxY();
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
            InputSourceCode.Instance.CodeInputWindow.SetVisible(false);
            InputSourceCode.Instance.CodeInputWindow.SetActive(false);
            if(PanelInputSourceCode.Instance != null)
              PanelInputSourceCode.Instance.gameObject.SetActive(false);
            // InputSourceCode.Instance.SetVisible(false);
            
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
            ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.1f, 0.1f, 0.1f, 1.0f);

            // ImGui.SetNextWindowCollapsed(true, ImGuiCond.Once); 

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
