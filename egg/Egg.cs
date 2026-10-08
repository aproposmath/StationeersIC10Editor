namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Assets.Scripts;
using Assets.Scripts.Atmospherics;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Serialization;
using Assets.Scripts.Sound;
using Assets.Scripts.UI;
using Assets.Scripts.Util;

using BepInEx;

using Cysharp.Threading.Tasks;

using ImGuiNET;

using Reagents;

using UnityEngine;
using UnityEngine.Networking;

using Sounds = Assets.Scripts.Util.Defines.Sounds;

public partial class Egg
{
    const int CharacterSelectionMode = 0;
    const int PlayingMode = 1;
    const int CeremonyMode = 2;
    const int IntroMode = 3;

    // 39x27 cells: on FullHD this gives ~40 px cells with a ~260 px side panel.
    const int W = 39;
    const int H = 27;
    float gridSize = 0.0f;

    const float ExplosionDuration = 2.5f;
    const int ExplosionParticleCount = 220;
    const float ExplosionMinSpeed = 6.0f;
    const float ExplosionMaxSpeed = 35.0f;
    const float ExplosionGravity = 28.0f;
    const float ExplosionSpinMin = -9.0f;
    const float ExplosionSpinMax = 9.0f;

    const float RestartCooldown = 2.0f;
    static readonly uint GameOverFieldTint = ColorWithAlpha(70, 70, 70, 255);
    const float ShakeMaxDuration = 0.9f;
    const float ShakeInitialAmplitude = 10.0f;
    const float ShakeFrequency = 42.0f;
    const double TailJumpCooldown = 2.0;
    const double TailJumpAnimationDuration = 0.35;
    const double StuckJumpMinAnimationDuration = 0.45;
    const double StuckJumpMaxAnimationDuration = 1.8;
    const double StuckJumpAnimationSecondsPerCell = 0.045;

    static readonly Dictionary<int, Recipe> Recipes = [];

    List<Vector2Int> Snake;
    Vector2Int Velocity;
    Vector2Int LastVelocity => Snake.Count > 1 ? (Snake[0] - Snake[1]) : new Vector2Int(0, 0);
    int Rotation = 0;

    bool AutoVent = false;
    int AutoVentState = 0;
    int NumLives = 1;
    int MiningYield = 1;
    int GameMode = 0;
    double StuckProbability = 0.0;
    double StuckTimeLeft = 0.0;
    double GameTime = 0.0;
    double Speed => 3.5 + 0.2 * (Snake.Count - 1) + GameTime / 90.0;

    public static int DefaultOresPerBelt = 3;
    int OresPerBelt;
    bool HasBackpack;
    const int EmptyCell = -1;
    const int BackpackCell = -2;
    const int DrillCell = -3;
    const int AirCell = -4;
    const int AirCanisters = 3;
    const int MinDrills = 1;
    const int MaxDrills = 3;
    // pickups with doubled yield per heavy drill
    const int DrillPickups = 20;
    int DrillPickupsLeft;
    double Stress = 0.0;
    double MaxStress = 100.0;
    Atmosphere Atmosphere;
    List<Song> JumpThemes = [];
    int JumpThemesIndex = 0;
    // Tracks the count of each ore added to the furnace (excluding ices)
    readonly Dictionary<string, double> FurnaceOres = [];
    public bool IsOpen { get; private set; } = true;
    bool _IsValveOpen = false;
    bool IsValveOpen
    {
        get => _IsValveOpen;
        set
        {
            if (_IsValveOpen != value)
            {
                _IsValveOpen = value;
                EggAudio.SetPlaying("WindLocal", value && Pressure > 1000);
            }
        }
    }

    bool IsGameOver = false;
    // Round ended by suffocation: no explosion, the avatar turns into a skull.
    bool Suffocated = false;
    const double SkullMorphDuration = 1.5;
    string GameOverMessage = "";
    double GameOverTime = 0.0;
    Vector2 GameOverHeadCell = Vector2.zero;
    double LastTailJumpTime = double.NegativeInfinity;
    double TailJumpAnimationStartTime = double.NegativeInfinity;
    double CurrentJumpAnimationDuration = TailJumpAnimationDuration;
    bool JumpAnimationPausesMovement = false;
    Vector2 TailJumpFrom = Vector2.zero;
    Vector2 TailJumpTo = Vector2.zero;
    readonly List<ExplosionParticle> ExplosionParticles = [];
    readonly System.Random Random = new();

    double LastUpdateTime;
    double LastFrameTime;
    double LastTickTime;

    readonly StyledText InitialCode;

    List<Thing> Things;
    List<Texture2D> OreSprites;
    Texture2D Vent;
    List<Ingot> AlloyTypes = [];

    struct AlloyState
    {
        public double Quantity;
        public uint BackgroundColor;
    }


    Dictionary<int, AlloyState> Alloys = [];
    double SmeltingSpeed = 0.0f;
    double SmeltingEfficiency = 1.0f;

    int[,] Field;
    char[,] Code;
    uint[,] CodeColors;

    Texture2D Furnace;
    Rect FurnaceWindow;
    Texture2D Wreckage;
    readonly List<Texture2D> RocketParts = [];
    // Build state thumbnails per part (construction order, last = finished), shown while the part is built.
    readonly List<List<Texture2D>> RocketPartStates = [];

    // Alloy ingots needed for the rocket (across games), in temperature order.
    static readonly (string Prefab, int Target)[] AlloyTargets =
    [
        ("ItemWaspaloyIngot", 120),
        ("ItemInconelIngot", 80),
        ("ItemHastelloyIngot", 80),
        ("ItemAstroloyIngot", 80),
        ("ItemStelliteIngot", 40),
    ];
    readonly HashSet<int> ReachedTargets = [];
    static Dictionary<string, double> RocketProgress => EggStore.State.RocketProgress;

    const double OxygenDuration = 60.0;
    // refilled on every respawn
    double OxygenStartTime = 0.0;
    // Aimee is a robot and does not breathe.
    double Oxygen => selectedCharacter == 3 ? 1.0 : Math.Min(1.0, Math.Max(0.0, 1.0 - (GameTime - OxygenStartTime) / OxygenDuration));

    static string[] CharacterNames => EggText.CharacterNames;
    static readonly int[] CharacterRequiredScore = [0, 100, 200, 500, 1000];
    Texture2D[] CharacterTextures => [Helmet, PDX, DH, Aimee, Hunter];
    public static bool UnlockAll;
    static bool IsUnlocked(int index) => UnlockAll || index < EggStore.State.UnlockedCharacters;

    // Debug: hold "+" to run the ceremony 10x faster.
    static bool FastForward =>
        UnityEngine.Input.GetKey(KeyCode.Plus) || UnityEngine.Input.GetKey(KeyCode.KeypadPlus) ||
        UnityEngine.Input.GetKey(KeyCode.Equals) || UnityEngine.Input.GetKey(KeyCode.RightBracket);

    static void LoadRocketProgress() => EggStore.Load();

    static void SaveRocketProgress() => EggStore.Save();

    static void ResetRocketProgress()
    {
        RocketProgress.Clear();
        EggStore.Save();
    }

    static double GlobalAlloyQuantity(string prefabName) => RocketProgress.TryGetValue(prefabName, out var q) ? q : 0.0;

    const double QuantityEpsilon = 1e-6;
    // finished ingots; the tolerance keeps 0.9999999 from counting as none
    static int WholeIngots(double quantity) => (int)Math.Floor(quantity + QuantityEpsilon);
    static double SnapToWhole(double quantity)
    {
        var rounded = Math.Round(quantity);
        return Math.Abs(quantity - rounded) < QuantityEpsilon ? rounded : quantity;
    }
    static bool TargetReached(string prefabName, int target) => WholeIngots(GlobalAlloyQuantity(prefabName)) >= target;

    // Ingots still missing for the rocket when this game started, per alloy hash (0 if already enough).
    readonly Dictionary<int, int> AlloyNeeded = [];

    // Raw ore still needed for this game's alloy targets, counting what is smelted already.
    int OreNeeded(string oreName)
    {
        var sum = 0.0;
        foreach (var alloyType in AlloyTypes)
        {
            var hash = alloyType.PrefabHash;
            if (!AlloyNeeded.TryGetValue(hash, out var needed) || !Recipes.TryGetValue(hash, out var recipe))
                continue;
            var remaining = needed - Alloys[hash].Quantity;
            if (remaining <= 0)
                continue;
            foreach (var reagent in Reagent.AllReagents)
                if (OreName(reagent) == oreName)
                    sum += remaining * recipe.Get(reagent);
        }
        return (int)Math.Ceiling(sum - QuantityEpsilon);
    }

    double RocketRemaining(int alloyHash)
    {
        foreach (var alloyType in AlloyTypes)
            if (alloyType.PrefabHash == alloyHash)
                foreach (var t in AlloyTargets)
                    if (t.Prefab == alloyType.PrefabName)
                        return t.Target - GlobalAlloyQuantity(t.Prefab);
        return double.NegativeInfinity;
    }

    readonly List<(string Text, double Expiry)> Toasts = [];
    bool DebugMenuOpen;
    bool ShowSoundBrowser;
    bool ShowAudioControl;
    Texture2D Backpack;
    Texture2D Drill;
    Texture2D AirCanister;
    Texture2D Aimee;
    Texture2D DH;
    Texture2D PDX;
    Texture2D Head;
    Texture2D Marine;
    Texture2D Tail;
    Texture2D Helmet;
    Texture2D Hunter;
    Texture2D KsaBanner;
    Texture2D Jxn;
    Texture2D Dj;

    double Temperature => Atmosphere.GasMixture.Temperature.ToDouble();
    double Pressure => Atmosphere.PressureGassesAndLiquids.ToDouble();

    public Egg(StyledText code)
    {
        InitialCode = code;
        EnsureMusicEnabled();
        LoadRocketProgress();
        ApplySettings();
        InitializeStaticAssets();
        StartNewGame(code);
        StartIntro();
        SpaceMusicPending = true;
    }

    // Set during the intro; the intro only ends once everything is in place.
    bool TexturesLoaded;
    bool SongsLoaded;
    bool AssetsReady => TexturesLoaded && SongsLoaded && EggAssets.Done;

    // Textures (online, cached), the asset zip (music, songs) and decoded music, all in parallel.
    async UniTaskVoid SetupAssets()
    {
        try
        {
            var textures = LoadTextures();
            await EggAssets.Ensure();
            LoadSongs();
            EggMusic.Reset();
            await EggMusic.Preload();
            await textures;
            // the DJ sprite needs the avatar (online) and the headphones (asset zip)
            Dj = Jxn != null && EggAssets.Ready ? EggSprites.CachedDj(Jxn) : Helmet;
            L.Debug($"Egg assets ready (zip: {(EggAssets.Ready ? "ok" : "unavailable")})");
        }
        catch (Exception e)
        {
            L.Debug($"Egg asset setup failed: {e}");
        }
        finally
        {
            // whatever failed falls back to game assets; the intro must not wait forever
            PDX ??= Helmet;
            DH ??= Helmet;
            Hunter ??= Helmet;
            TexturesLoaded = true;
            SongsLoaded = true;
        }
    }

    void LoadSongs()
    {
        JumpThemes.Clear();
        for (var i = 1; i <= 7; i++)
        {
            var path = EggAssets.SongPath($"kitt{i}");
            try
            {
                if (File.Exists(path))
                    JumpThemes.Add(Song.LoadFromJSON(path, -24));
            }
            catch (Exception e)
            {
                L.Debug($"Failed to load {path}: {e.Message}");
            }
        }
        SongsLoaded = true;
    }

    async UniTask LoadTextures()
    {
        var requestPDX = TextureCache.LoadTextureFromURL("aHR0cHM6Ly93d3cuc3RhdGlvbmVlcnMtd2lraS5jb20vaW1hZ2VzLzIvMmQvU2VtbGVyUERYX0xvZ28ucG5n");
        var requestDH = TextureCache.LoadTextureFromURL("aHR0cHM6Ly9wYnMudHdpbWcuY29tL3Byb2ZpbGVfaW1hZ2VzLzEzMTYyODk0OTA3NjAwMzIyNTgvXzR5UjUtQXhfNDAweDQwMC5qcGc=", true);
        var requestHU = TextureCache.LoadTextureFromURL("aHR0cHM6Ly9tZWRpYS5haHdvby5jb20vMDE5YTgwODEtNmEwZi03YWRiLWFkOTYtNWUyM2Q0YTNhMDBl", false, 800, 500, 800, 800);
        var requestJXN = TextureCache.LoadTextureFromURL("aHR0cHM6Ly9hdmF0YXJzLmdpdGh1YnVzZXJjb250ZW50LmNvbS91LzgxODA3ODI0P3Y9NA==", circular: false);

        DH = await requestDH ?? Helmet;
        PDX = await requestPDX ?? Helmet;
        Hunter = await requestHU ?? Helmet;
        Jxn = await requestJXN;
    }

    bool KsaBannerRequested;

    async UniTaskVoid LoadKsaBanner()
    {
        KsaBannerRequested = true;
        KsaBanner = await TextureCache.LoadTextureFromURL("aHR0cHM6Ly9tZWRpYS5haHdvby5jb20vMDE5YTgwODEtNmEwZi03YWRiLWFkOTYtNWUyM2Q0YTNhMDBl", circular: false);
    }

    Synth Synth;
    EggAudio.Voice DiscoveredVoice;
    bool SpaceMusicPending;

