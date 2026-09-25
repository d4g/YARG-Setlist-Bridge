using System.Collections.Generic;
using System.Runtime.CompilerServices;
using YARG;
using YARG.Core.Song;
using YARG.Menu.Persistent;
using YARG.Song;

namespace YargSetlistBridge
{
    /// <summary>
    /// Protocol-2 edits: which of YARG's two setlist lists a command lands in, and what
    /// has to happen around it. The rules themselves live in <see cref="SetlistCommands"/>.
    ///
    /// Three YARG details decide how this is done:
    ///
    /// - <c>Playlist.MoveSongUp/Down</c> save the playlist to disk even when it is the
    ///   ephemeral setlist, which would leave a stray <c>Setlist.*.json</c> among the user's
    ///   playlists. So the hash list is edited directly, never through those methods.
    /// - An on-screen library is redrawn with <c>RefreshAndReselect</c>, the call YARG makes
    ///   after its own edits. Its <c>Refresh</c> also rebuilds the help bar, so the green
    ///   button's label flips from "Play Song" to "Add to Setlist" as soon as a guest adds the
    ///   first song, and it defers that while a popup or dialog is open. Never call
    ///   <c>SetNavigationScheme(true)</c> directly instead: it pops the navigation stack
    ///   unconditionally.
    /// - While difficulty select is open for a setlist, the list has already been copied
    ///   into the show and the library's copy may already be cleared. Editing either one
    ///   there could start a different show than the one on screen, so edits wait: <c>busy</c>.
    /// </summary>
    internal sealed partial class SetlistProbe
    {
        private IReadOnlyDictionary<HashWrapper, List<SongEntry>> _indexedFrom;
        private Dictionary<string, SongEntry>                     _songsByHex;

        /// <summary>Applies a validated command on the main thread. Returns an error code, or null.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public string Apply(SetlistCommands.Args args, bool toastAdds)
        {
            var globals = GlobalVariables.Instance;
            var scene = globals != null ? globals.CurrentScene : SceneIndex.Persistent;
            var state = GlobalVariables.State;

            if (scene == SceneIndex.Gameplay || scene == SceneIndex.Score)
            {
                // A single song is playing: there is no setlist to add to, and the library
                // that holds one before a show is not loaded during gameplay.
                if (!state.PlayingAShow || state.ShowSongs == null) return SetlistCommands.Busy;

                var code = SetlistCommands.Apply(
                    state.ShowSongs, state.ShowIndex + 1, args,
                    song => song.Hash.ToString(),
                    TryResolveSong);

                if (code == null && args.Type == SetlistCommands.Add && toastAdds && scene == SceneIndex.Score)
                {
                    // Not during gameplay: a toast over the highway is a distraction mid-song.
                    ToastAdded(args.Hash);
                }
                return code;
            }

            if (scene != SceneIndex.Menu) return SetlistCommands.Busy;

            var difficultySelect = _difficultySelect.Find();
            if (state.PlayingAShow && difficultySelect != null && difficultySelect.isActiveAndEnabled)
            {
                return SetlistCommands.Busy;
            }

            var library = _library.Find();
            var playlist = library?.ShowPlaylist;
            if (playlist?.SongHashes == null) return SetlistCommands.Busy;

            var result = SetlistCommands.Apply(
                playlist.SongHashes, 0, args,
                hash => hash.ToString(),
                TryResolveHash);

            if (result != null) return result;

            // Redraw only a library that is on screen. One that isn't rebuilds its list
            // the next time it opens.
            if (library.isActiveAndEnabled) library.RefreshAndReselect();
            if (args.Type == SetlistCommands.Add && toastAdds) ToastAdded(args.Hash);
            return null;
        }

        private bool TryResolveSong(string hex, out SongEntry song) => SongsByHex().TryGetValue(hex, out song);

        private bool TryResolveHash(string hex, out HashWrapper hash)
        {
            if (SongsByHex().TryGetValue(hex, out var song))
            {
                hash = song.Hash;
                return true;
            }
            hash = default;
            return false;
        }

        /// <summary>
        /// The library keyed by hex hash. YARG keys it by <see cref="HashWrapper"/>, and
        /// building one from hex would mean depending on a constructor this plugin has no
        /// source for; the map is rebuilt whenever YARG swaps in a new library after a scan.
        /// </summary>
        private Dictionary<string, SongEntry> SongsByHex()
        {
            var source = SongContainer.SongsByHash;
            if (_songsByHex != null && ReferenceEquals(source, _indexedFrom) && _songsByHex.Count == source.Count)
            {
                return _songsByHex;
            }

            var map = new Dictionary<string, SongEntry>(source.Count);
            foreach (var pair in source)
            {
                if (pair.Value != null && pair.Value.Count > 0) map[pair.Key.ToString()] = pair.Value[0];
            }

            _indexedFrom = source;
            _songsByHex = map;
            return map;
        }

        private void ToastAdded(string hex)
        {
            if (SongsByHex().TryGetValue(hex, out var song))
            {
                ToastManager.ToastInformation($"Added to the setlist: {song.Name}");
            }
        }
    }
}
