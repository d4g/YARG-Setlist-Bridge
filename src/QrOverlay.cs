using UnityEngine;
using UnityEngine.UI;

namespace YargSetlistBridge
{
    /// <summary>Where the QR code goes, which is decided by which of YARG's screens is up.</summary>
    public enum QrPlacement
    {
        Hidden,
        /// <summary>Beside the main menu's buttons.</summary>
        MainMenu,
        /// <summary>
        /// On the music library's album cover, in its top right quarter — or, when the cover
        /// can't be found, in the header between the title and the search bar.
        /// </summary>
        MusicLibrary,
        /// <summary>Top right: the score screen, and the menu after a failed song.</summary>
        Corner,
    }

    /// <summary>
    /// Draws the client's QR code over YARG, on a canvas of its own.
    ///
    /// Its own canvas rather than a child of YARG's screens: nothing of YARG's is moved,
    /// resized or reparented, so a YARG update that rearranges a screen can at worst leave
    /// the code in a slightly wrong place — it cannot break the screen. The canvas scales
    /// exactly as YARG's do (1920×1080 reference, matched to width), so a position chosen
    /// against YARG's layout stays against it at any resolution.
    ///
    /// Touches Unity only, never YARG's types; <see cref="ScreenProbe"/> says which screen
    /// is up.
    /// </summary>
    public sealed class QrOverlay
    {
        /// <summary>Light modules around the code, as the QR spec asks, so it scans on a dark screen.</summary>
        private const int QuietZone = 4;

        private readonly GameObject    _root;
        private readonly Canvas        _canvas;
        private readonly RectTransform _frame;
        private readonly RawImage      _image;
        private Texture2D   _texture;
        private QrPlacement _placement = QrPlacement.Hidden;
        /// <summary>The album cover in screen pixels, while the code sits on it.</summary>
        private Rect? _cover;

        public QrOverlay()
        {
            _root = new GameObject("YargSetlistBridge.QrOverlay");
            Object.DontDestroyOnLoad(_root);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over YARG's own canvases, which stay well below this.
            _canvas.sortingOrder = 30000;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0;

            // No GraphicRaycaster: the code must never take a click meant for YARG.
            var image = new GameObject("Code");
            image.transform.SetParent(_root.transform, false);
            _image = image.AddComponent<RawImage>();
            _image.raycastTarget = false;
            _frame = _image.rectTransform;

            _root.SetActive(false);
        }

        /// <summary>Replaces the code, or with null takes it away.</summary>
        public void SetCode(bool[,] modules)
        {
            if (_texture != null) Object.Destroy(_texture);
            _texture = modules == null ? null : Render(modules);
            _image.texture = _texture;
            Refresh();
        }

        /// <param name="cover">
        /// For <see cref="QrPlacement.MusicLibrary"/>: the album cover's rectangle in screen
        /// pixels, when it was found. Null puts the code in the header instead.
        /// </param>
        public void Place(QrPlacement placement, Rect? cover = null)
        {
            if (placement == _placement && Same(cover, _cover)) return;
            _placement = placement;
            _cover = cover;
            Refresh();
        }

        /// <summary>Within a pixel counts as unmoved, so a still cover never re-lays the code.</summary>
        private static bool Same(Rect? a, Rect? b)
        {
            if (a == null || b == null) return a == null && b == null;
            var x = a.Value;
            var y = b.Value;
            return Mathf.Abs(x.xMin - y.xMin) < 1 && Mathf.Abs(x.yMin - y.yMin) < 1 &&
                   Mathf.Abs(x.width - y.width) < 1 && Mathf.Abs(x.height - y.height) < 1;
        }

        public void Destroy()
        {
            if (_texture != null) Object.Destroy(_texture);
            Object.Destroy(_root);
        }

        private void Refresh()
        {
            var visible = _texture != null && _placement != QrPlacement.Hidden;
            _root.SetActive(visible);
            if (!visible) return;

            // Positions in YARG's 1920×1080 reference units, measured against its screens.
            switch (_placement)
            {
                case QrPlacement.MainMenu:
                    // Right of the menu's buttons, a sixth of the screen's height below
                    // its middle. An anchor rather than an offset, so it is a sixth on a
                    // screen of any shape: the canvas matches width, and its height in
                    // reference units changes with the aspect ratio.
                    Anchor(new Vector2(1, 1f / 3), new Vector2(-220, 0), 320);
                    break;

                case QrPlacement.MusicLibrary when _cover != null:
                    // The top right quarter of the cover, exactly.
                    var cover = _cover.Value;
                    var side = Mathf.Min(cover.width, cover.height) / 2;
                    var centre = new Vector2(cover.xMax - side / 2, cover.yMax - side / 2);
                    AtScreenPoint(centre, side);
                    break;

                case QrPlacement.MusicLibrary:
                    // The header's free stretch: the title ends near x 415 and the search
                    // bar starts near x 1215, and the header is about 150 high.
                    Anchor(new Vector2(0, 1), new Vector2(815, -75), 130);
                    break;

                case QrPlacement.Corner:
                    Anchor(new Vector2(1, 1), new Vector2(-150, -150), 220);
                    break;
            }
        }

        /// <summary>
        /// Centres the code on a point in screen pixels, <paramref name="side"/> pixels wide.
        /// The canvas's scale factor turns pixels into its own units.
        /// </summary>
        private void AtScreenPoint(Vector2 centre, float side)
        {
            var scale = _canvas.scaleFactor > 0 ? _canvas.scaleFactor : 1;
            _frame.anchorMin = Vector2.zero;
            _frame.anchorMax = Vector2.zero;
            _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.anchoredPosition = centre / scale;
            _frame.sizeDelta = new Vector2(side, side) / scale;
        }

        /// <summary>Centres the code on <paramref name="offset"/> from <paramref name="anchor"/>.</summary>
        private void Anchor(Vector2 anchor, Vector2 offset, float size)
        {
            _frame.anchorMin = anchor;
            _frame.anchorMax = anchor;
            _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.anchoredPosition = offset;
            _frame.sizeDelta = new Vector2(size, size);
        }

        /// <summary>
        /// One texel per module, drawn with point filtering so every module stays a hard
        /// square at any scale — a smoothed QR code is one a phone cannot read.
        /// </summary>
        private static Texture2D Render(bool[,] modules)
        {
            int n = modules.GetLength(0);
            int side = n + QuietZone * 2;

            var texture = new Texture2D(side, side, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[side * side];
            var light = new Color32(255, 255, 255, 255);
            var dark = new Color32(9, 10, 11, 255);

            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    int row = y - QuietZone;
                    int col = x - QuietZone;
                    bool isDark = row >= 0 && row < n && col >= 0 && col < n && modules[row, col];
                    // Textures run bottom-up; the grid runs top-down.
                    pixels[(side - 1 - y) * side + x] = isDark ? dark : light;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
