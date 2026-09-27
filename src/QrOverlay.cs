using TMPro;
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
        /// <summary>
        /// Top right, a tenth of the screen's height down: the score screen and the menu
        /// after a failed song. The only place the caption is drawn.
        /// </summary>
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
    ///
    /// Under the code on the score screens goes the caption: "NEXT PLAYER:", the guest's
    /// emoji as a picture and their name in their colour, and the next song. Set in the
    /// font of a text YARG itself is showing, so it reads as part of the game.
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

        private const float CodeSize = 220;
        /// <summary>The code's centre from the corner anchor, in reference units.</summary>
        private static readonly Vector2 CornerOffset = new Vector2(-150, -150);
        private const float CaptionGap = 12;
        private const float SongMaxWidth = 520;

        private readonly RectTransform   _caption;
        private readonly GameObject      _playerRow;
        private readonly TextMeshProUGUI _playerLabel;
        private readonly RawImage        _playerEmoji;
        private readonly TextMeshProUGUI _playerName;
        private readonly TextMeshProUGUI _song;
        private readonly LayoutElement   _songLayout;
        private Texture2D           _emojiTexture;
        private QrCaption.Caption   _captionData;
        private bool                _fontApplied;
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

            // The caption: a dark panel, two rows, sized to its text.
            var caption = new GameObject("Caption");
            caption.transform.SetParent(_root.transform, false);
            _caption = caption.AddComponent<RectTransform>();
            var panel = caption.AddComponent<Image>();
            panel.color = new Color(0.01f, 0.02f, 0.05f, 0.78f);
            panel.raycastTarget = false;
            var column = caption.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(16, 16, 10, 12);
            column.spacing = 4;
            column.childAlignment = TextAnchor.UpperRight;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            var fitter = caption.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _playerRow = new GameObject("Player");
            _playerRow.transform.SetParent(caption.transform, false);
            var row = _playerRow.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 8;
            row.childAlignment = TextAnchor.MiddleRight;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            _playerLabel = Text(_playerRow.transform, "Label", 22, new Color32(0xC7, 0xE0, 0xFF, 0xFF));
            _playerLabel.text = "NEXT PLAYER:";

            var emoji = new GameObject("Emoji");
            emoji.transform.SetParent(_playerRow.transform, false);
            _playerEmoji = emoji.AddComponent<RawImage>();
            _playerEmoji.raycastTarget = false;
            var emojiLayout = emoji.AddComponent<LayoutElement>();
            emojiLayout.preferredWidth = 34;
            emojiLayout.preferredHeight = 34;

            _playerName = Text(_playerRow.transform, "Name", 28, Color.white);
            _playerName.fontStyle = FontStyles.Bold;

            _song = Text(caption.transform, "Song", 22, Color.white);
            _songLayout = _song.gameObject.AddComponent<LayoutElement>();

            caption.SetActive(false);
            _root.SetActive(false);
        }

        /// <summary>One line of plain text: no rich-text tags, so a name can't restyle itself.</summary>
        private static TextMeshProUGUI Text(Transform parent, string name, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = color;
            text.richText = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.Right;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// Replaces the caption, or with null takes it away. Shown only on the score screens.
        /// </summary>
        public void SetCaption(QrCaption.Caption caption)
        {
            _captionData = caption;

            if (_emojiTexture != null) Object.Destroy(_emojiTexture);
            _emojiTexture = null;

            if (caption != null)
            {
                _playerRow.SetActive(caption.HasPlayer);
                if (caption.HasPlayer)
                {
                    _playerName.text = caption.PlayerName;
                    _playerName.color = ColorUtility.TryParseHtmlString(caption.PlayerColor, out var color)
                        ? color
                        : Color.white;

                    if (caption.PlayerImage != null)
                    {
                        _emojiTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!_emojiTexture.LoadImage(caption.PlayerImage))
                        {
                            Object.Destroy(_emojiTexture);
                            _emojiTexture = null;
                        }
                    }
                    _playerEmoji.texture = _emojiTexture;
                    _playerEmoji.gameObject.SetActive(_emojiTexture != null);
                }

                _song.gameObject.SetActive(caption.Song != null);
                if (caption.Song != null)
                {
                    _song.text = caption.Song;
                    // Long titles end in an ellipsis rather than run off the screen.
                    _songLayout.preferredWidth = Mathf.Min(_song.GetPreferredValues(caption.Song).x, SongMaxWidth);
                }
            }

            Refresh();
        }

        /// <summary>
        /// YARG's own font, taken from a text it is showing, so the caption matches the game.
        /// TextMeshPro's default font is the fallback when none can be found.
        /// </summary>
        private void ApplyFont()
        {
            if (_fontApplied) return;

            TMP_FontAsset font = null;
            foreach (var text in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (text.font != null && !text.transform.IsChildOf(_root.transform))
                {
                    font = text.font;
                    break;
                }
            }
            font ??= TMP_Settings.defaultFontAsset;
            if (font == null) return;

            _playerLabel.font = font;
            _playerName.font = font;
            _song.font = font;
            _fontApplied = true;
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
            if (_emojiTexture != null) Object.Destroy(_emojiTexture);
            Object.Destroy(_root);
        }

        private void Refresh()
        {
            var visible = _texture != null && _placement != QrPlacement.Hidden;
            _root.SetActive(visible);

            var showCaption = visible && _placement == QrPlacement.Corner && _captionData != null;
            _caption.gameObject.SetActive(showCaption);
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
                    // A tenth of the screen's height below the top: an anchor, so a tenth on
                    // any aspect ratio. The caption hangs from the code's bottom right corner.
                    var anchor = new Vector2(1, 0.9f);
                    Anchor(anchor, CornerOffset, CodeSize);
                    if (showCaption)
                    {
                        ApplyFont();
                        _caption.anchorMin = anchor;
                        _caption.anchorMax = anchor;
                        _caption.pivot = new Vector2(1, 1);
                        _caption.anchoredPosition = new Vector2(
                            CornerOffset.x + CodeSize / 2,
                            CornerOffset.y - CodeSize / 2 - CaptionGap);
                        LayoutRebuilder.ForceRebuildLayoutImmediate(_caption);
                    }
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
