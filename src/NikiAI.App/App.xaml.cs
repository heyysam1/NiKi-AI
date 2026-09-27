using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NikiAI.App.Services;
using NikiAI.App.Views;
using NikiAI.Automation;
using NikiAI.Core.Automation;
using NikiAI.Browser;
using NikiAI.Core.Browser;
using NikiAI.Core.Logging;
using NikiAI.Core.Security;
using NikiAI.Character;
using NikiAI.Core.Character;
using NikiAI.Core.Tasks;
using NikiAI.Core.Tools;
using NikiAI.Agent;
using NikiAI.Core.Agent;
using NikiAI.Core.Notifications;
using NikiAI.Core.Scheduler;
using NikiAI.Notifications;
using NikiAI.Scheduler;
using NikiAI.Security;
using NikiAI.Storage;
using NikiAI.Tools;
using NikiAI.Core.Memory;
using NikiAI.Memory;
using NikiAI.Core.Widgets;
using NikiAI.Widgets;
using NikiAI.Widgets.Services;
using NikiAI.Widgets.Views;
using NikiAI.Core.Lifecycle;
using NikiAI.Core.Voice;
using NikiAI.Voice;
using NikiAI.Core.Workflows;
using NikiAI.Workflows;
using NikiAI.Core.Vision;
using NikiAI.App.Controls;

namespace NikiAI.App;

/// <summary>
/// Application startup and lifecycle coordinator.
/// Initializes services, system tray, global hotkeys, and the companion shell.
/// </summary>
public partial class App : System.Windows.Application
{
    public static IServiceProvider? ServiceProvider { get; private set; }
    public static IConfiguration? Configuration { get; private set; }

    private ILogger<App>? _logger;
    private MainWindow? _mainWindow;
    private CompanionWindow? _companionWindow;
    private ChatWindow? _chatWindow;
    private WidgetShelfWindow? _widgetShelfWindow;
    private SystemTrayManager? _trayManager;
    private GlobalHotkeyManager? _hotkeyManager;
    private readonly DateTimeOffset _appStartupTime = DateTimeOffset.UtcNow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Build Configuration
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        Configuration = configBuilder.Build();

        // 2. Configure Dependency Injection & Services
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        _logger = ServiceProvider.GetRequiredService<ILogger<App>>();
        _logger.LogInformation("Niki AI Application starting up (Phase 1: Companion Shell).");

