namespace StationeersIC10Editor;

using Assets.Scripts.UI;

using ImGuiNET;

using UnityEngine;

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
    public static string PathSeparator => IC10EditorPlugin.PathSeparator.Value;

    public static Vector2 largeButtonSize => Scale * new Vector2(120, 30);
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
    private static int _openGameWindowCount = 0;

    public static bool DidGameWindowOpen = false;
    public static bool DidGameWindowClose = false;
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

        int count = 0;
        count += Stationpedia.Instance.IsVisible ? 1 : 0;

        foreach (var window in InputSourceCode.Instance.HelpWindows)
            count += window.IsVisible ? 1 : 0;

        if (InputWindow.Instance.IsVisible)
            count += 1;

        DidGameWindowOpen = count > 0 && _openGameWindowCount == 0;
        DidGameWindowClose = count == 0 && _openGameWindowCount > 0;
        _openGameWindowCount = count;
    }

    public static void SetImGuiWindowCollapsed()
    {
        if (DidGameWindowOpen)
            ImGui.SetNextWindowCollapsed(true);
        if (DidGameWindowClose)
            ImGui.SetNextWindowCollapsed(false);
    }
}
