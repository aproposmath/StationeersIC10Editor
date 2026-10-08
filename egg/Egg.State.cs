namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Assets.Scripts.UI;

using ImGuiNET;

using Newtonsoft.Json;

using UnityEngine;

public class EggState
{
    public int Highscore;
    public int FurnacesExploded;
    public Dictionary<string, double> RocketProgress = [];
    public List<string> Achievements = [];
    // Characters are unlocked in order, each by scoring enough with the previous one.
    public int UnlockedCharacters = 1;
    // The launch ceremony was watched to the end; progress is kept, the party stays available from the menu.
    public bool CeremonySeen;
    // Settings menu
    public float SoundVolume = 1f;
    public float MusicVolume = 1f;
    public bool SkipIntro;
    public bool ScreenShake = true;
}

// Persistent egg state: JSON, xor-scrambled, base64, stored in the "egg" config entry.
// The plain Highscore entry is mirrored from it; a mismatch means somebody edited the config.
public static class EggStore
{
    static readonly byte[] Key = Encoding.UTF8.GetBytes("ThisIsFine.hcf");

    public static EggState State = new();
    public static bool Corrupt;

    public static bool HighscoreTampered => Corrupt || (IC10EditorPlugin.Highscore?.Value ?? State.Highscore) != State.Highscore;

    public static void Load()
    {
        IC10EditorPlugin.BindEggConfig();
        var blob = IC10EditorPlugin.EggData.Value;
        Corrupt = false;
        if (string.IsNullOrEmpty(blob))
        {
            State = new EggState();
            return;
        }
        try
        {
            var json = Encoding.UTF8.GetString(Xor(Convert.FromBase64String(blob)));
            State = JsonConvert.DeserializeObject<EggState>(json) ?? new EggState();
            State.RocketProgress ??= new Dictionary<string, double>();
            State.Achievements ??= new List<string>();
            // Saves from before the ordered unlocks: keep what the highscore had unlocked.
            if (State.UnlockedCharacters < 1)
                State.UnlockedCharacters = Math.Max(1, Egg.CharactersUnlockedByScore(State.Highscore));
        }
        catch (Exception e)
        {
            L.Debug($"Egg state unreadable: {e.Message}");
            State = new EggState();
            Corrupt = true;
        }
    }

    public static void Save()
    {
        IC10EditorPlugin.BindEggConfig();
        var json = JsonConvert.SerializeObject(State);
        IC10EditorPlugin.EggData.Value = Convert.ToBase64String(Xor(Encoding.UTF8.GetBytes(json)));
        IC10EditorPlugin.Highscore.Value = State.Highscore;
        // Write the config file right away (not only on the next BepInEx flush).
        IC10EditorPlugin.Instance?.Config.Save();
        Corrupt = false;
    }

    static byte[] Xor(byte[] data)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ Key[i % Key.Length]);
        return result;
    }
}

public partial class Egg
{
    const string AchievementTurboBoost = "TurboBoost";
    const string AchievementCleanSweep = "CleanSweep";
    const string AchievementHcf = "Hcf";
    const string AchievementFan = "Fan";
    const string AchievementSuperFan = "SuperFan";
    const string AchievementRocketBuilder = "RocketBuilder";
    const string ModAuthor = "aproposmath";
    const int TurboBoostJumps = 8;
    int JumpsThisGame;

    static bool HasAchievement(string id) => EggStore.State.Achievements.Contains(id);

    // Unlocks since the menu was last shown; highlighted on the next menu visit.
    readonly HashSet<string> PendingAchievements = [];
    HashSet<string> NewAchievements = [];
    readonly HashSet<int> NewCharacters = [];
    int UnlockedSeen = -1;
    // Character unlocked by the score of the current game, shown on the game over screen.
    int UnlockedCharacterThisGame = -1;

    public static int CharactersUnlockedByScore(int score) => CharacterRequiredScore.Count(required => score >= required);

    // Highscore update plus the next character if the current one scored enough.
    void RecordScore(int total)
    {
        if (total > EggStore.State.Highscore)
            EggStore.State.Highscore = total;
        var next = EggStore.State.UnlockedCharacters;
        if (next < CharacterRequiredScore.Length && selectedCharacter == next - 1 && total >= CharacterRequiredScore[next])
        {
            EggStore.State.UnlockedCharacters = next + 1;
            UnlockedCharacterThisGame = next;
        }
    }

    // Highscore, unlocks, achievements and rocket progress; the settings are kept.
    void ResetState()
    {
        EggStore.State.Highscore = 0;
        EggStore.State.UnlockedCharacters = 1;
        EggStore.State.CeremonySeen = false;
        EggStore.State.FurnacesExploded = 0;
        EggStore.State.Achievements.Clear();
        UnlockAll = false;
        ReachedTargets.Clear();
        UnlockedSeen = -1;
        NewCharacters.Clear();
        PendingAchievements.Clear();
        NewAchievements.Clear();
        ResetRocketProgress();
        if (selectedCharacter != 0)
            SelectCharacter(0);
        EggStore.Save();
    }

    void CollectNewUnlocks()
    {
        NewAchievements = [.. PendingAchievements];
        PendingAchievements.Clear();
        NewCharacters.Clear();
        if (UnlockedSeen >= 0)
            for (var i = UnlockedSeen; i < EggStore.State.UnlockedCharacters; i++)
                NewCharacters.Add(i);
        UnlockedSeen = EggStore.State.UnlockedCharacters;
    }

