using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace CodeWalker.DoorEditor
{
    /// <summary>
    /// Resolve door SoundSet → SimpleSound → AWC stream and play via CodeWalker AudioPlayer.
    /// </summary>
    public sealed class DoorAudioPreview : IDisposable
    {
        private readonly object _gate = new();
        private AudioDatabase? _db;
        private AudioPlayer? _player;
        private RpfManager? _rpf;
        private string _status = "Not initialized";

        public string Status => _status;
        public bool IsReady => _db?.IsInited == true;
        public int SoundCount => _db?.SoundsDB.Count ?? 0;
        public int AwcCount => _db?.ContainerDB.Count ?? 0;

        public void EnsureInit(RpfManager? rpf)
        {
            if (rpf == null)
            {
                _status = "Game files not ready — wait for main window init / set GTA folder";
                return;
            }
            lock (_gate)
            {
                if (_db?.IsInited == true && ReferenceEquals(_rpf, rpf)) return;
                _rpf = rpf;
                _status = "Indexing audio (dat54 + awc)…";
                try
                {
                    _db = BuildDatabase(rpf);
                    _status = $"Ready · {_db.SoundsDB.Count} sounds · {_db.ContainerDB.Count} awc";
                }
                catch (Exception ex)
                {
                    _db = null;
                    _status = "Audio index failed: " + ex.Message;
                }
            }
        }

        public List<DoorAudioCue> ListCues(string soundsName)
        {
            var list = new List<DoorAudioCue>();
            if (_db == null || string.IsNullOrWhiteSpace(soundsName)) return list;

            if (!TryFindSoundSet(soundsName.Trim(), out var set, out var detail))
            {
                _status = detail;
                return list;
            }

            if (set.SoundSets == null) return list;
            foreach (var item in set.SoundSets)
            {
                var label = DoorAudioDocument.ResolveAudioName(RelXml.HashString(item.ScriptName));
                if (string.IsNullOrEmpty(label) || label == "0")
                    label = "hash_" + item.ScriptName.Hex.ToLowerInvariant();
                list.Add(new DoorAudioCue(label, item.ScriptName, item.ChildSound));
            }
            _status = $"{list.Count} cue(s) in {soundsName}";
            return list;
        }

        /// <summary>Play first useful cue (prefers OPEN / START / …).</summary>
        public bool TryPlaySounds(string soundsName, out string message)
        {
            var cues = ListCues(soundsName);
            if (cues.Count == 0)
            {
                message = _status;
                return false;
            }

            var preferred = new[] { "open", "start", "opening", "raise", "up", "slide_open", "door_open" };
            var cue = cues.FirstOrDefault(c =>
                preferred.Any(p => c.Label.Contains(p, StringComparison.OrdinalIgnoreCase)));
            if (string.IsNullOrEmpty(cue.Label))
                cue = cues[0];

            return TryPlayCue(cue, out message);
        }

        public bool TryPlayCue(DoorAudioCue cue, out string message)
        {
            message = "";
            if (_db == null || _rpf == null)
            {
                message = "Audio index not ready";
                return false;
            }

            try
            {
                var streams = ResolveStreams(cue.ChildSoundHash, depth: 0);
                if (streams.Count == 0)
                {
                    message =
                        $"No playable AWC for cue “{cue.Label}”. " +
                        $"Child={cue.ChildSoundHash.Hex} · soundsDB={SoundCount} · awc={AwcCount}. " +
                        "Container path may not be indexed.";
                    return false;
                }

                _player ??= new AudioPlayer();
                _player.Stop();
                _player.LoadAudio(streams.ToArray());
                _player.SetVolume(1f);
                _player.Play();
                message = $"Playing “{cue.Label}” ({streams.Count} stream(s))";
                _status = message;
                return true;
            }
            catch (Exception ex)
            {
                message = "Play failed: " + ex.Message;
                _status = message;
                return false;
            }
        }

        public void Stop()
        {
            try { _player?.Stop(); }
            catch { /* ignore */ }
            if (IsReady) _status = "Stopped";
        }

        public void Dispose()
        {
            try { _player?.Stop(); }
            catch { /* ignore */ }
            _player = null;
            _db = null;
        }

        private bool TryFindSoundSet(string soundsName, out Dat54SoundSet set, out string detail)
        {
            set = null!;
            detail = "";
            if (_db == null)
            {
                detail = "Audio index not ready";
                return false;
            }

            // hash_XXXXXXXX form
            if (soundsName.StartsWith("hash_", StringComparison.OrdinalIgnoreCase))
            {
                var h = XmlRel.GetHash(soundsName);
                if (_db.SoundsDB.TryGetValue(h, out var byHash))
                {
                    if (byHash is Dat54SoundSet ss)
                    {
                        set = ss;
                        return true;
                    }
                    detail = $"Found {byHash.GetType().Name} for {soundsName}, expected SoundSet";
                    return false;
                }
            }

            foreach (var candidate in new[]
                     {
                         soundsName,
                         soundsName.ToLowerInvariant(),
                         soundsName.ToUpperInvariant()
                     }.Distinct(StringComparer.Ordinal))
            {
                JenkIndex.Ensure(candidate);
                var h = JenkHash.GenHash(candidate);
                if (_db.SoundsDB.TryGetValue(h, out var snd) && snd is Dat54SoundSet ss)
                {
                    set = ss;
                    return true;
                }
            }

            // Name string match (when Rel populated Name)
            foreach (var snd in _db.SoundsDB.Values)
            {
                if (snd is not Dat54SoundSet ss) continue;
                if (!string.IsNullOrEmpty(ss.Name) &&
                    string.Equals(ss.Name, soundsName, StringComparison.OrdinalIgnoreCase))
                {
                    set = ss;
                    return true;
                }
            }

            detail =
                $"SoundSet not found: “{soundsName}” (hash 0x{JenkHash.GenHash(soundsName.ToLowerInvariant()):X8}). " +
                $"Indexed {SoundCount} dat54 sounds — is GTA audio_rel loaded?";
            return false;
        }

        private List<AwcStream> ResolveStreams(MetaHash soundHash, int depth)
        {
            var result = new List<AwcStream>();
            if (_db == null || _rpf == null || soundHash == 0 || depth > 12)
                return result;

            if (!_db.SoundsDB.TryGetValue(soundHash, out var snd))
                return result;

            // Prefer already-linked children from RelFile load
            if (snd.ChildSounds is { Length: > 0 })
            {
                foreach (var child in snd.ChildSounds)
                {
                    if (child == null) continue;
                    if (child is Dat54SimpleSound simpleLinked)
                    {
                        var stream = LoadStream(simpleLinked.ContainerName, simpleLinked.FileName);
                        if (stream != null) result.Add(stream);
                    }
                    else
                    {
                        result.AddRange(ResolveStreams(child.NameHash, depth + 1));
                    }
                    if (result.Count > 0 && snd is not Dat54StreamingSound and not Dat54MultitrackSound)
                        break;
                }
                if (result.Count > 0)
                    return result;
            }

            switch (snd)
            {
                case Dat54SimpleSound simple:
                {
                    var stream = LoadStream(simple.ContainerName, simple.FileName);
                    if (stream != null) result.Add(stream);
                    break;
                }
                case Dat54StreamingSound streaming:
                {
                    foreach (var child in streaming.ChildSoundsHashes ?? [])
                        result.AddRange(ResolveStreams(child, depth + 1));
                    break;
                }
                case Dat54MultitrackSound multi:
                {
                    foreach (var child in multi.ChildSoundsHashes ?? [])
                        result.AddRange(ResolveStreams(child, depth + 1));
                    break;
                }
                case Dat54RandomizedSound rnd:
                {
                    var first = rnd.ChildSoundsHashes?.FirstOrDefault(h => h != 0) ?? 0;
                    if (first != 0) result.AddRange(ResolveStreams(first, depth + 1));
                    break;
                }
                default:
                {
                    if (snd.ChildSoundsHashes != null)
                    {
                        foreach (var child in snd.ChildSoundsHashes)
                        {
                            if (child == 0) continue;
                            result.AddRange(ResolveStreams(child, depth + 1));
                            if (result.Count > 0) break;
                        }
                    }
                    break;
                }
            }

            return result;
        }

        private AwcStream? LoadStream(MetaHash container, MetaHash fileName)
        {
            if (_db == null || _rpf == null) return null;
            if (!_db.ContainerDB.TryGetValue(container, out var entry))
                return null;

            AwcFile? awc;
            try { awc = _rpf.GetFile<AwcFile>(entry); }
            catch { return null; }
            if (awc?.StreamDict == null || awc.StreamDict.Count == 0)
            {
                try { awc?.BuildStreamDict(); }
                catch { return null; }
            }
            if (awc?.StreamDict == null) return null;

            var key = fileName.Hash & 0x1FFFFFFFu;
            if (awc.StreamDict.TryGetValue(key, out var stream))
                return stream;
            if (awc.StreamDict.TryGetValue(fileName, out stream))
                return stream;

            // Some streams store full hash in Id already masked
            foreach (var kv in awc.StreamDict)
            {
                if ((kv.Key & 0x1FFFFFFFu) == key)
                    return kv.Value;
            }
            return null;
        }

        private static AudioDatabase BuildDatabase(RpfManager rpf)
        {
            var db = new AudioDatabase();
            var datrelentries = new Dictionary<uint, RpfFileEntry>();
            var awcentries = new Dictionary<uint, RpfFileEntry>();

            void AddAwc(RpfFileEntry fentry)
            {
                var shortname = fentry.GetShortNameLower();
                var parentname = fentry.Parent?.GetShortNameLower() ?? "";
                if (string.IsNullOrEmpty(parentname) && fentry.Parent?.File != null)
                {
                    parentname = fentry.Parent.File.NameLower;
                    int ind = parentname.LastIndexOf('.');
                    if (ind > 0) parentname = parentname[..ind];
                }

                // Game ContainerName hashes use several casings — index common variants.
                void AddPath(string path)
                {
                    if (string.IsNullOrEmpty(path)) return;
                    JenkIndex.Ensure(path);
                    awcentries[JenkHash.GenHash(path)] = fentry;
                }

                AddPath(parentname + "/" + shortname);
                AddPath(parentname.ToUpperInvariant() + "/" + shortname);
                AddPath(parentname.ToUpperInvariant() + "/" + shortname.ToUpperInvariant());
                AddPath(parentname + "\\" + shortname);
            }

            void AddRpf(RpfFile? rpffile)
            {
                if (rpffile?.AllEntries == null) return;
                foreach (var entry in rpffile.AllEntries)
                {
                    if (entry is not RpfFileEntry fentry) continue;
                    var lower = entry.NameLower ?? "";
                    if (lower.EndsWith(".dat54.rel"))
                        datrelentries[entry.NameHash] = fentry;
                    if (lower.EndsWith(".awc"))
                        AddAwc(fentry);
                }
            }

            // Prefer audio_rel first, then full scan (sfx awc live in other packs).
            AddRpf(rpf.FindRpfFile("x64\\audio\\audio_rel.rpf"));
            AddRpf(rpf.FindRpfFile("x64/audio/audio_rel.rpf"));
            foreach (var file in rpf.AllRpfs ?? [])
                AddRpf(file);

            var soundsdb = new Dictionary<uint, Dat54Sound>();
            foreach (var datentry in datrelentries.Values)
            {
                RelFile? relfile;
                try { relfile = rpf.GetFile<RelFile>(datentry); }
                catch { continue; }
                if (relfile?.RelDatas == null) continue;
                foreach (var rd in relfile.RelDatas)
                {
                    if (rd is Dat54Sound sd)
                        soundsdb[sd.NameHash] = sd;
                }
            }

            db.SoundsDB = soundsdb;
            db.ContainerDB = awcentries;
            db.IsInited = true;
            return db;
        }
    }

    public readonly record struct DoorAudioCue(string Label, MetaHash ScriptName, MetaHash ChildSoundHash)
    {
        public override string ToString() => Label;
    }
}
