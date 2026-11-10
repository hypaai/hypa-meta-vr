using System;
using System.Threading;
using UnityEngine;

namespace HypaCore.Samples
{
    /// <summary>
    /// HypaCoreTest scene: translates a sentence and speaks it through the live Hypa API (translate_message +
    /// text_to_speech over MCP). Needs a key in hypa.local.json. Pinch "Run" (or press R) to repeat; pinch the language
    /// button to change the target (saved in HypaSettings).
    /// </summary>
    public sealed class HypaCoreTest : MonoBehaviour
    {
        [SerializeField] string sentence = "Good morning. Where is the market?";
        [SerializeField] bool runOnStart = true;

        HypaMcpClient _mcp;
        HypaLanguages _langs;
        TextMesh _out, _langLabel;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        bool _busy;

        void Start()
        {
            PinchPointer.EnsureExists();
            _mcp = new HypaMcpClient();
            _langs = new HypaLanguages(_mcp);
            var cam = Camera.main.transform;
            var root = new GameObject("Core Test UI").transform;
            root.position = cam.position + cam.forward * 0.7f;
            root.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
            Ui.Quad("bg", root, Vector3.zero, new Vector2(0.56f, 0.32f), Ui.Panel, false);
            _out = Ui.Text(root, "", 0.022f, new Vector3(0, 0.04f, 0), Color.white);
            Ui.Button("Run", root, new Vector3(-0.15f, -0.12f, 0), new Vector2(0.14f, 0.05f), Ui.Accent, () => Run());
            var lang = Ui.Button("To: " + HypaSettings.TargetLanguage, root, new Vector3(0.1f, -0.12f, 0), new Vector2(0.24f, 0.05f), Ui.Muted, () => NextLanguage());
            _langLabel = Ui.LabelOf(lang);
            if (runOnStart) Run();
        }

        void Update() { if (Application.isEditor && Input.GetKeyDown(KeyCode.R)) Run(); }

        async void NextLanguage()
        {
            try
            {
                var list = await _langs.GetAsync(HypaFeature.Tts, _cts.Token);
                if (list.Count == 0) return;
                var i = -1;
                for (var k = 0; k < list.Count; k++) if (list[k].Name == HypaSettings.TargetLanguage) i = k;
                HypaSettings.TargetLanguage = list[(i + 1) % list.Count].Name;
                _langLabel.text = "To: " + HypaSettings.TargetLanguage;
            }
            catch (Exception e) { Show(e); }
        }

        async void Run()
        {
            if (_busy) return;
            _busy = true;
            var target = HypaSettings.TargetLanguage;
            try
            {
                _out.text = Ui.Wrap($"\"{sentence}\"\n-> {target}...", 44);
                var t = await _mcp.TranslateAsync(sentence, target, null, _cts.Token);
                _out.text = Ui.Wrap($"\"{sentence}\"\n({t.DetectedLanguage ?? "source unknown"} -> {target})\n\n{t.Text}\n\nSpeaking...", 44);
                var speech = await _mcp.TextToSpeechAsync(t.Text, target, _cts.Token);
                await HypaAudioPlayer.Instance.PlayNowAsync(speech, _cts.Token);
                _out.text = Ui.Wrap($"\"{sentence}\"\n({t.DetectedLanguage ?? "source unknown"} -> {target})\n\n{t.Text}\n\nOK: translated and spoken ({speech.Mime}).", 44);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Show(e); }
            finally { _busy = false; }
        }

        void Show(Exception e)
        {
            if (_out == null) return;
            _out.text = Ui.Wrap(e is HypaException he ? he.UserMessage() : e.Message, 44);
            Debug.LogWarning("HypaCoreTest: " + e.Message);
        }

        void OnDestroy() { _cts.Cancel(); _cts.Dispose(); }
    }
}