    void InitializeStaticAssets()
    {
        DiscoveredVoice = EggAudio.Play("SFX_UI_PointOfInterestDiscovered");
        Synth = new Synth();

        Things = [];
        OreSprites = [];

        Helmet = Thumbnail("ItemSpaceHelmet");
        Aimee = Thumbnail("Robot");
        Tail = Thumbnail("ItemMiningBelt");
        Marine = Thumbnail("ApplianceBobbleHeadMarine");
        Head = Helmet;

        Vent = Thumbnail("StructurePassiveVentValve");
        Furnace = Thumbnail("StructureFurnace");
        // The only warm pixels in the thumbnail are the orange activate button; the view window
        // (grey with a yellow frame) sits left of it, offsets measured relative to the button size.
        var button = FindWarmRegion(Furnace, Rect.zero);
        FurnaceWindow = button.width > 0
            ? new Rect(button.center.x - 4.4f * button.width, button.center.y + 0.13f * button.height, 2.9f * button.width, 1.4f * button.height)
            : new Rect(0.40f, 0.32f, 0.22f, 0.20f);
        L.Debug($"Furnace window rect {FurnaceWindow}");

        Backpack = Thumbnail("ItemMiningBackPack");
        Drill = Thumbnail("ItemMiningDrillHeavy");
        AirCanister = Thumbnail("ItemGasCanisterOxygen");
        Skull = Thumbnail("HumanSkull");
        BreathInClips = HumanAudioEvent("BreathIn_Stressed");
        BreathOutClips = HumanAudioEvent("BreathOut_Stressed");
        GaspClips = HumanAudioEvent("BreathIn_LowPressure");
        L.Debug($"Breath clips: in={(BreathInClips != null)} out={(BreathOutClips != null)} gasp={(GaspClips != null)}");

        // bottom to top: launch mount, engine, two fuselage segments, crew module
        RocketParts.Clear();
        RocketPartStates.Clear();
        foreach (var name in new[] { "StructureLaunchMount", "StructurePressureFedGasEngine", "StructureFuselageTypeA1", "StructureFuselageTypeA1", "StructureCrewModuleFuselageSimple" })
        {
            var states = StructureThumbnails(name);
            if (states.Count == 0)
                states.Add(Helmet);
            RocketPartStates.Add(states);
            RocketParts.Add(states[states.Count - 1]);
        }

        // The furnace has no damaged build state with a thumbnail; the second last build state looks like a wreck.
        Wreckage = Furnace;
        var buildStates = Prefab.Find<Structure>("StructureFurnace")?.BuildStates;
        if (buildStates != null && buildStates.Count >= 2 && buildStates[buildStates.Count - 2].Thumbnail != null)
            Wreckage = buildStates[buildStates.Count - 2].Thumbnail.texture;

        foreach (var ore in Ore.AllOrePrefabs)
        {
            var name = ore.PrefabName;
            if (name.Contains("Uran") ||
                name.Contains("Pure") ||
                name.Contains("Space") ||
                name.Contains("Dirty") ||
                name.Contains("Reagent") ||
                name == "ItemCharcoal" ||
                name == "ItemIce" ||
                name == "ItemBiomass")
                continue;

            L.Debug($"Adding ore {name} to the game");
            Things.Add(ore);
            OreSprites.Add(ore.GetThumbnail().texture);
        }
        SetupAssets().Forget();

        AlloyTypes = [];
        AlloyTypes.AddRange(Ingot.AllSuperAlloyPrefabs);

        var tmax = new TemperatureKelvin(1000000.0f);
        var t1 = new TemperatureKelvin(1.0f);
        var pmax = new PressurekPa(1000000.0f);
        var p1 = new PressurekPa(1000.0f);

        Recipes[Animator.StringToHash("ItemAstroloyIngot")] = new Recipe(temperature: new Temperature(t1 * 1000, tmax), pressure: new Pressure(p1 * 30, pmax), copper: 1, cobalt: 1, iron: 1.5, hydrocarbon: 0.5);
        Recipes[Animator.StringToHash("ItemHastelloyIngot")] = new Recipe(temperature: new Temperature(t1 * 950, tmax), pressure: new Pressure(p1 * 25, pmax), nickel: 1, cobalt: 1, silver: 2);
        Recipes[Animator.StringToHash("ItemInconelIngot")] = new Recipe(temperature: new Temperature(t1 * 600, tmax), pressure: new Pressure(p1 * 23.5, pmax), nickel: 1, iron: 0.75, hydrocarbon: 0.25, gold: 2);
        Recipes[Animator.StringToHash("ItemWaspaloyIngot")] = new Recipe(temperature: new Temperature(t1 * 400, tmax), pressure: new Pressure(p1 * 50, pmax), nickel: 1, silver: 2, lead: 2);
        Recipes[Animator.StringToHash("ItemStelliteIngot")] = new Recipe(temperature: new Temperature(t1 * 1800, tmax), pressure: new Pressure(p1 * 10, pmax), cobalt: 1, silver: 2, silicon: 2);
    }

    void StartNewRound()
    {
        FurnaceOres.Clear();
        Atmosphere = new();
        Atmosphere.Volume = new VolumeLitres(1000);
        LastUpdateTime = ImGui.GetTime();
        LastFrameTime = LastUpdateTime;
        LastTickTime = LastUpdateTime;
        LastTailJumpTime = LastUpdateTime - TailJumpCooldown;
        TailJumpAnimationStartTime = double.NegativeInfinity;
        CurrentJumpAnimationDuration = TailJumpAnimationDuration;
        JumpAnimationPausesMovement = false;

        Velocity = new Vector2Int(1, 0);
        Rotation = 0;
        Snake = [new Vector2Int(W / 2, H / 2)];

        IsValveOpen = false;
        FlyingOres.Clear();
        NextBreathTime = double.NegativeInfinity;
        OxygenWarned = false;
        OxygenCriticalWarned = false;
        IsGameOver = false;
        Suffocated = false;
        GameOverMessage = "";
        GameOverTime = 0.0;
        GameOverHeadCell = Vector2.zero;
        ExplosionParticles.Clear();
        // the first round waits for the board to uncover before the snake moves
        OxygenStartTime = Math.Max(GameTime, UncoverOreTime);
    }

    void StartNewGame(StyledText code)
    {
        MiningYield = selectedCharacter == 3 ? 2 : 1;
        GameMode = CharacterSelectionMode;
        if (EggStore.HighscoreTampered && !CheaterDialogOpen)
            OpenCheaterDialog();
        if (!CheaterChecked)
        {
            CheaterChecked = true;
            CheckFanAchievements();
        }
        CollectNewUnlocks();
        MenuHint = MenuHints[Random.Next(MenuHints.Length)];
        JumpThemesIndex = 0;
        JumpsThisGame = 0;
        UnlockedCharacterThisGame = -1;
        SaveRocketProgress();
        ReachedTargets.Clear();
        for (var i = 0; i < AlloyTargets.Length; i++)
            if (TargetReached(AlloyTargets[i].Prefab, AlloyTargets[i].Target))
                ReachedTargets.Add(i);
        Toasts.Clear();
        NumLives = selectedCharacter == 4 ? 9 : 1;
        AutoVent = selectedCharacter == 1;

        GameTime = 0.0;
        Stress = 0.0;
        MaxStress = selectedCharacter == 2 ? 500 : 100;

        Field = new int[H, W];
        Code = new char[H, W];
        CodeColors = new uint[H, W];

        Alloys = [];
        AlloyNeeded.Clear();
        foreach (var alloy in AlloyTypes)
        {
            Alloys[alloy.PrefabHash] = new AlloyState { Quantity = 0.0, BackgroundColor = 0 };
            foreach (var t in AlloyTargets)
                if (t.Prefab == alloy.PrefabName)
                    AlloyNeeded[alloy.PrefabHash] = Math.Max(0, t.Target - WholeIngots(GlobalAlloyQuantity(t.Prefab)));
        }
        CurrentSmeltHash = 0;

        var rand = new System.Random();

        // the trigger line can be parsed before the rest of the code is in place
        if (code.Count == 0)
            code = [StyledLine.FromString("")];

        var cells = new List<Vector2Int>();
        for (var i = 0; i < H; i++)
        {
            var line = code[i % code.Count];
            for (var j = 0; j < W; j++)
            {
                var c = ' ';
                var color = 0xFFFFFFFF;
                Field[i, j] = EmptyCell;

                var token = line.GetTokenAt(j);
                if (token != null)
                {
                    color = token.Color;
                    c = token.Text[j - token.Column];
                }

                Code[i, j] = c;
                CodeColors[i, j] = color;

                if (c != ' ' && rand.NextDouble() >= EmptyFraction)
                    cells.Add(new Vector2Int(j, i));
            }
        }
        FillOres(cells, rand);

        HasBackpack = false;
        OresPerBelt = DefaultOresPerBelt;
        DrillPickupsLeft = 0;
        // items go anywhere except the start row ahead of the snake
        var start = new Vector2Int(W / 2, H / 2);
        var itemCells = new List<Vector2Int>();
        for (var i = 0; i < H; i++)
            for (var j = 0; j < W; j++)
                if (i != start.y || j < start.x || j > start.x + 3)
                    itemCells.Add(new Vector2Int(j, i));
        itemCells = itemCells.OrderBy(_ => rand.Next()).ToList();
        var items = new List<int> { BackpackCell };
        items.AddRange(Enumerable.Repeat(DrillCell, rand.Next(MinDrills, MaxDrills + 1)));
        items.AddRange(Enumerable.Repeat(AirCell, AirCanisters));
        for (var i = 0; i < items.Count && i < itemCells.Count; i++)
            Field[itemCells[i].y, itemCells[i].x] = items[i];

        StartNewRound();
    }

    const double EmptyFraction = 0.2;
    const double IceFraction = 0.4;
    const double VeinFraction = 0.7;
    const int MinVeinSize = 3;
    const int MaxVeinSize = 10;
    static readonly Dictionary<string, double> IceWeights = new()
    {
        ["ItemVolatiles"] = 4,
        ["ItemOxite"] = 2,
        ["ItemNitrice"] = 1,
    };

    // Target share per thing index: ices by fixed weights, ores by what the whole rocket needs.
    double[] OreDistribution()
    {
        var share = new double[Things.Count];
        foreach (var t in AlloyTargets)
        {
            var recipe = Recipes[Animator.StringToHash(t.Prefab)];
            foreach (var reagent in Reagent.AllReagents)
            {
                var index = Things.FindIndex(thing => thing.PrefabName == OreName(reagent));
                if (index >= 0)
                    share[index] += t.Target * recipe.Get(reagent);
            }
        }
        var oreSum = share.Sum();
        var iceSum = IceWeights.Values.Sum();
        for (var k = 0; k < share.Length; k++)
        {
            if (IceWeights.TryGetValue(Things[k].PrefabName, out var ice))
                share[k] = IceFraction * ice / iceSum;
            else if (oreSum > 0)
                share[k] *= (1 - IceFraction) / oreSum;
        }
        return share;
    }

    // Veins of one ore grown from random seeds, the rest filled cell by cell. Both pick the type
    // lagging most behind its target share, so the whole field converges to the distribution.
    void FillOres(List<Vector2Int> cells, System.Random rand)
    {
        var target = OreDistribution();
        var count = new int[Things.Count];
        var total = cells.Count;
        var open = new HashSet<Vector2Int>(cells);

        double Deficit(int k) => target[k] * total - count[k];

        int PickType()
        {
            var weights = new double[Things.Count];
            for (var k = 0; k < weights.Length; k++)
                weights[k] = Math.Max(0, Deficit(k));
            if (weights.Sum() <= 0)
                weights = target;
            var pick = rand.NextDouble() * weights.Sum();
            for (var k = 0; k < weights.Length; k++)
                if ((pick -= weights[k]) < 0)
                    return k;
            return weights.Length - 1;
        }

        void Place(Vector2Int cell, int k)
        {
            Field[cell.y, cell.x] = k;
            count[k]++;
            open.Remove(cell);
        }

        // Veins may skip a single space so they cross the gaps between tokens.
        IEnumerable<Vector2Int> OpenNeighbours(Vector2Int cell)
        {
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -2; dx <= 2; dx++)
                {
                    if (Math.Abs(dx) == 2 && dy != 0)
                        continue;
                    var n = new Vector2Int(cell.x + dx, cell.y + dy);
                    if (n != cell && open.Contains(n))
                        yield return n;
                }
        }

        var seeds = cells.OrderBy(_ => rand.Next()).ToList();
        foreach (var seed in seeds)
        {
            if (total - open.Count >= VeinFraction * total)
                break;
            if (!open.Contains(seed))
                continue;
            var k = PickType();
            var size = Math.Min(rand.Next(MinVeinSize, MaxVeinSize + 1), Math.Max(1, (int)Math.Ceiling(Deficit(k))));
            var frontier = new List<Vector2Int> { seed };
            while (size-- > 0 && frontier.Count > 0)
            {
                var cell = frontier[rand.Next(frontier.Count)];
                frontier.Remove(cell);
                Place(cell, k);
                foreach (var n in OpenNeighbours(cell))
                    if (!frontier.Contains(n))
                        frontier.Add(n);
            }
        }

