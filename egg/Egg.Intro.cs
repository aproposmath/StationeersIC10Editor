namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;

using ImGuiNET;

using UnityEngine;

using Sounds = Assets.Scripts.Util.Defines.Sounds;

// Intro: a rocket approaches a planet and crashes, then the stationeer stands alone in the wreckage.
public partial class Egg
{
    enum IntroStage { Flight, Impact, Alone }

    // Seconds from intro start to the crash; tune this to match the audio.
    const double ImpactTime = 5.1;
    // The rocket meets the planet at 83% of the flight (from FlightGeometry: 30u^2 + 7u = 26.5).
    const double FlightDuration = ImpactTime / 0.83;
    const double ImpactDuration = 1.8;
    const double IntroLineInterval = 2.6;
    const double IntroOutro = 2.5;

    IntroStage Intro;
    double IntroStageStart;
    double LastIntroTime;
    readonly List<ExhaustParticle> IntroParticles = [];
    EggAudio.Voice IntroEngine;

    double IntroTime => ImGui.GetTime() - IntroStageStart;
    // Vertical center of the flight scene in canvas units (set from the canvas each frame).
    float IntroMidY = 15f;

    void StartIntro()
    {
        GameMode = IntroMode;
        Intro = IntroStage.Flight;
        IntroStageStart = LastIntroTime = ImGui.GetTime();
        IntroParticles.Clear();
        IntroEngine = EggAudio.Play(EggAudio.FindByName("ShuttleSmall", "MainEngine", "LP") ?? EggAudio.FindByName("ShuttleSmall", "Engine"), 0.5f);
    }

    void EndIntro()
    {
        IntroEngine?.Stop();
        EggAudio.StopAll();
        GameMode = CharacterSelectionMode;
    }

    void SetIntroStage(IntroStage stage)
    {
        Intro = stage;
        IntroStageStart = ImGui.GetTime();
        switch (stage)
        {
            case IntroStage.Impact:
                IntroEngine?.Stop();
                EggAudio.Play(Sounds.AtmosphereFireStart);
                EggAudio.Play(Sounds.PipeFailHash);
                EggAudio.Play("PayloadDeploy");
                EggAudio.Play("ShuttleSmallSonicBoom", 0.4f, 1.5f);
                FlightGeometry(FlightDuration, IntroMidY, out var rocket, out _, out _);
                IntroParticles.Clear();
                for (var i = 0; i < 90; i++)
                {
                    var angle = NextFloat(0f, Mathf.PI * 2f);
                    var speed = NextFloat(2f, 14f);
                    IntroParticles.Add(new ExhaustParticle
                    {
                        Position = rocket + new Vector2(2.5f, 0),
                        Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed,
                        Life = NextFloat(0.6f, ImpactDuration > 1 ? (float)ImpactDuration : 1f),
                        Size = NextFloat(0.3f, 1.1f),
                    });
                }
                break;
            case IntroStage.Alone:
                EggAudio.SetPlaying("WindLocal", true, 0.6f);
                break;
        }
    }

    // Camera follows the rocket, so the rocket stays put and the planet closes in and grows.
    static void FlightGeometry(double t, float midY, out Vector2 rocketCenter, out Vector2 planetCenter, out float planetRadius)
    {
        var u = Mathf.Clamp01((float)(t / FlightDuration));
        rocketCenter = new Vector2(13f + Mathf.Sin((float)t * 1.3f) * 0.3f, midY + Mathf.Sin((float)t * 0.9f) * 0.4f);
        planetCenter = new Vector2(47f - 7f * u, midY);
        planetRadius = 3f + 30f * u * u;
    }

