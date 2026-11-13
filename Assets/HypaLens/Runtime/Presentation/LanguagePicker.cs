using System;
using System.Collections.Generic;
using HypaCore;
using UnityEngine;

namespace HypaLens
{
    /// <summary>
    /// Paged, pinchable language list (the API returns ~150 languages, too many to cycle through).
    /// Eight per page in two columns, with previous/next. Optionally offers "Auto-detect" first.
    /// </summary>
    public sealed class LanguagePicker
    {
        const int PerPage = 8;
        readonly Transform _root;
        readonly List<GameObject> _rows = new List<GameObject>();
        readonly IReadOnlyList<HypaLanguage> _all;
        readonly bool _offerAuto;
        readonly Action<string> _onPick; // null = auto
        readonly TextMesh _pageText;
        int _page;

        int Count => _all.Count + (_offerAuto ? 1 : 0);
        int Pages => Mathf.Max(1, (Count + PerPage - 1) / PerPage);

        public LanguagePicker(Transform parent, Vector3 localPos, string title, IReadOnlyList<HypaLanguage> all, string current, bool offerAuto, Action<string> onPick)
        {
            _all = all; _offerAuto = offerAuto; _onPick = onPick;
            _root = new GameObject("Language Picker").transform;
            _root.SetParent(parent, false);
            _root.localPosition = localPos;
            // Slightly behind the buttons so rays hit buttons first; its collider blocks labels behind the picker.
            Ui.Quad("bg", _root, new Vector3(0, 0, 0.003f), new Vector2(0.42f, 0.36f), Ui.Panel, true);
            Ui.Text(_root, title, 0.022f, new Vector3(0, 0.128f, 0), Color.white);
            Ui.Button("<", _root, new Vector3(-0.15f, -0.142f, 0), new Vector2(0.06f, 0.044f), Ui.Muted, () => Show(_page - 1));
            Ui.Button(">", _root, new Vector3(0.15f, -0.142f, 0), new Vector2(0.06f, 0.044f), Ui.Muted, () => Show(_page + 1));
            Ui.Button("Close", _root, new Vector3(0.04f, -0.142f, 0), new Vector2(0.09f, 0.044f), Ui.Muted, Close);
            _pageText = Ui.Text(_root, "", 0.016f, new Vector3(-0.06f, -0.142f, 0), Color.white);

            var idx = 0;
            for (var i = 0; i < all.Count; i++) if (all[i].Name == current) idx = i + (offerAuto ? 1 : 0);
            Show(idx / PerPage);
        }

        public bool IsOpen => _root != null;

        void Show(int page)
        {
            _page = (page % Pages + Pages) % Pages;
            foreach (var r in _rows) UnityEngine.Object.Destroy(r);
            _rows.Clear();
            for (var k = 0; k < PerPage; k++)
            {
                var i = _page * PerPage + k;
                if (i >= Count) break;
                var auto = _offerAuto && i == 0;
                var name = auto ? null : _all[i - (_offerAuto ? 1 : 0)].Name;
                var col = k % 2 == 0 ? -0.095f : 0.095f;
                var row = 0.084f - (k / 2) * LensTouch.PickerPitch;
                var btn = Ui.Button(auto ? "Auto-detect" : name, _root, new Vector3(col, row, 0), new Vector2(LensTouch.PickerRowW, LensTouch.PickerRowH), Ui.Muted, () =>
                {
                    _onPick(name);
                    Close();
                });
                _rows.Add(btn.transform.parent.gameObject);
            }
            _pageText.text = $"{_page + 1}/{Pages}";
        }

        public void Close() { if (_root != null) UnityEngine.Object.Destroy(_root.gameObject); }
    }
}
