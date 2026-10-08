namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;

using Assets.Scripts.UI;

using ImGuiNET;

using Reagents;

using UnityEngine;

// Help dialog on the title screen: goal, controls, alloy recipes and the furnace panel.
public partial class Egg
{
    bool HelpOpen;

    void DrawHelpDialog()
    {
        var viewport = ImGui.GetMainViewport();
        if (!ImGui.IsPopupOpen(EggText.HelpTitle))
            ImGui.OpenPopup(EggText.HelpTitle);
        var size = new Vector2(Mathf.Min(1200f, viewport.Size.x - 40f), Mathf.Min(900f, viewport.Size.y - 40f));
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(size);
        if (!ImGui.BeginPopupModal(EggText.HelpTitle, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove))
            return;

        var button = new Vector2(120, 36);
        var style = ImGui.GetStyle();
        ImGui.BeginChild("helpcontent", new Vector2(0, ImGui.GetContentRegionAvail().y - button.y - 2 * style.ItemSpacing.y));
        DrawHelpContent();
        ImGui.EndChild();

        ImGui.SetCursorPosX((size.x - button.x) * 0.5f);
        if (ImGui.Button(EggText.HelpClose, button) || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            HelpOpen = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    static readonly Vector4 HelpHeadingColor = Vec4(ColorWithAlpha(255, 200, 60, 255));
    static readonly Vector4 HelpKeyColor = Vec4(ColorWithAlpha(130, 200, 255, 255));
    static readonly Vector4 HelpTextColor = Vec4(ColorWithAlpha(215, 220, 230, 255));

    static void HelpHeading(string text)
    {
        ImGui.Dummy(new Vector2(0, 0.5f * LineHeight));
        ImGui.TextColored(HelpHeadingColor, text);
        ImGui.Separator();
    }

    static void HelpLines(IEnumerable<string> lines, bool bullets = false)
    {
        foreach (var line in lines)
        {
            if (bullets)
            {
                ImGui.Bullet();
                ImGui.SameLine();
            }
            ImGui.TextColored(HelpTextColor, line);
        }
    }

    void DrawHelpContent()
    {
        var charWidth = CharWidth;
        var x0 = ImGui.GetCursorPosX();

        HelpHeading(EggText.HelpGoalTitle);
        HelpLines(EggText.HelpGoal);

        HelpHeading(EggText.HelpControlsTitle);
        var keyColumn = 0f;
        foreach (var (key, _) in EggText.HelpControls)
            keyColumn = Math.Max(keyColumn, ImGui.CalcTextSize(key).x);
        keyColumn += 3 * charWidth;
        foreach (var (key, action) in EggText.HelpControls)
        {
            ImGui.TextColored(HelpKeyColor, key);
            ImGui.SameLine(x0 + keyColumn);
            ImGui.TextColored(HelpTextColor, action);
        }

        HelpHeading(EggText.HelpLoseTitle);
        HelpLines(EggText.HelpLose, bullets: true);

        HelpHeading(EggText.HelpHudTitle);
        var hudWidth = 300f;
        var itemSize = 1.3f * LineHeight;
        // eight bars, two item grids and the score line
        var hudHeight = 8 * (BarHeight + 4) + 3 * (itemSize + 6) + 4 * LineHeightWithSpacing;
        ImGui.BeginChild("helphud", new Vector2(hudWidth, hudHeight), true);
        var gaugeYs = DrawHelpHud(hudWidth - 2 * ImGui.GetStyle().WindowPadding.x);
        ImGui.EndChild();
        var bottom = ImGui.GetCursorPos();

        // one description per gauge, at the gauge's height
        var textX = ImGui.GetWindowPos().x - ImGui.GetScrollX() + x0 + hudWidth + 3 * charWidth;
        for (var i = 0; i < gaugeYs.Count && i < EggText.HelpHud.Length; i++)
        {
            ImGui.SetCursorScreenPos(new Vector2(textX, gaugeYs[i]));
            ImGui.TextColored(HelpTextColor, EggText.HelpHud[i]);
        }
        ImGui.SetCursorPos(bottom);

        HelpHeading(EggText.HelpAlloysTitle);
        DrawHelpAlloys(x0);
        ImGui.Dummy(new Vector2(0, 0.3f * LineHeight));
        ImGui.TextColored(HelpTextColor, EggText.HelpAlloyNote);
        ImGui.Dummy(new Vector2(0, LineHeight));
    }

    // One row per alloy: ingot, name, smelting conditions, ores per ingot, points.
    void DrawHelpAlloys(float x0)
    {
        var icon = 1.6f * LineHeight;
        var charWidth = CharWidth;
        var rowHeight = icon + ImGui.GetStyle().ItemSpacing.y;
        var nameColumn = icon + charWidth;
        var temperatureColumn = nameColumn + 11 * charWidth;
        var pressureColumn = temperatureColumn + 11 * charWidth;
        var oresColumn = pressureColumn + 14 * charWidth;
        var oreStep = icon + 2 + 6 * charWidth;
        var pointsColumn = oresColumn + 5 * oreStep;
        var textShift = (icon - LineHeight) * 0.5f;

        float[] headerColumns = [nameColumn, temperatureColumn, pressureColumn, oresColumn, pointsColumn];
        var headerY = ImGui.GetCursorPosY();
        for (var i = 0; i < headerColumns.Length && i < EggText.HelpAlloyHeader.Length; i++)
        {
            ImGui.SetCursorPos(new Vector2(x0 + headerColumns[i], headerY));
            ImGui.TextColored(HelpHeadingColor, EggText.HelpAlloyHeader[i]);
        }
        var top = headerY + LineHeightWithSpacing;
        var row = 0;

        foreach (var alloy in AlloyTypes)
        {
            if (!Recipes.TryGetValue(alloy.PrefabHash, out var recipe))
                continue;
            var y = top + row++ * rowHeight;

            ImGui.SetCursorPos(new Vector2(x0, y));
            ImGui.Image(ImGuiManager.ImGuiPointerFor(alloy.GetThumbnail().texture), new Vector2(icon, icon));

            ImGui.SetCursorPos(new Vector2(x0 + nameColumn, y + textShift));
            ImGui.TextColored(HelpKeyColor, alloy.PrefabName.Replace("Item", "").Replace("Ingot", ""));

            ImGui.SetCursorPos(new Vector2(x0 + temperatureColumn, y + textShift));
            ImGui.TextColored(HelpTextColor, string.Format(EggText.HelpAlloyTemperature, recipe.Temperature.Start));
            ImGui.SetCursorPos(new Vector2(x0 + pressureColumn, y + textShift));
            ImGui.TextColored(HelpTextColor, string.Format(EggText.HelpAlloyPressure, recipe.Pressure.Start / 1000.0));

            var x = x0 + oresColumn;
            foreach (var reagent in Reagent.AllReagents)
            {
                var amount = recipe.Get(reagent);
                if (amount <= 0)
                    continue;
                var sprite = OreSprite(OreName(reagent));
                if (sprite == null)
                    continue;
                ImGui.SetCursorPos(new Vector2(x, y));
                ImGui.Image(ImGuiManager.ImGuiPointerFor(sprite), new Vector2(icon, icon));
                ImGui.SetCursorPos(new Vector2(x + icon + 2, y + textShift));
                ImGui.TextColored(HelpTextColor, $"x{amount:0.##}");
                x += oreStep;
            }

            ImGui.SetCursorPos(new Vector2(x0 + pointsColumn, y + textShift));
            ImGui.TextColored(HelpTextColor, string.Format(EggText.HelpAlloyPoints, AlloyPoints.TryGetValue(alloy.PrefabName, out var p) ? p : 10));
        }

        ImGui.SetCursorPos(new Vector2(x0, top + row * rowHeight));
    }

    Texture2D OreSprite(string prefabName)
    {
        for (var i = 0; i < Things.Count; i++)
            if (Things[i].PrefabName == prefabName)
                return OreSprites[i];
        return null;
    }

    // The in-game side panel with sample values; returns the screen y of each gauge's text line.
    List<float> DrawHelpHud(float panelWidth)
    {
        var ys = new List<float>();
        void Bar(string label, string valueText, double fraction, uint color)
        {
            ys.Add(ImGui.GetCursorScreenPos().y + (BarHeight - LineHeight) * 0.5f);
            DrawBar(label, valueText, fraction, color);
        }

        Bar(EggText.BarOxygen, "72 %", 0.72, RampColor(0.72, (0.1, Red), (0.3, Orange), (0.6, Yellow), (1.0, Green)));
        Bar(EggText.BarStress, "20 / 100", 0.2, RampColor(0.2, (0.0, Green), (0.5, Yellow), (0.75, Orange), (1.0, Red)));
        Bar(EggText.BarPressure, "42.0 MPa", 0.42, RampColor(42, (50, Green), (55, Yellow), (60, Orange), (70, Red)));
        Bar(EggText.BarTemperature, "1150 K", 1150 / 2500.0, ColorWithAlpha(170, 60, 10, 255));
        Bar(EggText.BarSmeltSpeed, "0.35", 0.7, Green);
        Bar(EggText.BarSmeltEfficiency, "1.07", 0.02, Red);
        Bar(EggText.BarFuel, "180 mol", 0.3, ColorWithAlpha(240, 110, 30, 255));
        Bar(EggText.BarOxidizer, "95 mol", 0.16, ColorWithAlpha(60, 130, 240, 255));

        var imSize = 1.3f * LineHeight;
        ImGui.Dummy(new Vector2(0, 0.3f * LineHeight));
        var pos = ImGui.GetCursorScreenPos();
        var ores = new List<(Texture2D Sprite, double Amount, int Needed)>();
        foreach (var (name, amount, needed) in new[] { ("ItemIronOre", 4.0, 12), ("ItemNickelOre", 2.0, 6), ("ItemSilverOre", 7.0, 4) })
        {
            var sprite = OreSprite(name);
            if (sprite != null)
                ores.Add((sprite, amount, needed));
        }
        ys.Add(pos.y + (imSize - LineHeight) * 0.5f);
        var bottom = DrawItemGrid(pos, ores, imSize);

        var alloys = new List<(Texture2D Sprite, double Amount, int Needed)>();
        var samples = new[] { 1.6, 0.0, 2.0 };
        for (var i = 0; i < AlloyTypes.Count && i < samples.Length; i++)
            alloys.Add((AlloyTypes[i].GetThumbnail().texture, samples[i], i == 2 ? 2 : 8));
        ys.Add(bottom + 0.4f * LineHeight + (imSize - LineHeight) * 0.5f);
        bottom = DrawItemGrid(new Vector2(pos.x, bottom + 0.4f * LineHeight), alloys, imSize);

        ImGui.SetCursorScreenPos(new Vector2(pos.x, bottom + 0.4f * LineHeight));
        ys.Add(ImGui.GetCursorScreenPos().y);
        ImGui.Text(string.Format(EggText.Score, 96));
        return ys;
    }
}
