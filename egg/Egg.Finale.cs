namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;

using ImGuiNET;

using UnityEngine;

using Sounds = Assets.Scripts.Util.Defines.Sounds;

// Credits finale: once the acknowledgements have scrolled through, somebody finds an IC10 chip.
public partial class Egg
{
    const int SpeakerChip = -3;
    const int SpeakerNo = -4;
    const int SpeakerSurvivor = -5;

    const float CreditsLineStep = 1.6f;
    // Seconds after FinaleStart
    const double FinaleChipAt = 0.0;
    const double FinaleTurnOnAt = 6.0;
    const double FinaleNoteAt = 8.5;
    const double FinaleNoAt = 10.5;
    const double FinaleBoomAt = 11.5;
    const double FinaleFallAt = 15.5;
    const double FinaleLandAt = 17.5;
    const double FinaleLineAt = 18.0;
    const double FinaleEndAt = 26.0;
    const float FinaleFlashDuration = 0.4f;
    const double FinaleBurnDuration = 3.0;
    const float SurvivorX = 20.0f;

    struct Debris
    {
        public Texture2D Sprite;
        // rocket part index, or -1 for a plain sprite
        public int Part;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Rotation;
        public float Spin;
        public float Size;
    }

    readonly List<Debris> FinaleDebris = [];
    bool FinaleExploded;
    int FinaleStep;
    double FinaleBoomTime;

    // One minute after Aimee lost her grip.
    const double FinaleStart = AimeeLetGo + 60.0;
    double FinaleTime => Phase == CeremonyPhase.Credits ? PhaseTime - FinaleStart : double.NegativeInfinity;

    void ResetFinale()
    {
        FinaleDebris.Clear();
        FinaleExploded = false;
        FinaleStep = 0;
    }

    void UpdateFinale()
    {
        var t = FinaleTime;
        (double At, Action Action)[] script =
        [
            (FinaleChipAt, () => ShowBubble(SpeakerChip, EggText.FinaleChip)),
            (FinaleTurnOnAt, () => ShowBubble(SpeakerChip, EggText.FinaleTurnOn)),
            (FinaleNoteAt, () => EggAudio.Play(UIAudioManager.UiEquipBeltHash)),
            (FinaleNoAt, () => ShowBubble(SpeakerNo, EggText.FinaleNo)),
            (FinaleBoomAt, ExplodeRocket),
            (FinaleFallAt, () => { BubbleStart = -100; EggAudio.SetPlaying("WindLocal", true, 0.6f); }),
            (FinaleLandAt, () => EggAudio.Play("PayloadDeployTail")),
            (FinaleLineAt, () => ShowBubble(SpeakerSurvivor, EggText.FinaleNotAgain)),
            (FinaleEndAt, SkipCeremonyPhase),
        ];
        while (FinaleStep < script.Length && t >= script[FinaleStep].At)
            script[FinaleStep++].Action();

        if (!FinaleExploded)
            return;
        var dt = (float)CeremonyDt;
        UpdateIntroParticles(dt, 0f);
        // the wreck keeps burning for a while
        if (CTime - FinaleBoomTime < FinaleBurnDuration)
            foreach (var d in FinaleDebris)
                if (d.Part >= 0)
                    SpawnFire(d.Position, Mathf.CeilToInt(dt * 25f), 0.5f, 2.5f, 0.6f);
        for (var i = 0; i < FinaleDebris.Count; i++)
        {
            var d = FinaleDebris[i];
            d.Position += d.Velocity * dt;
            d.Rotation += d.Spin * dt;
            FinaleDebris[i] = d;
        }
    }