        foreach (var cell in seeds)
            if (open.Contains(cell))
                Place(cell, PickType());
    }

    // Tail length follows the stored ore count; one belt (or backpack) per OresPerBelt ores.
    void TrimTail()
    {
        var desired = 1 + (int)Math.Ceiling(GetStoredOreCount() / OresPerBelt);
        while (Snake.Count > desired)
            Snake.RemoveAt(Snake.Count - 1);
    }

    bool IsOutsidePlayfield(Vector2Int cell)
    {
        return cell.x < 0 || cell.x >= W || cell.y < 0 || cell.y >= H;
    }

    void StartJumpAnimation(Vector2Int from, Vector2Int to, double duration, bool pausesMovement)
    {
        TailJumpAnimationStartTime = ImGui.GetTime();
        CurrentJumpAnimationDuration = Math.Max(0.01, duration);
        JumpAnimationPausesMovement = pausesMovement;
        TailJumpFrom = new Vector2(from.x, from.y);
        TailJumpTo = new Vector2(to.x, to.y);
    }

    bool IsJumpAnimationRunning(double now)
    {
        return now - TailJumpAnimationStartTime < CurrentJumpAnimationDuration;
    }

    double GetStuckJumpAnimationDuration(Vector2Int from, Vector2Int to)
    {
        var dx = to.x - from.x;
        var dy = to.y - from.y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        return Math.Min(
            StuckJumpMaxAnimationDuration,
            Math.Max(StuckJumpMinAnimationDuration, distance * StuckJumpAnimationSecondsPerCell)
        );
    }

    bool TryJumpOverTail(Vector2Int blockedCell, double now, out Vector2Int landingCell)
    {
        landingCell = blockedCell;

        if (selectedCharacter != 3 || now - LastTailJumpTime < TailJumpCooldown)
            return false;

        var candidate = blockedCell + Velocity;
        if (IsOutsidePlayfield(candidate) || Snake.Contains(candidate))
            return false;

        LastTailJumpTime = now;
        StartJumpAnimation(Snake[0], candidate, TailJumpAnimationDuration, false);
        landingCell = candidate;

        if (JumpThemes.Count > 0)
        {
            Synth.PlaySong(JumpThemes[JumpThemesIndex]);
            JumpThemesIndex = (JumpThemesIndex + 1) % JumpThemes.Count;
        }
        if (++JumpsThisGame >= TurboBoostJumps)
            UnlockAchievement(AchievementTurboBoost, EggText.TurboBoostTitle);
        return true;
    }

    bool TryGetRandomEmptyCell(out Vector2Int cell)
    {
        var candidates = new List<Vector2Int>();
        var fallback = new List<Vector2Int>();

        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var candidate = new Vector2Int(x, y);
                if (Snake.Contains(candidate))
                    continue;

                fallback.Add(candidate);
                if (Field[y, x] == EmptyCell)
                    candidates.Add(candidate);
            }
        }

        var cells = candidates.Count > 0 ? candidates : fallback;
        if (cells.Count == 0)
        {
            cell = Vector2Int.zero;
            return false;
        }

        cell = cells[Random.Next(cells.Count)];
        return true;
    }

    struct FlyingOre
    {
        public int ThingIndex;
        public Vector2 From;
        public Vector2Int Target;
        public double Start;
        public double Duration;
    }

    readonly List<FlyingOre> FlyingOres = [];

    bool TryGetRandomEmptyCellNear(Vector2Int origin, int radius, out Vector2Int cell)
    {
        var candidates = new List<Vector2Int>();
        for (var y = Math.Max(0, origin.y - radius); y <= Math.Min(H - 1, origin.y + radius); y++)
            for (var x = Math.Max(0, origin.x - radius); x <= Math.Min(W - 1, origin.x + radius); x++)
            {
                var candidate = new Vector2Int(x, y);
                if (candidate == origin || Field[y, x] != EmptyCell || Snake.Contains(candidate))
                    continue;
                if (FlyingOres.Any(f => f.Target == candidate))
                    continue;
                candidates.Add(candidate);
            }

        if (candidates.Count > 0)
        {
            cell = candidates[Random.Next(candidates.Count)];
            return true;
        }
        return TryGetRandomEmptyCell(out cell);
    }

    // Aimee loses up to all of her stored ores when she jumps away; they fly off and land on nearby cells.
    void ShootStoredOres(Vector2Int origin)
    {
        var maxLoss = (int)Math.Min(GetStoredOreCount(), Snake.Count - 1);
        if (maxLoss <= 0)
            return;

        var loss = Random.Next(Math.Max(1, (maxLoss + 1) / 2), maxLoss + 1);
        var now = ImGui.GetTime();
        for (var i = 0; i < loss; i++)
        {
            var thingIndex = TakeRandomStoredOre();
            if (thingIndex < 0 || !TryGetRandomEmptyCellNear(origin, 7, out var target))
                break;

            var distance = Vector2Int.Distance(origin, target);
            FlyingOres.Add(new FlyingOre
            {
                ThingIndex = thingIndex,
                From = new Vector2(origin.x, origin.y),
                Target = target,
                Start = now + i * 0.04,
                Duration = 0.4 + 0.07 * distance,
            });
        }
    }

    void LandFlyingOres(double now)
    {
        for (var i = FlyingOres.Count - 1; i >= 0; i--)
        {
            var ore = FlyingOres[i];
            if (now < ore.Start + ore.Duration)
                continue;

            var target = ore.Target;
            if (Field[target.y, target.x] != EmptyCell || Snake.Contains(target))
                if (!TryGetRandomEmptyCell(out target))
                    target = ore.Target;
            Field[target.y, target.x] = ore.ThingIndex;
            FlyingOres.RemoveAt(i);
        }
    }

    void DrawFlyingOres(ImDrawListPtr list, Vector2 p0, Vector2 x, Vector2 y)
    {
        var now = ImGui.GetTime();
        foreach (var ore in FlyingOres)
        {
            var t = Mathf.Clamp01((float)((now - ore.Start) / ore.Duration));
            var cell = Vector2.Lerp(ore.From, new Vector2(ore.Target.x, ore.Target.y), t);
            var lift = Mathf.Sin(t * Mathf.PI) * 1.5f;
            var p = p0 + cell.x * x + (cell.y - lift) * y;
            list.AddImage(ImGuiManager.ImGuiPointerFor(OreSprites[ore.ThingIndex]), p + 0.05f * (x + y), p + 0.95f * (x + y));
        }
    }

    double GetStoredOreCount()
    {
        var count = 0.0;
        foreach (var oreCount in FurnaceOres.Values)
            count += Math.Max(0, oreCount);
        return count;
    }

    int TakeRandomStoredOre()
    {
        var stockedOres = FurnaceOres
            .Where(kvp => kvp.Value > 0)
            .Select(kvp => new
            {
                PrefabName = kvp.Key,
                Count = kvp.Value,
                ThingIndex = Things.FindIndex(thing => thing.PrefabName == kvp.Key)
            })
            .Where(ore => ore.ThingIndex >= 0)
            .ToList();

        var totalCount = stockedOres.Sum(ore => ore.Count);
        if (totalCount <= 0)
            return -1;

        double pick = Random.Next((int)Math.Floor(totalCount));
        foreach (var ore in stockedOres)
        {
            if (pick >= ore.Count)
            {
                pick -= ore.Count;
                continue;
            }

            FurnaceOres[ore.PrefabName]--;
            if (FurnaceOres[ore.PrefabName] <= 0)
                FurnaceOres.Remove(ore.PrefabName);
            return ore.ThingIndex;
        }

        return -1;
    }

    bool TrySetRandomOpenDirection()
    {
        var rotations = new List<int> { 0, 1, 2, 3 };
        while (rotations.Count > 0)
        {
            var index = Random.Next(rotations.Count);
            var rotation = rotations[index];
            rotations.RemoveAt(index);

            var velocity = rotation switch
            {
                0 => new Vector2Int(1, 0),
                1 => new Vector2Int(0, -1),
                2 => new Vector2Int(-1, 0),
                3 => new Vector2Int(0, 1),
                _ => Vector2Int.zero
            };

            var nextCell = Snake[0] + velocity;
            if (IsOutsidePlayfield(nextCell) || Snake.Contains(nextCell))
                continue;

            SetRotation(rotation);
            return true;
        }

        return false;
    }

    void FinishBeingStuck()
    {
        if (!TryGetRandomEmptyCell(out var landingCell))
            return;

        var oldHead = Snake[0];
        ShootStoredOres(oldHead);
        Snake[0] = landingCell;
        TrimTail();
        StartJumpAnimation(oldHead, landingCell, GetStuckJumpAnimationDuration(oldHead, landingCell), true);
        if (!TrySetRandomOpenDirection())
            SetRotation(Random.Next(0, 4));
    }

    struct ExplosionParticle
    {
        public Texture2D Sprite;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Size;
        public float Rotation;
        public float RotationVelocity;
        public float Age;
        public float Lifetime;
        public uint Color;
    }

    // Text metrics of the current (unscaled) font; Settings.* reflect the scaled editor font.
    static float CharWidth => ImGui.CalcTextSize("M").x;
    static float LineHeight => ImGui.GetTextLineHeight();
    static float LineHeightWithSpacing => ImGui.GetTextLineHeightWithSpacing();

    static uint ColorWithAlpha(byte r, byte g, byte b, byte a)
    {
        return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
    }

    float NextFloat(float min, float max)
    {
        return min + (float)Random.NextDouble() * (max - min);
    }

    static float SmootherStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
    }

    void SpawnExplosionAtHead()
    {
        EggAudio.Play(Sounds.AtmosphereFireStart);
        EggAudio.Play("ShuttleSmallSonicBoom", 0.3f, 2.0f);
        EggAudio.Play(Sounds.PipeFailHash);
        EggAudio.Play("PayloadDeploy");
        EggAudio.Play("PayloadDeployTail");
        ExplosionParticles.Clear();

        var spawnCount = Math.Max(ExplosionParticleCount, OreSprites.Count > 0 ? OreSprites.Count * 2 : ExplosionParticleCount);
        var warmCount = Math.Min(spawnCount / 8, OreSprites.Count);

        for (var i = 0; i < warmCount; i++)
        {
            if (i >= OreSprites.Count)
                break;

            var angle = NextFloat(0f, Mathf.PI * 2f);
            var speed = NextFloat(ExplosionMinSpeed, ExplosionMaxSpeed);
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            ExplosionParticles.Add(new ExplosionParticle
            {
                Sprite = OreSprites[i],
                Position = GameOverHeadCell + NextFloat(-0.2f, 0.2f) * Vector2.one,
                Velocity = dir * speed,
                Size = NextFloat(0.45f, 1.2f),
                Rotation = NextFloat(0f, Mathf.PI * 2f),
                RotationVelocity = NextFloat(ExplosionSpinMin, ExplosionSpinMax),
                Age = 0f,
                Lifetime = NextFloat(0.9f, ExplosionDuration),
                Color = ColorWithAlpha(255, (byte)NextFloat(140f, 255f), (byte)NextFloat(100f, 220f), 255),
            });
        }

        for (var i = warmCount; i < spawnCount; i++)
        {
            var sprite = OreSprites.Count > 0 ? OreSprites[Random.Next(OreSprites.Count)] : Tail;
            var angle = NextFloat(0f, Mathf.PI * 2f);
            var speed = NextFloat(ExplosionMinSpeed, ExplosionMaxSpeed);
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            ExplosionParticles.Add(new ExplosionParticle
            {
                Sprite = sprite,
                Position = GameOverHeadCell + new Vector2(NextFloat(-0.25f, 0.25f), NextFloat(-0.25f, 0.25f)),
                Velocity = dir * speed,
                Size = NextFloat(0.35f, 1.25f),
                Rotation = NextFloat(0f, Mathf.PI * 2f),
                RotationVelocity = NextFloat(ExplosionSpinMin, ExplosionSpinMax),
                Age = 0f,
                Lifetime = NextFloat(0.7f, ExplosionDuration),
                Color = ColorWithAlpha(255, (byte)NextFloat(110f, 255f), (byte)NextFloat(80f, 200f), 255),
            });
        }

        for (var i = 0; i < 16; i++)
        {
            var angle = NextFloat(0f, Mathf.PI * 2f);
            var speed = NextFloat(ExplosionMaxSpeed * 0.65f, ExplosionMaxSpeed * 1.2f);
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            ExplosionParticles.Add(new ExplosionParticle
            {
                Sprite = Head,
                Position = GameOverHeadCell,
                Velocity = dir * speed,
                Size = NextFloat(0.65f, 1.5f),
                Rotation = NextFloat(0f, Mathf.PI * 2f),
                RotationVelocity = NextFloat(ExplosionSpinMin * 1.8f, ExplosionSpinMax * 1.8f),
                Age = 0f,
                Lifetime = NextFloat(0.8f, ExplosionDuration),
                Color = 0xFFFFFFFF,
            });
        }
    }

    bool CanSmelt(Recipe recipe)
    {
        foreach (var reagent in Reagent.AllReagents)
        {
            var needed = recipe.Get(reagent);
            if (needed == 0) continue;
            if (!FurnaceOres.TryGetValue(OreName(reagent), out var count) || count + QuantityEpsilon < needed)
            {
                L.Debug($"Cannot smelt because we need {needed} of {reagent.TypeName} but only have {count}");
                return false;
            }
        }
        return true;
    }

    // Alloy currently being smelted; smelting sticks to it instead of alternating between candidates.
    int CurrentSmeltHash;

    void Smelt(double dt)
    {
        var temp = Temperature;
        var p = Pressure;

        // SmeltingSpeed = Math.Max(1.0, temp / 1000 - 1);
        SmeltingSpeed = 0.2 * Math.Max(1, temp / 1000);
        SmeltingEfficiency = Math.Max(1, p / 30000 - 1.0 / 3);

        var fraction = dt * SmeltingSpeed;

        if (fraction <= 0.0f)
            return;

        // Rocket alloys still missing come first, the one missing most on top; the alloy currently
        // being smelted keeps its place within its group instead of alternating between candidates.
        bool Needed(int hash) => RocketComplete || RocketRemaining(hash) > 0;
        List<int> Ordered(IEnumerable<int> hashes)
        {
            var order = hashes.OrderByDescending(RocketRemaining).ToList();
            if (CurrentSmeltHash != 0 && order.Remove(CurrentSmeltHash))
                order.Insert(0, CurrentSmeltHash);
            return order;
        }

        foreach (var hash in Ordered(Recipes.Keys.Where(Needed)))
            if (TrySmelt(hash, temp, p, fraction))
                return;

        // Finished alloys only to keep the furnace from filling up with unused ores.
        foreach (var hash in Ordered(Recipes.Keys.Where(hash => !Needed(hash))))
            if (HasOreSurplus(Recipes[hash]) && TrySmelt(hash, temp, p, fraction))
                return;

        CurrentSmeltHash = 0;
    }

    const double OreSurplus = 5;

    static string OreName(Reagent reagent) =>
        reagent.TypeName == "Reagents.Hydrocarbon" ? "ItemCoalOre" : reagent.TypeName.Replace("Reagents.", "Item") + "Ore";

    bool HasOreSurplus(Recipe recipe)
    {
        foreach (var reagent in Reagent.AllReagents)
            if (recipe.Get(reagent) > 0 && FurnaceOres.TryGetValue(OreName(reagent), out var count) && count >= OreSurplus)
                return true;
        return false;
    }

    bool TrySmelt(int hash, double temp, double p, double fraction)
    {
        var recipe = Recipes[hash];
        if (recipe.Temperature.Start > temp || recipe.Pressure.Start > p)
            return false;
        recipe = recipe * fraction;

        if (!Alloys.TryGetValue(hash, out var alloy))
        {
            L.Debug($"Alloy with hash {hash} not found in Alloys dictionary");
            return false;
        }

        if (!CanSmelt(recipe))
        {
            if (alloy.Quantity > 0)
                alloy.BackgroundColor = 0xFF008FFF;
            Alloys[hash] = alloy;
            return false;
        }

        L.Debug($"Smelting at temp {temp} and pressure {p} with fraction {fraction} and efficiency {SmeltingEfficiency}");
        foreach (var reagent in Reagent.AllReagents)
        {
            var needed = recipe.Get(reagent);
            if (needed > 0)
                FurnaceOres[OreName(reagent)] = Math.Max(0, SnapToWhole(FurnaceOres[OreName(reagent)] - needed));
        }

        alloy.Quantity = SnapToWhole(alloy.Quantity + fraction * SmeltingEfficiency);
        // finished parts stay at their target, surplus does not count for the rocket
        if (RocketRemaining(hash) > 0)
            foreach (var alloyType in AlloyTypes)
                if (alloyType.PrefabHash == hash)
                    RocketProgress[alloyType.PrefabName] = SnapToWhole(GlobalAlloyQuantity(alloyType.PrefabName) + fraction * SmeltingEfficiency);
        alloy.BackgroundColor = 0x00FF00FF;
        Alloys[hash] = alloy;
        CurrentSmeltHash = hash;
        return true;
    }

    void DrawAlloys(Vector2 pos)
    {
        var entries = new List<(Texture2D Sprite, double Amount, int Needed)>();
        foreach (var alloyType in AlloyTypes)
        {
            AlloyNeeded.TryGetValue(alloyType.PrefabHash, out var needed);
            entries.Add((alloyType.GetThumbnail().texture, Alloys[alloyType.PrefabHash].Quantity, needed));
        }
        var bottom = DrawItemGrid(pos, entries);
        ImGui.SetCursorScreenPos(new Vector2(pos.x, bottom + 0.4f * gridSize));
        DrawScore();
    }

    const int ItemColumns = 3;

    // Sprite + "count/needed" cells in three columns spread over the panel width.
    float DrawItemGrid(Vector2 pos, List<(Texture2D Sprite, double Amount, int Needed)> entries, float imSize = 0f)
    {
        if (imSize <= 0f)
            imSize = ItemImageSize;
        var columns = ItemColumns;
        var dx = ImGui.GetContentRegionAvail().x / columns;
        var dy = imSize + 6;
        var x0 = pos.x;
        var im = ImGui.GetWindowDrawList();

        for (var i = 0; i < entries.Count; i++)
        {
            var (sprite, amount, needed) = entries[i];
            var count = WholeIngots(amount);
            var fractional = amount - count;
            var cell = new Vector2(x0 + i % columns * dx, pos.y + i / columns * dy);

            im.AddRectFilled(cell + new Vector2(0, imSize - (float)fractional * imSize), cell + new Vector2(imSize, imSize), ColorWithAlpha(0, 200, 0, 64));
            im.AddImage(ImGuiManager.ImGuiPointerFor(sprite), cell, cell + new Vector2(imSize, imSize));
            im.AddText(cell + new Vector2(imSize + 3, (imSize - LineHeight) * 0.5f), CountColor(count, needed), CountLabel(count, needed));
        }

        var rows = (entries.Count + columns - 1) / columns;
        return pos.y + rows * dy;
    }

    float ItemImageSize => 1.0f * gridSize;

    static readonly Dictionary<string, int> AlloyPoints = new()
    {
        ["ItemWaspaloyIngot"] = 10,
        ["ItemInconelIngot"] = 15,
        ["ItemHastelloyIngot"] = 20,
        ["ItemAstroloyIngot"] = 30,
        ["ItemStelliteIngot"] = 40,
    };
    const double AllooBonus = 1.3;

    struct ScoreBreakdown
    {
        public int Base;
        public bool AllooDeal;
        public bool BoardCleared;
        public int OresOnBoard;
        public int Total;
    }

    ScoreBreakdown FinalScore;

    ScoreBreakdown GetScore()
    {
        var s = new ScoreBreakdown();

        var points = 0.0;
        foreach (var alloy in AlloyTypes)
            points += WholeIngots(Alloys[alloy.PrefabHash].Quantity) * (AlloyPoints.TryGetValue(alloy.PrefabName, out var p) ? p : 10);
        s.Base = (int)points;

        for (var i = 0; i < H; i++)
            for (var j = 0; j < W; j++)
                if (Field[i, j] >= 0)
                    s.OresOnBoard++;

        s.AllooDeal = selectedCharacter == 2;
        s.BoardCleared = s.OresOnBoard == 0 && FlyingOres.Count == 0;

        var total = (double)s.Base;
        if (s.AllooDeal) total *= AllooBonus;
        if (s.BoardCleared) total *= 2;
        s.Total = (int)Math.Round(total);
        return s;
    }

    static Vector4 Vec4(uint color) => ImGui.ColorConvertU32ToFloat4(color);

    double AlloyQuantity(string prefabName)
    {
        foreach (var alloy in AlloyTypes)
            if (alloy.PrefabName == prefabName && Alloys.TryGetValue(alloy.PrefabHash, out var state))
                return state.Quantity;
        return 0.0;
    }

    static int RocketPartsBuilt => AlloyTargets.Count(t => TargetReached(t.Prefab, t.Target));
    static bool RocketComplete => RocketPartsBuilt >= AlloyTargets.Length;
    // Game over leads into the ceremony only until it was watched once.
    static bool LaunchPending => RocketComplete && !EggStore.State.CeremonySeen;

    void Toast(string text, double seconds = 4.0) => Toasts.Add((text, ImGui.GetTime() + seconds));

    void DrawToasts(Vector2 topCenter)
    {
        var now = ImGui.GetTime();
        Toasts.RemoveAll(t => t.Expiry < now);
        var list = ImGui.GetWindowDrawList();
        var y = topCenter.y;
        foreach (var toast in Toasts)
        {
            var alpha = (byte)(255 * Mathf.Clamp01((float)(toast.Expiry - now) / 0.5f));
            var size = ImGui.CalcTextSize(toast.Text);
            var pos = new Vector2(topCenter.x - size.x * 0.5f, y);
            list.AddRectFilled(pos - new Vector2(8, 4), pos + size + new Vector2(8, 4), ColorWithAlpha(0, 0, 0, (byte)(alpha * 0.6f)), 4f);
            list.AddText(pos, ColorWithAlpha(255, 235, 180, alpha), toast.Text);
            y += size.y + 12;
        }
    }

    const double NowPlayingDuration = 5.0;

    // Name of the current track for a few seconds after it starts, bottom left.
    void DrawNowPlaying()
    {
        var track = EggMusic.Current;
        var elapsed = ImGui.GetTime() - EggMusic.CurrentStart;
        if (track == null || elapsed > NowPlayingDuration)
            return;
        var alpha = (byte)(255 * Mathf.Clamp01((float)(NowPlayingDuration - elapsed) / 0.5f));
        var text = string.Format(EggText.NowPlaying, track.DisplayName);
        var size = ImGui.CalcTextSize(text);
        var viewport = ImGui.GetMainViewport();
        var pos = viewport.Pos + new Vector2(20, viewport.Size.y - size.y - 20);
        var list = ImGui.GetForegroundDrawList();
        list.AddRectFilled(pos - new Vector2(8, 4), pos + size + new Vector2(8, 4), ColorWithAlpha(0, 0, 0, (byte)(alpha * 0.6f)), 4f);
        list.AddText(pos, ColorWithAlpha(255, 235, 180, alpha), text);
    }

    void DrawDebugMenu()
    {
        ImGui.SetNextWindowSize(new Vector2(340, 0), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Debug", ref DebugMenuOpen))
        {
            ImGui.End();
            return;
        }

        ImGui.Text("Hold + during the ceremony for 10x speed");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().x - 90);
        // ImGui.SliderFloat("Beat shift", ref BeatShift, -1f, 1f, "%.2f beats");
        ImGui.Text($"Mods by {ModAuthor} loaded: {CountModsByAuthor(ModAuthor)}");

        var button = new Vector2(ImGui.GetContentRegionAvail().x, 0);
        if (ImGui.Button("Reset stats", button))
            ResetState();
        if (ImGui.Button("Unlock all characters", button))
        {
            EggStore.State.UnlockedCharacters = CharacterRequiredScore.Length;
            EggStore.Save();
        }
        if (ImGui.Button("Unlock all achievements", button))
        {
            UnlockAchievement(AchievementTurboBoost, EggText.TurboBoostTitle);
            UnlockAchievement(AchievementCleanSweep, EggText.CleanSweepTitle);
            UnlockAchievement(AchievementFan, EggText.FanTitle);
            UnlockAchievement(AchievementSuperFan, EggText.SuperFanTitle);
            UnlockAchievement(AchievementRocketBuilder, EggText.RocketBuilderTitle);
            UnlockAchievement(AchievementHcf, EggText.HcfTitle);
        }
        if (ImGui.Button("Complete rocket", button))
        {
            foreach (var t in AlloyTargets)
                RocketProgress[t.Prefab] = Math.Max(GlobalAlloyQuantity(t.Prefab), t.Target);
            SaveRocketProgress();
        }
        if (ImGui.Button("Start party ceremony", button))
            StartCeremony();
        if (ImGui.Button("Show cheater dialog", button))
            OpenCheaterDialog();

        // ImGui.Separator();
        // ImGui.Checkbox("Sound browser", ref ShowSoundBrowser);
        // ImGui.Checkbox("Synth control", ref ShowAudioControl);
        // ImGui.Checkbox("Music via interface bus", ref EggAudio.MusicOnInterfaceBus);
        ImGui.End();
    }

    void DrawScore()
    {
        ImGui.Text(string.Format(EggText.Score, GetScore().Total));
    }

    float BarHeight => LineHeightWithSpacing * 1.25f;

    void DrawBar(string label, string valueText, double fraction, uint color)
    {
        var width = ImGui.GetContentRegionAvail().x;
        var height = BarHeight;
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = p0 + new Vector2(width, height);
        var im = ImGui.GetWindowDrawList();
        var fill = Mathf.Clamp01((float)fraction);

        im.AddRectFilled(p0, p1, ColorWithAlpha(28, 28, 32, 255), 3f);
        if (fill > 0f)
            im.AddRectFilled(p0, new Vector2(p0.x + width * fill, p1.y), color, 3f);
        im.AddRect(p0, p1, ColorWithAlpha(100, 100, 110, 255), 3f);

        var textY = p0.y + (height - LineHeight) * 0.5f;
        var valueSize = ImGui.CalcTextSize(valueText);
        var shadow = ColorWithAlpha(0, 0, 0, 200);
        im.AddText(new Vector2(p0.x + 7, textY + 1), shadow, label);
        im.AddText(new Vector2(p0.x + 6, textY), 0xFFFFFFFF, label);
        im.AddText(new Vector2(p1.x - valueSize.x - 5, textY + 1), shadow, valueText);
        im.AddText(new Vector2(p1.x - valueSize.x - 6, textY), 0xFFFFFFFF, valueText);

        ImGui.Dummy(new Vector2(width, height + 4));
    }

    static readonly uint Green = ColorWithAlpha(40, 140, 60, 255);
    static readonly uint Yellow = ColorWithAlpha(170, 140, 20, 255);
    static readonly uint Orange = ColorWithAlpha(190, 100, 15, 255);
    static readonly uint Red = ColorWithAlpha(180, 40, 30, 255);

    static uint WithAlpha(uint color, byte alpha) => (color & 0x00FFFFFF) | ((uint)alpha << 24);

    static uint LerpColor(uint a, uint b, float t)
    {
        t = Mathf.Clamp01(t);
        uint Channel(int shift)
        {
            var v = ((a >> shift) & 0xFF) * (1f - t) + ((b >> shift) & 0xFF) * t;
            return (uint)Mathf.RoundToInt(v) << shift;
        }
        return Channel(0) | Channel(8) | Channel(16) | Channel(24);
    }

    // Piecewise linear colormap through (value, color) stops sorted by value.
    static uint RampColor(double value, params (double at, uint color)[] stops)
    {
        if (value <= stops[0].at)
            return stops[0].color;
        for (var i = 1; i < stops.Length; i++)
            if (value <= stops[i].at)
                return LerpColor(stops[i - 1].color, stops[i].color, (float)((value - stops[i - 1].at) / (stops[i].at - stops[i - 1].at)));
        return stops[stops.Length - 1].color;
    }

    void DrawFurnace(float panelWidth)
    {
        var size = Mathf.Min(panelWidth * 0.4f, 5f * gridSize);
        var ventSize = size * 0.5f;
        var im = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var p0 = origin + new Vector2(0.1f * size, 0.2f * size);

        if (IsGameOver && !Suffocated)
        {
            im.AddImage(ImGuiManager.ImGuiPointerFor(Wreckage), p0, p0 + new Vector2(size, size));
        }
        else
        {
            im.AddImage(ImGuiManager.ImGuiPointerFor(Furnace), p0, p0 + new Vector2(size, size));

            var heat = Mathf.Clamp01((float)((Temperature - 400.0) / 1600.0));
            var pressureBoost = 0.3f + 0.7f * Mathf.Clamp01((float)(Pressure / 60000.0));
            var t = (float)ImGui.GetTime();
            var flicker = 0.9f + 0.1f * Mathf.Sin(t * 11f) * Mathf.Sin(t * 7.3f + 1f);
            var intensity = heat * pressureBoost * flicker;
            var glow = RampColor(Temperature,
                (400, ColorWithAlpha(120, 10, 0, 255)),
                (900, ColorWithAlpha(255, 60, 0, 255)),
                (1500, ColorWithAlpha(255, 170, 40, 255)),
                (2200, ColorWithAlpha(255, 240, 200, 255)));

            var winSize = new Vector2(FurnaceWindow.width, FurnaceWindow.height) * size;
            var center = p0 + new Vector2(FurnaceWindow.center.x, FurnaceWindow.center.y) * size + 0.25f * winSize;
            var radii = 0.25f * winSize;

            DrawEllipseGlow(im, center, 2.2f * radii, glow, (byte)(intensity * 90f));
            DrawEllipseGlow(im, center, 1.4f * radii, glow, (byte)(intensity * 150f));
            DrawEllipseGlow(im, center, radii, glow, (byte)(intensity * 230f));
        }

        var ventPos = new Vector2(p0.x + size + 0.8f * gridSize, p0.y + (size - ventSize) * 0.5f);
        var ventTint = IsValveOpen ? 0xFFFFFFFF : ColorWithAlpha(128, 128, 128, 255);
        im.AddImage(ImGuiManager.ImGuiPointerFor(Vent), ventPos, ventPos + new Vector2(ventSize, ventSize), Vector2.zero, Vector2.one, ventTint);

        var hintY = ventPos.y + ventSize + 2f;
        foreach (var line in EggText.VentHint.Split('\n'))
        {
            var lineSize = ImGui.CalcTextSize(line);
            im.AddText(new Vector2(ventPos.x + (ventSize - lineSize.x) * 0.5f, hintY), IsValveOpen ? 0xFFFFFFFF : ColorWithAlpha(170, 170, 180, 255), line);
            hintY += LineHeight;
        }

        ImGui.Dummy(new Vector2(panelWidth, Mathf.Max(1.2f * size, hintY - origin.y) + 0.5f * gridSize));
    }

    // Main menu: rocket parts with the alloy they need and the global counter (kept across games).
    void DrawRocketProgress(float cell = 104f)
    {
        var sprite = cell * 0.58f;
        var ingot = cell * 0.23f;
        var im = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        for (var i = 0; i < RocketParts.Count && i < AlloyTargets.Length; i++)
        {
            var prefab = AlloyTargets[i].Prefab;
            var global = WholeIngots(GlobalAlloyQuantity(prefab));
            var done = global >= AlloyTargets[i].Target;
            var center = pos + new Vector2(cell * (i + 0.5f), sprite * 0.5f);
            DrawRocketPart(i, center, sprite, 0f, done ? 0xFFFFFFFF : ColorWithAlpha(110, 110, 120, 255));

            var label = $"{global}/{AlloyTargets[i].Target}";
            var labelSize = ImGui.CalcTextSize(label);
            var rowY = pos.y + sprite + 4f;
            var rowX = center.x - (ingot + 4f + labelSize.x) * 0.5f;
            var alloyType = AlloyTypes.FirstOrDefault(a => a.PrefabName == prefab);
            if (alloyType != null)
                im.AddImage(ImGuiManager.ImGuiPointerFor(alloyType.GetThumbnail().texture), new Vector2(rowX, rowY), new Vector2(rowX + ingot, rowY + ingot));
            im.AddText(new Vector2(rowX + ingot + 4f, rowY + (ingot - labelSize.y) * 0.5f), done ? ColorWithAlpha(120, 230, 120, 255) : ColorWithAlpha(190, 190, 200, 255), label);
        }

        ImGui.Dummy(new Vector2(cell * RocketParts.Count, sprite + ingot + 8f));
    }

    // Elliptic radial gradient: `alpha` at the center fading to transparent at the ellipse edge.
    static void DrawEllipseGlow(ImDrawListPtr im, Vector2 center, Vector2 radii, uint color, byte alpha)
    {
        const int segments = 48;
        var inner = WithAlpha(color, alpha);
        var outer = WithAlpha(color, 0);
        var uv = ImGui.GetFontTexUvWhitePixel();

        im.PrimReserve(segments * 3, segments + 1);
        var baseIdx = im._VtxCurrentIdx;
        im.PrimWriteVtx(center, uv, inner);
        for (var i = 0; i < segments; i++)
        {
            var angle = i * 2f * Mathf.PI / segments;
            im.PrimWriteVtx(center + new Vector2(radii.x * Mathf.Cos(angle), radii.y * Mathf.Sin(angle)), uv, outer);
        }
        for (var i = 0; i < segments; i++)
        {
            im.PrimWriteIdx((ushort)baseIdx);
            im.PrimWriteIdx((ushort)(baseIdx + 1 + i));
            im.PrimWriteIdx((ushort)(baseIdx + 1 + (i + 1) % segments));
        }
    }

    // Prefab thumbnail; a renamed prefab in a game update must not take the editor down.
    static Texture2D Thumbnail(string prefabName)
    {
        var texture = Prefab.Find(prefabName)?.GetThumbnail()?.texture ?? LoadThumbnailResource(prefabName);
        if (texture == null)
            L.Debug($"No thumbnail for {prefabName}");
        return texture ?? Texture2D.whiteTexture;
    }

    static Texture2D LoadThumbnailResource(string name)
    {
        var path = $"ui/thumbnails/{name}";
        return Resources.Load<Sprite>(path)?.texture ?? Resources.Load<Texture2D>(path);
    }

    // Thumbnails of all build states in construction order; falls back to the prefab thumbnail.
    static List<Texture2D> StructureThumbnails(string prefabName)
    {
        List<Texture2D> result = [];
        var prefab = Prefab.Find(prefabName);
        if (prefab == null)
        {
            L.Debug($"Prefab {prefabName} not found");
            return result;
        }
        if (prefab is Structure structure && structure.BuildStates != null)
            foreach (var state in structure.BuildStates)
                if (state.Thumbnail != null && !state.DamagedBuildState)
                    result.Add(state.Thumbnail.texture);
        if (result.Count == 0 && prefab.GetThumbnail() != null)
            result.Add(prefab.GetThumbnail().texture);
        return result;
    }

    // Bounding box (UV, y down) of the warm/orange pixels in a thumbnail, i.e. the furnace window.
    static Rect FindWarmRegion(Texture2D tex, Rect fallback)
    {
        try
        {
            var w = tex.width;
            var h = tex.height;
            Color32[] pixels;
            if (tex.isReadable)
                pixels = tex.GetPixels32();
            else
            {
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readable.Apply();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                pixels = readable.GetPixels32();
                UnityEngine.Object.Destroy(readable);
            }

            int xMin = w, xMax = -1, yMin = h, yMax = -1, count = 0;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var c = pixels[y * w + x];
                    if (c.a < 64 || c.r < 120 || c.r < c.g + 40 || c.r < c.b + 80)
                        continue;
                    var yTop = h - 1 - y;
                    xMin = Math.Min(xMin, x); xMax = Math.Max(xMax, x);
                    yMin = Math.Min(yMin, yTop); yMax = Math.Max(yMax, yTop);
                    count++;
                }

            if (count < 10)
            {
                L.Debug($"Furnace window not found in thumbnail ({count} warm pixels), using fallback");
                return fallback;
            }
            var rect = new Rect(xMin / (float)w, yMin / (float)h, (xMax - xMin + 1) / (float)w, (yMax - yMin + 1) / (float)h);
            L.Debug($"Furnace window detected at {rect} ({count} warm pixels)");
            return rect;
        }
        catch (Exception e)
        {
            L.Debug($"Furnace window detection failed: {e.Message}");
            return fallback;
        }
    }

    // Returns the screen y below the last ore row.
    // "12/32" while something is still needed for the rocket, plain count otherwise; green once reached.
    static string CountLabel(int count, int needed) => needed > 0 ? $"{count}/{needed}" : $"{count}";
    static uint CountColor(int count, int needed) => needed > 0 && count >= needed ? ColorWithAlpha(120, 230, 120, 255) : 0xFFFFFFFF;

    float DrawOres(Vector2 ptOre)
    {
        var entries = new List<(Texture2D Sprite, double Amount, int Needed)>();
        foreach (var ore in Things)
        {
            FurnaceOres.TryGetValue(ore.PrefabName, out var amount);
            var needed = OreNeeded(ore.PrefabName);
            if (amount > 0 || needed > 0)
                entries.Add((ore.GetThumbnail().texture, amount, needed));
        }
        return DrawItemGrid(ptOre, entries);
    }

    void UpdateExplosion(double dt)
    {
        var dtf = (float)dt;

        for (var i = ExplosionParticles.Count - 1; i >= 0; i--)
        {
            var p = ExplosionParticles[i];
            p.Age += dtf;
            if (p.Age >= p.Lifetime)
            {
                ExplosionParticles.RemoveAt(i);
                continue;
            }

            p.Velocity.y += ExplosionGravity * dtf;
            p.Position += p.Velocity * dtf;
            p.Rotation += p.RotationVelocity * dtf;

            ExplosionParticles[i] = p;
        }
    }

    Vector2 GetShakeOffset()
    {
        if (!IsGameOver || Suffocated || !EggStore.State.ScreenShake)
            return Vector2.zero;

        var t = (float)Math.Min(GameOverTime, ShakeMaxDuration);
        var fade = 1f - Mathf.Clamp01(t / ShakeMaxDuration);
        var amp = ShakeInitialAmplitude * fade;

        var sx = Mathf.Sin((float)GameOverTime * ShakeFrequency) * amp;
        var sy = Mathf.Cos((float)GameOverTime * (ShakeFrequency * 0.77f)) * amp * 0.6f;
        return new Vector2(sx, sy);
    }

    void DrawExplosion(Vector2 p0, float cellSize, Vector2 playSize)
    {
        var list = ImGui.GetWindowDrawList();

        for (var i = 0; i < ExplosionParticles.Count; i++)
        {
            var p = ExplosionParticles[i];

            var life = Mathf.Clamp01(1f - p.Age / Mathf.Max(0.01f, p.Lifetime));
            var alpha = (byte)(255f * life);
            var baseColor = p.Color;
            var r = (byte)(baseColor & 0xFF);
            var g = (byte)((baseColor >> 8) & 0xFF);
            var b = (byte)((baseColor >> 16) & 0xFF);
            var color = ColorWithAlpha(r, g, b, alpha);

            var px = p0.x + p.Position.x * cellSize;
            var py = p0.y + p.Position.y * cellSize;
            var size = cellSize * p.Size;
            var half = size * 0.5f;

            var center = new Vector2(px + cellSize * 0.5f, py + cellSize * 0.5f);

            var c = Mathf.Cos(p.Rotation);
            var s = Mathf.Sin(p.Rotation);

            Vector2 Rotate(Vector2 v) => new(v.x * c - v.y * s, v.x * s + v.y * c);

            var v0 = center + Rotate(new Vector2(-half, -half));
            var v1 = center + Rotate(new Vector2(+half, -half));
            var v2 = center + Rotate(new Vector2(+half, +half));
            var v3 = center + Rotate(new Vector2(-half, +half));

            list.AddImageQuad(
                ImGuiManager.ImGuiPointerFor(p.Sprite),
                v0, v1, v2, v3,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                color
            );
        }

        var t = Mathf.Clamp01((float)(GameOverTime / ExplosionDuration));
        var messageAlpha = (byte)(255f * (1f - t * 0.55f));
        var textColor = ColorWithAlpha(255, 220, 180, messageAlpha);
        var boomColor = ColorWithAlpha(255, 120, 60, messageAlpha);

        if (Suffocated)
            DrawSkullMorph(p0, cellSize);

        var messageSize = ImGui.CalcTextSize(GameOverMessage);
        var bottomPos = p0 + new Vector2(W * cellSize - messageSize.x - CharWidth, H * cellSize - messageSize.y - 20 - 0.5f * LineHeight);
        if (!Suffocated)
            list.AddText(bottomPos, boomColor, EggText.Boom);
        list.AddText(bottomPos + new Vector2(0, 20), textColor, GameOverMessage);

        var centerX = p0.x + playSize.x * 0.5f;
        var centerY = p0.y + playSize.y * 0.18f;
        var banner = NumLives == 0 ? EggText.GameOver : string.Format(EggText.Respawn, NumLives, EggText.Hunter + (NumLives == 1 ? "" : "s"));
        var bannerSize = ImGui.CalcTextSize(banner);
        var bannerPos = new Vector2(centerX - bannerSize.x * 0.5f, centerY);
        list.AddText(bannerPos + new Vector2(1, 1), ColorWithAlpha(0, 0, 0, 180), banner);
        list.AddText(bannerPos, ColorWithAlpha(255, 210, 170, 255), banner);

        if (NumLives == 0)
        {
            var lines = new List<string> { string.Format(EggText.FinalScore, FinalScore.Total) };
            if (FinalScore.Total > 0 && FinalScore.Total >= EggStore.State.Highscore)
                lines.Add(EggText.NewHighscore);

            var lineY = centerY + 2 * LineHeightWithSpacing;
            foreach (var line in lines)
            {
                var lineSize = ImGui.CalcTextSize(line);
                var linePos = new Vector2(centerX - lineSize.x * 0.5f, lineY);
                list.AddText(linePos + new Vector2(1, 1), ColorWithAlpha(0, 0, 0, 180), line);
                list.AddText(linePos, ColorWithAlpha(255, 235, 200, 255), line);
                lineY += LineHeightWithSpacing;
            }

            if (UnlockedCharacterThisGame >= 0)
            {
                lineY += LineHeightWithSpacing;
                var unlockSize = ImGui.CalcTextSize(EggText.CharacterUnlocked);
                var unlockPos = new Vector2(centerX - unlockSize.x * 0.5f, lineY);
                list.AddText(unlockPos + new Vector2(1, 1), ColorWithAlpha(0, 0, 0, 180), EggText.CharacterUnlocked);
                list.AddText(unlockPos, NewUnlockColor(255), EggText.CharacterUnlocked);
                lineY += LineHeightWithSpacing * 1.5f;

                var portrait = 4f * cellSize;
                var portraitPos = new Vector2(centerX - portrait * 0.5f, lineY);
                var glow = new Vector2(6, 6);
                list.AddRectFilled(portraitPos - glow, portraitPos + new Vector2(portrait, portrait) + glow, NewUnlockColor((byte)(70 + 110 * Pulse)), 8f);
                list.AddImage(ImGuiManager.ImGuiPointerFor(CharacterTextures[UnlockedCharacterThisGame]), portraitPos, portraitPos + new Vector2(portrait, portrait));
                lineY += portrait + 0.5f * LineHeightWithSpacing;

                var name = CharacterNames[UnlockedCharacterThisGame];
                var nameSize = ImGui.CalcTextSize(name);
                var namePos = new Vector2(centerX - nameSize.x * 0.5f, lineY);
                list.AddText(namePos + new Vector2(1, 1), ColorWithAlpha(0, 0, 0, 180), name);
                list.AddText(namePos, ColorWithAlpha(255, 235, 200, 255), name);
            }
        }

        var launchReady = NumLives <= 0 && LaunchPending;
        var restartHint = launchReady ? EggText.Launch : NumLives > 0 ? EggText.NextRound : EggText.Restart;
        var hintSize = ImGui.CalcTextSize(restartHint);
        var hintPos = new Vector2(p0.x + playSize.x - hintSize.x - 8, p0.y + 8);
        list.AddText(hintPos, ColorWithAlpha(220, 235, 255, 240), restartHint);
    }

    // Heavy breathing once oxygen runs low, faster the emptier the tank; suit voice warnings at two levels.
    const double OxygenWarningLevel = 0.30;
    const double OxygenCriticalLevel = 0.15;
    double NextBreathTime = double.NegativeInfinity;
    bool BreathOut;
    GameAudioClipsData BreathInClips;
    GameAudioClipsData BreathOutClips;
    GameAudioClipsData GaspClips;

    // The breath sounds are audio events of the human prefab, not global clip data.
    static GameAudioClipsData HumanAudioEvent(string name)
    {
        foreach (var thing in Prefab.AllPrefabs)
            if (thing is Human human)
                foreach (var audioEvent in human.AudioEvents)
                    if (audioEvent.Name == name && audioEvent.ClipsData != null)
                        return audioEvent.ClipsData;
        return EggAudio.FindByName(name);
    }

    bool OxygenWarned;
    bool OxygenCriticalWarned;

    // An AudioSource caps at full volume, so loud clips are doubled up (+6 dB).
    static void PlayLoud(AudioClip clip, float pitch = 1f)
    {
        if (clip == null)
            return;
        EggAudio.PlayClip(clip, 2f, pitch);
        EggAudio.PlayClip(clip, 2f, pitch);
    }

    static void PlayLoud(GameAudioClipsData clips, float pitch = 1f)
    {
        if (clips == null || clips.Clips.Count == 0)
            return;
        PlayLoud(clips.Clips[UnityEngine.Random.Range(0, clips.Clips.Count)], pitch);
    }

    void UpdateBreathing(double now)
    {
        if (IsGameOver || Oxygen > OxygenWarningLevel)
            return;
        if (!OxygenWarned)
        {
            OxygenWarned = true;
            Toast(EggText.OxygenLow, 3);
            // the suit's voice line (current voice language), else the warning chime
            var voice = StatusUpdates.Instance?.OxygenWarning?.AudioAlert;
            if (voice != null)
                PlayLoud(voice);
            else
                EggAudio.Play("SFX_UI_Notify_WARNING", 2f);
        }
        if (!OxygenCriticalWarned && Oxygen <= OxygenCriticalLevel)
        {
            OxygenCriticalWarned = true;
            Toast(EggText.OxygenCritical, 3);
            EggAudio.Play("SFX_UI_Notify_CRITICAL", 2f);
            PlayLoud(StatusUpdates.Instance?.OxygenCritical?.AudioAlert);
        }
        if (now < NextBreathTime)
            return;
        var panic = (float)(1.0 - Oxygen / OxygenWarningLevel);
        PlayLoud(BreathOut ? BreathOutClips : BreathInClips, 1f + 0.15f * panic);
        BreathOut = !BreathOut;
        NextBreathTime = now + 1.4 - 0.9 * panic;
    }

    // Dimmed belts stay in place, the head crossfades into a skull.
    void DrawSkullMorph(Vector2 p0, float cellSize)
    {
        var list = ImGui.GetWindowDrawList();
        var tex = ImGuiManager.ImGuiPointerFor(HasBackpack ? Backpack : Tail);
        var cell = new Vector2(cellSize, cellSize);
        for (var i = 1; i < Snake.Count; i++)
        {
            var p = p0 + new Vector2(Snake[i].x, Snake[i].y) * cellSize;
            list.AddImage(tex, p + 0.1f * cell, p + 0.9f * cell, Vector2.zero, Vector2.one, GameOverFieldTint);
        }

        var scale = 1.6f;
        var shift = 0.5f * (scale - 1.0f);
        var pHead = p0 + (GameOverHeadCell - new Vector2(shift, shift)) * cellSize;
        var t = Mathf.Clamp01((float)(GameOverTime / SkullMorphDuration));
        DrawSprite(Head, pHead, cellSize * scale, Rotation, ColorWithAlpha(255, 255, 255, (byte)(255 * (1f - t))));
        DrawSprite(Skull, pHead, cellSize * scale, 0, ColorWithAlpha(255, 255, 255, (byte)(255 * t)));
    }

    public void UpdateAtmosphere(double dt)
    {
        LastTickTime = ImGui.GetTime();
        var oldPressure = Pressure;
        Atmosphere.TryCombust(0.3 * dt, true);
        Atmosphere.StateChange();
        var radiatedHeat = AtmosphereHelper.GetRadiatedHeat(
            Atmosphere,
            AtmosphericsController.ReadonlyGlobalAtmosphere(new Grid3(0, 0, 0)),
            10f,
            0.1f * AtmosphereHelper.NewAtmosSupressionMultiplier()
        );
        Atmosphere.GasMixture.AddEnergy(-radiatedHeat);

        if (IsValveOpen)
            Atmosphere.GasMixture.Scale(Math.Pow(0.9, dt));

        if (AutoVent)
        {
            var newPressure = Pressure;
            if (newPressure > 55000)
            {
                IsValveOpen = true;
                AutoVentState = 1;
            }
            if (newPressure < 50000)
            {
                IsValveOpen = false;
                AutoVentState = 0;
            }

            if (AutoVentState == 1 && newPressure < 50000)
            {
                IsValveOpen = false;
                AutoVentState = 0;
            }

            if (AutoVentState == 0 && newPressure > 55000)
            {
                IsValveOpen = true;
                AutoVentState = 1;
            }
        }
    }

    const double UncoverOreTime = 3.0;

    public void Update()
    {
        var now = ImGui.GetTime();
        // continuous parts (clock, atmosphere, smelting) use the frame time, the snake moves per tick
        var frameDt = Math.Min(0.1, now - LastFrameTime);
        LastFrameTime = now;
        var dt = now - LastUpdateTime;
        GameTime += frameDt;

        if (GameTime < UncoverOreTime)
        {
            LastUpdateTime = now;
            return;
        }

        if (IsGameOver)
        {
            GameOverTime += frameDt;
            // the oxygen clock pauses with the round
            OxygenStartTime += frameDt;
            UpdateExplosion(frameDt);
            LastUpdateTime = now;
            return;
        }

        LandFlyingOres(now);

        var updateRate = 1.0 / Speed;

        UpdateAtmosphere(frameDt);
        Smelt(frameDt);

        for (var i = 0; i < AlloyTargets.Length; i++)
            if (TargetReached(AlloyTargets[i].Prefab, AlloyTargets[i].Target) && ReachedTargets.Add(i))
            {
                var alloy = AlloyTargets[i].Prefab.Replace("Item", "").Replace("Ingot", "");
                Toast(EggText.AlloyTargetReached(alloy, EggText.AlloyParts[i], ReachedTargets.Count, AlloyTargets.Length), 5);
                if (ReachedTargets.Count == AlloyTargets.Length)
                    Toast(EggText.RocketCompleteToast, 5);
            }
        if (RocketComplete)
            UnlockAchievement(AchievementRocketBuilder, EggText.RocketBuilderTitle);

        UpdateBreathing(now);
        if (Oxygen <= 0)
        {
            GameOver(EggText.GameOverOxygen, explosion: false);
            PlayLoud(GaspClips);
            return;
        }

        if (dt <= updateRate)
            return;

        LastUpdateTime += updateRate;

        if (JumpAnimationPausesMovement)
        {
            if (IsJumpAnimationRunning(now))
            {
                LastUpdateTime = now;
                return;
            }

            JumpAnimationPausesMovement = false;
        }

        if (StuckTimeLeft > 0)
        {
            SetRotation(Random.Next(0, 4));
            StuckTimeLeft -= dt;
            if (StuckTimeLeft < 0)
            {
                StuckTimeLeft = 0;
                StuckProbability = 0;
                FinishBeingStuck();
            }
            return;
        }

        var head = Snake[0] + Velocity;
        if (IsOutsidePlayfield(head))
        {
            GameOver(EggText.GameOverCollision);
            return;
        }

        if (Snake.Contains(head) && !TryJumpOverTail(head, now, out head))
        {
            GameOver(EggText.GameOverCollision);
            return;
        }

        Snake.Insert(0, head);

        var value = Field[head.y, head.x];

        if (value == BackpackCell)
        {
            HasBackpack = true;
            OresPerBelt *= 2;
            EggAudio.Play(UIAudioManager.UiEquipBackHash);
        }
        else if (value == DrillCell)
        {
            DrillPickupsLeft += DrillPickups;
            EggAudio.Play(UIAudioManager.UiEquipBackHash);
        }
        else if (value == AirCell)
        {
            OxygenStartTime = GameTime;
            OxygenWarned = false;
            OxygenCriticalWarned = false;
            EggAudio.Play(UIAudioManager.UiEquipBackHash);
        }
        else if (value >= 0)
        {
            EggAudio.Play(UIAudioManager.UiEquipBeltHash);
            var yield = MiningYield;
            if (DrillPickupsLeft > 0)
            {
                DrillPickupsLeft--;
                yield *= 2;
            }
            if (Things[value] is Ore ore)
            {
                foreach (var gas in ore.SpawnContents)
                {
                    var mole = new Mole(gas.Type, gas.GetQuantity(), gas.GetEnergy());
                    mole.Scale(yield);
                    Atmosphere.GasMixture.Add(mole);
                }

                if (ore is not Ice)
                {
                    if (!FurnaceOres.ContainsKey(ore.PrefabName))
                        FurnaceOres[ore.PrefabName] = 0;
                    FurnaceOres[ore.PrefabName] += yield;
                }
            }
        }

        Field[head.y, head.x] = EmptyCell;
        TrimTail();

        if (value >= 0 && GetScore().BoardCleared)
            UnlockAchievement(AchievementCleanSweep, EggText.CleanSweepTitle);

        var p = Pressure;

        Stress += dt * (p - 60000.0) / 1000;
        Stress = Math.Max(0.0, Stress);
        EggAudio.SetPlaying("PipeDamage", Pressure > 50000);
        var stressLevel = Stress / MaxStress;
        EggAudio.SetPlaying("Alarm7", stressLevel > 0 && stressLevel <= 0.5);
        EggAudio.SetPlaying("Alarm4", stressLevel > 0.5);
        if (stressLevel > 1.0)
        {
            GameOver(EggText.GameOverExplosion, furnaceExploded: true);
            UnlockAchievement(AchievementHcf, EggText.HcfTitle);
        }

        if (selectedCharacter == 3)
        {
            StuckProbability += dt * Random.NextDouble() * 0.05;
            if (StuckProbability > 1.0)
                StuckTimeLeft = 2.0;
        }
    }

    // furnaceExploded: the furnace is gone, so the game ends regardless of lives left.
    public void GameOver(string msg, bool explosion = true, bool furnaceExploded = false)
    {
        if (IsGameOver)
            return;

        if (furnaceExploded && NumLives > 1)
            msg += "\n" + string.Format(EggText.OneFurnace, NumLives, EggText.Hunters);
        NumLives = furnaceExploded ? 0 : NumLives - 1;
        if (explosion)
            EggStore.State.FurnacesExploded++;
        if (NumLives <= 0)
        {
            FinalScore = GetScore();
            RecordScore(FinalScore.Total);
        }
        EggStore.Save();

        EggAudio.StopAll();
        if (UnlockedCharacterThisGame >= 0)
            EggAudio.Play("SFX_UI_PointOfInterestDiscovered");

        IsGameOver = true;
        Suffocated = !explosion;
        GameOverMessage = msg;
        GameOverTime = 0.0;
        GameOverHeadCell = Snake.Count > 0 ? new Vector2(Snake[0].x, Snake[0].y) : new Vector2(W / 2f, H / 2f);

        if (explosion)
            SpawnExplosionAtHead();

        Velocity = Vector2Int.zero;
        IsValveOpen = false;
    }

    public void SetRotation(int rotation)
    {
        var newVel = new Vector2Int(0, 0);
        switch (rotation)
        {
            case 0: newVel = new Vector2Int(1, 0); break;
            case 1: newVel = new Vector2Int(0, -1); break;
            case 2: newVel = new Vector2Int(-1, 0); break;
            case 3: newVel = new Vector2Int(0, 1); break;
        }

        if (rotation >= 0 && newVel != new Vector2Int(0, 0) && newVel != -LastVelocity)
        {
            Rotation = rotation;
            Velocity = newVel;
        }

    }

    public void HandleInput()
    {
        if (UnityEngine.Input.GetKey(KeyCode.H) && UnityEngine.Input.GetKey(KeyCode.C) && UnityEngine.Input.GetKey(KeyCode.F))
            DebugMenuOpen = true;

        // The cheater modal owns all input; closing the egg underneath it would leave the popup stuck open.
        if (CheaterDialogOpen && GameMode == CharacterSelectionMode)
            return;
        // The help and settings dialogs handle their own keys (Escape closes them, not the egg).
        if (HelpOpen || SettingsOpen)
            return;

        // Letter keys are not mapped to ImGuiKey in the game's backend, so use Unity input (like the HCF combo).
        if (GameMode != CeremonyMode && UnityEngine.Input.GetKeyDown(KeyCode.N) && !ImGui.GetIO().WantTextInput)
        {
            EggMusic.AutoAdvance = true;
            EggMusic.PlayRandom();
        }

        if (GameMode == CharacterSelectionMode && !ImGui.GetIO().WantTextInput)
            for (var i = 0; i < CharacterHotkeys.Length; i++)
                if (UnityEngine.Input.GetKeyDown(CharacterHotkeys[i]) || UnityEngine.Input.GetKeyDown(CharacterKeypadHotkeys[i]))
                    SelectCharacter(i);

        if (GameMode == IntroMode)
        {
            if ((ImGui.IsKeyPressed(ImGuiKey.Space) || IntroSkipped) && AssetsReady)
                EndIntro();
            else if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                Close();
            return;
        }

        if (GameMode == CeremonyMode)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape) || (Phase == CeremonyPhase.Announce && ImGui.IsKeyPressed(ImGuiKey.Space)))
                SkipCeremonyPhase();
            return;
        }

        if (IsGameOver)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                StartNewGame(InitialCode);
                return;
            }

            // Space skips the cooldown: next round, or a new game with the same character.
            if (ImGui.IsKeyPressed(ImGuiKey.Space))
            {
                if (NumLives > 0)
                    StartNewRound();
                // the rocket is only checked between games so the run can go on for a higher score
                else if (LaunchPending)
                    StartCeremony();
                else
                    StartPlaying();
                return;
            }

            if (GameOverTime >= RestartCooldown && NumLives > 0)
                StartNewRound();

            return;
        }

        var rotation = -1;

        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow)) rotation = 2;
        if (ImGui.IsKeyPressed(ImGuiKey.RightArrow)) rotation = 0;
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) rotation = 1;
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) rotation = 3;

        if (rotation != -1)
            SetRotation(rotation);

        if (ImGui.IsKeyDown(ImGuiKey.Space))
        {
            IsValveOpen = true;
            AutoVentState = 0;
        }
        else
        {
            if (AutoVentState == 0)
                IsValveOpen = false;
        }

        // Escape: from the game back to the title screen, from the title screen out of the egg.
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            if (GameMode == PlayingMode)
                StartNewGame(InitialCode);
            else
                Close();
        }
    }

    static float Pulse => 0.5f + 0.5f * Mathf.Sin((float)ImGui.GetTime() * 4f);
    static uint NewUnlockColor(byte alpha) => ColorWithAlpha(255, 200, 60, alpha);

    static Vector2 AchievementBadgeSize => new(Math.Max(ImGui.CalcTextSize("M").x * 9, 110), 76 + ImGui.GetTextLineHeightWithSpacing() * 2.3f);

    const float AchievementIconArea = 64f;
    static float AchievementContentTop => 8f + 0.3f * ImGui.GetTextLineHeight();

    // Icon callbacks get the badge rect; most only use the icon area at the top.
    static void IconArea(Vector2 p0, Vector2 p1, out Vector2 center, out float radius)
    {
        center = new Vector2((p0.x + p1.x) * 0.5f, p0.y + AchievementContentTop + AchievementIconArea * 0.5f);
        radius = AchievementIconArea * 0.5f;
    }

    // titleInIcon: the unlocked icon draws the whole badge including the title.
    void DrawAchievement(string id, string line1, string line2, string tooltip, bool unlocked, Action<ImDrawListPtr, Vector2, Vector2> icon, bool hidden = false, bool titleInIcon = false)
    {
        var isNew = NewAchievements.Contains(id);
        if (hidden && !unlocked)
        {
            line1 = "";
            line2 = "";
            tooltip = EggText.HiddenAchievementTooltip;
        }
        var size = AchievementBadgeSize;
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = p0 + size;
        ImGui.InvisibleButton("##achievement" + id, size);

        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered() && Settings.ShowTooltip)
            ImGui.SetTooltip(tooltip);

        var draw = ImGui.GetWindowDrawList();
        var hovered = ImGui.IsItemHovered();

        if (isNew)
            draw.AddRectFilled(p0 - new Vector2(6, 6), p1 + new Vector2(6, 6), NewUnlockColor((byte)(70 + 110 * Pulse)), 8f);
        draw.AddRectFilled(p0, p1, ColorWithAlpha(8, 8, 8, 255), 4f);
        var border = isNew ? NewUnlockColor(255) : hovered ? ColorWithAlpha(180, 180, 180, 255) : ColorWithAlpha(90, 90, 90, 255);
        draw.AddRect(p0, p1, border, 4f);

        IconArea(p0, p1, out var center, out var iconArea);
        iconArea *= 2f;
        if (unlocked)
        {
            icon(draw, p0, p1);
            if (titleInIcon)
                return;
        }
        else
        {
            var fontSize = ImGui.GetFontSize() * 2.6f;
            var q = ImGui.GetFont().CalcTextSizeA(fontSize, float.MaxValue, 0f, "?");
            draw.AddText(ImGui.GetFont(), fontSize, center - q * 0.5f, ColorWithAlpha(90, 90, 100, 255), "?");
        }

        var textColor = unlocked ? ColorWithAlpha(255, 235, 220, 255) : ColorWithAlpha(140, 140, 145, 255);
        var textY = p0.y + AchievementContentTop + 4f + iconArea;
        var size1 = ImGui.CalcTextSize(line1);
        var size2 = ImGui.CalcTextSize(line2);
        draw.AddText(new Vector2(p0.x + (size.x - size1.x) * 0.5f, textY), textColor, line1);
        draw.AddText(new Vector2(p0.x + (size.x - size2.x) * 0.5f, textY + 0.9f * ImGui.GetTextLineHeight()), textColor, line2);
    }

    static Vector2 Dir(float angle) => new(Mathf.Cos(angle), Mathf.Sin(angle));

    // KITT dashboard: three indicator LEDs over the big red TURBO BOOST button, spanning the whole badge.
    static void DrawTurboBoostIcon(ImDrawListPtr draw, Vector2 p0, Vector2 p1)
    {
        var size = p1 - p0;
        var ledRadius = 5.0f;
        var ledGap = 15.0f;
        // LEDs and button as in the original compact badge, centered vertically in the larger frame.
        var buttonHeight = 2 * ImGui.GetTextLineHeightWithSpacing() - 10f;
        var groupHeight = 2 * ledRadius + 0.8f * ledGap + buttonHeight;
        var top = p0.y + (size.y - groupHeight) * 0.5f;
        var ledStart = new Vector2(p0.x + (size.x - 2 * ledGap) * 0.5f, top + ledRadius);
        uint[] leds = [ColorWithAlpha(40, 220, 70, 255), ColorWithAlpha(240, 210, 40, 255), ColorWithAlpha(235, 35, 30, 255)];
        for (var i = 0; i < leds.Length; i++)
            draw.AddCircleFilled(ledStart + new Vector2(i * ledGap, 0), ledRadius, leds[i], 12);

        var buttonMin = new Vector2(p0.x + 1.3f * ImGui.CalcTextSize("M").x, top + 2.5f * ledRadius + 0.8f * ledGap);
        var buttonMax = new Vector2(p1.x - 1.3f * ImGui.CalcTextSize("M").x, buttonMin.y + buttonHeight + 0.5f * ledRadius);
        draw.AddRectFilled(buttonMin, buttonMax, ColorWithAlpha(160, 10, 8, 255));
        draw.AddRect(buttonMin, buttonMax, ColorWithAlpha(255, 95, 80, 255));

        var textColor = ColorWithAlpha(255, 235, 220, 255);
        var size1 = ImGui.CalcTextSize("TURBO");
        var size2 = ImGui.CalcTextSize("BOOST");
        var textY = (buttonMin.y + buttonMax.y) * 0.5f - 0.95f * ImGui.GetTextLineHeight();
        draw.AddText(new Vector2(p0.x + (size.x - size1.x) * 0.5f, textY), textColor, "TURBO");
        draw.AddText(new Vector2(p0.x + (size.x - size2.x) * 0.5f, textY + 0.9f * ImGui.GetTextLineHeight()), textColor, "BOOST");
    }

    // Cleared board: empty green grid with twinkling sparkles.
    // Flickering starburst with furnace fragments flying out.
    static void DrawHcfIcon(ImDrawListPtr draw, Vector2 p0, Vector2 p1)
    {
        IconArea(p0, p1, out var c, out var r);
        var t = (float)ImGui.GetTime();
        const int spikes = 12;
        var outer = new Vector2[spikes * 2];
        for (var i = 0; i < spikes * 2; i++)
        {
            var a = i * Mathf.PI / spikes + t * 0.6f;
            var flicker = 0.85f + 0.15f * Mathf.Sin(t * 9f + i * 1.7f);
            var rad = (i % 2 == 0 ? 0.95f : 0.5f) * r * flicker;
            outer[i] = c + Dir(a) * rad;
        }
        var glow = ColorWithAlpha(255, 90, 20, (byte)(90 + 60 * Pulse));
        draw.AddCircleFilled(c, r * 1.05f, glow, 32);
        // stars are not convex: fan them from the center
        for (var i = 0; i < outer.Length; i++)
            draw.AddTriangleFilled(c, outer[i], outer[(i + 1) % outer.Length], ColorWithAlpha(255, 150, 30, 255));
        var inner = new Vector2[spikes];
        for (var i = 0; i < spikes; i++)
            inner[i] = c + Dir(i * 2f * Mathf.PI / spikes - t * 0.9f) * r * (i % 2 == 0 ? 0.45f : 0.25f);
        for (var i = 0; i < inner.Length; i++)
            draw.AddTriangleFilled(c, inner[i], inner[(i + 1) % inner.Length], ColorWithAlpha(255, 240, 160, 255));

        // fragments on a repeating outward flight
        for (var i = 0; i < 6; i++)
        {
            var phase = (t * 0.7f + i * 0.17f) % 1f;
            var a = i * 1.05f + 0.4f;
            var p = c + Dir(a) * r * (0.3f + 0.75f * phase);
            var size = r * 0.13f * (1f - 0.5f * phase);
            var alpha = (byte)(255 * (1f - phase));
            var d1 = Dir(a + phase * 6f) * size;
            var d2 = new Vector2(-d1.y, d1.x);
            draw.AddQuadFilled(p + d1, p + d2, p - d1, p - d2, ColorWithAlpha(50, 50, 55, alpha));
        }
    }

    static void DrawCleanSweepIcon(ImDrawListPtr draw, Vector2 p0, Vector2 p1)
    {
        IconArea(p0, p1, out var c, out var r);
        const int cols = 4, rows = 3;
        var cell = r * 0.42f;
        var origin = c - new Vector2(cols, rows) * cell * 0.5f;
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < cols; x++)
            {
                var a = origin + new Vector2(x, y) * cell;
                draw.AddRectFilled(a + new Vector2(1, 1), a + new Vector2(cell - 1, cell - 1), ColorWithAlpha(20, 70, 30, 255));
                draw.AddRect(a + new Vector2(1, 1), a + new Vector2(cell - 1, cell - 1), ColorWithAlpha(60, 200, 90, 255));
            }
        var t = (float)ImGui.GetTime();
        Vector2[] sparkles = [new(-0.55f, -0.6f), new(0.6f, -0.1f), new(-0.1f, 0.65f)];
        for (var i = 0; i < sparkles.Length; i++)
        {
            var s = 0.5f + 0.5f * Mathf.Sin(t * 3f + i * 2.1f);
            var len = r * (0.18f + 0.22f * s);
            var p = c + sparkles[i] * r;
            var col = ColorWithAlpha(255, 255, 230, (byte)(120 + 135 * s));
            draw.AddLine(p - new Vector2(len, 0), p + new Vector2(len, 0), col, 1.5f);
            draw.AddLine(p - new Vector2(0, len), p + new Vector2(0, len), col, 1.5f);
            draw.AddCircleFilled(p, len * 0.25f, col, 8);
        }
    }

    // The finished rocket (without launch mount) hovering on a flickering flame.
    void DrawRocketBuilderIcon(ImDrawListPtr draw, Vector2 p0, Vector2 p1)
    {
        IconArea(p0, p1, out var c, out var r);
        var t = (float)ImGui.GetTime();
        var unit = 2f * r / 16f;
        var sway = Mathf.Sin(t * 1.3f) * 0.15f;
        var x = c.x + sway * unit;
        var bottom = c.y + r - 2.2f * unit;

        var flicker = 0.7f + 0.3f * Mathf.Sin(t * 23f) * Mathf.Sin(t * 7.1f);
        var flame = new Vector2(x, bottom + 0.8f * unit);
        DrawEllipseGlow(draw, flame, new Vector2(1.6f, 2.6f * flicker) * unit, ColorWithAlpha(255, 150, 40, 255), 200);
        DrawEllipseGlow(draw, flame, new Vector2(0.8f, 1.6f * flicker) * unit, ColorWithAlpha(255, 240, 180, 255), 230);

        for (var i = 1; i < RocketParts.Count; i++)
        {
            var partBottom = bottom - (i - 1) * RocketPartHeight * unit;
            DrawRocketPart(i, new Vector2(x, partBottom - RocketPartWidth * unit * 0.5f), RocketPartWidth * unit, sway * 0.08f);
        }
    }

    // Spinning wall fan; the superfan one is golden, five-bladed and glows.
    static void DrawFanIcon(ImDrawListPtr draw, Vector2 p0, Vector2 p1, int blades, bool gold)
    {
        IconArea(p0, p1, out var c, out var r);
        var t = (float)ImGui.GetTime();
        var bladeColor = gold ? ColorWithAlpha(255, 200, 60, 255) : ColorWithAlpha(150, 170, 200, 255);
        var cageColor = gold ? ColorWithAlpha(255, 230, 150, 255) : ColorWithAlpha(110, 115, 125, 255);
        if (gold)
            DrawEllipseGlow(draw, c, new Vector2(r * 1.3f, r * 1.3f), bladeColor, (byte)(60 + 50 * Pulse));
        var rot = t * (gold ? 9f : 4f);
        for (var k = 0; k < blades; k++)
        {
            var a = rot + k * 2f * Mathf.PI / blades;
            var hubA = c + Dir(a - 0.7f) * r * 0.2f;
            var hubB = c + Dir(a + 0.7f) * r * 0.2f;
            var tipA = c + Dir(a - 0.32f) * r * 0.78f;
            var tipB = c + Dir(a + 0.32f) * r * 0.78f;
            draw.AddQuadFilled(hubA, tipA, tipB, hubB, bladeColor);
            draw.AddQuad(hubA, tipA, tipB, hubB, WithAlpha(cageColor, 160), 1f);
        }
        draw.AddCircleFilled(c, r * 0.2f, cageColor, 16);
        draw.AddCircle(c, r * 0.92f, cageColor, 32, 2f);
        for (var k = 0; k < 4; k++)
        {
            var a = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            draw.AddLine(c + Dir(a) * r * 0.22f, c + Dir(a) * r * 0.9f, WithAlpha(cageColor, 110), 1f);
        }
    }

    static float SectionTitleHeight(float scale = 1.3f) => ImGui.GetTextLineHeight() * (scale + 0.6f);

    // Section heading, centered over `width` at the current cursor.
    void DrawSectionTitle(string text, float width, float scale = 1.3f)
    {
        var draw = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize() * scale;
        var textSize = ImGui.CalcTextSize(text) * scale;
        var pos = ImGui.GetCursorScreenPos() + new Vector2((width - textSize.x) * 0.5f, 0);
        draw.AddText(font, fontSize, pos, ColorWithAlpha(230, 235, 245, 255), text);
        ImGui.Dummy(new Vector2(width, SectionTitleHeight(scale)));
    }

    string MenuHint = "";
    string[] MenuHints =>
        [string.Format(EggText.HintOxygen, OxygenDuration), .. EggText.Hints.Select(h => string.Format(h, DefaultOresPerBelt))];

    string selectedPerks = "";
    int selectedCharacter = 0;

    void SelectCharacter(int i)
    {
        if (!IsUnlocked(i))
            return;
        selectedCharacter = i;
        Head = CharacterTextures[i];
        selectedPerks = EggText.CharacterPerks[i];
    }

    static readonly KeyCode[] CharacterHotkeys = [KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5];
    static readonly KeyCode[] CharacterKeypadHotkeys = [KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5];
    public void DrawCharacterSelection()
    {
        var vp = ImGui.GetMainViewport();
        var w = vp.Size.x;
        var h = vp.Size.y;
        var pad = 0.02f * h;

        var lineHeight = LineHeightWithSpacing;
        var charWidth = CharWidth;

        const int numCharacters = 5;

        var names = CharacterNames;
        var tex = CharacterTextures;
        var requiredScore = CharacterRequiredScore;
        var perks = EggText.CharacterPerks;

        var avail = ImGui.GetContentRegionAvail();
        var img = Math.Min((avail.x - pad * (numCharacters - 1)) / numCharacters, Math.Max(150f, 14 * charWidth));

        ImGui.SetCursorPosY(0.03f * h);

        DrawSectionTitle(EggText.SelectTitle, avail.x, 2.0f);

        foreach (var line in EggText.Story)
        {
            ImGui.SetCursorPosX((avail.x - ImGui.CalcTextSize(line).x) * .5f);
            ImGui.TextColored(Vec4(ColorWithAlpha(200, 205, 220, 255)), line);
        }

        ImGui.Dummy(new Vector2(0, 0.02f * h));

        var totalW = img * numCharacters + pad * 3;
        ImGui.SetCursorPosX((avail.x - totalW) * .5f);

        for (var i = 0; i < numCharacters; i++)
        {
            ImGui.BeginGroup();
            if (i == selectedCharacter)
            {
                var col = new Vector4(0.33f, 0.57f, 0.93f, 1.00f);
                ImGui.PushStyleColor(ImGuiCol.Button, col);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, col);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, col);
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(.18f, .18f, .18f, 1));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.22f, .22f, .22f, 1));
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.12f, .12f, .12f, 1));
            }

            var disabled = !IsUnlocked(i);
            if (NewCharacters.Contains(i))
            {
                var bp = ImGui.GetCursorScreenPos();
                var bsize = new Vector2(img, img) + 2 * ImGui.GetStyle().FramePadding;
                ImGui.GetWindowDrawList().AddRectFilled(bp - new Vector2(8, 8), bp + bsize + new Vector2(8, 8), NewUnlockColor((byte)(70 + 110 * Pulse)), 10f);
            }
            if (disabled)
            {
                ImGui.Button(EggText.Locked, new Vector2(img, img));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(string.Format(EggText.UnlockedAt, requiredScore[i]));
            }
            else
            {
                if (ImGui.ImageButton(ImGuiManager.ImGuiPointerFor(tex[i]), new Vector2(img, img)))
                    SelectCharacter(i);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(string.Format(EggText.CharacterTooltip, i + 1));

                var tw = ImGui.CalcTextSize(names[i]).x;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (img - tw) * .5f);
                ImGui.Text(names[i]);
            }

            ImGui.EndGroup();

            if (i != numCharacters - 1)
                ImGui.SameLine(0, pad);

            ImGui.PopStyleColor(3);

        }

        // Perks block (tall enough for the longest one), separator, hint and the progress/achievements
        // row share the space down to the bottom bar; the gaps around the perks are half the others.
        var half = avail.x * 0.5f;
        var cell = Mathf.Clamp((half - 6 * charWidth) / AlloyTargets.Length, Math.Max(104f, 10 * charWidth), 150f);
        var badge = AchievementBadgeSize;
        var gap = 2 * charWidth;
        var badgesWidth = 6 * badge.x + 5 * gap;
        var rowHeight = SectionTitleHeight() + Math.Max(cell * 0.58f + cell * 0.23f + 8f, badge.y);
        var perksHeight = (perks.Max(s => s.Split('\n').Length) + 0.5f) * lineHeight;
        var separatorHeight = ImGui.GetStyle().ItemSpacing.y * 2 + 1f;
        var bottomY = vp.WorkSize.y - 0.07f * h;
        var free = bottomY - ImGui.GetCursorPosY() - perksHeight - separatorHeight - lineHeight - rowHeight - lineHeight;
        var space = Math.Max(0.01f * h, free / 2.5f);

        ImGui.Dummy(new Vector2(0, space * 0.5f));
        var x0 = (avail.x - charWidth * 60) * .5f;
        var y0 = ImGui.GetCursorPosY() + perksHeight;
        foreach (var line in selectedPerks.Split('\n'))
        {
            ImGui.SetCursorPosX(x0);
            ImGui.Text(line);
        }
        ImGui.SetCursorPosY(y0);

        ImGui.Separator();

        ImGui.Dummy(new Vector2(0, space));
        var hint = string.Format(EggText.HintFormat, MenuHint);
        ImGui.SetCursorPosX((avail.x - ImGui.CalcTextSize(hint).x) * .5f);
        ImGui.TextColored(Vec4(ColorWithAlpha(200, 205, 220, 255)), hint);
        ImGui.Dummy(new Vector2(0, space));

        var rowY = ImGui.GetCursorPosY();

        // Left half: rocket progress
        var progressX = Math.Max(2 * charWidth, (half - cell * AlloyTargets.Length) * 0.5f);
        ImGui.SetCursorPos(new Vector2(progressX, rowY));
        ImGui.BeginGroup();
        DrawSectionTitle(EggText.RocketProgressTitle, cell * AlloyTargets.Length);
        DrawRocketProgress(cell);
        ImGui.EndGroup();

        // Right half: achievements
        ImGui.SetCursorPos(new Vector2(half + Math.Max(0, (half - badgesWidth) * 0.5f), rowY));
        ImGui.BeginGroup();
        DrawSectionTitle(EggText.AchievementsTitle, badgesWidth);
        DrawAchievement(AchievementRocketBuilder, "ROCKET", "BUILDER", EggText.RocketBuilderTooltip, HasAchievement(AchievementRocketBuilder), DrawRocketBuilderIcon);
        ImGui.SameLine(0, gap);
        DrawAchievement(AchievementHcf, "HALT AND", "CATCH FIRE", EggText.HcfTooltip, HasAchievement(AchievementHcf), DrawHcfIcon);
        ImGui.SameLine(0, gap);
        DrawAchievement(AchievementTurboBoost, "TURBO", "BOOST", EggText.TurboBoostTooltip, HasAchievement(AchievementTurboBoost), DrawTurboBoostIcon, hidden: true, titleInIcon: true);
        ImGui.SameLine(0, gap);
        DrawAchievement(AchievementCleanSweep, "CLEAN", "SWEEP", EggText.CleanSweepTooltip, HasAchievement(AchievementCleanSweep), DrawCleanSweepIcon, hidden: true);
        ImGui.SameLine(0, gap);
        DrawAchievement(AchievementFan, "FAN", "", EggText.FanTooltip, HasAchievement(AchievementFan), (d, a, b) => DrawFanIcon(d, a, b, 3, false), hidden: true);
        ImGui.SameLine(0, gap);
        DrawAchievement(AchievementSuperFan, "SUPER", "FAN", EggText.SuperFanTooltip, HasAchievement(AchievementSuperFan), (d, a, b) => DrawFanIcon(d, a, b, 5, true), hidden: true);
        ImGui.EndGroup();

        DrawToasts(new Vector2(ImGui.GetWindowPos().x + avail.x * 0.5f, ImGui.GetWindowPos().y + 0.12f * h));

        ImGui.SetCursorPosY(bottomY);
        ImGui.Separator();



        ImGui.Text(string.Format(EggText.Highscore, EggStore.State.Highscore));

        var menuButton = new Vector2(120, 40);
        var menuStep = menuButton.x + 10f;
        var startX = avail.x - menuButton.x - 20f;

        // Help and settings in the center, scenes and start on the right.
        ImGui.SameLine((avail.x - 2 * menuStep + 10f) * 0.5f);
        if (ImGui.Button(EggText.MenuHelp, menuButton))
            HelpOpen = true;
        ImGui.SameLine();
        if (ImGui.Button(EggText.MenuSettings, menuButton))
            SettingsOpen = true;

        ImGui.SameLine(startX - (RocketComplete ? 3 : 1) * menuStep);
        if (ImGui.Button(EggText.MenuIntro, menuButton))
            StartIntro(manual: true);
        if (RocketComplete)
        {
            ImGui.SameLine();
            if (ImGui.Button(EggText.MenuParty, menuButton))
                StartCeremony();
            ImGui.SameLine();
            if (ImGui.Button(EggText.MenuCredits, menuButton))
            {
                StartCeremony();
                SetPhase(CeremonyPhase.Credits);
            }
        }

        ImGui.SameLine(startX);
        if (ImGui.Button(EggText.Start, menuButton) || (ImGui.IsKeyPressed(ImGuiKey.Enter) && !CheaterDialogOpen && !HelpOpen && !SettingsOpen))
            StartPlaying();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(EggText.StartTooltip);
    }

    // New game with the selected character, straight into the playfield.
    void StartPlaying()
    {
        StartNewGame(InitialCode);
        GameMode = PlayingMode;
        EggMusic.AutoAdvance = true;
        if (EggMusic.Current == null || EggMusic.Current == EggMusic.SpaceMusic)
            EggMusic.PlayRandom();
    }

    public void DrawGame()
    {
        var viewport = ImGui.GetMainViewport();
        var style = ImGui.GetStyle();

        var frameSize = 2 * (style.FramePadding + style.ItemSpacing);
        var grid = (viewport.Size - new Vector2(31 * CharWidth, 0) - frameSize) / new Vector2(W, H);
        gridSize = Mathf.Floor(Math.Min(grid.x, grid.y));

        var w = gridSize;
        var h = gridSize;
        var offsetY = -(h - CharWidth) / 2 / h;
        var x = new Vector2(w, 0);
        var y = new Vector2(0, h);

        var playSize = new Vector2(W * w, H * h) + frameSize;
        var pt0 = ImGui.GetCursorPos();

        ImGui.Indent(playSize.x + 10);
        var panelWidth = ImGui.GetContentRegionAvail().x;

        DrawFurnace(panelWidth);

        if (selectedCharacter == 3)
        {
            var sinceJump = ImGui.GetTime() - LastTailJumpTime;
            var jumpReady = Math.Min(1.0, sinceJump / TailJumpCooldown);
            DrawBar(EggText.BarJump, jumpReady >= 1.0 ? EggText.JumpReady : $"{TailJumpCooldown - sinceJump:F1} s", jumpReady,
                RampColor(jumpReady, (0.0, Orange), (0.6, Yellow), (1.0, Green)));
        }
        else
            DrawBar(EggText.BarOxygen, $"{Oxygen * 100:F0} %", Oxygen,
                RampColor(Oxygen, (0.1, Red), (0.3, Orange), (0.6, Yellow), (1.0, Green)));

        var stressLevel = Stress / MaxStress;
        DrawBar(EggText.BarStress, $"{Stress:F0} / {MaxStress:F0}", stressLevel,
            RampColor(stressLevel, (0.0, Green), (0.5, Yellow), (0.75, Orange), (1.0, Red)));

        var pressureMPa = Pressure / 1000.0;
        DrawBar(EggText.BarPressure, $"{pressureMPa:F1} MPa", pressureMPa / 100.0,
            RampColor(pressureMPa, (50, Green), (55, Yellow), (60, Orange), (70, Red)));

        DrawBar(EggText.BarTemperature, $"{Temperature:F0} K", Temperature / 2500.0,
            RampColor(Temperature,
                (0, ColorWithAlpha(40, 60, 120, 255)),
                (300, ColorWithAlpha(70, 70, 80, 255)),
                (700, ColorWithAlpha(130, 15, 0, 255)),
                (1200, ColorWithAlpha(170, 60, 10, 255)),
                (1800, ColorWithAlpha(190, 110, 20, 255)),
                (2500, ColorWithAlpha(200, 150, 40, 255))));

        DrawBar(EggText.BarSmeltSpeed, $"{SmeltingSpeed:F2}", SmeltingSpeed * 2,
            RampColor(SmeltingSpeed, (0.1, Red), (0.2, Yellow), (0.3, Green)));

        DrawBar(EggText.BarSmeltEfficiency, $"{SmeltingEfficiency:F2}", (SmeltingEfficiency - 1.0) / 3.0,
            RampColor(SmeltingEfficiency, (1.0, Red), (2.0, Yellow), (3.0, Green)));

        var totalMoles = Atmosphere.GasMixture.GetTotalMolesGassesAndLiquids.ToDouble();
        var fuel = Atmosphere.GasMixture.TotalFuel.ToDouble();
        var oxidizer = Atmosphere.GasMixture.TotalOxidiser.ToDouble();
        DrawBar(EggText.BarFuel, $"{fuel:F0} mol", totalMoles > 0 ? fuel / totalMoles : 0, ColorWithAlpha(240, 110, 30, 255));
        DrawBar(EggText.BarOxidizer, $"{oxidizer:F0} mol", totalMoles > 0 ? oxidizer / totalMoles : 0, ColorWithAlpha(60, 130, 240, 255));

        // space stays reserved so the panel does not jump when the drill runs out
        if (DrillPickupsLeft > 0)
            DrawBar(EggText.BarDrill, $"{DrillPickupsLeft}", (double)DrillPickupsLeft / DrillPickups, ColorWithAlpha(200, 160, 40, 255));
        else
            ImGui.Dummy(new Vector2(0, BarHeight + 4));

        ImGui.Dummy(new Vector2(0, 0.3f * h));
        var ptOre = ImGui.GetCursorScreenPos();
        var oresBottom = DrawOres(ptOre);
        DrawAlloys(new Vector2(ptOre.x, oresBottom + 0.4f * h));

        ImGui.Unindent(playSize.x + 10);

        Update();

        ImGui.SetCursorPos(pt0);
        ImGui.BeginChild("playfield", playSize, true);

        var p0 = ImGui.GetCursorScreenPos();
        var list = ImGui.GetWindowDrawList();
        var font = ImGui.GetIO().Fonts.Fonts[0];

        for (var i = 0; i < H; i++)
        {
            var p = p0 + i * y;
            var drawImages = GameTime / UncoverOreTime * H > i;
            // dimmed after the round so the game over texts stay readable
            var tint = IsGameOver ? GameOverFieldTint : 0xFFFFFFFF;
            for (var j = 0; j < W; j++)
            {
                var e = Field[i, j];
                if (drawImages)
                {
                    if (e >= 0)
                        list.AddImage(ImGuiManager.ImGuiPointerFor(OreSprites[e]), p, p + x + y, Vector2.zero, Vector2.one, tint);
                    else if (e == BackpackCell)
                        list.AddImage(ImGuiManager.ImGuiPointerFor(Backpack), p, p + x + y, Vector2.zero, Vector2.one, tint);
                    else if (e == DrillCell)
                        list.AddImage(ImGuiManager.ImGuiPointerFor(Drill), p, p + x + y, Vector2.zero, Vector2.one, tint);
                    else if (e == AirCell)
                        list.AddImage(ImGuiManager.ImGuiPointerFor(AirCanister), p, p + x + y, Vector2.zero, Vector2.one, tint);
                }
                else if (Code[i, j] != ' ')
                {
                    list.AddText(font, 30, p + offsetY * y, IsGameOver ? CodeColors[i, j] & 0xFF000000 | GameOverFieldTint & 0x00FFFFFF : CodeColors[i, j], Code[i, j].ToString());
                }

                p += x;
            }
        }


        if (!IsGameOver)
        {
            DrawFlyingOres(list, p0, x, y);

            var scale = 1.6f;
            var headCell = new Vector2(Snake[0].x, Snake[0].y);
            var jumpLift = 0.0f;
            var jumpLiftScale = 0.9f;
            var jumpT = (float)((ImGui.GetTime() - TailJumpAnimationStartTime) / CurrentJumpAnimationDuration);
            if (jumpT >= 0.0f && jumpT < 1.0f)
            {
                var easedT = SmootherStep(jumpT);
                var jumpDistance = Vector2.Distance(TailJumpFrom, TailJumpTo);
                headCell = Vector2.Lerp(TailJumpFrom, TailJumpTo, easedT);
                jumpLift = Mathf.Sin(easedT * Mathf.PI);
                jumpLiftScale = Mathf.Clamp(0.9f + 0.04f * jumpDistance, 0.9f, 2.1f);
                scale += Mathf.Clamp(0.35f + 0.015f * jumpDistance, 0.35f, 0.55f) * jumpLift;
            }

            var shift = 0.5f * (scale - 1.0f);
            var pHead = p0 + (headCell.y - shift) * y + (headCell.x - shift) * x - jumpLift * jumpLiftScale * y;
            DrawSprite(Head, pHead, w * scale, Rotation); // && (Rotation % 2 == 0));
            var tex = ImGuiManager.ImGuiPointerFor(HasBackpack ? Backpack : Tail);
            for (var i = 1; i < Snake.Count; i++)
            {
                scale = 0.8f;
                shift = 0.5f * (1.0f - scale);
                var p = p0 + Snake[i].y * y + Snake[i].x * x;
                list.AddImage(tex, p + shift * (x + y), p + (1.0f - shift) * (x + y));
            }
        }
        else
        {
            DrawExplosion(p0, w, playSize);
        }
        DrawToasts(p0 + new Vector2(playSize.x * 0.5f, 10));
        ImGui.EndChild();

    }

    public void Draw()
    {
        if (!IsOpen)
            return;

        // The egg ignores the editor's UI scaling.
        using var _fontScale = new ScopedFontScale(1f);

        EggAudio.Update();
        if (SpaceMusicPending && !(DiscoveredVoice?.IsPlaying ?? false))
        {
            SpaceMusicPending = false;
            EggMusic.AutoAdvance = false;
            EggMusic.Play(EggMusic.SpaceMusic);
        }
        EggMusic.Update();
        Synth.Update();

        var viewport = ImGui.GetMainViewport();

        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);


        var flags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoSavedSettings;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);

        // keep the egg above the editor window; while helper windows are open they refocus themselves instead
        var cheaterOpen = CheaterDialogOpen && GameMode == CharacterSelectionMode;
        var helperWindowOpen = DebugMenuOpen || ShowSoundBrowser || ShowAudioControl || cheaterOpen || HelpOpen || SettingsOpen;
        if (!helperWindowOpen)
            ImGui.SetNextWindowFocus();

        ImGui.Begin(EggText.WindowTitle, flags);
        ImGui.PopStyleVar(2);

        var windowPos = ImGui.GetWindowPos();
        var shake = GameMode == PlayingMode ? GetShakeOffset() : Vector2.zero;
        ImGui.SetWindowPos(windowPos + shake);

        switch (GameMode)
        {
            case CharacterSelectionMode:
                DrawCharacterSelection();
                break;
            case CeremonyMode:
                DrawCeremony();
                break;
            case IntroMode:
                if (IntroSkipped)
                    DrawLoadingScreen();
                else
                    DrawIntro();
                break;
            default:
                DrawGame();
                break;
        }
        HandleInput();
        DrawNowPlaying();

        ImGui.End();

        // Helper windows stay on top of the fullscreen egg (unless a text field is being edited).
        // Focusing a window closes popups, so never refocus while the cheater modal is open.
        var keepOnTop = !ImGui.IsAnyItemActive() && !cheaterOpen;
        if (ShowAudioControl)
        {
            if (keepOnTop) ImGui.SetNextWindowFocus();
            Synth.DrawAudioControl();
        }
        if (ShowSoundBrowser)
        {
            if (keepOnTop) ImGui.SetNextWindowFocus();
            EggAudio.DrawSoundBrowser();
        }
        if (DebugMenuOpen)
        {
            if (keepOnTop) ImGui.SetNextWindowFocus();
            DrawDebugMenu();
        }
        if (cheaterOpen)
            DrawCheaterDialog();
        if (HelpOpen)
            DrawHelpDialog();
        if (SettingsOpen)
            DrawSettingsDialog();
    }

    public void DrawSprite(Texture2D sprite, Vector2 pos, float size, int rotations = 0, uint tint = 0xFFFFFFFF)
    {
        var x = new Vector2(size, 0);
        var y = new Vector2(0, size);
        Vector2[] vpos = [pos, pos + x, pos + x + y, pos + y];
        var shift = 4 - rotations;
        vpos = [vpos[(0 + shift) % 4], vpos[(1 + shift) % 4], vpos[(2 + shift) % 4], vpos[(3 + shift) % 4]];
        if (rotations == 2)
            vpos = [vpos[3], vpos[2], vpos[1], vpos[0]];
        var im = ImGui.GetWindowDrawList();
        im.AddImageQuad(ImGuiManager.ImGuiPointerFor(sprite), vpos[0], vpos[1], vpos[2], vpos[3],
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), tint);
    }

    public void Close()
    {
        Synth.Stop();
        EngineVoice?.Stop();
        EngineStartVoice?.Stop();
        EggMusic.Stop();
        EggMusic.AutoAdvance = true;
        SaveRocketProgress();
        EggAudio.StopAll(includeMusic: true);
        RestoreMusicSetting();
        IsOpen = false;
    }

    // The egg music runs on the game's music bus; a muted music setting is lifted while the egg is open.
    static int? SavedMusicVolume;
    const int EggMusicVolume = 100;

    static void EnsureMusicEnabled()
    {
        var data = Assets.Scripts.Serialization.Settings.CurrentData;
        if (data == null || data.MusicVolume > 0 || SavedMusicVolume.HasValue)
            return;
        try
        {
            SavedMusicVolume = data.MusicVolume;
            data.MusicVolume = EggMusicVolume;
            Assets.Scripts.Serialization.Settings.ApplyVolumeSetting(SettingType.MusicVolume);
            L.Debug($"Egg: music volume raised from {SavedMusicVolume} to {EggMusicVolume}");
        }
        catch (Exception e)
        {
            L.Debug($"Egg: could not change the music volume: {e.Message}");
        }
    }

    static void RestoreMusicSetting()
    {
        if (!SavedMusicVolume.HasValue)
            return;
        try
        {
            Assets.Scripts.Serialization.Settings.CurrentData.MusicVolume = SavedMusicVolume.Value;
            Assets.Scripts.Serialization.Settings.ApplyVolumeSetting(SettingType.MusicVolume);
        }
        catch (Exception e)
        {
            L.Debug($"Egg: could not restore the music volume: {e.Message}");
        }
        SavedMusicVolume = null;
    }
}