    public void DrawIntro()
    {
        var now = ImGui.GetTime();
        var dt = (float)Math.Min(0.1, now - LastIntroTime);
        LastIntroTime = now;

        ImGui.BeginChild("intro", ImGui.GetContentRegionAvail(), false);
        // 40 units wide, the logical height follows the screen aspect so the scene fills everything.
        var available = ImGui.GetContentRegionAvail();
        var unit = Mathf.Max(1.0f, available.x / 40.0f);
        var height = available.y / unit;
        var origin = ImGui.GetCursorScreenPos();
        if (Intro == IntroStage.Impact)
        {
            var fade = 1f - Mathf.Clamp01((float)(IntroTime / ImpactDuration));
            origin += new Vector2(Mathf.Sin((float)now * 53f), Mathf.Cos((float)now * 41f)) * 0.5f * unit * fade;
        }
        var canvas = new CeremonyCanvas(origin, unit, height);
        var midY = height * 0.5f;
        IntroMidY = midY;

        switch (Intro)
        {
            case IntroStage.Flight:
                UpdateIntroParticles(dt, 0f);
                FlightGeometry(IntroTime, midY, out var rocket, out var planet, out var radius);
                SpawnIntroExhaust(rocket, dt);
                DrawSpace(canvas, planet, radius);
                DrawIntroParticles(canvas);
                DrawFlyingRocket(canvas, rocket);
                if (rocket.x + 4.5f >= planet.x - radius)
                    SetIntroStage(IntroStage.Impact);
                break;

            case IntroStage.Impact:
                UpdateIntroParticles(dt, 0f);
                FlightGeometry(FlightDuration, midY, out _, out planet, out radius);
                DrawSpace(canvas, planet, radius);
                var flash = 1f - Mathf.Clamp01((float)(IntroTime / 0.5));
                if (flash > 0f)
                    ImGui.GetWindowDrawList().AddRectFilled(canvas.Point(0, 0), canvas.Point(40, canvas.Height), ColorWithAlpha(255, 240, 220, (byte)(255 * flash)));
                DrawIntroParticles(canvas);
                if (IntroTime >= ImpactDuration)
                    SetIntroStage(IntroStage.Alone);
                break;

            case IntroStage.Alone:
                DrawAlone(canvas);
                if (IntroTime >= IntroLineInterval * EggText.IntroLines.Length + IntroOutro && AssetsReady)
                    EndIntro();
                break;
        }

        var introHint = AssetsReady ? EggText.IntroSkip : string.Format(EggText.IntroLoading, EggAssets.Status);
        ImGui.GetWindowDrawList().AddText(canvas.Point(0.5f, canvas.Height - 1.0f), ColorWithAlpha(180, 190, 210, 220), introHint);
        ImGui.EndChild();
    }

    void DrawSpace(CeremonyCanvas canvas, Vector2 planet, float radius)
    {
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(canvas.Point(0, 0), canvas.Point(40, canvas.Height), ColorWithAlpha(4, 5, 12, 255));
        for (var i = 0; i < 120; i++)
        {
            var x = (i * 73 % 400) / 10f;
            var y = (i * 37 % 300) / 300f * canvas.Height;
            var brightness = (byte)(120 + (i * 29 % 120));
            draw.AddCircleFilled(canvas.Point(x, y), (0.05f + (i % 3) * 0.03f) * canvas.Unit, ColorWithAlpha(brightness, brightness, (byte)Math.Min(255, brightness + 20), 255), 6);
        }

        var center = canvas.Point(planet);
        var r = radius * canvas.Unit;
        draw.AddCircleFilled(center, r, ColorWithAlpha(150, 70, 40, 255), 64);
        draw.AddCircleFilled(center + new Vector2(-0.25f, -0.2f) * r, r * 0.75f, ColorWithAlpha(175, 90, 50, 255), 64);
        draw.AddCircleFilled(center + new Vector2(0.3f, 0.35f) * r, r * 0.22f, ColorWithAlpha(120, 55, 35, 255), 32);
        draw.AddCircleFilled(center + new Vector2(-0.4f, 0.3f) * r, r * 0.14f, ColorWithAlpha(125, 60, 38, 255), 32);
        draw.AddCircle(center, r * 1.03f, ColorWithAlpha(200, 150, 120, 60), 64, 0.08f * r);
    }

    // Parts 1..4 (engine to crew module) laid out horizontally, nose pointing at the planet.
    void DrawFlyingRocket(CeremonyCanvas canvas, Vector2 center)
    {
        const float partSize = 2.4f;
        const float spacing = 1.7f;
        for (var i = 1; i < RocketParts.Count; i++)
        {
            var offset = ((i - 1) - 1.5f) * spacing;
            DrawRocketPart(i, canvas.Point(center.x + offset, center.y), partSize * canvas.Unit, Mathf.PI * 0.5f);
        }
    }

