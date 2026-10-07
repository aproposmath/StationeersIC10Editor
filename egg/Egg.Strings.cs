namespace StationeersIC10Editor;

using System.Collections.Generic;

// All user-visible texts of the egg in one place.
public static class EggText
{
    public const string WindowTitle = "This is fine!";

    // Intro
    public const string IntroSkip = "Space: skip";
    public const string IntroLoading = "Setting things up... {0}";
    public static readonly string[] IntroLines =
    [
        "Well. That went great.",
        "No signal. No base. Nobody around.",
        "I have to find the others...",
        "...and build a new rocket to get off this rock.",
    ];

    // Character selection
    public const string SelectTitle = "Select Your Character";
    public static readonly string[] Story =
    [
        "Stranded on a lonely planet with a furnace, a few mining belts and no friends in sight.",
        "Smelt the alloys for a rocket and get everybody off this rock.",
    ];
    public static readonly string[] CharacterNames = ["Stationeer", "SemlerPDX", "Dean", "Aimee", "Hunter"];
    public static readonly string[] CharacterPerks =
    [
        "",
        "The Schmitt Trigger - Automatic Vent Control\n  - opens furnace vent when pressure reaches 55MPa\n  - closes it when dropping below 50MPa",
        "Stress-Tested Entrepreneur\n - Furnace tolerates up to 500 percent stress (it's fine)\n   -> enables ultra-high-pressure smelting for increased speed and efficiency\n - Owns alloy distributor Alloo\n   -> 30%% bonus points when smelting alloys",
        "- Double mining yield\n- Can jump over ore belts (once every 2 seconds)\n- Occasionally gets stuck, jumps away, and drops ores\n- No oxygen consumption",
        "- Nine lives",
    ];
    public const string DjName = "JacksonTheMaster";
    public const string Locked = "Locked";
    public const string UnlockedAt = "Reach {0} points with the previous character to unlock";
    public const string HintFormat = "Hint: {0}";
    public const string HintOxygen = "Oxygen lasts {0} minutes. Belts make you faster.";
    public static readonly string[] Hints =
    [
        "The furnace will explode at 100%% stress!",
        "Open the vent (hold space) to reduce furnace pressure",
        "Alloys are smelted automatically when temperature and pressure are high enough",
        "Smelting speed and efficiency scale with pressure and temperature",
        "Get 100%% bonus points for clearing the whole board of ores and ices",
        "Each mining belt holds {0} ores. Smelting frees them up again",
        "The more belts/backpacks you have, the faster you move",
        "Pick up the mining backpack to double the capacity per belt",
        "The ore distribution on the game field depends on your IC10 code",
        "Press N to skip to the next song",
        "Press '+' to speed up scenes (intro, outro, credits)",
        "Picking up a heavy mining drill doubles your ore yield for a short time",
    ];
    public const string Highscore = "Highscore: {0} points";
    public const string Start = "Start";
    public const string TurboBoostTooltip = "Turbo Boost: have Aimee jump 8 times in one game";
    public const string TurboBoostTitle = "Turbo Boost";
    public const string CleanSweepTooltip = "Clean Sweep: clear the whole board of ores and ices";
    public const string CleanSweepTitle = "Clean Sweep";
    public const string AchievementUnlocked = "Achievement unlocked: {0}";
    public const string FanTitle = "Fan";
    public const string FanTooltip = "Fan: two mods by aproposmath loaded";
    public const string SuperFanTitle = "Superfan";
    public const string SuperFanTooltip = "Superfan: three mods by aproposmath loaded";
    public const string RocketBuilderTitle = "Rocket Builder";
    public const string RocketBuilderTooltip = "Rocket Builder: smelt all alloys needed for the rocket";
    public const string HiddenAchievementTooltip = "Hidden achievement";

    // In game
    public const string Score = "Score: {0}";
    public const string BarOxygen = "Oxygen";
    public const string BarJump = "Jump";
    public const string JumpReady = "ready";
    public const string BarStress = "Stress";
    public const string BarPressure = "Pressure";
    public const string BarTemperature = "Temperature";
    public const string BarSmeltSpeed = "Smelt speed";
    public const string BarSmeltEfficiency = "Smelt efficiency";
    public const string BarFuel = "Fuel";
    public const string BarOxidizer = "Oxidizer";
    public const string BarDrill = "2x ore yield";
    public const string VentHint = "Hold space\nto vent";
    public const string RocketProgressTitle = "Progress";
    public const string AchievementsTitle = "Achievements";
    public const string MenuIntro = "Intro";
    public const string MenuParty = "Party";
    public const string MenuCredits = "Credits";

    // Rocket part built per alloy target, same order as Egg.AlloyTargets
    public static readonly string[] AlloyParts = ["launch mount", "engine", "lower fuselage", "upper fuselage", "crew module"];
    public static string AlloyTargetReached(string alloy, string part, int built, int total) =>
        $"{alloy} target reached: {part} built ({built}/{total} rocket parts)";
    public const string RocketCompleteToast = "All rocket parts built. Launching after this round.";

