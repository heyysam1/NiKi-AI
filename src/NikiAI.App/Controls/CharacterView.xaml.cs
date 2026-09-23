using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using NikiAI.Character;
using NikiAI.Core.Character;

namespace NikiAI.App.Controls;

/// <summary>
/// Interaction logic for CharacterView.xaml.
/// Renders 2D pixel-art character animations with nearest-neighbor scaling and decoupled state presentation.
/// </summary>
public partial class CharacterView : System.Windows.Controls.UserControl
{
    private static readonly Dictionary<string, BitmapImage> ImageCache = new(StringComparer.OrdinalIgnoreCase);

    private CharacterAnimationController? _controller;
    private ICharacterStateMachine? _stateMachine;
    private ICharacterRegistry? _registry;

    public CharacterView()
    {
        InitializeComponent();
    }

    public System.Windows.Controls.Image ImageControl => SpriteImage;

    public void Initialize(
        CharacterAnimationController controller,
        ICharacterStateMachine stateMachine,
        ICharacterRegistry registry)
    {
        _controller = controller;
        _stateMachine = stateMachine;
        _registry = registry;

        _controller.FrameChanged += OnFrameChanged;
        _controller.AnimationStateChanged += OnAnimationStateChanged;

        if (_controller.CurrentAnimation.Frames.Count > 0)
        {
            DisplayFrame(_controller.CurrentAnimation.GetFrame(_controller.CurrentFrameIndex));
        }
    }

    public void SetSpriteDimensions(double size)
    {
        SpriteImage.Width = size;
        SpriteImage.Height = size;
    }

    private void OnFrameChanged(object? sender, SpriteFrame frame)
    {
        if (Dispatcher.CheckAccess())
        {
            DisplayFrame(frame);
        }
        else
        {
            Dispatcher.Invoke(() => DisplayFrame(frame));
        }
    }

    private void OnAnimationStateChanged(object? sender, CharacterState state)
    {
        // State updates are conveyed strictly via pixel sprite animations and ExpressionOverlayControl.
    }

    public void DisplayFrame(SpriteFrame frame)
    {
        if (string.IsNullOrEmpty(frame.ImagePath)) return;

        var image = LoadCachedBitmap(frame.ImagePath);
        if (image != null)
        {
            SpriteImage.Source = image;
        }
    }

    private BitmapImage? LoadCachedBitmap(string path)
    {
        if (ImageCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        string resolvedPath = path;
        if (!File.Exists(resolvedPath))
        {
            resolvedPath = Path.Combine(AppContext.BaseDirectory, path);
        }
        if (!File.Exists(resolvedPath))
        {
            resolvedPath = Path.Combine(Directory.GetCurrentDirectory(), path);
        }

        if (!File.Exists(resolvedPath))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(Path.GetFullPath(resolvedPath), UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            ImageCache[path] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void OnCharacterClicked(object sender, MouseButtonEventArgs e)
    {
        // Interactive click reaction: briefly transitions to Happy reaction, then returns to Idle
        if (_stateMachine != null)
        {
            _stateMachine.TriggerReaction(CharacterState.Happy, TimeSpan.FromSeconds(2), CharacterState.Idle);
        }
    }
}
