namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.Linq;

using Assets.Scripts.Objects;
using Assets.Scripts.UI;

using ImGuiNET;

using UnityEngine;

// Ending: launch party while the rocket is assembled, boarding, lift-off, acknowledgements.
// Runs on its own clock (CTime) so it can be fast-forwarded for debugging.
public partial class Egg
{
    enum CeremonyPhase { Announce, Party, Boarding, Launch, Credits }

    const double AnnounceDuration = 6.0;

    struct PartySong
    {
        public string Match;
        public double Bpm;
        public double BeatOffset;
        public double Length;
    }

    // Measured offline (onset flux comb filter); first match found in the music folder is used.
    static readonly PartySong[] PartySongs =
    [
        new() { Match = "Riding", Bpm = 136.87, BeatOffset = 0.384, Length = 172.1 },
        new() { Match = "KERNKRAFT", Bpm = 140.0, BeatOffset = 0.232, Length = 85.3 },
        new() { Match = "Monorail_Man", Bpm = 86.0, BeatOffset = 0.610, Length = 89.8 },
    ];

    const double BoardingStep = 1.0;
    const double BoardingTravel = 1.8;
    const double LaunchDuration = 12.0;
    // Aimee stays stuck through boarding; after lift-off she teleports onto the rocket's nose.
    const double AimeeUnstick = 1.4;
    const double AimeeTeleport = 2.6;
    // Fraction of the way to the hatch where Aimee gets stuck during boarding.
    const float AimeeStuckAt = 0.55f;
    const double FallbackPartyLength = 20.0;
    const float PartyMusicVolume = 1.2f;
    // The party ends (boarding starts) after this at the latest, even if the song is longer.
    const double MaxPartyLength = 90.0;
    // Credits: Aimee hangs on this long before she drifts off.
    const double AimeeLetGo = 60.0;
    const float GuestX = 2.6f;
    const float GuestY = 21.8f;
    const double BubbleDuration = 7.0;
    const double BubbleMinGap = 2.0;
    const double BubbleMaxGap = 5.0;
    const float BoothTop = 4.5f;
    const float RocketX = 34.5f;
    const float RocketBaseY = 25.5f;
    const float RocketPartHeight = 3.2f;
    const float RocketPartWidth = 4.6f;
    const int RocketPartCount = 5;
    const int AlloyPileCount = 14;
    const float FloorTop = 18.0f;
    const float CrewSize = 2.9f;
    const float CreditsTextX = 14.5f;
    // The credits canvas fills the screen; its logical height is stored for the update step.
    float CreditsHeight = 30f;
    // Rocket, exhaust and Aimee are drawn on a canvas scaled by this factor (same origin), so their
    // coordinates are in "rocket units": screen position = value * CreditsRocketScale.
    const float CreditsRocketScale = 0.8f;
    float CreditsRocketX => 34.0f / CreditsRocketScale;
    float CreditsRocketBaseY => CreditsHeight * 0.72f / CreditsRocketScale;
    const float CreditsScrollSpeed = 1.0f;

    // Sprite corrections per part: the crew module thumbnail is rendered tilted and larger than the fuselage.
    static readonly float[] RocketPartRotation = [0f, 0f, 0f, 0f, -0.765f];
    static readonly float[] RocketPartScale = [1f, 1f, 1f, 1f, 1.43f];
    static readonly Vector2[] RocketPartOffset = [Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(-0.03f, -0.144f)];

    static readonly Vector2 LunaHome = new(38.2f, 28.0f);

    readonly struct CeremonyCanvas
    {
        public readonly Vector2 Origin;
        public readonly float Unit;
        // Logical height in units; the width is always 40.
        public readonly float Height;

        public CeremonyCanvas(Vector2 origin, float unit, float height = 30f)
        {
            Origin = origin;
            Unit = unit;
            Height = height;
        }

        public Vector2 Point(float x, float y) => Origin + new Vector2(x * Unit, y * Unit);
        public Vector2 Point(Vector2 p) => Point(p.x, p.y);
    }

