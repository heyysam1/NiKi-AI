using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NikiAI.App.Views;
using NikiAI.Character;
using NikiAI.Core.Character;
using NikiAI.Core.Companion;
using NikiAI.Core.Tasks;
using NikiAI.Core.Memory;
using NikiAI.Core.Lifecycle;
using NikiAI.Core.Voice;
using NikiAI.Core.Workflows;
using NikiAI.Core.Security;
using NikiAI.Widgets.Views;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace NikiAI.App;

/// <summary>
/// Interaction logic for CompanionWindow.xaml.
/// Implements dragging, desktop-bound clamping, sizing presets, opacity control, show/hide,
/// hosts the 2D pixel-art Character Runtime, handles purposeful micro-animations (hover, press, drag),
/// and coordinates with the Casual Idle Behavior and ChatWindow.
/// The companion remains strictly transparent and free-floating with zero boxed containers.
/// </summary>
public partial class CompanionWindow : Window
{
    public CompanionSettings Settings { get; } = new();

    public System.Windows.Controls.MenuItem MenuCharNiki => MenuItemCharNiki;
    public System.Windows.Controls.MenuItem MenuCharBiscuit => MenuItemCharBiscuit;
    public System.Windows.Controls.MenuItem MenuCharMochi => MenuItemCharMochi;
    public Controls.CharacterView CharacterDisplayControl => CharacterDisplay;

    private CharacterAnimationController? _animationController;
    private ICharacterStateMachine? _stateMachine;
    private ICharacterRegistry? _registry;
    private ITaskRepository? _taskRepository;
    private CasualIdleController? _casualIdleController;
    private TaskDetailWindow? _taskDetailWindow;
    private ChatWindow? _chatWindow;
    private IMemoryService? _memoryService;
    private ITimelineRepository? _timelineRepository;
    private IPetContextProvider? _petContextProvider;
    private MemoryManagementWindow? _memoryManagementWindow;
    private IAppLifecycleManager? _lifecycleManager;
    private IVoiceService? _voiceService;
    private IWorkflowRepository? _workflowRepository;
    private IWorkflowEngine? _workflowEngine;
    private ITaskLifecycleSignalHub? _signalHub;
    private WorkflowEditorWindow? _workflowEditorWindow;
    private BehaviorExecutionCoordinator? _behaviorCoordinator;
    private AiBehaviorDirector? _behaviorDirector;
    private ISecureSettingsStore? _secureStore;

    private bool _isDragging;
    private bool _hasMoved;
    private bool _isPressReactionActive;
    private System.Windows.Point _dragStartCursor;
    private System.Windows.Point _dragStartWindow;

    private readonly ScaleTransform _characterScaleTransform = new(1.0, 1.0);
    private readonly TranslateTransform _characterTranslateTransform = new(0, 0);