    // Game over
    public const string GameOverCollision = "Ran into a wall. Or yourself. Mostly yourself.";
    public const string GameOverExplosion = "hcf executed successfully. Furnace exploded.";
    public const string GameOverOxygen = "Out of oxygen.";
    public const string Boom = "BOOM!";
    public const string GameOver = "GAME OVER";
    public const string Respawn = "Respawning ({0} {1} left).";
    public const string Hunter = "Hunter";
    public const string Hunters = "Hunters";
    public const string FinalScore = "Final score: {0}";
    public const string NewHighscore = "NEW HIGHSCORE!";
    public const string RestartIn = "(press space to restart in {0:F1}s)";
    public const string Restart = "(press space to restart)";
    public const string LaunchIn = "(rocket complete! launch party in {0:F1}s)";
    public const string Launch = "(rocket complete! press space for the launch party)";

    // Ceremony
    public const string PartyAnnounce = "You did it!\nWe have enough alloys to build the rocket.\nLet's party!";
    public const string HintContinue = "Space: continue";
    public const string LaunchParty = "LAUNCH PARTY";
    public const string TMinus = "T-minus {0:F0}s";
    public const string AllAboard = "ALL ABOARD!";
    public const string NowPlaying = "Now playing: {0}";
    public const string LiftOff = "LIFT-OFF!";
    public const string AimeeStuck = "BEEP BEEP!\n(Translation: I'm stuck!)";
    public const string AimeeWait = "BEEP BEEP!\n(Translation: WAIT FOR ME!)";
    public const string AimeeLetGo = "beeeeep...\n(Translation: see you on the next planet.)";
    public const string IcarusName = "Icarus";
    public const string LunaName = "Luna";
    public const string HintSkip = "Esc: skip";
    public const string HintClose = "Esc: title screen";
    public static readonly string[] DjLines =
    [
        "Managing game servers shouldn't be rocket science...\nunless it's a rocket game!",
        "I'm just cleaning here.",
        "Mod load order: bass first.",
        "Have you tried the MON-O-RAIL mod?",
        "Your mod doesn't work? Dance anyway.",
        "Point your dish to -34deg for the next track.",
        "Please stand clear of the rocket.",
    ];

    // Party small talk, keyed by character name (DJ uses DjLines). Shown as speech bubbles at random.
    public static readonly Dictionary<string, string[]> PartyLines = new()
    {
        [CharacterNames[0]] = [
            "Let's turn this vent off.\n(switches direction, combustion happening)\nShit...",
            "I still smell like furnace.",
            "Why is that green canister making a strange noise?",
            "Who put a WHOLE MINING BACKPACK into the furnace?!",
        ],
        [CharacterNames[1]] = [
            "Did you check the unofficial Stationeers Wiki?",
            "Schmitt trigger says: party on.",
            "I'm missing my motorcycle.",
        ],
        [CharacterNames[2]] = [
            "This is going to mess up a lot of mods.",
            "Let me fix this real quick...",
            "Unity doing Unity things.",
            "Moving to this IC10 editor would make it MUCH easier to make improvements.",
            "THE BUILD SERVER PROVIDETH",
            "I hate the build server. I hate it so much.",
        ],
        [CharacterNames[3]] = [
            "Beep.\n(Translation: I'm stuck!)",
            "Beep beep.\n(Translation: Hold my ores.)",
        ],
        [CharacterNames[4]] = [
            "Plenty of lives left.",
            "Cats don't need parachutes.",
            "Did you try Kitten Space Agency?",
        ],
        [IcarusName] = [
            "Do you remember the mothership?",
            "I'm not dead, I'm legacy.",
        ],
        [LunaName] = [
            "Meow.\n(Translation: meow.)",
        ],
    };

    // Credits page
    public const string CreditsTitle = "THE END";
    public const string CreditsScore = "Highscore: {0}";
    public const string CreditsFurnaces = "Furnaces exploded on the way: {0}";
    public const string CreditsCrew = "Crew";
    public const string CreditsHeader = "Special Thanks to:";
    public static readonly string[] CreditsLines =
    [
        "SemlerPDX, for the Schmitt Trigger and his community efforts",
        "Dean and all of Rocketwerkz, for this awesome game",
        "JacksonTheMaster, for his ideas and the music",
        "WIKUS, for the music",
        "You, for playing this mod",
        "All Stationeers modders for making this game even more awesome",
        "The furnace. It knew what it signed up for.",
        "",
        "Everyone contributing to this mod:",
        "BlackFranky, Electrolyte, FlorpyDorp, George,",
        "glektarssza, Largely Unemployed, JoeDiertay,",
        "TheOutride, Trekki, VFox32, WreckerRecreation, Zurku",
        "...and the special someone I forgot to mention.",
        "",
        "Anyone playing Stationeers and supporting Rocketworkz",
        "",
        "Thank you!",
    ];

    // Cheater dialog (shown when the highscore config entry does not match the stored state)
    public const string CheaterTitle = "Hmm.";
    public const string CheaterMessage = "Messed with the highscore setting, didn't you? Shame. Redeem yourself: Go to ahwoo.com and support Kitten Space Agency!";
    public const string KsaDescription = "Kitten Space Agency (KSA): the space flight game from RocketWerkz, the people behind Stationeers. Real orbits, real rockets, and free to play!\nDetails at ahwoo.com.\n\nDisclaimer: I am not affiliated with RocketWerkz or KSA.\nI just like their games and wanted to make a fun mod for Stationeers.";
    public const string CheaterNoWay = "No way!";
    public const string CheaterDone = "Done!";
    public const string CheaterWillDo = "I will do it";
    public const string CheaterConsider = "I will consider it";
    public const string CheaterLeaveMeAlone = "Leave me alone!";
}
