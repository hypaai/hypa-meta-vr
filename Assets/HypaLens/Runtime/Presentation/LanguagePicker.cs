using System;
using System.Collections.Generic;
using HypaCore;
using UnityEngine;

namespace HypaLens
{
    /// <summary>
    /// The language list: a clean card in the popup style. Title and X, the total ("160 languages"), eight languages a
    /// page in two roomy columns (the current one filled in), and a pager with Stories' chevrons either side of
    /// "9–16 of 160", so it is always clear how many there are and where you are. Optionally offers "Auto-detect" first.
    /// </summary>
    public sealed class LanguagePicker
    {
        const int PerPage = 8;
        const float W = 0.50f, H = 0.42f, Pad = 0.03f;
        const float RowW = 0.205f, RowH = 0.046f, RowPitch = 0.058f, ColX = 0.112f;
        static readonly Color RowBg = new Color(0.21f, 0.21f, 0.21f, 1f); // lighter than the card, so each row reads as a button
        static readonly Color Muted = new Color(1f, 1f, 1f, 0.6f);

        readonly Transform _root;
        readonly List<GameObject> _rows = new List<GameObject>();
        readonly IReadOnlyList<HypaLanguage> _all;
        readonly bool _offerAuto;
        readonly Action<string> _onPick; // null = auto
        readonly string _current;        // the selected language, filled in
        readonly TextMesh _range;
        int _page;

        int Count => _all.Count + (_offerAuto ? 1 : 0);
        int Pages => Mathf.Max(1, (Count + PerPage - 1) / PerPage);

        public LanguagePicker(Transform parent, Vector3 localPos, string title, IReadOnlyList<HypaLanguage> all, string current, bool offerAuto, Action<string> onPick)
        {
            _all = all; _offerAuto = offerAuto; _onPick = onPick; _current = current;
            _root = new GameObject("Language Picker").transform;
            _root.SetParent(parent, false);
            _root.localPosition = localPos;

            // Behind the buttons; its collider stops rays reaching anything behind the card.
            Ui.Quad("bg", _root, new Vector3(0, 0, 0.003f), new Vector2(W, H), Ui.Panel, true).AddComponent<PinchTarget>();
            var left = -W / 2f + Pad;
            var top = H / 2f - Pad - 0.012f;
            Ui.Text(_root, title, 0.022f, new Vector3(left, top, 0), Color.white, TextAnchor.MiddleLeft);
            Ui.IconButton("x", _root, new Vector3(W / 2f - Pad - 0.022f, top, -0.001f), 0.022f, Close);
            Ui.Text(_root, LanguagesTotal(all.Count), 0.0135f, new Vector3(left, top - 0.034f, 0), Muted, TextAnchor.MiddleLeft);

            // Pager: chevrons at the edges, the range in the middle.
            var footY = -H / 2f + Pad + 0.022f;
            Ui.IconButton("chevron-left", _root, new Vector3(-W / 2f + Pad + 0.022f, footY, -0.001f), 0.022f, () => Show(_page - 1));
            Ui.IconButton("chevron-right", _root, new Vector3(W / 2f - Pad - 0.022f, footY, -0.001f), 0.022f, () => Show(_page + 1));
            _range = Ui.Text(_root, "", 0.0145f, new Vector3(0f, footY, 0), Muted);

            var idx = 0;
            for (var i = 0; i < all.Count; i++) if (all[i].Name == current) idx = i + (offerAuto ? 1 : 0);
            Show(idx / PerPage);
        }

        public bool IsOpen => _root != null;

        /// <summary>"160 languages" (or "1 language"). Pure, unit tested.</summary>
        public static string LanguagesTotal(int n) => n == 1 ? "1 language" : $"{n} languages";

        /// <summary>"9–16 of 160": which languages this page shows, out of how many. Pure, unit tested.</summary>
        public static string RangeLabel(int page, int perPage, int count)
        {
            if (count <= 0) return "0 of 0";
            var first = page * perPage + 1;
            var last = Mathf.Min(count, first + perPage - 1);
            return $"{first}–{last} of {count}";
        }

        void Show(int page)
        {
            _page = (page % Pages + Pages) % Pages;
            foreach (var r in _rows) UnityEngine.Object.Destroy(r);
            _rows.Clear();
            var firstRowY = H / 2f - Pad - 0.012f - 0.034f - 0.044f;
            for (var k = 0; k < PerPage; k++)
            {
                var i = _page * PerPage + k;
                if (i >= Count) break;
                var auto = _offerAuto && i == 0;
                var name = auto ? null : _all[i - (_offerAuto ? 1 : 0)].Name;
                var x = k % 2 == 0 ? -ColX : ColX;
                var y = firstRowY - (k / 2) * RowPitch;
                var selected = !auto && name == _current;
                var btn = Ui.Button(auto ? "Auto-detect" : name, _root, new Vector3(x, y, -0.001f), new Vector2(RowW, RowH), selected ? Ui.Accent : RowBg, () =>
                {
                    _onPick(name);
                    Close();
                });
                _rows.Add(btn.transform.parent.gameObject);
            }
            _range.text = RangeLabel(_page, PerPage, Count);
        }

        public void Close() { if (_root != null) UnityEngine.Object.Destroy(_root.gameObject); }
    }
}