    public CompanionWindow()
    {
        InitializeComponent();

        var group = new TransformGroup();
        group.Children.Add(_characterScaleTransform);
        group.Children.Add(_characterTranslateTransform);
        CharacterDisplay.RenderTransform = group;

        Loaded += OnWindowLoaded;
        IsVisibleChanged += OnWindowVisibilityChanged;
        StateChanged += OnWindowStateChanged;
        Closing += (s, e) => _lifecycleManager?.HandleWindowClosing(this, e);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, -20); // GWL_EXSTYLE
            SetWindowLong(hwnd, -20, exStyle | 0x08000000); // WS_EX_NOACTIVATE
        }
        catch { }
    }

    public void InitializeCharacterRuntime(
        CharacterAnimationController controller,
        ICharacterStateMachine stateMachine,
        ICharacterRegistry registry)
    {
        _animationController = controller;
        _stateMachine = stateMachine;
        _registry = registry;

        _animationController.ReducedMotion = Settings.ReducedMotion;
        CharacterDisplay.Initialize(controller, stateMachine, registry);

        _registry.ActiveCharacterChanged += (s, profile) =>
        {
            Dispatcher.InvokeAsync(() => UpdateCharacterMenuChecks(profile.Id));
        };

        UpdateCharacterMenuChecks(_registry.ActiveCharacter.Id);
    }

    public void ConnectBehaviorExecution(BehaviorExecutionCoordinator coordinator, AiBehaviorDirector director)
    {
        _behaviorCoordinator = coordinator;
        _behaviorDirector = director;
        _behaviorDirector.ReducedMotion = Settings.ReducedMotion;

        _behaviorCoordinator.VisualOffsetChanged += (s, offset) =>
        {
            Dispatcher.InvokeAsync(() => ApplyVisualPhysicsOffset(offset));
        };

        _behaviorCoordinator.ExpressionTriggered += (s, expr) =>
        {
            Dispatcher.InvokeAsync(() => ExpressionOverlay.ShowExpression(expr));
        };
    }

    public void ApplyVisualPhysicsOffset(PhysicsOffset offset)
    {
        if (offset == null) return;
        _characterTranslateTransform.X = offset.OffsetX;
        _characterTranslateTransform.Y = offset.OffsetY;
        _characterScaleTransform.ScaleX = offset.ScaleX;
        _characterScaleTransform.ScaleY = offset.ScaleY;
    }

    public void InitializeCasualIdle(CasualIdleController casualIdleController)
    {
        _casualIdleController = casualIdleController;
        _casualIdleController.ReducedMotion = Settings.ReducedMotion;
        UpdateAnimationThrottling();
    }

    public void InitializeTaskSystem(ITaskRepository taskRepository)
    {
        _taskRepository = taskRepository;
    }

    public void InitializeMemorySystem(IMemoryService memoryService, ITimelineRepository timelineRepository, IPetContextProvider petContextProvider)
    {
        _memoryService = memoryService;
        _timelineRepository = timelineRepository;
        _petContextProvider = petContextProvider;
    }

    private IPetMovementController? _movementController;
    private DispatcherTimer? _movementTimer;

    public void InitializeDesktopPetMovement(IPetMovementController movementController)
    {
        _movementController = movementController;
        // Sync initial position with companion window's current coordinates
        _movementController.SetPosition(new System.Drawing.Point((int)Left, (int)Top));

        _movementController.PositionChanged += (s, point) =>
        {
            if (!_isDragging)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    Left = point.X;
                    Top = point.Y;
                });
            }
        };

        _movementController.MovementIntentChanged += (s, intent) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (!Settings.ReducedMotion)
                {
                    _characterScaleTransform.ScaleX = intent.FacingLeft ? -Math.Abs(_characterScaleTransform.ScaleX) : Math.Abs(_characterScaleTransform.ScaleX);
                }
            });
        };

        _movementTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _movementTimer.Tick += (s, e) =>
        {
            if (!_isDragging && _movementController != null && Visibility == Visibility.Visible)
            {
                _movementController.Update(TimeSpan.FromMilliseconds(50));
            }
        };
        _movementTimer.Start();
    }

    private MainWindow? _mainWindow;

    public void InitializeMainWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void OpenMainWindow()
    {
        if (_mainWindow == null) return;

        if (!_mainWindow.IsLoaded)
        {
            _mainWindow.Show();
        }
        else
        {
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    public void InitializeChatWindow(ChatWindow chatWindow)
    {
        _chatWindow = chatWindow;
        _chatWindow.OpenTasksAction = OpenTaskDetailWindow;
        _chatWindow.OpenMemoryAction = OpenMemoryManagementWindow;
        _chatWindow.OpenWidgetsAction = ToggleWidgets;
        _chatWindow.OpenWorkflowsAction = OpenWorkflowEditorWindow;
    }

    public void OpenTaskDetailWindow()
    {
        if (_taskRepository == null) return;

        if (_taskDetailWindow == null || !_taskDetailWindow.IsLoaded)
        {
            _taskDetailWindow = new TaskDetailWindow(_taskRepository);
            _taskDetailWindow.Closed += (s, e) => _taskDetailWindow = null;
            _taskDetailWindow.Show();
        }
        else
        {
            if (_taskDetailWindow.WindowState == WindowState.Minimized)
            {
                _taskDetailWindow.WindowState = WindowState.Normal;
            }
            _taskDetailWindow.Activate();
        }
    }

    public void OpenChatWindow()
    {
        if (_mainWindow != null)
        {
            OpenMainWindow();
            return;
        }

        if (_chatWindow == null) return;

        if (!_chatWindow.IsLoaded)
        {
            _chatWindow.Show();
        }
        else
        {
            if (_chatWindow.WindowState == WindowState.Minimized)
            {
                _chatWindow.WindowState = WindowState.Normal;
            }
            _chatWindow.Show();
            _chatWindow.Activate();
        }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        ResetToDefaultBottomRight();
        ApplySettings();
    }

    private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UpdateAnimationThrottling();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        UpdateAnimationThrottling();
    }

    private void UpdateAnimationThrottling()
    {
        bool shouldThrottle = !IsVisible || WindowState == WindowState.Minimized;
        _animationController?.SetThrottled(shouldThrottle);
        _casualIdleController?.SetThrottled(shouldThrottle);
    }

    public void ResetToDefaultBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - Height - 24;
        ClampToDesktopWorkArea();
        _movementController?.SetPosition(new System.Drawing.Point((int)Left, (int)Top));
    }

    public void ApplySizePreset(CompanionSizePreset preset)
    {
        Settings.SetSizePreset(preset);
        Width = Settings.Width;
        Height = Settings.Height;

        // Adjust pixel-art sprite display proportionally with nearest-neighbor crispness
        switch (preset)
        {
            case CompanionSizePreset.Compact:
                CharacterDisplay.SetSpriteDimensions(32);
                break;
            case CompanionSizePreset.Standard:
                CharacterDisplay.SetSpriteDimensions(48);
                break;
            case CompanionSizePreset.Large:
                CharacterDisplay.SetSpriteDimensions(64);
                break;
        }

        // Update ContextMenu radio checkmarks
        MenuItemSizeCompact.IsChecked = preset == CompanionSizePreset.Compact;
        MenuItemSizeStandard.IsChecked = preset == CompanionSizePreset.Standard;
        MenuItemSizeLarge.IsChecked = preset == CompanionSizePreset.Large;

        ClampToDesktopWorkArea();
    }

    public void ApplyOpacity(double opacity)
    {
        Settings.SetOpacity(opacity);
        Opacity = Settings.Opacity;

        MenuItemOpacity100.IsChecked = Math.Abs(opacity - 1.0) < 0.01;
        MenuItemOpacity85.IsChecked = Math.Abs(opacity - 0.85) < 0.01;
        MenuItemOpacity70.IsChecked = Math.Abs(opacity - 0.70) < 0.01;
        MenuItemOpacity50.IsChecked = Math.Abs(opacity - 0.50) < 0.01;
    }

    public void SetAlwaysOnTop(bool alwaysOnTop)
    {
        Settings.AlwaysOnTop = alwaysOnTop;
        Topmost = alwaysOnTop;
        MenuItemAlwaysOnTop.IsChecked = alwaysOnTop;
    }

    public void ToggleVisibility()
    {
        if (Visibility == Visibility.Visible)
        {
            Hide();
        }
        else
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            ClampToDesktopWorkArea();
        }
    }

    public void ClampToDesktopWorkArea()
    {
        var vLeft = SystemParameters.VirtualScreenLeft;
        var vTop = SystemParameters.VirtualScreenTop;
        var vWidth = SystemParameters.VirtualScreenWidth;
        var vHeight = SystemParameters.VirtualScreenHeight;

        var (clampedX, clampedY) = Settings.ClampPosition(
            Left, Top,
            vLeft, vTop,
            vWidth, vHeight
        );

        Left = clampedX;
        Top = clampedY;
    }

    private void ApplySettings()
    {
        ApplySizePreset(Settings.SizePreset);
        ApplyOpacity(Settings.Opacity);
        SetAlwaysOnTop(Settings.AlwaysOnTop);
        SetReducedMotion(Settings.ReducedMotion);
    }

    public void InitializeSettingsStore(ISecureSettingsStore secureStore)
    {
        _secureStore = secureStore;
        try
        {
            var reducedMotionVal = Task.Run(async () => await _secureStore.GetSecretAsync("Companion.ReducedMotion").ConfigureAwait(false)).GetAwaiter().GetResult();
            if (bool.TryParse(reducedMotionVal, out var rm))
            {
                SetReducedMotion(rm);
            }
        }
        catch { }
    }

    public void SetReducedMotion(bool reducedMotion)
    {
        Settings.ReducedMotion = reducedMotion;
        if (MenuItemReducedMotion != null)
        {
            MenuItemReducedMotion.IsChecked = reducedMotion;
        }

        try
        {
            if (_secureStore != null)
            {
                _ = Task.Run(async () => await _secureStore.SetSecretAsync("Companion.ReducedMotion", reducedMotion.ToString()).ConfigureAwait(false));
            }
        }
        catch { }

        if (_animationController != null)
        {
            _animationController.ReducedMotion = reducedMotion;
        }
        if (_casualIdleController != null)
        {
            _casualIdleController.ReducedMotion = reducedMotion;
        }
        if (_behaviorDirector != null)
        {
            _behaviorDirector.ReducedMotion = reducedMotion;
        }
    }

    private void OnReducedMotionToggleClick(object sender, RoutedEventArgs e)
    {
        SetReducedMotion(MenuItemReducedMotion.IsChecked);
    }

    public void SetActiveCharacter(string id)
    {
        _registry?.SetActiveCharacter(id);
        UpdateCharacterMenuChecks(id);

        if (CharacterDisplay != null && _animationController != null)
        {
            var anim = _registry?.ActiveCharacter.GetAnimation(_stateMachine?.CurrentState ?? CharacterState.Idle);
            if (anim != null && anim.Frames.Count > 0)
            {
                CharacterDisplay.DisplayFrame(anim.GetFrame(_animationController.CurrentFrameIndex));
            }
        }
    }

    private void UpdateCharacterMenuChecks(string activeId)
    {
        MenuItemCharNiki.IsChecked = string.Equals(activeId, "niki", StringComparison.OrdinalIgnoreCase);
        MenuItemCharMochi.IsChecked = string.Equals(activeId, "mochi", StringComparison.OrdinalIgnoreCase);
        MenuItemCharBiscuit.IsChecked = string.Equals(activeId, "biscuit", StringComparison.OrdinalIgnoreCase) || string.Equals(activeId, "dog", StringComparison.OrdinalIgnoreCase);
    }

    // Purposeful Micro-Animations (Hover, Press Squash, Drag Posture)
    private void OnWindowMouseEnter(object sender, MouseEventArgs e)
    {
        if (Settings.ReducedMotion) return; // Respect Reduced Motion

        // Subtle lift reaction
        var liftAnim = new DoubleAnimation(0, -3, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        _characterTranslateTransform.BeginAnimation(TranslateTransform.YProperty, liftAnim);
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        if (Settings.ReducedMotion) return;

        // Smooth return to baseline
        var returnAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        _characterTranslateTransform.BeginAnimation(TranslateTransform.YProperty, returnAnim);
    }

    private void OnWindowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 && e.ChangedButton == MouseButton.Left)
        {
            OpenChatWindow();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            _petContextProvider?.RecordUserInteraction();
            _isDragging = true;
            _hasMoved = false;
            _dragStartCursor = PointToScreen(e.GetPosition(this));
            _dragStartWindow = new System.Windows.Point(Left, Top);
            CaptureMouse();

            // Tactile Squash Feedback (Scale Y: 0.94, Scale X: 1.05)
            if (!Settings.ReducedMotion)
            {
                _isPressReactionActive = true;
                var squashY = new DoubleAnimation(1.0, 0.94, TimeSpan.FromMilliseconds(80))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                var squashX = new DoubleAnimation(1.0, 1.05, TimeSpan.FromMilliseconds(80))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                _characterScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, squashY);
                _characterScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, squashX);
            }
        }
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
        {
            var currentCursor = PointToScreen(e.GetPosition(this));
            var deltaX = currentCursor.X - _dragStartCursor.X;
            var deltaY = currentCursor.Y - _dragStartCursor.Y;

            // When moved beyond a slight threshold, switch to Walk state for active moving posture
            if (Math.Abs(deltaX) > 4 || Math.Abs(deltaY) > 4)
            {
                _hasMoved = true;
                Left = _dragStartWindow.X + deltaX;
                Top = _dragStartWindow.Y + deltaY;

                ClampToDesktopWorkArea();

                if (_stateMachine?.CurrentState == CharacterState.Idle)
                {
                    _stateMachine.SetState(CharacterState.Walk);
                }
            }
        }
    }

    private void OnWindowMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();

            if (_hasMoved)
            {
                ClampToDesktopWorkArea();
                _movementController?.SetPosition(new System.Drawing.Point((int)Left, (int)Top));

                // Restore Walk posture to Idle on drag release
                if (_stateMachine?.CurrentState == CharacterState.Walk)
                {
                    _stateMachine.SetState(CharacterState.Idle);
                }
            }
            else
            {
                // Single click without drag: trigger interaction and open Chat workspace
                _behaviorDirector?.TriggerUserInteraction();
                OpenChatWindow();
            }

            // Restore Squash to 1.0 with subtle overshoot ease
            if (_isPressReactionActive && !Settings.ReducedMotion)
            {
                _isPressReactionActive = false;
                var restoreY = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 }
                };
                var restoreX = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 }
                };
                _characterScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, restoreY);
                _characterScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, restoreX);
            }
        }
        else
        {
            _behaviorDirector?.TriggerUserInteraction();
            OpenChatWindow();
        }
    }

    // Context Menu: Character Selection
    private void OnSelectCharacterNikiClick(object sender, RoutedEventArgs e) => SetActiveCharacter("niki");
    private void OnSelectCharacterMochiClick(object sender, RoutedEventArgs e) => SetActiveCharacter("mochi");
    private void OnSelectCharacterBiscuitClick(object sender, RoutedEventArgs e) => SetActiveCharacter("biscuit");

    // Context Menu: State Preview
    private void OnStateIdleClick(object sender, RoutedEventArgs e) => _stateMachine?.SetState(CharacterState.Idle);
    private void OnStateListeningClick(object sender, RoutedEventArgs e) => _stateMachine?.SetState(CharacterState.Listening);
    private void OnStateThinkingClick(object sender, RoutedEventArgs e) => _stateMachine?.SetState(CharacterState.Thinking);
    private void OnStateWorkingClick(object sender, RoutedEventArgs e) => _stateMachine?.SetState(CharacterState.Working);
    private void OnStateHappyClick(object sender, RoutedEventArgs e) => _stateMachine?.SetState(CharacterState.Happy);
    private void OnStateNotificationClick(object sender, RoutedEventArgs e) => _stateMachine?.SetState(CharacterState.Notification);
    private void OnStateSleepClick(object sender, RoutedEventArgs e) => _stateMachine?.SetState(CharacterState.Sleep);

    // Context Menu: Sizing
    private void OnSizeCompactClick(object sender, RoutedEventArgs e) => ApplySizePreset(CompanionSizePreset.Compact);
    private void OnSizeStandardClick(object sender, RoutedEventArgs e) => ApplySizePreset(CompanionSizePreset.Standard);
    private void OnSizeLargeClick(object sender, RoutedEventArgs e) => ApplySizePreset(CompanionSizePreset.Large);

    // Context Menu: Opacity
    private void OnOpacity100Click(object sender, RoutedEventArgs e) => ApplyOpacity(1.0);
    private void OnOpacity85Click(object sender, RoutedEventArgs e) => ApplyOpacity(0.85);
    private void OnOpacity70Click(object sender, RoutedEventArgs e) => ApplyOpacity(0.70);
    private void OnOpacity50Click(object sender, RoutedEventArgs e) => ApplyOpacity(0.50);

    // Context Menu: Window controls
    private void OnAlwaysOnTopToggleClick(object sender, RoutedEventArgs e) => SetAlwaysOnTop(MenuItemAlwaysOnTop.IsChecked);
    private void OnResetPositionClick(object sender, RoutedEventArgs e) => ResetToDefaultBottomRight();
    private void OnHidePetClick(object sender, RoutedEventArgs e) => _lifecycleManager?.HidePet();
    private void OnStartWithWindowsToggleClick(object sender, RoutedEventArgs e)
    {
        if (_lifecycleManager != null)
        {
            _lifecycleManager.StartWithWindows = MenuItemStartWithWindows.IsChecked;
        }
    }
    private void OnCloseMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (_lifecycleManager != null)
        {
            _lifecycleManager.CloseBehavior = AppCloseBehavior.MinimizeToTray;
            UpdateLifecycleMenuChecks();
        }
    }
    private void OnCloseExitClick(object sender, RoutedEventArgs e)
    {
        if (_lifecycleManager != null)
        {
            _lifecycleManager.CloseBehavior = AppCloseBehavior.ExitApplication;
            UpdateLifecycleMenuChecks();
        }
    }
    private void OnExitMenuItemClick(object sender, RoutedEventArgs e) => _lifecycleManager?.ExitApplication();

    // Voice Interaction (Phase 12)
    public void InitializeVoice(IVoiceService voiceService)
    {
        _voiceService = voiceService;
    }

    private async void OnVoiceCommandClick(object sender, RoutedEventArgs e)
    {
        if (_voiceService != null)
        {
            await _voiceService.ToggleListeningAsync();
        }
    }

    // Lifecycle System Integration (Phase 12)
    public void InitializeLifecycle(IAppLifecycleManager lifecycleManager)
    {
        _lifecycleManager = lifecycleManager;
        UpdateLifecycleMenuChecks();
        _lifecycleManager.StartWithWindowsChanged += (s, enabled) => Dispatcher.InvokeAsync(UpdateLifecycleMenuChecks);
        _lifecycleManager.CloseBehaviorChanged += (s, behavior) => Dispatcher.InvokeAsync(UpdateLifecycleMenuChecks);
    }

    private void UpdateLifecycleMenuChecks()
    {
        if (_lifecycleManager == null) return;
        MenuItemStartWithWindows.IsChecked = _lifecycleManager.StartWithWindows;
        MenuItemCloseMinimize.IsChecked = _lifecycleManager.CloseBehavior == AppCloseBehavior.MinimizeToTray;
        MenuItemCloseExit.IsChecked = _lifecycleManager.CloseBehavior == AppCloseBehavior.ExitApplication;
    }

    // AI Chat Workspace Navigation
    private void OnOpenChatClick(object sender, RoutedEventArgs e)
    {
        OpenChatWindow();
    }

    // Double-click opens Chat Window by default
    private void OnWindowMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            OpenChatWindow();
        }
    }

    // Task Management & History Navigation
    private void OnOpenTasksClick(object sender, RoutedEventArgs e)
    {
        OpenTaskDetailWindow();
    }

    // Memory & Privacy Management Navigation
    public void OpenMemoryManagementWindow()
    {
        if (_memoryService == null || _timelineRepository == null) return;

        if (_memoryManagementWindow == null || !_memoryManagementWindow.IsLoaded)
        {
            _memoryManagementWindow = new MemoryManagementWindow(_memoryService, _timelineRepository);
            _memoryManagementWindow.Closed += (s, ev) => _memoryManagementWindow = null;
            _memoryManagementWindow.Show();
        }
        else
        {
            if (_memoryManagementWindow.WindowState == WindowState.Minimized)
            {
                _memoryManagementWindow.WindowState = WindowState.Normal;
            }
            _memoryManagementWindow.Activate();
        }
    }

    private void OnOpenMemoryManagementClick(object sender, RoutedEventArgs e)
    {
        OpenMemoryManagementWindow();
    }

    // Widget Shelf Navigation (Phase 11)
    private WidgetShelfWindow? _widgetShelfWindow;

    public void InitializeWidgets(WidgetShelfWindow widgetShelfWindow)
    {
        _widgetShelfWindow = widgetShelfWindow;
    }

    public void ToggleWidgets()
    {
        _widgetShelfWindow?.ToggleVisibility();
    }

    private void OnOpenWidgetsClick(object sender, RoutedEventArgs e)
    {
        ToggleWidgets();
    }

    // Workflows & Task Lifecycle Integration (Phase 13)
    public void InitializeWorkflows(IWorkflowRepository workflowRepository, IWorkflowEngine workflowEngine, ITaskLifecycleSignalHub signalHub)
    {
        _workflowRepository = workflowRepository;
        _workflowEngine = workflowEngine;
        _signalHub = signalHub;

        _signalHub.SignalEmitted += OnTaskLifecycleSignalEmitted;
    }

    private void OnTaskLifecycleSignalEmitted(TaskLifecycleSignal signal)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_stateMachine == null) return;

            switch (signal.SignalType)
            {
                case TaskLifecycleSignalType.TaskStarted:
                    _stateMachine.SetState(CharacterState.Working);
                    break;
                case TaskLifecycleSignalType.TaskWaiting:
                    _stateMachine.SetState(CharacterState.Thinking);
                    break;
                case TaskLifecycleSignalType.ApprovalRequired:
                    _stateMachine.SetState(CharacterState.WaitingForApproval);
                    break;
                case TaskLifecycleSignalType.TaskCompleted:
                    _stateMachine.TriggerReaction(CharacterState.TaskComplete, TimeSpan.FromSeconds(3), CharacterState.Idle);
                    break;
                case TaskLifecycleSignalType.TaskFailed:
                    _stateMachine.TriggerReaction(CharacterState.Error, TimeSpan.FromSeconds(3), CharacterState.Idle);
                    break;
                case TaskLifecycleSignalType.WorkflowNotification:
                    _stateMachine.TriggerReaction(CharacterState.Notification, TimeSpan.FromSeconds(3), CharacterState.Idle);
                    break;
            }
        });
    }

    public void OpenWorkflowEditorWindow()
    {
        if (_workflowRepository == null || _workflowEngine == null) return;

        if (_workflowEditorWindow == null || !_workflowEditorWindow.IsLoaded)
        {
            _workflowEditorWindow = new WorkflowEditorWindow(_workflowRepository, _workflowEngine);
            _workflowEditorWindow.Closed += (s, e) => _workflowEditorWindow = null;
            _workflowEditorWindow.Show();
        }
        else
        {
            if (_workflowEditorWindow.WindowState == WindowState.Minimized)
            {
                _workflowEditorWindow.WindowState = WindowState.Normal;
            }
            _workflowEditorWindow.Activate();
        }
    }

    private void OnOpenWorkflowsClick(object sender, RoutedEventArgs e)
    {
        OpenWorkflowEditorWindow();
    }
}