        // 3. Global Exception Handling with Secret Redaction
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var safeMessage = SecretRedactor.Redact(ex?.ToString() ?? "Unknown exception");
            _logger.LogCritical("Unhandled AppDomain exception: {SafeMessage}", safeMessage);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            var safeMessage = SecretRedactor.Redact(args.Exception.ToString());
            _logger.LogError(args.Exception, "Unhandled Dispatcher exception: {SafeMessage}", safeMessage);
            args.Handled = true;
            if (Environment.GetCommandLineArgs().Any(a => a.StartsWith("--verify-")))
            {
                _logger.LogCritical("Aborting verification process due to unhandled dispatcher exception during startup.");
                Shutdown(1);
            }
        };

        // 4. Create & Show Companion Window
        _companionWindow = new CompanionWindow();
        MainWindow = _companionWindow;
        _companionWindow.Show();

        var hwnd = new WindowInteropHelper(_companionWindow).EnsureHandle();
        _logger.LogInformation("Companion window created with handle: {Hwnd} (Standard 150x100)", hwnd);

        // 5. Initialize Character Runtime (Phase 2)
        var stateMachine = ServiceProvider.GetRequiredService<ICharacterStateMachine>();
        var charRegistry = ServiceProvider.GetRequiredService<ICharacterRegistry>();
        var animController = ServiceProvider.GetRequiredService<CharacterAnimationController>();
        _companionWindow.InitializeCharacterRuntime(animController, stateMachine, charRegistry);
        _logger.LogInformation("Character Runtime initialized with active character: {ActiveChar}", charRegistry.ActiveCharacter.DisplayName);

        // 5b. Initialize Storage & Local Task System (Phase 3)
        var storageContext = ServiceProvider.GetRequiredService<StorageContext>();
        var taskRepository = ServiceProvider.GetRequiredService<ITaskRepository>();
        storageContext.InitializeAsync().GetAwaiter().GetResult();
        _companionWindow.InitializeTaskSystem(taskRepository);
        _logger.LogInformation("Storage Context & Versioned SQLite initialized at {DbPath}", storageContext.DatabasePath);

        // 5c. Initialize Casual Idle & AI Chat System (Phase 4), Lifecycle & Voice (Phase 12)
        var casualIdleController = ServiceProvider.GetRequiredService<CasualIdleController>();
        _companionWindow.InitializeCasualIdle(casualIdleController);

        var lifecycleManager = ServiceProvider.GetRequiredService<AppLifecycleManager>();
        lifecycleManager.AttachCompanionWindow(_companionWindow);
        _companionWindow.InitializeLifecycle(lifecycleManager);

        var voiceService = ServiceProvider.GetRequiredService<IVoiceService>();
        var voiceSync = ServiceProvider.GetRequiredService<VoiceStateSynchronizer>();
        _companionWindow.InitializeVoice(voiceService);

        var credManager = ServiceProvider.GetRequiredService<ProviderCredentialManager>();
        var secureStore = ServiceProvider.GetRequiredService<ISecureSettingsStore>();
        _companionWindow.InitializeSettingsStore(secureStore);
        var activeProvider = e.Args.Contains("--verify-phase4")
            ? (IAgentProvider)ServiceProvider.GetRequiredService<MockAgentProvider>()
            : ServiceProvider.GetRequiredService<IAgentProvider>();
        _chatWindow = new ChatWindow(activeProvider, credManager, stateMachine, secureStore, lifecycleManager, voiceService);
        _companionWindow.InitializeChatWindow(_chatWindow);

        _mainWindow = new MainWindow(
            ServiceProvider.GetRequiredService<IAgentOperator>(),
            taskRepository,
            ServiceProvider.GetRequiredService<IWorkflowRepository>(),
            ServiceProvider.GetRequiredService<IWorkflowEngine>(),
            ServiceProvider.GetRequiredService<IMemoryService>(),
            secureStore,
            activeProvider,
            voiceService
        );
        _mainWindow.AttachCompanionWindow(_companionWindow);
        _companionWindow.InitializeMainWindow(_mainWindow);

        _logger.LogInformation("AI Provider Abstraction, Lifecycle, Voice, & MainWindow Primary Shell initialized.");

        // 5d. Initialize Tool Registry (Phase 5)
        var toolRegistry = ServiceProvider.GetRequiredService<IToolRegistry>();
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<OpenAppTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<CreateReminderTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<SearchWebTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<ReadClipboardTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<WriteClipboardTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<BrowserSearchTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<BrowserPageReadTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<BrowserExtractTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<AppListTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<RecentAppsTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<WindowFocusTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<WindowUiInteractTool>());

        // 5e. Initialize Memory Subsystem Tools (Phase 10)
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<MemorySaveTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<MemoryQueryTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<MemoryDeleteTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<MemoryClearTool>());
        
        // 5e2. Initialize Screen Awareness Tools (Phase 14)
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<CaptureScreenTool>());
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<AnalyzeScreenTool>());

        // 5e3. Initialize Workflow Execution Tool
        toolRegistry.RegisterTool(ServiceProvider.GetRequiredService<RunWorkflowTool>());
        _logger.LogInformation("Tool Registry initialized with {Count} tools.", toolRegistry.Count);

        // 5f. Initialize Desktop Pet Stage B Movement Controller (Phase 9)
        var petMovement = ServiceProvider.GetRequiredService<IPetMovementController>();
        _companionWindow.InitializeDesktopPetMovement(petMovement);
        _logger.LogInformation("Desktop Pet Stage B Window Awareness & Movement Controller initialized.");

        // 5g. Initialize Desktop Pet Context & Memory System (Phase 10)
        var memoryService = ServiceProvider.GetRequiredService<IMemoryService>();
        var timelineRepo = ServiceProvider.GetRequiredService<ITimelineRepository>();
        var petContextProvider = ServiceProvider.GetRequiredService<IPetContextProvider>();
        _companionWindow.InitializeMemorySystem(memoryService, timelineRepo, petContextProvider);
        _logger.LogInformation("Desktop Pet Context & Memory System initialized.");

        // 5h. Initialize Widgets Subsystem & Shared Shell (Phase 11)
        var widgetRegistry = ServiceProvider.GetRequiredService<WidgetRegistry>();
        var widgetCoordinator = ServiceProvider.GetRequiredService<WidgetRefreshCoordinator>();
        _widgetShelfWindow = new WidgetShelfWindow(widgetRegistry, widgetCoordinator);
        _companionWindow.InitializeWidgets(_widgetShelfWindow);
        widgetCoordinator.StartAsync().GetAwaiter().GetResult();
        widgetCoordinator.Suspend(); // Suspended initially while shelf is hidden
        _logger.LogInformation("Widgets Subsystem initialized with {Count} core widgets.", widgetRegistry.Count);

        // 5f. Initialize Permission Engine & Prompt Handler (Phase 6)
        var permEngine = ServiceProvider.GetRequiredService<IPermissionEngine>();
        var promptHandler = ServiceProvider.GetRequiredService<WpfApprovalPromptHandler>();
        permEngine.SetPromptHandler(promptHandler);
        _logger.LogInformation("Permission Engine initialized with WPF prompt handler and persistent SQLite rules.");

        // 5f. Initialize Scheduler & Notification Subsystems (Phase 7)
        var schedulerService = ServiceProvider.GetRequiredService<ISchedulerService>();
        var notifService = ServiceProvider.GetRequiredService<INotificationService>();
        schedulerService.StartAsync().GetAwaiter().GetResult();
        _logger.LogInformation("Scheduler and Notification Subsystems initialized and running.");

        // 6. Initialize System Tray Manager
        var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "niki Logo.png");
        if (!File.Exists(logoPath))
        {
            // Fallback to project root or instruction asset path
            logoPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "niki Logo.png");
        }

        _trayManager = ServiceProvider.GetRequiredService<SystemTrayManager>();
        _trayManager.TrayLeftClicked += () =>
        {
            _companionWindow.Dispatcher.Invoke(() => _companionWindow.ToggleVisibility());
        };
        _trayManager.TrayRightClicked += () =>
        {
            _companionWindow.Dispatcher.Invoke(() =>
            {
                if (_companionWindow.ContextMenu != null)
                {
                    _companionWindow.ContextMenu.IsOpen = true;
                }
            });
        };
        _trayManager.Initialize(hwnd, logoPath);

        // 7. Initialize Global Hotkey Manager (Preferred: Win + Alt + N, with fallback)
        _hotkeyManager = ServiceProvider.GetRequiredService<GlobalHotkeyManager>();
        _hotkeyManager.HotkeyPressed += () =>
        {
            _companionWindow.Dispatcher.Invoke(() => _companionWindow.ToggleVisibility());
        };
        _hotkeyManager.Initialize(hwnd);
        _logger.LogInformation("Active global hotkey: {Hotkey}", _hotkeyManager.ActiveHotkeyDescription);

        // 7b. Initialize Workflows & Task Lifecycle Subsystem (Phase 13)
        var workflowRepo = ServiceProvider.GetRequiredService<IWorkflowRepository>();
        var workflowEngine = ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var signalHub = ServiceProvider.GetRequiredService<ITaskLifecycleSignalHub>();
        if (signalHub is TaskLifecycleSignalHub hubImpl)
        {
            hubImpl.AttachTaskRepository(taskRepository);
        }
        var scheduledTriggerListener = ServiceProvider.GetRequiredService<ScheduledWorkflowTriggerListener>();
        var eventTriggerListener = ServiceProvider.GetRequiredService<ConstrainedEventTriggerListener>();
        _companionWindow.InitializeWorkflows(workflowRepo, workflowEngine, signalHub);

        _hotkeyManager.WorkflowHotkeyPressed += (wfId) =>
        {
            _ = workflowEngine.ExecuteWorkflowAsync(wfId);
        };
        _logger.LogInformation("Workflows & Task Lifecycle Subsystem initialized.");

        // 7c. Initialize Desktop Pet Character Intelligence & AI Behavior Director (Phase 15)
        var behaviorCoordinator = ServiceProvider.GetRequiredService<BehaviorExecutionCoordinator>();
        var behaviorDirector = ServiceProvider.GetRequiredService<IAiBehaviorDirector>();
        _companionWindow.ConnectBehaviorExecution(behaviorCoordinator, (AiBehaviorDirector)behaviorDirector);
        _logger.LogInformation("Character Intelligence & AI Behavior Director initialized (100% offline, bounded scheduling).");

        // 7d. On normal interactive launch, open the primary MainWindow shell alongside the floating Desktop Pet
        if (!e.Args.Any(a => a.StartsWith("--verify-") || a == "--smoke-test"))
        {
            _mainWindow?.Show();
            _logger.LogInformation("Niki AI Primary MainWindow shell opened on startup.");
        }

        // 8. Phase Verification Harness (Extracted to Verification/PhaseVerificationHarness.cs)
        if (e.Args.Any(a => a.StartsWith("--verify-") || a == "--smoke-test"))
        {
            RunPhaseVerification(e.Args);
            return;
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Configuration!);

        // Structured Logging
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(Configuration!.GetSection("Logging"));
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Core & Security Singletons (Phase 0 & Phase 6)
        services.AddSingleton<ISecureSettingsStore, DpapiSecureSettingsStore>();
        services.AddSingleton<IPermissionAuditLogger, PermissionAuditLogger>();
        services.AddSingleton<WpfApprovalPromptHandler>();
        services.AddSingleton<IApprovalPromptHandler>(sp => sp.GetRequiredService<WpfApprovalPromptHandler>());
        services.AddSingleton<IPermissionEngine, PermissionEngine>();
        services.AddSingleton<IBrowserAdapterRegistry>(sp =>
        {
            var registry = new BrowserAdapterRegistry();
            registry.RegisterAdapter(new ChromiumCdpAdapter());
            return registry;
        });
        services.AddSingleton<IBrowserService>(sp =>
            new BrowserService(SupportedBrowser.Edge, sp.GetRequiredService<IBrowserAdapterRegistry>()));

        // Character Runtime Singletons (Phase 2)
        services.AddSingleton<ICharacterStateMachine, CharacterStateMachine>();
        services.AddSingleton<ICharacterRegistry, CharacterRegistry>();
        services.AddSingleton<CharacterAnimationController>();

        // Storage & Task System Singletons (Phase 3 & Phase 6)
        services.AddSingleton<StorageContext>();
        services.AddSingleton<ITaskRepository, SqliteTaskRepository>();
        services.AddSingleton<IPermissionRuleRepository, SqlitePermissionRuleRepository>();

        // AI Provider & Credential Management (Phase 4)
        services.AddSingleton(new HttpClient());
        services.AddSingleton<ProviderCredentialManager>();
        services.AddSingleton<MockAgentProvider>();
        services.AddSingleton<OpenAiCompatibleProvider>();
        services.AddSingleton<IAgentProvider>(sp => sp.GetRequiredService<OpenAiCompatibleProvider>());

        // Casual Idle Animation System (Phase 4)
        services.AddSingleton<CasualIdleController>();

        // Tool Registry & Subsystem (Phase 5)
        services.AddSingleton<IApprovedAppRegistry, ApprovedAppRegistry>();
        services.AddSingleton<IProcessLauncher, WindowsProcessLauncher>();
        services.AddSingleton<IClipboardService, WindowsClipboardService>();
        services.AddSingleton<IWebSearchService, LocalWebSearchService>();
        services.AddSingleton<IToolAuditLogger, ToolAuditLogger>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<OpenAppTool>();
        services.AddSingleton<CreateReminderTool>(sp => new CreateReminderTool(
            timeProvider: null,
            schedulerService: sp.GetRequiredService<ISchedulerService>()
        ));
        services.AddSingleton<SearchWebTool>();
        services.AddSingleton<ReadClipboardTool>();
        services.AddSingleton<WriteClipboardTool>();
        services.AddSingleton<IToolExecutor, ToolExecutor>();
        services.AddSingleton<IToolCatalog, ToolCatalog>();
        services.AddSingleton<IAgentOperator, AgentOperator>();

        // Browser Subsystem (Phase 8)
        services.AddSingleton<IBrowserAutomationEngine>(sp =>
            BrowserAutomationEngine.Create(sp.GetRequiredService<IBrowserService>()));
        services.AddSingleton<BrowserSearchTool>();
        services.AddSingleton<BrowserPageReadTool>();
        services.AddSingleton<BrowserExtractTool>();

        // Scheduler & Notification Subsystems (Phase 7)
        services.AddSingleton<SystemTrayManager>();
        services.AddSingleton<GlobalHotkeyManager>();
        services.AddSingleton<IScheduledItemRepository, SqliteScheduledItemRepository>();
        services.AddSingleton<INotificationRepository, SqliteNotificationRepository>();
        services.AddSingleton<INativeNotificationHandler, TrayNotificationHandler>();
        services.AddSingleton<IResultPopupHandler, WpfResultPopupHandler>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ISchedulerService, SchedulerService>();

        // Windows & Application Automation Subsystem (Phase 9)
        services.AddSingleton<IWindowsAutomationService, WindowsAutomationService>();
        services.AddSingleton<AppListTool>();
        services.AddSingleton<RecentAppsTool>();
        services.AddSingleton<WindowFocusTool>();
        services.AddSingleton<WindowUiInteractTool>();

        // Screen Awareness Subsystem (Phase 14)
        services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
        services.AddSingleton<IVisionProvider, OpenAiVisionProvider>();
        services.AddSingleton<CaptureScreenTool>();
        services.AddSingleton<AnalyzeScreenTool>();

        // Desktop Pet Stage B: Window Awareness & Pet Surfaces (Phase 9)
        services.AddSingleton<IWindowObserver, WindowObserver>();
        services.AddSingleton<IPetSurfaceManager, PetSurfaceManager>();
        services.AddSingleton<IPetMovementController>(sp => new PetMovementController(
            sp.GetRequiredService<IPetSurfaceManager>(),
            sp.GetRequiredService<ICharacterStateMachine>()
        ));

        // Memory & Privacy Foundation Subsystem (Phase 10)
        services.AddSingleton<IMemoryStore, SqliteMemoryRepository>();
        services.AddSingleton<IProjectRepository, SqliteProjectRepository>();
        services.AddSingleton<ITimelineRepository, SqliteTimelineRepository>();
        services.AddSingleton<MemoryService>();
        services.AddSingleton<IMemoryService>(sp => sp.GetRequiredService<MemoryService>());
        services.AddSingleton<MemoryContextManager>();
        services.AddSingleton<IPetContextProvider, DesktopPetContextProvider>();
        services.AddSingleton<MemorySaveTool>();
        services.AddSingleton<MemoryQueryTool>();
        services.AddSingleton<MemoryDeleteTool>();
        services.AddSingleton<MemoryClearTool>();

        // Widgets Subsystem (Phase 11)
        services.AddSingleton<IWeatherProvider, LocalWeatherProvider>();
        services.AddSingleton<WidgetRegistry>(sp =>
        {
            var registry = new WidgetRegistry();
            registry.RegisterWidget(new ClockWidget());
            registry.RegisterWidget(new CalendarWidget(sp.GetRequiredService<IScheduledItemRepository>()));
            registry.RegisterWidget(new WeatherWidget(sp.GetRequiredService<IWeatherProvider>()));
            registry.RegisterWidget(new QuickNoteWidget());
            registry.RegisterWidget(new TasksWidget(sp.GetRequiredService<ITaskRepository>()));
            registry.RegisterWidget(new RemindersWidget(sp.GetRequiredService<IScheduledItemRepository>()));
            registry.RegisterWidget(new SystemMonitorWidget());
            registry.RegisterWidget(new MusicControlWidget());
            registry.RegisterWidget(new ClipboardWidget(sp.GetRequiredService<IClipboardService>()));
            registry.RegisterWidget(new FocusTimerWidget(sp.GetRequiredService<INotificationService>()));
            registry.RegisterWidget(new AiTaskProgressWidget(sp.GetRequiredService<IAgentProvider>(), sp.GetRequiredService<ITaskRepository>()));
            registry.RegisterWidget(new WebResultsWidget(sp.GetRequiredService<IWebSearchService>()));
            return registry;
        });
        services.AddSingleton<WidgetRefreshCoordinator>();
        services.AddSingleton<IWidgetRefreshCoordinator>(sp => sp.GetRequiredService<WidgetRefreshCoordinator>());

        // Application Lifecycle Subsystem (Phase 12)
        services.AddSingleton<IStartupManager, WindowsStartupManager>();
        services.AddSingleton<AppLifecycleManager>();
        services.AddSingleton<IAppLifecycleManager>(sp => sp.GetRequiredService<AppLifecycleManager>());

        // Voice Subsystem (Phase 12 & Phase 14)
        services.AddSingleton<ISpeechToTextService, WindowsSpeechToTextService>();
        services.AddSingleton<WindowsTextToSpeechService>();
        services.AddSingleton<WindowsAudioPlayer>();
        services.AddSingleton<ITtsProvider, WindowsSapiTtsProvider>();
        services.AddSingleton<ITtsProvider, OpenAiTtsProvider>();
        services.AddSingleton<ITextToSpeechService>(sp =>
        {
            var providers = sp.GetServices<ITtsProvider>();
            var player = sp.GetRequiredService<WindowsAudioPlayer>();
            var fallback = sp.GetRequiredService<WindowsTextToSpeechService>();
            var logger = sp.GetService<ILogger<PluggableTextToSpeechService>>();
            return new PluggableTextToSpeechService(providers, player, fallback, logger);
        });
        services.AddSingleton<VoiceService>();
        services.AddSingleton<IVoiceService>(sp => sp.GetRequiredService<VoiceService>());
        services.AddSingleton<VoiceStateSynchronizer>();
        services.AddSingleton<PushToTalkController>();

        // Workflows & Task Lifecycle Subsystem (Phase 13)
        services.AddSingleton<IWorkflowRepository, SqliteWorkflowRepository>();
        services.AddSingleton<ITaskLifecycleSignalHub, TaskLifecycleSignalHub>();
        services.AddSingleton<IWorkflowEngine, WorkflowEngine>();
        services.AddSingleton<RunWorkflowTool>();
        services.AddSingleton<ScheduledWorkflowTriggerListener>();
        services.AddSingleton<ConstrainedEventTriggerListener>();

        // Character Intelligence & AI Behavior Director Subsystem (Phase 15)
        services.AddSingleton<MotionPhysicsSimulator>();
        services.AddSingleton<CharacterCapabilityValidator>();
        services.AddSingleton<IBehaviorPlanValidator>(sp => sp.GetRequiredService<CharacterCapabilityValidator>());
        services.AddSingleton<ExpressionComposer>();
        services.AddSingleton<IExpressionComposer>(sp => sp.GetRequiredService<ExpressionComposer>());
        services.AddSingleton<MoodEngine>();
        services.AddSingleton<PersonalityAdaptationService>();
        services.AddSingleton<LocalBehaviorEngine>();
        services.AddSingleton<BehaviorExecutionCoordinator>();
        services.AddSingleton<AiBehaviorDirector>();
        services.AddSingleton<IAiBehaviorDirector>(sp => sp.GetRequiredService<AiBehaviorDirector>());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Stopping Scheduler and disposing managers...");
        try
        {
            var widgetCoordinator = ServiceProvider?.GetService<WidgetRefreshCoordinator>();
            if (widgetCoordinator != null)
            {
                widgetCoordinator.StopAsync().Wait(TimeSpan.FromSeconds(2));
                widgetCoordinator.Dispose();
            }
        }
        catch { }

        try
        {
            var scheduler = ServiceProvider?.GetService<ISchedulerService>();
            if (scheduler != null)
            {
                var stopTask = scheduler.StopAsync();
                stopTask.Wait(TimeSpan.FromSeconds(3));
                (scheduler as IDisposable)?.Dispose();
            }
        }
        catch { }

        try
        {
            var browserEngine = ServiceProvider?.GetService<IBrowserAutomationEngine>();
            if (browserEngine != null)
            {
                browserEngine.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
            }
        }
        catch { }

        try
        {
            var voice = ServiceProvider?.GetService<IVoiceService>();
            (voice as IDisposable)?.Dispose();
        }
        catch { }

        try
        {
            var director = ServiceProvider?.GetService<IAiBehaviorDirector>();
            (director as IDisposable)?.Dispose();
            var coordinator = ServiceProvider?.GetService<BehaviorExecutionCoordinator>();
            coordinator?.Dispose();
        }
        catch { }

        _trayManager?.Dispose();
        _hotkeyManager?.Dispose();

        _logger?.LogInformation("Niki AI Application exiting cleanly with code {ExitCode}.", e.ApplicationExitCode);
        base.OnExit(e);

        if (Environment.GetCommandLineArgs().Any(a => a.StartsWith("--verify-")))
        {
            Environment.Exit(e.ApplicationExitCode);
        }
    }
}
