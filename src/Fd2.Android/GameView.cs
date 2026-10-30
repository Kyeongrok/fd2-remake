using System.Collections.Concurrent;

using Android.Content;
using Android.Graphics;
using Android.Views;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 게임 화면 — 뒤 스레드에서 틀마다 <see cref="GameWindow.Step"/> 한 뒤 그 화면(640 x 화면 비율)을 점 그대로 늘려 SurfaceView 에 찍는다.
/// </summary>
/// <remarks>
/// 손가락: 누르고 떼면 그 자리를 누른 것(마우스 왼쪽), 0.5초 넘게 누르고 있으면 물리기(오른쪽 단추).
/// 키: 방향 · 확인(Enter) · 뒤로(Esc), 게임 패드 A 결정 · B 취소 · X 공격 · Y 대기 · Start 시스템 메뉴 · R1 다음 아군 · L1 체력바 · Select 단축키 목록.
/// 입력은 UI 스레드에서 받아 줄에 넣고, 게임 스레드가 틀마다 꺼내 쓴다.
/// </remarks>
public sealed class GameView : SurfaceView, ISurfaceHolderCallback
{
    private const int FrameW = 640;
    private const double LongPress = 0.5;

    private readonly Activity _activity;
    private readonly string _dir;
    private readonly ConcurrentQueue<Action<GameWindow>> _input = new();
    private Thread? _thread;
    private volatile bool _alive;
    private volatile int _surfaceW, _surfaceH;

    // 손가락 — 누른 때 · 자리(화면 점), 길게 누르기로 이미 물렸나
    private long _downAt = -1;
    private float _downX, _downY;
    private bool _longDone;
    private Rect _dst = new();
    private int _fbW = FrameW, _fbH = 312;

