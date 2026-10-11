namespace Compile.Client.App
{
    // A screen is one step of the flow (Title, Lobby, Draft, ...). Screens build their own objects on
    // the table when entered and remove them when left.
    public abstract class TableScreen
    {
        protected AppContext App { get; private set; }

        internal void Enter(AppContext app)
        {
            App = app;
            OnEnter();
        }

        internal void Exit() => OnExit();

        protected abstract void OnEnter();
        protected virtual void OnExit() { }
        public virtual void Tick(float deltaTime) { }
    }

    public sealed class Navigator
    {
        readonly AppContext _app;

        public TableScreen Current { get; private set; }

        public Navigator(AppContext app) => _app = app;

        public void Go(TableScreen next)
        {
            Current?.Exit();
            _app.Hud.Clear();
            Current = next;
            next.Enter(_app);
        }

        public void Tick(float deltaTime) => Current?.Tick(deltaTime);
    }
}