public static class TextureCache
{
    static readonly string CacheDir = Path.Combine(EggAssets.AssetsDir, "textures");

    public static async UniTask<Texture2D> LoadTextureFromURL(string encodedUrl, bool flipX = false, int x0 = 0, int y0 = 0, int width = 0, int height = 0, bool circular = true)
    {
        Directory.CreateDirectory(CacheDir);

        var url = Encoding.UTF8.GetString(Convert.FromBase64String(encodedUrl));
        var cachePath = GetCachePath(url);

        // The processed result is cached too, so a warm start only decodes the final image.
        var crop = x0 != 0 || y0 != 0 || width != 0 || height != 0;
        var processedPath = crop || circular || flipX ? GetCachePath($"{url}|{x0},{y0},{width},{height}|{circular}|{flipX}") : null;
        if (processedPath != null && File.Exists(processedPath))
            return LoadPng(File.ReadAllBytes(processedPath));

        byte[] pngData;
        if (File.Exists(cachePath))
            pngData = File.ReadAllBytes(cachePath);
        else
        {
            using var request = UnityWebRequestTexture.GetTexture(url);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Texture download failed: {request.error}");
                return null;
            }

            pngData = DownloadHandlerTexture.GetContent(request).EncodeToPNG();
            File.WriteAllBytes(cachePath, pngData);
        }
        var texture = LoadPng(pngData);
        L.Debug($"Loaded texture from {url}, size {texture.width}x{texture.height}");
        if (processedPath == null)
            return texture;

