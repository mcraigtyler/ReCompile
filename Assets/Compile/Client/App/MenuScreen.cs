using Compile.Client.Table;
using Compile.Client.UI;
using UnityEngine;

namespace Compile.Client.App
{
    // A screen that is a single panel resting on the desk. The camera frames the panel, so the same
    // layout works on wide and tall screens.
    public abstract class MenuScreen : TableScreen
    {
        const float Pitch = 28f;

        protected RectTransform Panel { get; private set; }

        protected virtual float Width => 1000f;
        protected virtual Vector2 Spot => MatLayout.MenuSpot;
        protected virtual float RevealBeyond => 3.5f;

        protected abstract void Build(RectTransform panel);

        protected override void OnEnter()
        {
            var canvas = Ui.WorldCanvas(GetType().Name, App.Table, App.Camera, sortingOrder: 20);
            Panel = (RectTransform)canvas.transform;
            Panel.sizeDelta = new Vector2(Width, 600f);
            Panel.localPosition = new Vector3(Spot.x, Spot.y, -0.03f);   // just above the mat and its cards

            Ui.Backdrop(Panel, Palette.PanelFill, Palette.AccentDim);
            Ui.VStack(Panel, spacing: 22f, padX: 72, padY: 64);
            var fitter = Panel.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            Build(Panel);

            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(Panel);
            App.CameraRig.Show(Frame);
            PanelMotion.In(Panel);
        }

        protected override void OnExit()
        {
            if (Panel != null)
                PanelMotion.Out(Panel);
        }

        // Called every frame while this screen is shown, so the framing follows panel size changes.
        protected virtual CameraShot Frame()
        {
            if (Panel == null)
                return new CameraShot(new Rect(Spot - new Vector2(5f, 3f), new Vector2(10f, 6f)), Pitch);

            // Frame the panel plus a strip of table beyond it, so the near edge of the mat shows behind it.
            var size = Panel.sizeDelta / Ui.PixelsPerUnit;
            var area = new Rect(Spot - size / 2f, size);
            area.yMax += RevealBeyond;
            return new CameraShot(area, Pitch, margin: 0.06f);
        }
    }

    // Panels arrive by settling onto the desk and leave by lifting away.
    public sealed class PanelMotion : MonoBehaviour
    {
        const float Seconds = 0.4f;

        Vector3 _from, _to;
        float _alphaFrom, _alphaTo, _t;
        bool _destroyWhenDone;
        CanvasGroup _group;

        public static void In(RectTransform panel) =>
            Start(panel, panel.localPosition + new Vector3(0f, -0.6f, -1.2f), panel.localPosition, 0f, 1f, false);

        public static void Out(RectTransform panel)
        {
            panel.name += " (leaving)";
            Start(panel, panel.localPosition, panel.localPosition + new Vector3(0f, 0.8f, -0.8f), 1f, 0f, true);
        }

        static void Start(RectTransform panel, Vector3 from, Vector3 to, float alphaFrom, float alphaTo, bool destroy)
        {
            if (!panel.TryGetComponent<PanelMotion>(out var motion))
                motion = panel.gameObject.AddComponent<PanelMotion>();
            motion._group = panel.GetComponent<CanvasGroup>();
            motion._from = from;
            motion._to = to;
            motion._alphaFrom = alphaFrom;
            motion._alphaTo = alphaTo;
            motion._destroyWhenDone = destroy;
            motion._t = 0f;
            motion.enabled = true;   // it disables itself after arriving
            motion._group.interactable = !destroy;
            motion._group.blocksRaycasts = !destroy;
            motion.Step();
        }

        void Update() => Step();

        void Step()
        {
            _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime / Seconds);
            var k = Ease.OutCubic(_t);
            transform.localPosition = Vector3.Lerp(_from, _to, k);
            _group.alpha = Mathf.Lerp(_alphaFrom, _alphaTo, k);

            if (_t < 1f)
                return;
            if (_destroyWhenDone)
                Destroy(gameObject);
            else
                enabled = false;
        }
    }
}