    // Enabled mods (workshop or local) whose About.xml names the author.
    static int CountModsByAuthor(string author)
    {
        try
        {
            var config = WorkshopMenu.ModsConfig;
            if (config == null)
                return 0;
            return config.GetEnabledMods().Count(mod =>
                mod.GetAboutData()?.Author?.IndexOf(author, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch (Exception e)
        {
            L.Debug($"Mod list not available: {e.Message}");
            return 0;
        }
    }

    void CheckFanAchievements()
    {
        var count = CountModsByAuthor(ModAuthor);
        if (count >= 2)
            UnlockAchievement(AchievementFan, EggText.FanTitle);
        if (count >= 3)
            UnlockAchievement(AchievementSuperFan, EggText.SuperFanTitle);
    }

    void UnlockAchievement(string id, string title)
    {
        if (HasAchievement(id))
            return;
        EggStore.State.Achievements.Add(id);
        PendingAchievements.Add(id);
        EggStore.Save();
        Toast(string.Format(EggText.AchievementUnlocked, title), 5);
        EggAudio.Play("SFX_UI_PointOfInterestDiscovered");
    }

    bool CheaterChecked;
    bool CheaterDialogOpen;
    int CheaterJumps;
    readonly int[] CheaterButtonSlot = [0, 2];

    // Non-overlapping button slots inside the dialog (window coordinates).
    // Height from the previous frame: banner and wrapped text depend on the UI scale.
    float CheaterDialogHeight = 740f;

    // Six button slots in two rows of three, placed below the text.
    static Vector2 CheaterSlot(int slot, float top, float width, Vector2 button, float row)
    {
        float[] columns = [30f, (width - button.x) * 0.5f, width - 30f - button.x];
        return new Vector2(columns[slot % 3], top + slot / 3 * row);
    }

    string CheaterDoneLabel =>
        CheaterJumps >= 25 ? EggText.CheaterLeaveMeAlone :
        CheaterJumps >= 10 ? EggText.CheaterConsider :
        CheaterJumps >= 3 ? EggText.CheaterWillDo :
        EggText.CheaterDone;

    void DrawCheaterDialog()
    {
        if (!KsaBannerRequested)
            LoadKsaBanner().Forget();
        if (CheaterAlertPending)
        {
            if (CheaterAlertAt < 0)
                CheaterAlertAt = ImGui.GetTime() + 0.3;
            else if (ImGui.GetTime() >= CheaterAlertAt)
            {
                CheaterAlertPending = false;
                var voice = EggAudio.Play("IntruderAlert");
                L.Debug($"Intruder alert: {(voice != null ? "playing" : "clip not found")}");
            }
        }
        var viewport = ImGui.GetMainViewport();
        if (!ImGui.IsPopupOpen(EggText.CheaterTitle))
            ImGui.OpenPopup(EggText.CheaterTitle);
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        var width = Mathf.Min(720f, viewport.Size.x - 40f);
        ImGui.SetNextWindowSize(new Vector2(width, CheaterDialogHeight));
        if (!ImGui.BeginPopupModal(EggText.CheaterTitle, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove))
            return;

        if (KsaBanner != null)
        {
            // Full width, but never more than a third of the screen height.
            var bannerWidth = ImGui.GetContentRegionAvail().x;
            var bannerHeight = Mathf.Min(bannerWidth * KsaBanner.height / KsaBanner.width, viewport.Size.y * 0.33f);
            bannerWidth = bannerHeight * KsaBanner.width / KsaBanner.height;
            ImGui.SetCursorPosX((width - bannerWidth) * 0.5f);
            ImGui.Image(ImGuiManager.ImGuiPointerFor(KsaBanner), new Vector2(bannerWidth, bannerHeight));
            ImGui.Dummy(new Vector2(0, 12));
        }

        ImGui.PushTextWrapPos(width - 30f);
        ImGui.TextWrapped(EggText.CheaterMessage);
        ImGui.Dummy(new Vector2(0, 16));
        ImGui.TextWrapped(EggText.KsaDescription);
        ImGui.PopTextWrapPos();

        var size = new Vector2(200, 60);
        var row = size.y + 10f;
        var buttonsTop = ImGui.GetCursorPosY() + 24f;
        CheaterDialogHeight = buttonsTop + 2 * row + 20f;

        // "No way!" runs from the mouse, the other button closes the dialog.
        ImGui.SetCursorPos(CheaterSlot(CheaterButtonSlot[0], buttonsTop, width, size, row));
        ImGui.Button(EggText.CheaterNoWay, size);
        if (ImGui.IsItemHovered())
            JumpCheaterButton(0);

        ImGui.SetCursorPos(CheaterSlot(CheaterButtonSlot[1], buttonsTop, width, size, row));
        if (ImGui.Button(CheaterDoneLabel, size))
        {
            CloseCheaterDialog();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    void JumpCheaterButton(int button)
    {
        var current = CheaterButtonSlot[button];
        var other = CheaterButtonSlot[1 - button];
        var next = current;
        while (next == current || next == other)
            next = Random.Next(6);
        CheaterButtonSlot[button] = next;
        CheaterJumps++;
    }

    // The alert plays shortly after the dialog actually shows (the menu), not when it is scheduled during
    // the intro; the delay keeps it clear of the StopAll that ends the intro.
    bool CheaterAlertPending;
    double CheaterAlertAt = -1;

    void OpenCheaterDialog()
    {
        CheaterDialogOpen = true;
        CheaterAlertPending = true;
        CheaterAlertAt = -1;
    }

    // A tampered plain highscore is ignored, the stored state wins (already written back by the save).
    void CloseCheaterDialog()
    {
        CheaterDialogOpen = false;
        CheaterJumps = 0;
        CheaterButtonSlot[0] = 0;
        CheaterButtonSlot[1] = 2;
        EggStore.Save();
    }
}