    // Rocket parts and everybody inside fly off in all directions (rocket canvas units).
    void ExplodeRocket()
    {
        FinaleExploded = true;
        FinaleBoomTime = CTime;
        Exhaust.Clear();
        EngineVoice?.Stop();
        EggAudio.Play(Sounds.AtmosphereFireStart);
        EggAudio.Play(Sounds.PipeFailHash);
        EggAudio.Play("PayloadDeploy");
        EggAudio.Play("ShuttleSmallSonicBoom", 0.4f, 1.5f);

        var sway = CreditsSway();
        var rocketX = CreditsRocketX + sway;
        var center = new Vector2(rocketX, CreditsRocketBaseY - 1.5f * RocketPartHeight);
        IntroParticles.Clear();
        SpawnFire(center, 140, 2f, 14f, 1.2f);
        for (var i = 1; i < RocketParts.Count; i++)
        {
            var bottom = CreditsRocketBaseY - (i - 1) * RocketPartHeight;
            var position = new Vector2(rocketX, bottom - RocketPartWidth * 0.5f);
            var dir = (position - center).normalized;
            if (dir == Vector2.zero)
                dir = Vector2.up;
            var angle = Mathf.Atan2(dir.y, dir.x) + NextFloat(-0.7f, 0.7f);
            FinaleDebris.Add(new Debris
            {
                Part = i,
                Position = position,
                Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * NextFloat(3f, 7f),
                Rotation = sway * 0.08f,
                Spin = NextFloat(-3f, 3f),
                Size = RocketPartWidth,
            });
        }

        var passengers = new List<Texture2D>();
        for (var i = 0; i < Crew.Count; i++)
            if (i != AimeeIndex || PhaseTime < AimeeLetGo)
                passengers.Add(Crew[i]);
        passengers.Add(Luna);
        for (var i = 0; i < passengers.Count; i++)
        {
            var angle = i * 2f * Mathf.PI / passengers.Count + NextFloat(-0.3f, 0.3f);
            FinaleDebris.Add(new Debris
            {
                Part = -1,
                Sprite = passengers[i],
                Position = center + new Vector2(NextFloat(-1f, 1f), NextFloat(-2f, 2f)),
                Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * NextFloat(6f, 13f),
                Spin = NextFloat(-7f, 7f),
                Size = passengers[i] == Luna ? CrewSize * 0.6f : CrewSize,
            });
        }
    }

    // Fireball particles (same look as the intro crash) in rocket canvas units.
    void SpawnFire(Vector2 origin, int count, float minSpeed, float maxSpeed, float maxSize)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = NextFloat(0f, Mathf.PI * 2f);
            IntroParticles.Add(new ExhaustParticle
            {
                Position = origin + new Vector2(NextFloat(-0.5f, 0.5f), NextFloat(-0.5f, 0.5f)),
                Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * NextFloat(minSpeed, maxSpeed),
                Life = NextFloat(0.5f, 1.8f),
                Size = NextFloat(0.3f, maxSize),
            });
        }
    }

    void DrawFinaleDebris(CeremonyCanvas rc, CeremonyCanvas canvas)
    {
        DrawIntroParticles(rc);
        foreach (var d in FinaleDebris)
        {
            if (d.Part >= 0)
                DrawRocketPart(d.Part, rc.Point(d.Position), d.Size * rc.Unit, d.Rotation);
            else
                DrawRotatedCeremonySprite(d.Sprite, rc.Point(d.Position), d.Size * rc.Unit, d.Rotation);
        }

        var flash = 1f - Mathf.Clamp01((float)(CTime - FinaleBoomTime) / FinaleFlashDuration);
        if (flash > 0f)
            ImGui.GetWindowDrawList().AddRectFilled(canvas.Point(0, 0), canvas.Point(40, canvas.Height), ColorWithAlpha(255, 240, 220, (byte)(255 * flash)));
    }

    // System note while the chip runs, until the cut to the planet.
    void DrawFinaleNote(CeremonyCanvas canvas)
    {
        var t = FinaleTime;
        if (t < FinaleNoteAt || t >= FinaleFallAt)
            return;
        var alpha = (byte)(255 * Mathf.Clamp01((float)((t - FinaleNoteAt) / 0.5)));
        DrawCenteredText(canvas, EggText.FinaleNote, canvas.Height * 0.08f, ColorWithAlpha(255, 200, 60, alpha), 0.9f * canvas.Unit);
    }

    // Back on the planet: the stationeer drops out of the sky next to the old wreckage.
    void DrawFinaleLanding(CeremonyCanvas canvas)
    {
        var t = (float)FinaleTime;
        var ground = DrawCrashSite(canvas, t);
        var fall = Mathf.Clamp01((float)((FinaleTime - FinaleFallAt) / (FinaleLandAt - FinaleFallAt)));
        var feet = Mathf.Lerp(-4f, ground, fall * fall);
        var shiver = fall >= 1f ? Mathf.Sin(t * 38f) * 0.07f : 0f;
        DrawCeremonyCharacter(canvas, Helmet, SurvivorX + shiver, feet, 3.6f, fall >= 1f ? 0 : (int)(t * 6) % 4, 0f);
    }

    Vector2 FinaleBubbleAnchor(int speaker)
    {
        if (speaker == SpeakerSurvivor)
            return new Vector2(SurvivorX, CreditsHeight * 2f / 3f - 3.6f - 0.3f);
        // from a porthole: left side of the lower fuselage, right side of the upper one
        var rocketX = CreditsRocketX + CreditsSway();
        var anchor = speaker == SpeakerChip
            ? new Vector2(rocketX - 0.8f, CreditsRocketBaseY - 1.5f * RocketPartHeight)
            : new Vector2(rocketX + 0.8f, CreditsRocketBaseY - 2.5f * RocketPartHeight);
        return anchor * CreditsRocketScale;
    }
}
