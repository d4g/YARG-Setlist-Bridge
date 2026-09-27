using System.Runtime.CompilerServices;
using UnityEngine;
using YARG.Gameplay.HUD;
using YARG.Menu.Main;
using YARG.Menu.MusicLibrary;
using YARG.Menu.ScoreScreen;

namespace YargSetlistBridge
{
    /// <summary>
    /// Which of YARG's screens is up, for <see cref="QrOverlay"/>.
    ///
    /// The one other class, beside <see cref="SetlistProbe"/>, that names YARG's types, and
    /// kept apart from it for the reason that matters: if a YARG update renames one of these
    /// screens, only the QR code should switch off, not the setlist. Its caller catches for
    /// that (see <c>Plugin.PlaceQrCode</c>), so every method naming a YARG type stays out of
    /// line, as in <see cref="SetlistProbe"/>.
    ///
    /// Public members only, no patches. YARG's <c>MenuManager</c> deactivates a menu's object
    /// while another covers it, so "the component is active" is "that screen is on top".
    /// </summary>
    internal sealed class ScreenProbe
    {
        private const float SearchInterval = 1f;

        private readonly Cached<ScoreScreenMenu>  _score   = new Cached<ScoreScreenMenu>();
        private readonly Cached<MusicLibraryMenu> _library = new Cached<MusicLibraryMenu>();
        private readonly Cached<MainMenu>         _main    = new Cached<MainMenu>();
        private float _nextFailSearch;
        private PauseMenuObject _failMenu;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public QrPlacement Current()
        {
            if (FailMenuShowing() || IsUp(_score.Find())) return QrPlacement.Corner;
            if (IsUp(_library.Find())) return QrPlacement.MusicLibrary;
            if (IsUp(_main.Find())) return QrPlacement.MainMenu;
            return QrPlacement.Hidden;
        }

        /// <summary>
        /// The menu YARG opens when a song is failed: one of the gameplay HUD's pause menus,
        /// told apart by its <c>Menu</c>. Looked up by that value, since the pause menus all
        /// share one component type.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool FailMenuShowing()
        {
            if (_failMenu == null && Time.unscaledTime >= _nextFailSearch)
            {
                _nextFailSearch = Time.unscaledTime + SearchInterval;
                foreach (var menu in Object.FindObjectsByType<PauseMenuObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (menu.Menu == PauseMenuManager.Menu.FailPause)
                    {
                        _failMenu = menu;
                        break;
                    }
                }
            }

            return IsUp(_failMenu);
        }

        private static bool IsUp(Behaviour behaviour) => behaviour != null && behaviour.isActiveAndEnabled;

        /// <summary>
        /// A screen's component, found once per scene and then held. Inactive ones included,
        /// because a covered menu is an inactive one and still the one to watch.
        /// </summary>
        private sealed class Cached<T> where T : Object
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
