namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Assets.Scripts;
using Assets.Scripts.Objects;
using Assets.Scripts.Sound;
using Assets.Scripts.Util;

using Cysharp.Threading.Tasks;

using ImGuiNET;

using Newtonsoft.Json;

using Objects.Pipes;

using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;

// Plays the game's audio clips on plain Unity AudioSources routed to the interface mixer bus.
// The game mutes the "WorldVolume" mixer parameter while paused, so anything going through
// AudioManager/PooledAudioSource is silent in the pause menu. This layer is independent of
// Time.timeScale, world position and game state.
public enum AudioBus { Interface, Music }

public static class EggAudio
{
    public sealed class Voice
    {
        internal AudioSource Source;
        internal int Hash;
        internal AudioBus Bus;
        internal float StartedAt;
        internal float StopAt = float.PositiveInfinity;

        public bool IsPlaying => Source != null && Source.isPlaying;

        public void Stop()
        {
            if (Source != null)
                Source.Stop();
            StopAt = float.PositiveInfinity;
        }
    }

    const int VoiceCount = 48;
    public static float MasterVolume = 0.5f;
    // Debug: bypass the game's Music mixer group to rule it out when music sounds odd.
    public static bool MusicOnInterfaceBus;

    static GameObject _root;
    static readonly List<Voice> _voices = [];
    static readonly Dictionary<int, GameAudioClipsData> _clips = [];
    static readonly Dictionary<int, Voice> _exclusive = [];
    static readonly System.Random _random = new();
    static AudioMixerGroup _interfaceGroup;
    static AudioMixerGroup _musicGroup;

    public static IReadOnlyDictionary<int, GameAudioClipsData> Clips
    {
        get { Init(); return _clips; }
    }

    static void Init()
    {
        if (_root != null)
            return;

        foreach (var kv in Singleton<AudioManager>.Instance._clipsDataHashLookup)
            _clips[kv.Value.NameHash] = kv.Value;

        _interfaceGroup = FindMixerGroup("Interface", "UI");
        _musicGroup = FindMixerGroup("Music") ?? _interfaceGroup;
        L.Debug($"Egg audio mixer groups: interface={_interfaceGroup?.name ?? "none"} music={_musicGroup?.name ?? "none"}");

        _root = new GameObject("IC10EditorAudio") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(_root);
        for (var i = 0; i < VoiceCount; i++)
        {
            var source = _root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.bypassReverbZones = true;
            source.outputAudioMixerGroup = _interfaceGroup;
            _voices.Add(new Voice { Source = source });
        }
    }

