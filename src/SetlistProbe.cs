using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using YARG;
using YARG.Core.Song;
using YARG.Menu.DifficultySelect;
using YARG.Menu.MusicLibrary;

namespace YargSetlistBridge
{
    /// <summary>
    /// With SetlistProbe.Edit.cs, the only code that touches YARG's internals. Everything here is a public member of
    /// YARG's own assemblies, read without Harmony patches or reflection.
    ///
    /// YARG holds the setlist in two places, and which one is live depends on the phase:
    ///
    /// - Before a show, it is <c>MusicLibraryMenu.ShowPlaylist</c>, an ephemeral playlist
    ///   owned by the menu instance. The menu scene is unloaded during gameplay, so that
    ///   instance comes and goes and has to be looked up again.
    /// - Once a show starts, it is copied into <c>GlobalVariables.State.ShowSongs</c> with
    ///   <c>ShowIndex</c> as the position. The score screen reads that list afresh on every
    ///   Continue, which is what will make live edits possible in a later protocol version.
    ///
    /// <c>PlayingAShow</c> alone does not mean a show is running. YARG sets it, and copies
    /// the list, when Start is pressed and difficulty select opens; backing out of difficulty
    /// select leaves it set, with a copy that goes stale as soon as the player edits the
    /// setlist again. Only quitting a song, finishing the show or playing a single song
    /// clears it. So in the menu scene the library's own list wins, and the show's copy is
    /// trusted only while difficulty select is actually on screen. (Found in the first
    /// in-game test: v0.1.0 reported "playing" from the menu and missed seven additions.)
    ///
    /// Methods that name YARG types are kept out of line (<see cref="MethodImplOptions.NoInlining"/>)
    /// so that when a YARG update renames something, the JIT failure surfaces as a
    /// catchable exception at the call site rather than breaking the plugin's own methods.
    /// </summary>
    internal sealed partial class SetlistProbe
    {
        private const float SearchInterval = 1f;

        private readonly SceneObject<MusicLibraryMenu>     _library          = new SceneObject<MusicLibraryMenu>();
        private readonly SceneObject<DifficultySelectMenu> _difficultySelect = new SceneObject<DifficultySelectMenu>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string YargVersion() => GlobalVariables.Instance != null ? GlobalVariables.Instance.CurrentVersion : null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string PersistentDataPath() => YARG.Helpers.PathHelper.PersistentDataPath;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public SetlistSnapshot Capture()
        {
            var globals = GlobalVariables.Instance;
            var scene = globals != null ? globals.CurrentScene : SceneIndex.Persistent;
            var state = GlobalVariables.State;
            bool inSong = scene == SceneIndex.Gameplay || scene == SceneIndex.Score;

            var snapshot = new SetlistSnapshot
            {
                Scene   = scene.ToString(),
                Current = inSong && state.CurrentSong != null ? state.CurrentSong.Hash.ToString() : null,
                Mode    = SetlistSnapshot.ModeIdle,
                Songs   = SetlistSnapshot.NoSongs,
            };

            bool hasShowSongs = state.PlayingAShow && state.ShowSongs != null && state.ShowSongs.Count > 0;

            if (inSong)
            {
                if (hasShowSongs)
                {
                    snapshot.Mode  = SetlistSnapshot.ModePlaying;
                    snapshot.Index = state.ShowIndex;
                    snapshot.Songs = HashesOf(state.ShowSongs);
                }
                return snapshot;
            }

            if (scene != SceneIndex.Menu) return snapshot;

            var playlist = _library.Find()?.ShowPlaylist;
            if (playlist?.SongHashes != null && playlist.SongHashes.Count > 0)
            {
                var songs = new List<string>(playlist.SongHashes.Count);
                foreach (var hash in playlist.SongHashes)
                {
                    songs.Add(hash.ToString());
                }

                snapshot.Mode  = SetlistSnapshot.ModeBuilding;
                snapshot.Songs = SetlistSnapshot.ToArray(songs);
                return snapshot;
            }

            // Show mode's Play button empties the library's list before difficulty select
            // opens, so during that screen the show's copy is the only place the list exists.
            var difficultySelect = _difficultySelect.Find();
            if (hasShowSongs && difficultySelect != null && difficultySelect.isActiveAndEnabled)
            {
                snapshot.Mode  = SetlistSnapshot.ModeBuilding;
                snapshot.Songs = HashesOf(state.ShowSongs);
            }

            return snapshot;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string[] HashesOf(List<SongEntry> entries)
        {
            var songs = new List<string>(entries.Count);
            foreach (var song in entries)
            {
                songs.Add(song.Hash.ToString());
            }
            return SetlistSnapshot.ToArray(songs);
        }

        /// <summary>
        /// Caches one object from the loaded menu scene. Menus exist, inactive, from the moment
        /// the scene loads and are destroyed with it, so a miss is retried at most once a
        /// second rather than searching every poll.
        /// </summary>
        private sealed class SceneObject<T> where T : Object
        {
            private T     _cached;
            private float _nextSearch;

            public T Find()
            {
                // Unity's overloaded == is what reports a destroyed instance as null.
                if (_cached != null) return _cached;
                if (Time.unscaledTime < _nextSearch) return null;
                _nextSearch = Time.unscaledTime + SearchInterval;

                var found = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                _cached = found.Length > 0 ? found[0] : null;
                return _cached;
            }
        }
    }
}
