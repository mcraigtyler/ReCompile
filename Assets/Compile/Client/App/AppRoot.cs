using Compile.Client.Cards;
using Compile.Client.Screens;
using Compile.Client.Table;
using Compile.Client.UI;
using Compile.Engine.Cards;
using Compile.Net;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Compile.Client.App
{
    // The only object in the Main scene. Builds the table, camera, and UI at startup and runs the screen flow.
    public sealed class AppRoot : MonoBehaviour
    {
        [SerializeField] Theme theme;
        [SerializeField] CardArtLibrary art;
        [SerializeField] TextAsset cardsJson;
        [SerializeField] TextAsset protocolsJson;
        [SerializeField] TextAsset rulesText;

        AppContext _app;

        void Awake()
        {
            Application.targetFrameRate = 60;
            Ui.Init(theme);

            var fake = new FakeSessionService();
            _app = new AppContext
            {
                Theme = theme,
                Art = art,
                Cards = CardDatabase.Parse(cardsJson.text, protocolsJson.text),
                RulesText = rulesText.text,
                Session = fake,
                FakeSession = fake,
                Profile = new PlayerProfile(),
            };

            CreateCamera();
            CreateEventSystem();
            CreateTable();
            CreateHud();
            _app.Nav = new Navigator(_app);

            _app.Session.Failed += OnSessionFailed;
            _app.Nav.Go(new TitleScreen());
        }

        void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _app.FakeSession?.Advance(dt);
            _app.Nav.Tick(dt);
        }

        void OnDestroy()
        {
            if (_app?.Session != null)
                _app.Session.Failed -= OnSessionFailed;
        }

        void OnSessionFailed(SessionError error)
        {
            if (error != SessionError.OpponentLeft)
                return;

            _app.Mat.ClearProtocols();
            _app.Nav.Go(new TitleScreen());
            _app.Hud.Toast("Your opponent left the session.", error: true);
        }

        void CreateCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.SetParent(transform, false);
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.016f, 0.035f);
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 400f;
            go.AddComponent<AudioListener>();

            _app.Camera = camera;
            _app.CameraRig = go.AddComponent<CameraRig>();
            _app.CameraRig.Init(camera);
        }

        void CreateEventSystem()
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.transform.SetParent(transform, false);
        }

        void CreateTable()
        {
            var table = new GameObject("Table").transform;
            table.SetParent(transform, false);
            _app.Table = table;

            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "DeskSurface";
            Destroy(surface.GetComponent<Collider>());
            surface.transform.SetParent(table, false);
            surface.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            surface.transform.localScale = new Vector3(400f, 400f, 1f);
            var renderer = surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = theme.tableMaterial;
            renderer.sortingOrder = -200;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var mat = new GameObject("Mat").AddComponent<MatView>();
            mat.transform.SetParent(table, false);
            mat.Init(theme, art, _app.Camera);
            _app.Mat = mat;
        }

        void CreateHud()
        {
            var go = new GameObject("Hud", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _app.Hud = go.AddComponent<Hud>();
            _app.Hud.Init();
        }
    }
}