    void SpawnIntroExhaust(Vector2 rocket, float dt)
    {
        var count = Mathf.CeilToInt(dt * 120f);
        for (var i = 0; i < count; i++)
            IntroParticles.Add(new ExhaustParticle
            {
                Position = rocket + new Vector2(-3.2f, NextFloat(-0.3f, 0.3f)),
                Velocity = new Vector2(NextFloat(-9f, -5f), NextFloat(-1f, 1f)),
                Life = NextFloat(0.3f, 0.7f),
                Size = NextFloat(0.25f, 0.6f),
            });
    }

    void UpdateIntroParticles(float dt, float gravity)
    {
        for (var i = IntroParticles.Count - 1; i >= 0; i--)
        {
            var p = IntroParticles[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                IntroParticles.RemoveAt(i);
                continue;
            }
            p.Velocity.y += gravity * dt;
            p.Position += p.Velocity * dt;
            p.Size += 0.8f * dt;
            IntroParticles[i] = p;
        }
    }

    void DrawIntroParticles(CeremonyCanvas canvas)
    {
        var draw = ImGui.GetWindowDrawList();
        foreach (var p in IntroParticles)
        {
            var life = Mathf.Clamp01(p.Age / p.Life);
            var color = RampColor(life,
                (0.0, ColorWithAlpha(255, 240, 180, 255)),
                (0.2, ColorWithAlpha(255, 140, 40, 230)),
                (0.5, ColorWithAlpha(120, 110, 110, 150)),
                (1.0, ColorWithAlpha(90, 90, 95, 0)));
            draw.AddCircleFilled(canvas.Point(p.Position), p.Size * canvas.Unit, color, 12);
        }
    }

    void DrawAlone(CeremonyCanvas canvas)
    {
        var draw = ImGui.GetWindowDrawList();
        var t = (float)IntroTime;

        var ground = canvas.Height * 2f / 3f;
        draw.AddRectFilled(canvas.Point(0, 0), canvas.Point(40, canvas.Height), ColorWithAlpha(10, 8, 20, 255));
        for (var i = 0; i < 60; i++)
        {
            var x = (i * 73 % 400) / 10f;
            var y = (i * 37 % 180) / 180f * (ground - 2f);
            draw.AddCircleFilled(canvas.Point(x, y), 0.06f * canvas.Unit, ColorWithAlpha(200, 200, 220, 200), 6);
        }
        draw.AddRectFilled(canvas.Point(0, ground), canvas.Point(40, canvas.Height), ColorWithAlpha(120, 55, 35, 255));
        draw.AddRectFilled(canvas.Point(0, ground), canvas.Point(40, ground + 0.6f), ColorWithAlpha(150, 75, 45, 255));

        // smouldering wreckage
        var wrecks = new[] { (9.0f, -0.4f, 1.2f, 1), (27.0f, -0.1f, -0.5f, 2), (33.0f, -0.6f, 2.4f, 4), (15.0f, -0.2f, 3.0f, 3) };
        foreach (var (x, dy, rotation, part) in wrecks)
            if (part < RocketParts.Count)
                DrawRocketPart(part, canvas.Point(x, ground + dy), 2.6f * canvas.Unit, rotation);
        var smoke = Mathf.Max(0f, Mathf.Sin(t * 0.8f));
        draw.AddCircleFilled(canvas.Point(27.0f + Mathf.Sin(t) * 0.3f, ground - 2.5f - t % 3f), (0.6f + 0.2f * smoke) * canvas.Unit, ColorWithAlpha(90, 90, 95, (byte)(90 * (1f - t % 3f / 3f))), 12);

        // the stationeer, shivering
        var shiver = Mathf.Sin(t * 38f) * 0.07f;
        DrawCeremonyCharacter(canvas, Helmet, 20.0f + shiver, ground, 3.6f, 0, 0f);

        var shown = Math.Min(EggText.IntroLines.Length, (int)(t / IntroLineInterval) + 1);
        for (var i = 0; i < shown; i++)
        {
            var age = t - i * (float)IntroLineInterval;
            var alpha = (byte)(255 * Mathf.Clamp01(age / 0.6f));
            var color = i == shown - 1 ? ColorWithAlpha(255, 240, 200, alpha) : ColorWithAlpha(160, 160, 175, alpha);
            DrawCenteredText(canvas, EggText.IntroLines[i], canvas.Height * 0.17f + i * 1.3f, color, 0.85f * canvas.Unit);
        }
    }
}