    static AudioMixerGroup FindMixerGroup(params string[] names)
    {
        foreach (var group in Singleton<AudioManager>.Instance.MixerGroups.Values)
            foreach (var name in names)
                if (group.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return group;
        return null;
    }

    public static GameAudioClipsData Find(string name) => Find(Animator.StringToHash(name));

    // First clip data whose name contains all given parts (case-insensitive).
    public static GameAudioClipsData FindByName(params string[] parts)
    {
        foreach (var clips in Clips.Values)
            if (parts.All(p => clips.Name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                return clips;
        return null;
    }

    public static GameAudioClipsData Find(int hash)
    {
        Init();
        _clips.TryGetValue(hash, out var clips);
        return clips;
    }

    public static Voice Play(string name, float volume = 1f, float pitch = 1f, float duration = 0f, AudioBus bus = AudioBus.Interface) =>
        Play(Find(name), volume, pitch, duration, bus);

    public static Voice Play(int hash, float volume = 1f, float pitch = 1f, float duration = 0f, AudioBus bus = AudioBus.Interface) =>
        Play(Find(hash), volume, pitch, duration, bus);

    // duration > 0 stops the voice after that many real-time seconds (used for synth notes).
    public static Voice Play(GameAudioClipsData clips, float volume = 1f, float pitch = 1f, float duration = 0f, AudioBus bus = AudioBus.Interface)
    {
        if (clips == null || clips.Clips.Count == 0)
            return null;
        var voice = PlayClip(clips.Clips[_random.Next(clips.Clips.Count)], volume, pitch, clips.Looping, duration, bus);
        if (voice != null)
            voice.Hash = clips.NameHash;
        return voice;
    }

    // Play() on a clip whose data is still decoding runs the source clock anyway and skips the start
    // of the clip once the data arrives, so music waits for the decode to finish.
    public static async UniTask EnsureLoaded(AudioClip clip)
    {
        if (clip == null || clip.loadState == AudioDataLoadState.Loaded)
            return;
        if (clip.loadState == AudioDataLoadState.Unloaded)
            clip.LoadAudioData();
        while (clip.loadState == AudioDataLoadState.Loading)
            await UniTask.Yield();
    }

    public static Voice PlayClip(AudioClip clip, float volume = 1f, float pitch = 1f, bool loop = false, float duration = 0f, AudioBus bus = AudioBus.Interface)
    {
        if (clip == null)
            return null;
        Init();

        var voice = AcquireVoice();
        var source = voice.Source;
        source.outputAudioMixerGroup = bus == AudioBus.Music && !MusicOnInterfaceBus ? _musicGroup : _interfaceGroup;
        source.clip = clip;
        source.volume = MasterVolume * volume;
        source.pitch = pitch;
        source.loop = loop;
        source.Play();

        voice.Hash = 0;
        voice.Bus = bus;
        voice.StartedAt = Time.realtimeSinceStartup;
        voice.StopAt = duration > 0f ? voice.StartedAt + duration : float.PositiveInfinity;
        return voice;
    }

    // Keeps exactly one instance of a (typically looping) sound running while `on` is true.
    public static void SetPlaying(string name, bool on, float volume = 1f, float pitch = 1f)
    {
        var hash = Animator.StringToHash(name);
        var running = _exclusive.TryGetValue(hash, out var voice) && voice.IsPlaying && voice.Hash == hash;
        if (on == running)
            return;

        if (on)
        {
            voice = Play(hash, volume, pitch);
            if (voice != null)
                _exclusive[hash] = voice;
        }
        else
            Stop(hash);
    }

    public static void Stop(string name) => Stop(Animator.StringToHash(name));

    public static void Stop(int hash)
    {
        if (_exclusive.TryGetValue(hash, out var voice))
        {
            if (voice.Hash == hash)
                voice.Stop();
            _exclusive.Remove(hash);
        }
    }

    // Stops sound effects; background music keeps playing unless includeMusic is set.
    public static void StopAll(bool includeMusic = false)
    {
        foreach (var voice in _voices)
            if (includeMusic || voice.Bus != AudioBus.Music)
                voice.Stop();
        _exclusive.Clear();
    }

    // Releases the audio sources; needed on plugin unload/hot reload, the GameObject would outlive the assembly.
    public static void Shutdown()
    {
        EggMusic.Stop();
        StopAll(includeMusic: true);
        _voices.Clear();
        _clips.Clear();
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _root = null;
    }

    static AudioClip _keepAwakeClip;
    static Voice _keepAwake;

    // The game's mixer auto-suspends after a while of silence (paused game, announce screen) and loses the
    // first second or two after waking up; a 20 Hz hum (about -32 dB, below hearing there) keeps it running.
    // Interface bus so the music volume setting can't push it below the suspend threshold.
    static void KeepMixerAwake()
    {
        if (_keepAwake != null && _keepAwake.IsPlaying && _keepAwake.Source.clip == _keepAwakeClip)
            return;
        if (_keepAwakeClip == null)
        {
            const int rate = 44100;
            var samples = new float[rate];
            for (var i = 0; i < rate; i++)
                samples[i] = 0.05f * Mathf.Sin(2f * Mathf.PI * 20f * i / rate);
            _keepAwakeClip = AudioClip.Create("EggKeepAwake", rate, 1, rate, false);
            _keepAwakeClip.SetData(samples, 0);
        }
        _keepAwake = PlayClip(_keepAwakeClip, 1f, 1f, loop: true, bus: AudioBus.Interface);
        // StopAll() would end it with the sound effects; the Music tag keeps it running
        if (_keepAwake != null)
            _keepAwake.Bus = AudioBus.Music;
    }

    public static void Update()
    {
        KeepMixerAwake();
        var now = Time.realtimeSinceStartup;
        foreach (var voice in _voices)
            if (now >= voice.StopAt)
                voice.Stop();
    }

    static Voice AcquireVoice()
    {
        Voice oldest = null;
        var now = Time.realtimeSinceStartup;
        foreach (var voice in _voices)
        {
            // isPlaying can lag behind Play() for a moment; a voice started just now is busy
            if (!voice.IsPlaying && now - voice.StartedAt > 0.5f)
                return voice;
            if (voice.Bus == AudioBus.Music)
                continue;
            if (oldest == null || voice.StartedAt < oldest.StartedAt)
                oldest = voice;
        }
        oldest ??= _voices[0];
        L.Debug($"Egg audio: all voices busy, stealing one on bus {oldest.Bus} (clip {oldest.Source.clip?.name})");
        oldest.Stop();
        return oldest;
    }

    static string _browserQuery = "";

    public static void DrawSoundBrowser()
    {
        ImGui.Begin("All sounds");
        ImGui.InputText("Search", ref _browserQuery, 100);
        var i = 0;
        foreach (var clips in Clips.Values)
        {
            if (!string.IsNullOrEmpty(_browserQuery) && clips.Name.IndexOf(_browserQuery, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (ImGui.Button(clips.Name, new Vector2(200, 0)))
            {
                StopAll();
                Play(clips, 2.0f);
            }
            if (++i % 8 != 0)
                ImGui.SameLine();
        }
        ImGui.End();
    }
}

// A background music track. Implementations: game audio clips and synth-played JSON songs
// (e.g. MIDI conversions of arcade tunes); drop more kinds in by subclassing.
public abstract class MusicTrack
{
    public string Name;
    // ImGui time at which audio actually started (0 while loading); used for beat sync.
    public double PlaybackStart;
    public abstract bool IsPlaying { get; }

    // Human readable: underscores and camel case split, quality suffix dropped, MusicN are the klaxons.
    public string DisplayName
    {
        get
        {
            var name = Regex.Replace(Name.Replace('_', ' '), "([a-z])([A-Z])", "$1 $2");
            name = Regex.Replace(name, @"\s(Low|High) Q$", "", RegexOptions.IgnoreCase);
            return Regex.Replace(name, @"^Music(\d+)$", "Klaxon $1");
        }
    }
    public virtual float Length => 0f;
    // Playback position in seconds (audio clock).
    public virtual double Position => 0.0;
    public abstract void Start();
    public abstract void Stop();
    public virtual void Update() { }
    // Decode ahead of time so Start() does not stall or skip the beginning.
    public virtual UniTask Preload() => UniTask.CompletedTask;
    public override string ToString() => Name;
}

public class ClipTrack : MusicTrack
{
    readonly AudioClip _clip;
    readonly float _volume;
    readonly bool _loop;
    EggAudio.Voice _voice;

    public ClipTrack(string name, AudioClip clip, float volume = 1f, bool loop = false)
    {
        Name = name;
        _clip = clip;
        _volume = volume;
        _loop = loop;
    }

    bool _loading;
    bool _stopRequested;

    public override bool IsPlaying => _loading || (_voice != null && _voice.IsPlaying);
    public override float Length => _clip.length;

    public override void Start()
    {
        _stopRequested = false;
        PlaybackStart = 0;
        if (_clip.loadState == AudioDataLoadState.Loaded)
            Begin();
        else if (!_loading)
            LoadAndBegin().Forget();
    }

    async UniTaskVoid LoadAndBegin()
    {
        _loading = true;
        await EggAudio.EnsureLoaded(_clip);
        _loading = false;
        if (!_stopRequested)
            Begin();
    }

    public override UniTask Preload() => EggAudio.EnsureLoaded(_clip);

    void Begin()
    {
        _voice = EggAudio.PlayClip(_clip, _volume, 1f, loop: _loop, bus: AudioBus.Music);
        PlaybackStart = ImGui.GetTime();
    }

    public override void Stop()
    {
        _stopRequested = true;
        _voice?.Stop();
    }
}

// OGG file from disk, decoded on first Start.
public class FileTrack : MusicTrack
{
    readonly string _path;
    readonly float _volume;
    AudioClip _clip;
    bool _loading;
    bool _starting;
    bool _stopRequested;
    EggAudio.Voice _voice;

    public FileTrack(string path, float volume = 1f)
    {
        Name = Path.GetFileNameWithoutExtension(path);
        _path = path;
        _volume = volume;
    }

    public override bool IsPlaying => _starting || (_voice != null && _voice.IsPlaying);
    public override float Length => _clip != null ? _clip.length : 0f;
    public override double Position => _voice?.Source != null && _voice.IsPlaying ? _voice.Source.time : 0.0;

    public override void Start()
    {
        _stopRequested = false;
        PlaybackStart = 0;
        if (_clip != null)
            Begin();
        else
            LoadAndBegin().Forget();
    }

    async UniTaskVoid LoadAndBegin()
    {
        _starting = true;
        await Preload();
        _starting = false;
        if (_clip != null && !_stopRequested)
            Begin();
    }

    public override async UniTask Preload()
    {
        if (_clip != null)
            return;
        if (_loading)
        {
            while (_loading)
                await UniTask.Yield();
            return;
        }
        _loading = true;
        try
        {
            using var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(_path).AbsoluteUri, AudioType.OGGVORBIS);
            // stream (decode while playing); otherwise GetContent decodes the whole file to PCM on the main thread
            ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = true;
            await request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                var clip = DownloadHandlerAudioClip.GetContent(request);
                await EggAudio.EnsureLoaded(clip);
                _clip = clip;
            }
            else
                L.Debug($"Failed to load {_path}: {request.error}");
        }
        catch (Exception e)
        {
            L.Debug($"Failed to load {_path}: {e.Message}");
        }
        finally
        {
            _loading = false;
        }
    }

    void Begin()
    {
        _voice = EggAudio.PlayClip(_clip, _volume, 1f, loop: false, bus: AudioBus.Music);
        PlaybackStart = ImGui.GetTime();
        L.Debug($"Music '{Name}' started (clip state {_clip.loadState}, {_clip.length:F0}s)");
        CheckStart(_voice).Forget();
    }

    // Diagnostics: where the source is one second after start (0 = started late, ~1 = fine).
    async UniTaskVoid CheckStart(EggAudio.Voice voice)
    {
        await UniTask.Delay(1000, ignoreTimeScale: true);
        if (voice != null && voice.Source != null)
            L.Debug($"Music '{Name}' after 1s: playing={voice.Source.isPlaying} time={voice.Source.time:F2}s clip={voice.Source.clip?.name}");
    }

    public override void Stop()
    {
        _stopRequested = true;
        _voice?.Stop();
    }
}

public class SynthTrack : MusicTrack
{
    readonly Song _song;
    readonly Synth _synth = new() { Bus = AudioBus.Music };

    public SynthTrack(string name, Song song)
    {
        Name = name;
        _song = song;
        _song.Repeat = true;
    }

    public override bool IsPlaying => _synth.Song == _song;
    public override void Start() => _synth.PlaySong(_song);
    public override void Stop() => _synth.Stop();
    public override void Update() => _synth.Update();
}

public static class EggMusic
{
    public static readonly List<MusicTrack> Tracks = [];
    public static MusicTrack Current { get; private set; }
    public static double CurrentStart { get; private set; }
    public static float ClipVolume = 0.6f;
    public static float SpaceMusicVolume = 2.0f;
    // When a track ends, continue with another random one.
    public static bool AutoAdvance = true;

    static readonly System.Random _random = new(unchecked(Environment.TickCount * 31 + (int)DateTime.Now.Ticks));
    static bool _discovered;
    // Reserved for the party, never picked at random.
    static readonly string[] PartyOnly = ["Riding_on_the_Monorail"];
    static MusicTrack _spaceMusic;

    // The game's space map music, looped; not part of the random playlist.
    public static MusicTrack SpaceMusic
    {
        get
        {
            if (_spaceMusic == null)
            {
                var clips = EggAudio.Find("SpaceMapMusic");
                if (clips != null && clips.Clips.Count > 0)
                    _spaceMusic = new ClipTrack("SpaceMapMusic", clips.Clips[0], SpaceMusicVolume, loop: true);
            }
            return _spaceMusic;
        }
    }

    // Forget the track list (e.g. after the asset zip was installed) so the next use rescans.
    public static void Reset()
    {
        _discovered = false;
        Tracks.Clear();
    }

    // Decode all file tracks up front (done during the intro).
    public static async UniTask Preload()
    {
        Discover();
        foreach (var track in Tracks.ToList())
        {
            await track.Preload();
            await UniTask.Yield();
        }
        if (SpaceMusic != null)
            await SpaceMusic.Preload();
    }

    public static MusicTrack Find(string nameContains)
    {
        Discover();
        return Tracks.FirstOrDefault(t => t.Name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    // Game clips whose name contains "Music" and OGG/JSON songs from <cache>/ic10editor/music.
    static void Discover()
    {
        if (_discovered)
            return;
        _discovered = true;

        foreach (var clips in EggAudio.Clips.Values)
        {
            if (clips.Name.IndexOf("Music", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            L.Debug($"Music clip data '{clips.Name}': {string.Join(", ", clips.Clips.Select(c => $"{c.name} ({c.length:F0}s)"))}");
            if (clips.Clips.Count > 0 && clips.Name != "SpaceMapMusic")
                Tracks.Add(new ClipTrack(clips.Name, clips.Clips[0], ClipVolume));
        }

        var musicDir = EggAssets.MusicDir;
        if (Directory.Exists(musicDir))
        {
            foreach (var file in Directory.GetFiles(musicDir, "*.ogg"))
                Tracks.Add(new FileTrack(file, ClipVolume));
            foreach (var file in Directory.GetFiles(musicDir, "*.json"))
            {
                try { Tracks.Add(new SynthTrack(Path.GetFileNameWithoutExtension(file), Song.LoadFromJSON(file))); }
                catch (Exception e) { L.Debug($"Failed to load song {file}: {e.Message}"); }
            }
        }

        L.Debug($"Music tracks: {string.Join(", ", Tracks)}");
    }

    public static void Play(MusicTrack track)
    {
        Stop();
        Current = track;
        CurrentStart = ImGui.GetTime();
        track?.Start();
    }

    public static void PlayRandom()
    {
        Discover();
        var playlist = Tracks.Where(t => !PartyOnly.Any(p => t.Name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        if (playlist.Count == 0)
            return;
        var candidates = playlist.Count > 1 && Current != null ? playlist.Where(t => t != Current).ToList() : playlist;
        Play(candidates[_random.Next(candidates.Count)]);
    }

    public static void Stop()
    {
        Current?.Stop();
        Current = null;
    }

    public static void Update()
    {
        if (Current == null)
            return;
        Current.Update();
        if (!Current.IsPlaying && AutoAdvance)
            PlayRandom();
    }
}

public struct MidiNote
{
    public int Tick;
    public int Note;
    public int Duration;
    public int Velocity;
}

public class Track
{
    public List<MidiNote> Notes;
    public int InstrumentId;
    public bool Enabled = true;
}

public class Instrument
{
    public struct NoteData
    {
        public GameAudioClipsData ClipsData;
        public float VolumeMultiplier;
        public float PitchMultiplier;
    }

    public Dictionary<int, NoteData> Notes; // key is MIDI note number

    static readonly Dictionary<string, int> NoteNames = new()
    {
        { "C", 0 }, { "C#", 1 }, { "Db", 1 }, { "D", 2 }, { "D#", 3 }, { "Eb", 3 },
        { "E", 4 }, { "F", 5 }, { "F#", 6 }, { "Gb", 6 }, { "G", 7 }, { "G#", 8 },
        { "Ab", 8 }, { "A", 9 }, { "A#", 10 }, { "Bb", 10 }, { "B", 11 },
    };

    // "C#4", "Eb3", ... octave 4 is the middle C octave
    public static int GetMidiNoteNumber(string note)
    {
        var octave = int.Parse(note.Substring(note.Length - 1));
        return octave * 12 + NoteNames[note.Substring(0, note.Length - 1)];
    }

    public static Instrument GetInstrument(string namePrefix)
    {
        Dictionary<int, GameAudioClipsData> availableNotes = [];

        if (namePrefix == "PipeOrgan")
        {
            var pipeOrgan = Prefab.Find<OrganPipe>("StructurePipeOrgan");
            if (OrganPipe._noteNamesLookup == null)
                pipeOrgan.PopulateNoteNamesLookup();

            foreach (var audioEvent in pipeOrgan.AudioEvents)
            {
                if (!OrganPipe._noteNamesLookup.ContainsValue(audioEvent.Name))
                    continue;
                availableNotes[GetMidiNoteNumber(audioEvent.Name) + 12] = audioEvent.ClipsData;
            }
        }
        else
        {
            foreach (var clips in EggAudio.Clips.Values)
            {
                if (!clips.Name.StartsWith(namePrefix))
                    continue;
                availableNotes[GetMidiNoteNumber(clips.Name.Split('_').Last())] = clips;
            }
        }

        Dictionary<int, NoteData> notes = [];
        for (var midiNote = 0; midiNote < 128; midiNote++)
        {
            if (availableNotes.TryGetValue(midiNote, out var clipsData))
            {
                notes[midiNote] = new NoteData { ClipsData = clipsData, VolumeMultiplier = 1.0f, PitchMultiplier = 1.0f };
                continue;
            }

            var closest = availableNotes.Keys.OrderBy(k => Math.Abs(k - midiNote)).First();
            notes[midiNote] = new NoteData
            {
                ClipsData = availableNotes[closest],
                VolumeMultiplier = 1.0f,
                PitchMultiplier = (float)Math.Pow(2, (midiNote - closest) / 12.0)
            };
        }

        return new Instrument { Notes = notes };
    }
}

public class Song
{
    public int TicksPerBeat;
    public int BPM;
    public List<Track> Tracks;
    public bool Repeat = false;
    // Loop length in ticks (optional "length" in the json); 0 = end one second after the last note.
    public int Length;
    public int EndTick;

    // { "tpb": 480, "bpm": 140, "length": 39225, "tracks": [[instrument_id, [[tick, note, duration, velocity], ...]], ...] }
    public static Song LoadFromJSON(string filename, int pitchCorrection = 0)
    {
        var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(filename));

        var song = new Song
        {
            TicksPerBeat = Convert.ToInt32(data["tpb"]),
            BPM = Convert.ToInt32(data["bpm"]),
            Length = data.TryGetValue("length", out var length) ? Convert.ToInt32(length) : 0,
            Tracks = []
        };

        var firstTick = int.MaxValue;
        foreach (var trackData in (Newtonsoft.Json.Linq.JArray)data["tracks"])
        {
            var track = new Track { InstrumentId = Convert.ToInt32(trackData[0]), Notes = [] };
            foreach (var noteData in (Newtonsoft.Json.Linq.JArray)trackData[1])
            {
                var midiNote = new MidiNote
                {
                    Tick = Convert.ToInt32(noteData[0]),
                    Note = Convert.ToInt32(noteData[1]) + pitchCorrection,
                    Duration = Convert.ToInt32(noteData[2]),
                    Velocity = Convert.ToInt32(noteData[3])
                };
                firstTick = Math.Min(firstTick, midiNote.Tick);
                track.Notes.Add(midiNote);
            }
            song.Tracks.Add(track);
        }

        foreach (var track in song.Tracks)
            for (var i = 0; i < track.Notes.Count; i++)
            {
                var note = track.Notes[i];
                note.Tick -= firstTick;
                track.Notes[i] = note;
                song.EndTick = Math.Max(song.EndTick, note.Tick + note.Duration);
            }
        return song;
    }
}

public class Synth
{
    public Song Song;
    public AudioBus Bus = AudioBus.Interface;
    public double StartTime;
    public int LastTick;
    public float TicksPerSecond;
    public int PitchCorrection;
    public List<int> TrackPositions;

    static List<Instrument> _instruments;
    public static List<Instrument> Instruments
    {
        get
        {
            if (_instruments == null)
            {
                _instruments = [];
                foreach (var name in InstrumentNames)
                    _instruments.Add(Instrument.GetInstrument(name));
            }
            return _instruments;
        }
    }

    public static readonly List<string> InstrumentNames =
    [
        "SamplerTest",
        "DarkBass",
        "PluckyBass",
        "StabbyBass",
        "OldSkoolDrums",
        "AccousticDrums",
        "EpicDrums",
        "SmoothSwell",
        "Sweeper",
        "Stabby5ths",
        "Replicant",
        "SpaceChoir",
        "SpaceSymphonia",
        "SawLead",
        "SparkleLead",
        "PipeOrgan",
    ];

    public void PlaySong(Song song)
    {
        Song = song;
        TrackPositions = [];
        LastTick = 0;
        StartTime = ImGui.GetTime();
        TicksPerSecond = song.TicksPerBeat * song.BPM / 60.0f;
        foreach (var track in Song.Tracks)
            TrackPositions.Add(0);
    }

    public void Stop()
    {
        Song = null;
    }

    public void Update()
    {
        if (Song == null)
            return;

        var currentTick = (int)((ImGui.GetTime() - StartTime) * TicksPerSecond);
        if (currentTick <= LastTick)
            return;
        LastTick = currentTick;

        for (var i = 0; i < Song.Tracks.Count; i++)
        {
            var track = Song.Tracks[i];
            var pos = TrackPositions[i];
            while (pos < track.Notes.Count && track.Notes[pos].Tick <= currentTick)
            {
                if (track.Enabled)
                    PlayNote(track.InstrumentId, track.Notes[pos]);
                pos++;
            }
            TrackPositions[i] = pos;
        }

        var endTick = Song.Length > 0 ? Song.Length : Song.EndTick + (int)TicksPerSecond;
        if (currentTick < endTick)
            return;
        if (Song.Repeat)
        {
            // keep the phase so the loop stays on the beat
            var nextStart = StartTime + endTick / TicksPerSecond;
            PlaySong(Song);
            StartTime = nextStart;
        }
        else
            Song = null;
    }

    void PlayNote(int instrumentId, MidiNote note)
    {
        if (instrumentId < 0 || instrumentId >= Instruments.Count)
            return;
        var noteId = note.Note + PitchCorrection;
        if (noteId < 0 || noteId >= 128)
            return;
        var noteData = Instruments[instrumentId].Notes[noteId];
        var volume = noteData.VolumeMultiplier * note.Velocity / 127.0f;
        EggAudio.Play(noteData.ClipsData, volume, noteData.PitchMultiplier, note.Duration / TicksPerSecond, Bus);
    }

    public void DrawAudioControl()
    {
        ImGui.Begin("Audio Control");
        foreach (var file in Directory.Exists(EggAssets.SongsDir) ? Directory.GetFiles(EggAssets.SongsDir, "*.json") : [])
        {
            if (ImGui.Button(Path.GetFileNameWithoutExtension(file)))
                PlaySong(Song.LoadFromJSON(file));
            ImGui.SameLine();
        }
        ImGui.Text("");

        ImGui.Text("Ticks per second:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.InputFloat("##tickspersecond", ref TicksPerSecond);
        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("##pitchcorrection", ref PitchCorrection);

        for (var i = 0; i < Instruments.Count; i++)
            ImGui.Text($"Instrument {i}: {InstrumentNames[i]}");

        if (Song != null)
            for (var trackId = 0; trackId < Song.Tracks.Count; trackId++)
            {
                var track = Song.Tracks[trackId];
                ImGui.Text($"Track {trackId}:");
                ImGui.SameLine();
                var enabled = track.Enabled;
                if (ImGui.Checkbox($"##enabled{trackId}", ref enabled))
                    track.Enabled = enabled;
                ImGui.SameLine();
                var newId = track.InstrumentId;
                ImGui.SetNextItemWidth(100);
                if (ImGui.InputInt($"##track{trackId}", ref newId))
                    track.InstrumentId = newId;
            }
        ImGui.End();
    }
}