    struct ExhaustParticle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Age;
        public float Life;
        public float Size;
    }

    CeremonyPhase Phase;
    double CTime;
    double CeremonyDt;
    double LastRealTime;
    double PhaseStart;
    double PartyStartVirtual = -1;
    PartySong PartySongInfo;
    MusicTrack PartyTrack;
    ScoreBreakdown LaunchScore;
    readonly List<Texture2D> Crew = [];
    readonly List<string> CrewNames = [];
    double BoardingDuration;
    double BoardingStartTime;
    bool AimeeStuckShown;
    bool AimeeToastShown;
    bool AimeeLetGoShown;
    EggAudio.Voice EngineVoice;
    EggAudio.Voice EngineStartVoice;

    // Party small talk: who talks (crew index, or a guest id), what, since when.
    const int SpeakerIcarus = -1;
    const int SpeakerLuna = -2;
    int BubbleSpeaker;
    string BubbleText;
    double BubbleStart = -100;
    double NextBubbleTime;
    // (speaker, line) pairs already shown this party, so nothing repeats until everything was said.
    readonly HashSet<(int, int)> UsedBubbles = [];
    readonly List<ExhaustParticle> Exhaust = [];
    double LastExhaustTime;

    Texture2D Luna;
    Texture2D IcarusSuit;
    Texture2D Skull;

    double PhaseTime => CTime - PhaseStart;
    double PartyLength => Math.Min(MaxPartyLength, PartyTrack != null ? PartySongInfo.Length : FallbackPartyLength);
    // The rocket is complete when boarding starts, so everybody is inside when the song ends.
    double BuildLength => Math.Max(5.0, PartyLength - BoardingDuration);
    double PartyProgress => PartyStartVirtual >= 0 ? Math.Min(1.0, (CTime - PartyStartVirtual) / BuildLength) : 0.0;
    double PartyRemaining => Math.Max(0.0, PartyLength - (PartyStartVirtual >= 0 ? CTime - PartyStartVirtual : 0.0));

    // Visual beat phase correction in beats (tune with the debug slider, then hardcode).
    public static float BeatShift = 0.5f;

    // Beat follows the audio clock of the track, not the frame clock.
    double Beat => PartyTrack != null && PartyStartVirtual >= 0
        ? (PartyTrack.Position - PartySongInfo.BeatOffset) * PartySongInfo.Bpm / 60.0 + BeatShift
        : CTime * 2.0;

    Vector2 Hatch => new(RocketX - RocketPartWidth * 0.5f - 0.6f, RocketBaseY - 2 * RocketPartHeight + 0.6f);

    public void StartCeremony()
    {
        Luna = Thumbnail("ToyLuna");
        IcarusSuit = Thumbnail("ItemIcarusSuit");

        EggAudio.StopAll();
        EggMusic.Stop();
        EggMusic.AutoAdvance = false;
        IsValveOpen = false;
        Exhaust.Clear();

        LaunchScore = GetScore();
        RecordScore(LaunchScore.Total);
        EggStore.Save();

        Crew.Clear();
        CrewNames.Clear();
        var textures = CharacterTextures;
        for (var i = 0; i < CharacterNames.Length; i++)
        {
            if (!IsUnlocked(i))
                continue;
            Crew.Add(textures[i]);
            CrewNames.Add(CharacterNames[i]);
        }
        Crew.Add(Dj ?? Helmet);
        CrewNames.Add(EggText.DjName);
        BoardingDuration = BoardingStep * Crew.Count + BoardingTravel + 1.0;
        AimeeStuckShown = false;
        AimeeToastShown = false;
        AimeeLetGoShown = false;
        BubbleStart = -100;
        UsedBubbles.Clear();
        NextBubbleTime = AnnounceDuration + 3.0;

        PartyTrack = null;
        PartySongInfo = PartySongs[0];
        foreach (var song in PartySongs)
        {
            var track = EggMusic.Find(song.Match);
            if (track == null)
                continue;
            PartyTrack = track;
            PartyTrack.Volume = PartyMusicVolume;
            PartySongInfo = song;
            break;
        }
        if (PartyTrack == null)
            L.Debug("No party song found in the cache music folder");

        GameMode = CeremonyMode;
        CTime = 0;
        LastRealTime = ImGui.GetTime();
        PartyStartVirtual = -1;
        SetPhase(CeremonyPhase.Announce);
    }

    void SetPhase(CeremonyPhase phase)
    {
        Phase = phase;
        PhaseStart = CTime;
        switch (phase)
        {
            case CeremonyPhase.Party:
                PartyStartVirtual = PartyTrack == null ? CTime : -1;
                if (PartyTrack != null)
                    EggMusic.Play(PartyTrack);
                break;
            case CeremonyPhase.Boarding:
                BoardingStartTime = CTime;
                Toast(EggText.AllAboard, 3);
                break;
            case CeremonyPhase.Launch:
                // Ignition one-shot plus the engine loop from the intro; the loop ends with the credits.
                EngineStartVoice = EggAudio.Play(EggAudio.FindByName("ShuttleSmall", "MainEngine", "Start") ?? EggAudio.FindByName("RocketBlast"), 0.8f);
                EngineVoice = EggAudio.Play(EggAudio.FindByName("ShuttleSmall", "MainEngine", "LP") ?? EggAudio.FindByName("ShuttleSmall", "Engine"), 0.8f);
                LastExhaustTime = CTime;
                if (EggMusic.SpaceMusic != null)
                    EggMusic.Play(EggMusic.SpaceMusic);
                else
                    EggMusic.Stop();
                break;
            case CeremonyPhase.Credits:
                EngineVoice?.Stop();
                EngineStartVoice?.Stop();
                Toasts.Clear();
                Exhaust.Clear();
                LastExhaustTime = CTime;
                GenerateCreditsStars();
                ResetFinale();
                if (EggMusic.SpaceMusic != null && EggMusic.Current != EggMusic.SpaceMusic)
                    EggMusic.Play(EggMusic.SpaceMusic);
                break;
        }
    }

    // Escape: next phase, or back to the title screen from the credits. Progress is kept.
    void SkipCeremonyPhase()
    {
        if (Phase == CeremonyPhase.Credits)
        {
            EggStore.State.CeremonySeen = true;
            EggStore.Save();
            EngineVoice?.Stop();
            EngineStartVoice?.Stop();
            EggAudio.StopAll();
            Exhaust.Clear();
            StartNewGame(InitialCode);
        }
        else
            SetPhase(Phase + 1);
    }

    void UpdateCeremony()
    {
        var real = ImGui.GetTime();
        CeremonyDt = (real - LastRealTime) * (FastForward ? 10.0 : 1.0);
        LastRealTime = real;
        CTime += CeremonyDt;

        if (PartyStartVirtual < 0 && PartyTrack != null && PartyTrack.PlaybackStart > 0)
            PartyStartVirtual = CTime;

        switch (Phase)
        {
            case CeremonyPhase.Announce:
                if (PhaseTime >= AnnounceDuration)
                    SetPhase(CeremonyPhase.Party);
                break;
            case CeremonyPhase.Party:
                UpdateSpeechBubbles();
                // Boarding starts early enough that the launch coincides with the end of the song.
                var boardNow = PartyStartVirtual >= 0 && CTime - PartyStartVirtual >= PartyLength - BoardingDuration;
                if (PartyTrack != null && PartyStartVirtual >= 0 && !PartyTrack.IsPlaying && !FastForward)
                    boardNow = true;
                if (PhaseTime > PartyLength + 15)
                    boardNow = true;
                if (boardNow)
                    SetPhase(CeremonyPhase.Boarding);
                break;
            case CeremonyPhase.Boarding:
                if (!AimeeStuckShown && AimeeIndex >= 0 && PhaseTime >= BoardingOrder(AimeeIndex) * BoardingStep + BoardingTravel * AimeeStuckAt + 0.3)
                {
                    AimeeStuckShown = true;
                    ShowBubble(AimeeIndex, EggText.AimeeStuck);
                }
                if (PhaseTime >= BoardingDuration)
                    SetPhase(CeremonyPhase.Launch);
                break;
            case CeremonyPhase.Credits:
                UpdateFinale();
                if (!FinaleExploded)
                    UpdateExhaust(new Vector2(CreditsRocketX + CreditsSway(), CreditsRocketBaseY + 0.1f));
                if (!AimeeLetGoShown && PhaseTime >= AimeeLetGo && AimeeIndex >= 0 && !FinaleExploded)
                {
                    AimeeLetGoShown = true;
                    ShowBubble(AimeeIndex, EggText.AimeeLetGo);
                }
                break;
            case CeremonyPhase.Launch:
                UpdateExhaust(new Vector2(RocketX, RocketBaseY - RocketPartHeight - Rise(PhaseTime) + 0.1f));
                if (!AimeeToastShown && PhaseTime >= AimeeUnstick && AimeeIndex >= 0)
                {
                    AimeeToastShown = true;
                    ShowBubble(AimeeIndex, EggText.AimeeWait);
                }
                if (PhaseTime >= LaunchDuration)
                    SetPhase(CeremonyPhase.Credits);
                break;
        }
    }

    int AimeeIndex => CrewNames.IndexOf(EggText.CharacterNames[3]);

    float Rise(double launchTime) => Mathf.Pow((float)(launchTime / LaunchDuration), 2.2f) * 45f;

    Vector2 AimeeStuckPosition()
    {
        DancePose(AimeeIndex, BoardingStartTime, out var start, out _);
        return Vector2.Lerp(start, Hatch, AimeeStuckAt);
    }

    // Where Aimee sits once she teleported onto the rocket: the nose of the crew module (feet position).
    Vector2 AimeeAttachPoint(double launchTime) =>
        new(RocketX, RocketBaseY - RocketPartCount * RocketPartHeight - Rise(launchTime) + 0.2f);

    // Like her in-game stuck routine: wiggle, then she is suddenly somewhere else (on the rocket).
    void GetAimeeLaunchPose(double t, out Vector2 position, out int facing, out float jump)
    {
        var stuck = AimeeStuckPosition();
        facing = 0;
        jump = 0f;

        if (t < AimeeTeleport)
        {
            position = stuck + new Vector2(Mathf.Sin((float)t * 25f) * 0.15f, 0);
            facing = (int)(t / 0.15) % 2 == 0 ? 0 : 2;
            return;
        }

        facing = (int)((t - AimeeTeleport) / 0.4) % 2 == 0 ? 0 : 2;
        position = AimeeAttachPoint(t) + new Vector2(Mathf.Sin((float)t * 9f) * 0.08f, 0);
    }


    public void DrawCeremony()
    {
        UpdateCeremony();

        var childSize = ImGui.GetContentRegionAvail();
        ImGui.BeginChild("ceremony", childSize, false);

        var available = ImGui.GetContentRegionAvail();
        CeremonyCanvas canvas;
        if (Phase == CeremonyPhase.Credits)
        {
            // full screen, 40 units wide, height from the aspect ratio
            var unit = Mathf.Max(1.0f, available.x / 40.0f);
            CreditsHeight = available.y / unit;
            canvas = new CeremonyCanvas(ImGui.GetCursorScreenPos(), unit, CreditsHeight);
        }
        else
        {
            var unit = Mathf.Max(1.0f, Mathf.Floor(Mathf.Min(available.x / 40.0f, available.y / 30.0f)));
            var sceneSize = new Vector2(40.0f * unit, 30.0f * unit);
            var origin = ImGui.GetCursorScreenPos() + (available - sceneSize) * 0.5f;
            if (Phase == CeremonyPhase.Launch)
                origin += LaunchShake(unit);
            canvas = new CeremonyCanvas(origin, unit);
        }

        switch (Phase)
        {
            case CeremonyPhase.Announce:
                DrawAnnounce(canvas);
                break;
            case CeremonyPhase.Credits:
                DrawCredits(canvas);
                break;
            default:
                DrawPartyScene(canvas);
                break;
        }

        DrawToasts(canvas.Point(20.0f, 12.5f));

        // no hint during the credits, the ending should not be spoiled
        if (Phase != CeremonyPhase.Credits)
        {
            var hint = Phase == CeremonyPhase.Announce ? EggText.HintContinue : EggText.HintSkip;
            ImGui.GetWindowDrawList().AddText(canvas.Point(0.5f, canvas.Height - 1.0f), ColorWithAlpha(180, 190, 210, 220), hint);
        }

        ImGui.EndChild();
    }

    void DrawAnnounce(CeremonyCanvas canvas)
    {
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(canvas.Point(0, 0), canvas.Point(40, 30), ColorWithAlpha(10, 8, 20, 255));
        var lines = EggText.PartyAnnounce.Split('\n');
        var alpha = (byte)(255 * Mathf.Clamp01((float)PhaseTime / 0.8f));
        for (var i = 0; i < lines.Length; i++)
            DrawCenteredText(canvas, lines[i], 11.0f + i * 3.0f, ColorWithAlpha(255, 225, 105, alpha), 40.0f);
    }

    Vector2 LaunchShake(float unit)
    {
        if (!EggStore.State.ScreenShake)
            return Vector2.zero;
        var t = (float)PhaseTime;
        var fade = 1f - Mathf.Clamp01(t / (float)LaunchDuration);
        var amp = 0.25f * unit * fade;
        return new Vector2(Mathf.Sin(t * 47f) * amp, Mathf.Cos(t * 31f) * amp * 0.7f);
    }

    void DrawPartyScene(CeremonyCanvas canvas)
    {
        var beat = Beat;
        var beatIndex = (int)Math.Floor(beat);
        var frac = (float)(beat - Math.Floor(beat));
        var pulse = Mathf.Exp(-4f * frac);

        DrawPartyRoom(canvas, beat, beatIndex, pulse);
        DrawAlloyPile(canvas);
        DrawRocket(canvas);
        DrawDjBooth(canvas, beat, pulse);
        DrawPartyGuests(canvas, beat, beatIndex, pulse);
        DrawCrew(canvas, beatIndex, frac);

        var gold = ColorWithAlpha(255, 225, 105, 255);
        switch (Phase)
        {
            case CeremonyPhase.Party:
                DrawTextAt(canvas, EggText.LaunchParty, 1.0f, 0.6f, gold, 34.0f);
                DrawTextAt(canvas, string.Format(EggText.TMinus, PartyRemaining), 1.0f, 2.6f, ColorWithAlpha(220, 225, 255, 255), 36.0f);
                DrawSpeechBubble(canvas);
                break;
            case CeremonyPhase.Boarding:
                DrawTextAt(canvas, EggText.AllAboard, 1.0f, 0.8f, gold, 26.0f);
                DrawTextAt(canvas, string.Format(EggText.TMinus, PartyRemaining), 1.0f, 2.6f, ColorWithAlpha(220, 225, 255, 255), 36.0f);
                DrawSpeechBubble(canvas);
                break;
            case CeremonyPhase.Launch:
                DrawTextAt(canvas, EggText.LiftOff, 1.0f, 0.8f, gold, 28.0f);
                DrawSpeechBubble(canvas);
                break;
        }

        if (Phase != CeremonyPhase.Launch)
            DrawConfetti(canvas, (float)CTime, 80);
    }

    void DrawPartyRoom(CeremonyCanvas canvas, double beat, int beatIndex, float pulse)
    {
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(canvas.Point(0, 0), canvas.Point(40, 30), ColorWithAlpha(15, 12, 32, 255));

        var lightColors = new[]
        {
            ColorWithAlpha(255, 60, 110, 255),
            ColorWithAlpha(60, 190, 255, 255),
            ColorWithAlpha(250, 210, 50, 255),
        };

        for (var i = 0; i < lightColors.Length; i++)
        {
            // beat is negative before the first downbeat, so wrap the index properly
            var color = lightColors[((beatIndex + i) % lightColors.Length + lightColors.Length) % lightColors.Length];
            var alpha = (byte)(45 + 80 * pulse);
            var source = canvas.Point(7 + i * 13, 1.5f);
            var sweep = Mathf.Sin((float)(beat * Math.PI / 8.0) + i * 2.1f) * 14f;
            var target = canvas.Point(20 + sweep, 24);
            draw.AddLine(source, target, WithAlpha(color, alpha), (2.4f + 1.2f * pulse) * canvas.Unit);
            draw.AddCircleFilled(source, 0.7f * canvas.Unit, ColorWithAlpha(240, 240, 255, 255), 16);
        }

        for (var y = (int)FloorTop; y < 30; y += 2)
        {
            for (var x = 0; x < 40; x += 4)
            {
                var even = ((x / 4) + (y / 2)) % 2 == 0;
                var color = even ? ColorWithAlpha(55, 45, 90, 255) : ColorWithAlpha(25, 28, 55, 255);
                draw.AddRectFilled(canvas.Point(x, y), canvas.Point(x + 4, y + 2), color);
            }
        }
    }

    // Ingots waiting next to the rocket; the pile shrinks as the parts get built.
    void DrawAlloyPile(CeremonyCanvas canvas)
    {
        if (Phase != CeremonyPhase.Party || AlloyTypes.Count == 0)
            return;
        var remaining = Mathf.CeilToInt(AlloyPileCount * (1f - (float)PartyProgress));
        for (var i = 0; i < remaining; i++)
        {
            var x = RocketX - 6.2f + (i * 7 % 11) * 0.28f + (i % 3) * 0.15f;
            var y = FloorTop + 1.2f + (i * 5 % 7) * 0.22f;
            var sprite = AlloyTypes[i % AlloyTypes.Count].GetThumbnail().texture;
            DrawSprite(sprite, canvas.Point(x, y), 1.1f * canvas.Unit);
        }
    }

    void DrawDjBooth(CeremonyCanvas canvas, double beat, float pulse)
    {
        var draw = ImGui.GetWindowDrawList();
        const float top = BoothTop;

        // standing behind the table: sprite bottom just below the table's top edge
        if (Phase == CeremonyPhase.Party)
            DrawCeremonyCharacter(canvas, Dj ?? Helmet, 20.0f, top + 2.4f, 6.5f, 0, pulse * 0.4f);

        draw.AddRectFilled(canvas.Point(13.5f, top + 1.0f), canvas.Point(26.5f, top + 6.5f), ColorWithAlpha(30, 32, 42, 255));
        draw.AddRect(canvas.Point(13.5f, top + 1.0f), canvas.Point(26.5f, top + 6.5f), ColorWithAlpha(130, 150, 190, 255), 0, 0, 0.18f * canvas.Unit);
        draw.AddRectFilled(canvas.Point(12.5f, top), canvas.Point(27.5f, top + 2.5f), ColorWithAlpha(65, 70, 88, 255));

        var platterColor = ColorWithAlpha((byte)(100 + 120 * pulse), 90, 255, 255);
        var platterAngle = (float)(beat * Math.PI / 2.0);
        foreach (var cx in new[] { 16.0f, 24.0f })
        {
            var center = canvas.Point(cx, top + 1.2f);
            draw.AddCircleFilled(center, 1.3f * canvas.Unit, ColorWithAlpha(18, 18, 22, 255), 24);
            draw.AddCircle(center, 1.0f * canvas.Unit, platterColor, 24, 0.15f * canvas.Unit);
            draw.AddCircleFilled(center + new Vector2(Mathf.Cos(platterAngle), Mathf.Sin(platterAngle)) * 0.6f * canvas.Unit, 0.18f * canvas.Unit, 0xFFFFFFFF, 12);
        }

        draw.AddRectFilled(canvas.Point(18.5f, top + 0.3f), canvas.Point(21.5f, top + 2.2f), ColorWithAlpha(20, 20, 25, 255));
        for (var i = 0; i < 4; i++)
        {
            var level = 0.3f + 1.1f * Mathf.Clamp01(pulse * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin((float)beat * 3.1f + i))));
            draw.AddLine(canvas.Point(19.0f + i * 0.65f, top + 1.8f), canvas.Point(19.0f + i * 0.65f, top + 1.8f - level), ColorWithAlpha(80, 255, 130, 255), 0.18f * canvas.Unit);
        }

        foreach (var sx in new[] { 8.5f, 28.0f })
        {
            draw.AddRectFilled(canvas.Point(sx, top + 1.5f), canvas.Point(sx + 3.5f, top + 8.0f), ColorWithAlpha(24, 24, 30, 255));
            draw.AddCircle(canvas.Point(sx + 1.75f, top + 3.5f), (1.15f + 0.12f * pulse) * canvas.Unit, ColorWithAlpha(150, 100, 255, 255), 20, 0.25f * canvas.Unit);
            draw.AddCircle(canvas.Point(sx + 1.75f, top + 6.5f), (0.8f + 0.1f * pulse) * canvas.Unit, ColorWithAlpha(80, 190, 255, 255), 20, 0.2f * canvas.Unit);
        }

    }

    // Icarus suit with a skull nodding forward on the beat; the last two bars of every eight it spins.
    void DrawPartyGuests(CeremonyCanvas canvas, double beat, int beatIndex, float pulse)
    {
        const float bodySize = 3.2f;
        const float guestY = GuestY;
        DrawCeremonyCharacter(canvas, IcarusSuit, GuestX, guestY, bodySize, 0, 0);

        var bar = beatIndex / 4;
        var spinning = bar % 8 >= 6;
        var spinStart = Math.Floor(beat / 32.0) * 32.0 + 24.0;
        var headRotation = spinning ? (float)((beat - spinStart) / 8.0) * 2f * Mathf.PI * 5f : 0.35f * pulse;
        var headOffset = spinning
            ? new Vector2(Mathf.Cos(headRotation) * 0.35f, Mathf.Sin(headRotation) * 0.35f)
            : new Vector2(0.6f * headRotation - 0.2f, -0.15f * headRotation + 0.3f);
        const float skullSize = 1.55f * 1.3f;
        var headCenter = canvas.Point(GuestX + headOffset.x, guestY - bodySize * 0.78f - 1.55f * 0.3f - headOffset.y);
        DrawRotatedCeremonySprite(Skull, headCenter, skullSize * canvas.Unit, headRotation);

        if (Phase == CeremonyPhase.Launch)
            return;

        var halfBeat = beat * 0.5;
        var halfFrac = (float)(halfBeat - Math.Floor(halfBeat));
        var lunaRotation = 0.12f * Mathf.Exp(-3f * halfFrac) - 0.04f;
        var lunaCenter = LunaHome + new Vector2(0, 0.2f * Mathf.Exp(-3f * halfFrac));
        if (Phase == CeremonyPhase.Boarding)
        {
            GetLunaPose(PhaseTime, out lunaCenter, out var boarded);
            if (boarded)
                return;
            lunaRotation = 0f;
        }
        DrawRotatedCeremonySprite(Luna, canvas.Point(lunaCenter), 3.0f * canvas.Unit, lunaRotation);
    }

    // The cat boards last, after everybody else has left the dance floor.
    void GetLunaPose(double t, out Vector2 center, out bool boarded)
    {
        var local = t - BoardingStep * Crew.Count;
        var target = Hatch + new Vector2(0.3f, -1.0f);
        boarded = false;
        center = LunaHome;
        if (local <= 0)
            return;
        var u = (float)(local / BoardingTravel);
        if (u >= 1f)
        {
            boarded = true;
            center = target;
            return;
        }
        center = Vector2.Lerp(LunaHome, target, u) + new Vector2(0, -Mathf.Abs(Mathf.Sin(u * Mathf.PI * 5f)) * 0.4f);
    }

    // state: build state to show, -1 = finished part
    void DrawRocketPart(int index, Vector2 centerPx, float sizePx, float extraRotation = 0f, uint tint = 0xFFFFFFFF, int state = -1)
    {
        var offset = RocketPartOffset[index] * sizePx;
        var c = Mathf.Cos(extraRotation);
        var s = Mathf.Sin(extraRotation);
        var rotatedOffset = new Vector2(offset.x * c - offset.y * s, offset.x * s + offset.y * c);
        var states = RocketPartStates[index];
        var sprite = state >= 0 && state < states.Count ? states[state] : RocketParts[index];
        DrawRotatedCeremonySprite(sprite, centerPx + rotatedOffset, sizePx * RocketPartScale[index], RocketPartRotation[index] + extraRotation, tint);
    }

    // Spread over the floor in two depth rows.
    Vector2 DanceBase(int index)
    {
        var dancers = Crew.Count - 1;
        var x = dancers <= 1 ? 16.0f : 4.0f + index * 25.0f / (dancers - 1);
        return new Vector2(x, index % 2 == 0 ? 24.5f : 27.0f);
    }

    // Slow wandering around the base spot; facing follows the direction of movement.
    void DancePose(int index, double time, out Vector2 position, out int facing)
    {
        var phase = (float)time * 0.35f + index * 1.7f;
        var sway = Mathf.Sin(phase) * 2.2f;
        var depth = Mathf.Sin((float)time * 0.23f + index * 0.9f) * 0.8f;
        position = DanceBase(index) + new Vector2(sway, depth);
        facing = Mathf.Cos(phase) >= 0f ? 0 : 2;
    }

    int BoardingOrder(int index)
    {
        var name = CrewNames[index];
        if (name == EggText.DjName) return 0;
        if (name == EggText.CharacterNames[3]) return Crew.Count - 1;
        var order = 1;
        for (var i = 0; i < index; i++)
            if (CrewNames[i] != EggText.DjName && CrewNames[i] != EggText.CharacterNames[3])
                order++;
        return order;
    }

    // Position, visibility and animation of a crew member during boarding.
    void GetBoardingPose(int index, double t, out Vector2 position, out bool boarded, out int facing, out float jump)
    {
        var name = CrewNames[index];
        Vector2 start;
        if (name == EggText.DjName)
            start = new Vector2(20.0f, BoothTop + 4.5f);
        else
            DancePose(index, BoardingStartTime, out start, out _);
        var hatch = Hatch;
        var local = t - BoardingOrder(index) * BoardingStep;
        boarded = false;
        facing = hatch.x >= start.x ? 0 : 2;
        jump = 0f;
        position = start;

        if (local <= 0)
            return;

        if (name != EggText.CharacterNames[3])
        {
            var u = (float)(local / BoardingTravel);
            if (u >= 1f)
            {
                boarded = true;
                position = hatch;
                return;
            }
            position = Vector2.Lerp(start, hatch, u);
            jump = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 6f)) * 0.3f;
            return;
        }

        var travel = BoardingTravel * AimeeStuckAt;
        var stuckPos = Vector2.Lerp(start, hatch, AimeeStuckAt);
        if (local < travel)
        {
            var u = (float)(local / travel);
            position = Vector2.Lerp(start, stuckPos, u);
            jump = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 4f)) * 0.3f;
            return;
        }

        // stuck until lift-off
        var s = local - travel;
        position = stuckPos + new Vector2(Mathf.Sin((float)s * 25f) * 0.15f, 0);
        facing = (int)(s / 0.15) % 2 == 0 ? 0 : 2;
    }

    void DrawCrew(CeremonyCanvas canvas, int beatIndex, float frac)
    {
        for (var i = 0; i < Crew.Count; i++)
        {
            var isDj = CrewNames[i] == EggText.DjName;
            switch (Phase)
            {
                case CeremonyPhase.Party:
                    if (isDj)
                        continue;
                    DancePose(i, CTime, out var dance, out var facing);
                    var jump = (beatIndex + i) % 2 == 0 ? Mathf.Sin(frac * Mathf.PI) * 1.2f : 0f;
                    DrawCeremonyCharacter(canvas, Crew[i], dance.x, dance.y, CrewSize, facing, jump);
                    break;
                case CeremonyPhase.Boarding:
                    GetBoardingPose(i, PhaseTime, out var pos, out var boarded, out var face, out var bounce);
                    if (!boarded)
                        DrawCeremonyCharacter(canvas, Crew[i], pos.x, pos.y, isDj ? CrewSize + 0.7f : CrewSize, face, bounce);
                    break;
                case CeremonyPhase.Launch:
                    if (i != AimeeIndex)
                        continue;
                    GetAimeeLaunchPose(PhaseTime, out var apos, out var aface, out var ajump);
                    DrawCeremonyCharacter(canvas, Crew[i], apos.x, apos.y, CrewSize, aface, ajump);
                    break;
            }
        }
    }

    int RocketPartsVisible()
    {
        if (Phase != CeremonyPhase.Party)
            return RocketPartCount;
        return Mathf.Clamp(1 + (int)(PartyProgress * RocketPartCount), 1, RocketPartCount);
    }

    void DrawRocket(CeremonyCanvas canvas)
    {
        var draw = ImGui.GetWindowDrawList();
        var rise = Phase == CeremonyPhase.Launch ? Mathf.Pow((float)(PhaseTime / LaunchDuration), 2.2f) * 45f : 0f;
        var visible = RocketPartsVisible();

        if (Phase == CeremonyPhase.Launch)
            DrawExhaust(canvas);

        // part 0 is the launch mount and stays on the ground
        for (var i = 0; i < visible && i < RocketParts.Count; i++)
        {
            var scale = 1f;
            var state = -1;
            if (Phase == CeremonyPhase.Party && i == visible - 1)
            {
                // the part under construction pops in and steps through its build states
                var progress = (float)(PartyProgress * RocketPartCount - i);
                scale = SmootherStep(Mathf.Clamp01(progress * 6f));
                state = Math.Min((int)(progress * RocketPartStates[i].Count), RocketPartStates[i].Count - 1);
            }
            var size = RocketPartWidth * scale;
            var bottom = RocketBaseY - i * RocketPartHeight - (i == 0 ? 0f : rise);
            var center = new Vector2(RocketX, bottom - RocketPartWidth * 0.5f);
            DrawRocketPart(i, canvas.Point(center), size * canvas.Unit, state: state);
        }

        if (Phase == CeremonyPhase.Boarding || Phase == CeremonyPhase.Launch)
            DrawPortholes(canvas, RocketX, RocketBaseY, rise);
    }

    // Boarded crew (and the cat) look out of round windows on the fuselage segments and crew module.
    void DrawPortholes(CeremonyCanvas canvas, float rocketX, float baseY, float rise)
    {
        var allBoarded = Phase >= CeremonyPhase.Launch;
        var slot = 0;
        for (var order = 0; order < Crew.Count; order++)
        {
            var index = -1;
            for (var i = 0; i < Crew.Count; i++)
                if (BoardingOrder(i) == order) { index = i; break; }
            if (index < 0)
                continue;

            var boarded = allBoarded && index != AimeeIndex;
            if (!allBoarded)
                GetBoardingPose(index, PhaseTime, out _, out boarded, out _, out _);
            if (boarded)
                DrawPorthole(canvas, slot++, Crew[index], rocketX, baseY, rise);
        }

        var lunaBoarded = allBoarded;
        if (!allBoarded)
            GetLunaPose(PhaseTime, out _, out lunaBoarded);
        if (lunaBoarded)
            DrawPorthole(canvas, slot, Luna, rocketX, baseY, rise);
    }

    void DrawPorthole(CeremonyCanvas canvas, int slot, Texture2D face, float rocketX, float baseY, float rise)
    {
        var part = 2 + slot / 2;
        if (part >= RocketPartCount)
            return;
        var side = slot % 2 == 0 ? -0.95f : 0.95f;
        var draw = ImGui.GetWindowDrawList();
        var bob = Phase >= CeremonyPhase.Launch ? Mathf.Sin((float)CTime * 6f + slot) * 0.08f : 0f;
        var center = canvas.Point(rocketX + side, baseY - part * RocketPartHeight - RocketPartHeight * 0.5f - rise + bob);
        var radius = 0.62f * canvas.Unit;
        draw.AddCircleFilled(center, radius + 0.12f * canvas.Unit, ColorWithAlpha(30, 32, 40, 255), 24);
        draw.AddImageRounded(ImGuiManager.ImGuiPointerFor(face), center - new Vector2(radius, radius), center + new Vector2(radius, radius),
            Vector2.zero, Vector2.one, 0xFFFFFFFF, radius, ImDrawFlags.RoundCornersAll);
        draw.AddCircle(center, radius + 0.06f * canvas.Unit, ColorWithAlpha(190, 200, 220, 255), 24, 0.12f * canvas.Unit);
    }

    void UpdateExhaust(Vector2 nozzle)
    {
        var dt = (float)CeremonyDt;

        while (CTime - LastExhaustTime > 0.01)
        {
            LastExhaustTime += 0.01;
            Exhaust.Add(new ExhaustParticle
            {
                Position = nozzle + new Vector2(NextFloat(-0.5f, 0.5f), 0),
                Velocity = new Vector2(NextFloat(-2.5f, 2.5f), NextFloat(6f, 12f)),
                Age = 0,
                Life = NextFloat(0.5f, 1.2f),
                Size = NextFloat(0.4f, 0.9f),
            });
        }

        for (var i = Exhaust.Count - 1; i >= 0; i--)
        {
            var p = Exhaust[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                Exhaust.RemoveAt(i);
                continue;
            }
            p.Velocity.y -= 4f * dt;
            p.Velocity.x *= 1f - 0.5f * dt;
            p.Position += p.Velocity * dt;
            p.Size += 1.2f * dt;
            Exhaust[i] = p;
        }
    }

    void DrawExhaust(CeremonyCanvas canvas)
    {
        var draw = ImGui.GetWindowDrawList();
        foreach (var p in Exhaust)
        {
            var life = Mathf.Clamp01(p.Age / p.Life);
            var color = RampColor(life,
                (0.0, ColorWithAlpha(255, 240, 180, 255)),
                (0.2, ColorWithAlpha(255, 140, 40, 230)),
                (0.5, ColorWithAlpha(120, 110, 110, 150)),
                (1.0, ColorWithAlpha(90, 90, 95, 0)));
            draw.AddCircleFilled(canvas.Point(p.Position), p.Size * canvas.Unit, color, 16);
        }
    }

    float CreditsSway() => Mathf.Sin((float)PhaseTime * 0.7f) * 0.4f;

    // Sprite center of Aimee in the credits: sitting on the nose (the credits rocket has no launch mount,
    // so the top part is RocketPartCount - 1 segments above the base), later drifting away.
    Vector2 CreditsAimeeCenter(float rocketX, float t)
    {
        var attach = new Vector2(rocketX, CreditsRocketBaseY - (RocketPartCount - 1) * RocketPartHeight + 0.2f - CrewSize * 0.5f);
        if (t < AimeeLetGo)
            return attach + new Vector2(Mathf.Sin(t * 9f) * 0.08f, 0);
        var drift = (float)(t - AimeeLetGo);
        return attach + new Vector2(-0.12f * drift, 0.012f * drift * drift + 0.08f * drift);
    }

    void DrawCreditsAimee(CeremonyCanvas canvas, float rocketX, float t)
    {
        if (AimeeIndex < 0)
            return;
        var center = CreditsAimeeCenter(rocketX, t);
        if (center.y - CrewSize > canvas.Height || center.x + CrewSize < 0f)
            return;
        var rotation = t < AimeeLetGo ? 0f : 0.15f * (float)(t - AimeeLetGo);
        DrawRotatedCeremonySprite(Crew[AimeeIndex], canvas.Point(center), CrewSize * canvas.Unit, rotation);
    }

    // ---- party small talk ----

    string[] LinesFor(int speaker)
    {
        if (speaker == SpeakerIcarus) return EggText.PartyLines[EggText.IcarusName];
        if (speaker == SpeakerLuna) return EggText.PartyLines[EggText.LunaName];
        var name = CrewNames[speaker];
        if (name == EggText.DjName) return EggText.DjLines;
        return EggText.PartyLines.TryGetValue(name, out var lines) ? lines : [];
    }

    void ShowBubble(int speaker, string text)
    {
        BubbleSpeaker = speaker;
        BubbleText = text;
        BubbleStart = CTime;
    }

    void UpdateSpeechBubbles()
    {
        if (CTime < NextBubbleTime)
            return;
        var speakers = Enumerable.Range(0, Crew.Count).Concat([SpeakerIcarus, SpeakerLuna]).ToList();
        var candidates = speakers.SelectMany(s => Enumerable.Range(0, LinesFor(s).Length).Select(i => (s, i))).ToList();
        if (candidates.Count == 0)
            return;
        var unused = candidates.Where(c => !UsedBubbles.Contains(c)).ToList();
        if (unused.Count == 0)
        {
            UsedBubbles.Clear();
            unused = candidates;
        }
        var pick = unused[Random.Next(unused.Count)];
        UsedBubbles.Add(pick);
        ShowBubble(pick.s, LinesFor(pick.s)[pick.i]);
        NextBubbleTime = CTime + BubbleDuration + BubbleMinGap + Random.NextDouble() * (BubbleMaxGap - BubbleMinGap);
    }

    // Point above the speaker's head, in canvas units.
    Vector2 BubbleAnchor(int speaker)
    {
        if (speaker == SpeakerIcarus) return new Vector2(GuestX, GuestY - 3.2f - 1.9f);
        if (speaker == SpeakerLuna) return LunaHome + new Vector2(0, -1.7f);
        if (speaker == SpeakerChip || speaker == SpeakerNo || speaker == SpeakerSurvivor) return FinaleBubbleAnchor(speaker);
        // the DJ's head touches the top edge, so his bubble hangs off the right side of it
        if (CrewNames[speaker] == EggText.DjName) return new Vector2(23.5f, BoothTop - 2.3f);
        Vector2 pos;
        switch (Phase)
        {
            case CeremonyPhase.Boarding:
                GetBoardingPose(speaker, PhaseTime, out pos, out _, out _, out _);
                break;
            case CeremonyPhase.Launch:
                GetAimeeLaunchPose(PhaseTime, out pos, out _, out var jump);
                pos.y -= jump;
                break;
            case CeremonyPhase.Credits:
                var top = CreditsAimeeCenter(CreditsRocketX + CreditsSway(), (float)PhaseTime) - new Vector2(0, CrewSize * 0.5f);
                return top * CreditsRocketScale + new Vector2(0, -0.3f);
            default:
                DancePose(speaker, CTime, out pos, out _);
                break;
        }
        return new Vector2(pos.x, pos.y - CrewSize - (Phase == CeremonyPhase.Party ? 1.4f : 0.3f));
    }

    void DrawSpeechBubble(CeremonyCanvas canvas)
    {
        var age = CTime - BubbleStart;
        if (age < 0 || age > BubbleDuration || string.IsNullOrEmpty(BubbleText))
            return;
        // after lift-off only Aimee is still outside (plus the finale voices from inside the rocket)
        if (Phase >= CeremonyPhase.Launch && BubbleSpeaker != AimeeIndex && BubbleSpeaker > SpeakerLuna)
            return;
        var fade = (float)Math.Min(1.0, Math.Min(age / 0.25, (BubbleDuration - age) / 0.4));
        var alpha = (byte)(255 * fade);

        var draw = ImGui.GetWindowDrawList();
        var font = ImGui.GetIO().Fonts.Fonts[0];
        var fontSize = 0.8f * canvas.Unit;
        var textSize = ImGui.CalcTextSize(BubbleText) * (fontSize / ImGui.GetFontSize());
        var pad = new Vector2(0.35f, 0.2f) * canvas.Unit;

        var anchor = canvas.Point(BubbleAnchor(BubbleSpeaker));
        var size = textSize + 2 * pad;
        var min = new Vector2(anchor.x - size.x * 0.5f, anchor.y - size.y - 0.5f * canvas.Unit);
        var left = canvas.Point(0.3f, 0).x;
        var right = canvas.Point(39.7f, 0).x;
        min.x = Mathf.Clamp(min.x, left, right - size.x);
        var max = min + size;

        draw.AddRectFilled(min, max, ColorWithAlpha(245, 245, 250, alpha), 0.3f * canvas.Unit);
        draw.AddTriangleFilled(new Vector2(anchor.x - 0.3f * canvas.Unit, max.y - 1), new Vector2(anchor.x + 0.3f * canvas.Unit, max.y - 1), anchor, ColorWithAlpha(245, 245, 250, alpha));
        draw.AddRect(min, max, ColorWithAlpha(40, 40, 60, alpha), 0.3f * canvas.Unit, 0, 0.06f * canvas.Unit);
        draw.AddText(font, fontSize, min + pad, ColorWithAlpha(25, 25, 40, alpha), BubbleText);
    }

    // x, start y, fall speed, brightness; randomized once per ceremony so no grid pattern shows.
    readonly List<Vector4> CreditsStars = [];

    void GenerateCreditsStars()
    {
        CreditsStars.Clear();
        for (var i = 0; i < 160; i++)
            CreditsStars.Add(new Vector4(NextFloat(0f, 40f), NextFloat(0f, 30f), NextFloat(4f, 18f), NextFloat(100f, 240f)));
    }

    // The rocket climbs through a star stream while the acknowledgements scroll down beside it.
    void DrawCredits(CeremonyCanvas canvas)
    {
        if (FinaleTime >= FinaleFallAt)
        {
            DrawFinaleLanding(canvas);
            DrawSpeechBubble(canvas);
            return;
        }

        var draw = ImGui.GetWindowDrawList();
        var font = ImGui.GetIO().Fonts.Fonts[0];
        var t = (float)PhaseTime;
        var height = canvas.Height;
        draw.AddRectFilled(canvas.Point(0, 0), canvas.Point(40, height), ColorWithAlpha(4, 5, 12, 255));

        foreach (var star in CreditsStars)
        {
            var y = (star.y + t * star.z) % height;
            var brightness = (byte)star.w;
            var length = star.z * 0.08f;
            draw.AddLine(canvas.Point(star.x, y - length), canvas.Point(star.x, y), ColorWithAlpha(brightness, brightness, (byte)Math.Min(255, brightness + 20), 255), 0.08f * canvas.Unit);
        }

        var rc = new CeremonyCanvas(canvas.Origin, canvas.Unit * CreditsRocketScale, height / CreditsRocketScale);
        if (FinaleExploded)
            DrawFinaleDebris(rc, canvas);
        else
        {
            DrawExhaust(rc);
            var sway = CreditsSway();
            for (var i = 1; i < RocketParts.Count; i++)
            {
                var bottom = CreditsRocketBaseY - (i - 1) * RocketPartHeight;
                DrawRocketPart(i, rc.Point(CreditsRocketX + sway, bottom - RocketPartWidth * 0.5f), RocketPartWidth * rc.Unit, sway * 0.08f);
            }
            // part 1 sits at the launch base position of part 0, so the base line is one part higher
            DrawPortholes(rc, CreditsRocketX + sway, CreditsRocketBaseY + RocketPartHeight, 0f);
            DrawCreditsAimee(rc, CreditsRocketX + sway, t);
        }
        DrawSpeechBubble(canvas);
        DrawFinaleNote(canvas);

        var header = ColorWithAlpha(255, 225, 105, 255);
        var body = header;
        var lines = new List<(string Text, uint Color, float Size)>
        {
            (EggText.CreditsTitle, header, 52f),
            ("", body, 36f),
            (string.Format(EggText.CreditsScore, EggStore.State.Highscore), body, 36f),
            (string.Format(EggText.CreditsFurnaces, EggStore.State.FurnacesExploded), body, 36f),
            ("", body, 36f),
            (EggText.CreditsHeader, header, 44f),
        };
        foreach (var line in EggText.CreditsLines)
            lines.Add((line, body, 36f));

        // scrolls through once until the last line has left the screen, no wrap around
        var scroll = Math.Min(t * CreditsScrollSpeed, lines.Count * CreditsLineStep + height + 2f);
        for (var i = 0; i < lines.Count; i++)
        {
            var y = height + 1f - scroll + i * CreditsLineStep;
            if (y < -1f || y > height)
                continue;
            var scale = lines[i].Size / ImGui.GetFontSize();
            var textSize = ImGui.CalcTextSize(lines[i].Text) * scale;
            var pos = canvas.Point(CreditsTextX, y) - new Vector2(textSize.x * 0.5f, 0);
            draw.AddText(font, lines[i].Size, pos + Vector2.one, ColorWithAlpha(0, 0, 0, 220), lines[i].Text);
            draw.AddText(font, lines[i].Size, pos, lines[i].Color, lines[i].Text);
        }
    }

    void DrawCeremonyCharacter(CeremonyCanvas canvas, Texture2D texture, float x, float y, float size, int facing, float jump, uint tint = 0xFFFFFFFF)
    {
        var pixelSize = size * canvas.Unit;
        var pos = canvas.Point(x - size * 0.5f, y - size - jump);
        DrawSprite(texture, pos, pixelSize, facing, tint);
    }

    void DrawRotatedCeremonySprite(Texture2D texture, Vector2 center, float size, float rotation, uint tint = 0xFFFFFFFF)
    {
        var half = size * 0.5f;
        var cosine = Mathf.Cos(rotation);
        var sine = Mathf.Sin(rotation);

        Vector2 Rotate(float x, float y)
        {
            return center + new Vector2(x * cosine - y * sine, x * sine + y * cosine);
        }

        var draw = ImGui.GetWindowDrawList();
        draw.AddImageQuad(
            ImGuiManager.ImGuiPointerFor(texture),
            Rotate(-half, -half),
            Rotate(half, -half),
            Rotate(half, half),
            Rotate(-half, half),
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
            tint
        );
    }

    void DrawTextAt(CeremonyCanvas canvas, string text, float x, float y, uint color, float fontSize)
    {
        var draw = ImGui.GetWindowDrawList();
        var font = ImGui.GetIO().Fonts.Fonts[0];
        var pos = canvas.Point(x, y);
        draw.AddText(font, fontSize, pos + Vector2.one, ColorWithAlpha(0, 0, 0, 220), text);
        draw.AddText(font, fontSize, pos, color, text);
    }

    void DrawCenteredText(CeremonyCanvas canvas, string text, float y, uint color, float fontSize)
    {
        var draw = ImGui.GetWindowDrawList();
        var font = ImGui.GetIO().Fonts.Fonts[0];
        var scale = fontSize / ImGui.GetFontSize();
        var textSize = ImGui.CalcTextSize(text) * scale;
        var pos = canvas.Point(20, y) - new Vector2(textSize.x * 0.5f, 0);
        draw.AddText(font, fontSize, pos + Vector2.one, ColorWithAlpha(0, 0, 0, 220), text);
        draw.AddText(font, fontSize, pos, color, text);
    }

    void DrawConfetti(CeremonyCanvas canvas, float time, int count)
    {
        var draw = ImGui.GetWindowDrawList();
        var colors = new[]
        {
            ColorWithAlpha(255, 75, 105, 255),
            ColorWithAlpha(70, 200, 255, 255),
            ColorWithAlpha(255, 220, 70, 255),
            ColorWithAlpha(120, 255, 145, 255),
            ColorWithAlpha(210, 100, 255, 255),
        };

        for (var i = 0; i < count; i++)
        {
            var speed = 4.0f + i % 6;
            var x = (i * 37 % 400) / 10.0f + Mathf.Sin(time * 2.0f + i) * 0.5f;
            var y = (i * 17 % 90) / 10.0f + time * speed;
            y %= 28.0f;
            draw.AddRectFilled(canvas.Point(x, y), canvas.Point(x + 0.22f, y + 0.42f), colors[i % colors.Length]);
        }
    }
}
