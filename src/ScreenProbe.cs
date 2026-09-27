using System.Runtime.CompilerServices;
using UnityEngine;
using YARG.Gameplay.HUD;
using YARG.Menu.Main;
using YARG.Menu.MusicLibrary;
using YARG.Menu.ScoreScreen;
using YARG.Menu.Settings;

namespace YargSetlistBridge
{
    /// <summary>
    /// Which of YARG's screens is up, for <see cref="QrOverlay"/>, and where on it the code goes.
    ///
    /// The one other class, beside <see cref="SetlistProbe"/>, that names YARG's types, and
    /// kept apart from it for the reason that matters: if a YARG update renames one of these
    /// screens, only the QR code should switch off, not the setlist. Its caller catches for
    /// that (see <c>Plugin.PlaceQrCode</c>), so every method naming a YARG type stays out of
    /// line, as in <see cref="SetlistProbe"/>.
    ///
    /// Public members and Unity's own lookups only, no patches and no reflection. YARG's
    /// <c>MenuManager</c> deactivates a menu's object while another covers it, so "the
    /// component is active" is "that screen is on top" — except for the settings, which
    /// open over the main menu without covering it in that sense, and are asked about first.
    /// </summary>
    internal sealed class ScreenProbe
    {
        private const float SearchInterval = 1f;

        /// <summary>
        /// The music library's visible album cover, by its object's name in YARG's prefab.
        /// The field holding it is private, and a name is the one handle Unity offers to
        /// everyone. Unique in the prefab, on YARG's master and dev alike.
        /// </summary>
        private const string AlbumCoverName = "Album Cover Small";

        private readonly Cached<SettingsMenu>     _settings = new Cached<SettingsMenu>();
        private readonly Cached<ScoreScreenMenu>  _score    = new Cached<ScoreScreenMenu>();
        private readonly Cached<MusicLibraryMenu> _library  = new Cached<MusicLibraryMenu>();
        private readonly Cached<MainMenu>         _main     = new Cached<MainMenu>();
        private float _nextFailSearch;
        private PauseMenuObject _failMenu;
        private RectTransform _albumCover;
        private float _nextCoverSearch;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public QrPlacement Current()
        {
            // First: YARG draws its settings over whatever menu opened them.
            if (IsUp(_settings.Find())) return QrPlacement.Hidden;
            if (FailMenuShowing() || IsUp(_score.Find())) return QrPlacement.Corner;
            if (IsUp(_library.Find())) return QrPlacement.MusicLibrary;
            if (IsUp(_main.Find())) return QrPlacement.MainMenu;
            return QrPlacement.Hidden;
        }

        /// <summary>
        /// Where the music library's album cover is on the screen, in pixels from the
        /// bottom left — or false when it can't be found, and the code falls back to a
        /// fixed place in the header.
        ///
        /// Asked of the cover itself rather than measured once, so the code sits on the
        /// cover at any resolution and aspect ratio, and follows it if YARG moves it.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool TryAlbumCover(out Rect screenRect)
        {
            screenRect = default;

            if (_albumCover == null && Time.unscaledTime >= _nextCoverSearch)
            {
                _nextCoverSearch = Time.unscaledTime + SearchInterval;
                var library = _library.Find();
                if (library != null)
                {
                    foreach (var child in library.GetComponentsInChildren<RectTransform>(true))
                    {
                        if (child.name == AlbumCoverName)
                        {
                            _albumCover = child;
                            break;
                        }
                    }
                }
            }

            if (_albumCover == null || !_albumCover.gameObject.activeInHierarchy) return false;

            var canvas = _albumCover.GetComponentInParent<Canvas>();
            if (canvas == null) return false;
            canvas = canvas.rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            var corners = new Vector3[4];
            _albumCover.GetWorldCorners(corners);
            // Bottom left, then top right.
            var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);

            screenRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return screenRect.width > 1 && screenRect.height > 1;
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