    public GameView(Activity activity, string dir) : base(activity)
    {
        _activity = activity;
        _dir = dir;
        Holder!.AddCallback(this);
        Focusable = true;
        FocusableInTouchMode = true;
    }

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        _alive = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "FD2 game" };
        _thread.Start();
    }

    public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
    {
        _surfaceW = width;
        _surfaceH = height;
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        _alive = false;
        _thread?.Join(1000);
    }

    /// <summary>게임 — 화면(Surface)이 다시 만들어져도(앱이 잠깐 뒤로 갔다 와도) 이어서 쓴다.</summary>
    private GameWindow? _game;

    private void Loop()
    {
        if (_game == null)
        {
            try { _game = new GameWindow(_dir); }
            catch (Exception e) { Fail(e); return; }
            _game.Start();
        }
        var game = _game;
        Bitmap? bitmap = null;
        int[] pixels = [];
        using var paint = new Paint { FilterBitmap = false };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double last = 0;
        int sizedW = 0, sizedH = 0;
        try
        {
            while (_alive && game.Running)
            {
                // 그릴 판 크기 — 가로 640 점, 세로는 화면 비율대로(원본 화면 높이 312 보다 작지 않게)
                if (_surfaceW > 0 && (_surfaceW != sizedW || _surfaceH != sizedH))
                {
                    (sizedW, sizedH) = (_surfaceW, _surfaceH);
                    _fbW = FrameW;
                    _fbH = Math.Max(240, (int)Math.Round(FrameW * (double)sizedH / sizedW));
                    game.SetClientSize(_fbW, _fbH);
                    float s = Math.Min(sizedW / (float)_fbW, sizedH / (float)_fbH);
                    int w = (int)(_fbW * s), h = (int)(_fbH * s);
                    _dst = new Rect((sizedW - w) / 2, (sizedH - h) / 2, (sizedW - w) / 2 + w, (sizedH - h) / 2 + h);
                }

                double now = clock.Elapsed.TotalSeconds;
                double dt = Math.Min(now - last, 0.1);
                last = now;
                long downAt = _downAt;
                if (downAt >= 0 && !_longDone && Clock.ElapsedMilliseconds - downAt > LongPress * 1000)
                {
                    _longDone = true;
                    _input.Enqueue(g => g.PointerCancel());
                }
                while (_input.TryDequeue(out var act)) act(game);
                game.Step(dt);

                int fw = game.FrameWidth, fh = game.FrameHeight;
                if (fw <= 0 || fh <= 0) continue;
                if (bitmap == null || bitmap.Width != fw || bitmap.Height != fh)
                {
                    bitmap?.Dispose();
                    bitmap = Bitmap.CreateBitmap(fw, fh, Bitmap.Config.Argb8888!)!;
                    pixels = new int[fw * fh];
                }
                Buffer.BlockCopy(game.FrameBuffer, 0, pixels, 0, Math.Min(pixels.Length, game.FrameBuffer.Length) * 4);
                bitmap.SetPixels(pixels, 0, fw, 0, 0, fw, fh);
                var canvas = Holder!.LockCanvas();
                if (canvas == null) continue;
                try
                {
                    canvas.DrawColor(Color.Black);
                    canvas.DrawBitmap(bitmap, new Rect(0, 0, fw, fh), _dst, paint);
                }
                finally { Holder.UnlockCanvasAndPost(canvas); }

                double spent = clock.Elapsed.TotalSeconds - now;
                if (spent < 1 / 60.0) Thread.Sleep((int)((1 / 60.0 - spent) * 1000));
            }
        }
        catch (Exception e) { Fail(e); return; }
        finally { bitmap?.Dispose(); }
        if (!game.Running) _activity.RunOnUiThread(_activity.Finish);
    }

    private void Fail(Exception e)
    {
        try
        {
            string log = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "crash.txt");
            File.WriteAllText(log, e.ToString());
        }
        catch (IOException) { }
        _activity.RunOnUiThread(() => _activity.SetContentView(new TextView(_activity) { Text = $"오류로 멈췄습니다.\n{e.GetType().Name}: {e.Message}", TextSize = 16 }));
    }

    /// <summary>화면 점 → 그릴 판 점.</summary>
    private (int X, int Y) ToFrame(float x, float y) =>
        ((int)((x - _dst.Left) * _fbW / Math.Max(1, _dst.Width())), (int)((y - _dst.Top) * _fbH / Math.Max(1, _dst.Height())));

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null) return false;
        var (x, y) = ToFrame(e.GetX(), e.GetY());
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _downAt = Clock.ElapsedMilliseconds;
                (_downX, _downY) = (e.GetX(), e.GetY());
                _longDone = false;
                _input.Enqueue(g => g.PointerMove(x, y));
                return true;
            case MotionEventActions.Move:
                if (Math.Abs(e.GetX() - _downX) > 24 || Math.Abs(e.GetY() - _downY) > 24) _downAt = -1;   // 움직이면 길게 누르기가 아니다
                _input.Enqueue(g => g.PointerMove(x, y));
                return true;
            case MotionEventActions.Up:
                bool tap = !_longDone;
                _downAt = -1;
                if (tap) _input.Enqueue(g => g.PointerDown(x, y));
                return true;
        }
        return base.OnTouchEvent(e);
    }

    /// <summary>손가락 누른 때를 재는 시계 — UI 스레드와 게임 스레드가 함께 본다.</summary>
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>안드로이드 키 → 게임 키(Win32 가상 키 번호). 받았으면 참.</summary>
    public bool Key(Keycode code, bool down)
    {
        int vk = code switch
        {
            Keycode.DpadUp => Win32.VK_UP,
            Keycode.DpadDown => Win32.VK_DOWN,
            Keycode.DpadLeft => Win32.VK_LEFT,
            Keycode.DpadRight => Win32.VK_RIGHT,
            Keycode.Enter or Keycode.DpadCenter or Keycode.ButtonA or Keycode.NumpadEnter => Win32.VK_RETURN,
            Keycode.Back or Keycode.Escape or Keycode.ButtonB => Win32.VK_ESCAPE,
            Keycode.Space => Win32.VK_SPACE,
            Keycode.Tab or Keycode.ButtonR1 => Win32.VK_TAB,
            Keycode.ButtonX => 'E',
            Keycode.ButtonY => 'Q',
            Keycode.ButtonStart => 'X',
            Keycode.ButtonL1 => 'H',
            Keycode.ButtonSelect => 'J',
            >= Keycode.A and <= Keycode.Z => 'A' + (code - Keycode.A),
            _ => 0,
        };
        if (vk == 0) return false;
        if (down) _input.Enqueue(g => g.KeyDown(vk));
        else _input.Enqueue(g => g.KeyUp(vk));
        return true;
    }
}
