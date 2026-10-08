namespace StationeersIC10Editor;

using ImGuiNET;

using UnityEngine;

// Settings dialog on the title screen; values live in EggStore.State.
public partial class Egg
{
    bool SettingsOpen;

    void ApplySettings()
    {
        EggAudio.SoundVolume = EggStore.State.SoundVolume;
        EggAudio.MusicVolume = EggStore.State.MusicVolume;
        EggAudio.ApplyVolumes();
    }

    void DrawSettingsDialog()
    {
        var viewport = ImGui.GetMainViewport();
        if (!ImGui.IsPopupOpen(EggText.SettingsTitle))
            ImGui.OpenPopup(EggText.SettingsTitle);
        var width = Mathf.Min(460f, viewport.Size.x - 40f);
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(width, 0));
        if (!ImGui.BeginPopupModal(EggText.SettingsTitle, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove))
            return;

        var state = EggStore.State;
        var changed = false;
        var labelWidth = 14 * CharWidth;
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().x - labelWidth);

        var sound = Mathf.RoundToInt(state.SoundVolume * 100f);
        if (ImGui.SliderInt(EggText.SettingsSoundVolume, ref sound, 0, 100, "%d %%"))
        {
            state.SoundVolume = sound / 100f;
            changed = true;
            ApplySettings();
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
            EggAudio.Play("SFX_UI_PointOfInterestDiscovered");

        var music = Mathf.RoundToInt(state.MusicVolume * 100f);
        if (ImGui.SliderInt(EggText.SettingsMusicVolume, ref music, 0, 100, "%d %%"))
        {
            state.MusicVolume = music / 100f;
            changed = true;
            ApplySettings();
        }
        ImGui.PopItemWidth();

        ImGui.Dummy(new Vector2(0, 0.5f * LineHeight));
        changed |= ImGui.Checkbox(EggText.SettingsSkipIntro, ref state.SkipIntro);
        changed |= ImGui.Checkbox(EggText.SettingsScreenShake, ref state.ScreenShake);

        if (changed)
            SettingsDirty = true;

        ImGui.Dummy(new Vector2(0, LineHeight));
        var button = new Vector2(120, 36);
        var resetButton = new Vector2(160, 36);
        ImGui.SetCursorPosX((width - resetButton.x) * 0.5f);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.12f, 0.12f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.75f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.9f, 0.25f, 0.25f, 1f));
        if (ImGui.Button(EggText.SettingsReset, resetButton))
            ImGui.OpenPopup(EggText.SettingsResetTitle);
        ImGui.PopStyleColor(3);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(EggText.SettingsResetTooltip);
        var confirmOpen = DrawResetConfirmation();

        ImGui.Dummy(new Vector2(0, LineHeight));
        ImGui.SetCursorPosX((width - button.x) * 0.5f);
        if (ImGui.Button(EggText.SettingsClose, button) || (ImGui.IsKeyPressed(ImGuiKey.Escape) && !confirmOpen))
        {
            SettingsOpen = false;
            if (SettingsDirty)
                EggStore.Save();
            SettingsDirty = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    // Nested modal; returns true while it is open (Escape then closes it instead of the settings).
    bool DrawResetConfirmation()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(Mathf.Min(420f, viewport.Size.x - 40f), 0));
        if (!ImGui.BeginPopupModal(EggText.SettingsResetTitle, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove))
            return false;

        ImGui.TextWrapped(EggText.SettingsResetMessage);
        ImGui.Dummy(new Vector2(0, LineHeight));
        var button = new Vector2(140, 36);
        var gap = 20f;
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - 2 * button.x - gap) * 0.5f);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.12f, 0.12f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.75f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.9f, 0.25f, 0.25f, 1f));
        var reset = ImGui.Button(EggText.SettingsResetConfirm, button);
        ImGui.PopStyleColor(3);
        ImGui.SameLine(0, gap);
        var cancel = ImGui.Button(EggText.SettingsResetCancel, button) || ImGui.IsKeyPressed(ImGuiKey.Escape);
        if (reset)
        {
            ResetState();
            Toast(EggText.SettingsResetDone, 4);
        }
        if (reset || cancel)
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return true;
    }

    bool SettingsDirty;
}
