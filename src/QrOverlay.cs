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
        /// <summary>In the music library's header, between its title and the search bar.</summary>
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
        private readonly RectTransform _frame;
        private readonly RawImage      _image;
        private Texture2D   _texture;
        private QrPlacement _placement = QrPlacement.Hidden;

        public QrOverlay()
        {
            _root = new GameObject("YargSetlistBridge.QrOverlay");
            Object.DontDestroyOnLoad(_root);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over YARG's own canvases, which stay well below this.
            canvas.sortingOrder = 30000;

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

        public void Place(QrPlacement placement)
        {
            if (placement == _placement) return;
            _placement = placement;
            Refresh();
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
                    // Right of the menu's buttons, centred on the screen's height.
                    Anchor(new Vector2(1, 0.5f), new Vector2(-220, 0), 320);
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