        var pixels = crop ? texture.GetPixels(x0, y0, width, height) : texture.GetPixels();
        if (crop)
        {
            UnityEngine.Object.Destroy(texture);
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        }

        if (circular)
        {
            // make everything outside the largest centered circle transparent
            var size = Mathf.Min(texture.width, texture.height);
            var center = new Vector2(size / 2f, size / 2f);
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width; x++)
                {
                    var idx = y * texture.width + x;
                    if (Vector2.Distance(new Vector2(x, y), center) > size / 2f)
                    {
                        var c = pixels[idx];
                        pixels[idx] = new Color(c.r, c.g, c.b, 0);
                    }
                }
            }
        }

        if (flipX)
        {
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width / 2; x++)
                {
                    var idx1 = y * texture.width + x;
                    var idx2 = y * texture.width + (texture.width - 1 - x);
                    var temp = pixels[idx1];
                    pixels[idx1] = pixels[idx2];
                    pixels[idx2] = temp;
                }
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        File.WriteAllBytes(processedPath, texture.EncodeToPNG());

        return texture;
    }

    // Always build the texture the same way (no mipmaps, RGBA32); the downloaded texture
    // object has a different setup and rendered blurry.
    static Texture2D LoadPng(byte[] pngData)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(pngData);
        return texture;
    }

    static string GetCachePath(string url)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
        var fileName = Convert.ToBase64String(hash).Replace('/', '_').Replace('+', '-') + ".png";
        return Path.Combine(CacheDir, fileName);
    }
}
