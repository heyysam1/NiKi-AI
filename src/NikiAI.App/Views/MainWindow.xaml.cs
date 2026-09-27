using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NikiAI.Core.Agent;
using NikiAI.Core.Memory;
using NikiAI.Core.Security;
using NikiAI.Core.Tasks;
using NikiAI.Core.Voice;
using NikiAI.Core.Workflows;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace NikiAI.App.Views;

/// <summary>
/// Authoritative primary application shell for Niki AI.
/// Hosts conversational operator, task management, workflow automation, explicit memory, and settings.
/// Maintains complete architectural separation from the floating DesktopPetWindow (CompanionWindow).
/// </summary>
public partial class MainWindow : Window
{
    private readonly IAgentOperator _agentOperator;
    private readonly ITaskRepository _taskRepository;
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly IMemoryService _memoryService;
    private readonly ISecureSettingsStore _secureSettingsStore;
    private readonly IAgentProvider _agentProvider;
    private readonly IVoiceService? _voiceService;
    private CompanionWindow? _companionWindow;

    private readonly List<AgentMessage> _conversationHistory = new();

    public MainWindow(
        IAgentOperator agentOperator,
        ITaskRepository taskRepository,
        IWorkflowRepository workflowRepository,
        IWorkflowEngine workflowEngine,
        IMemoryService memoryService,
        ISecureSettingsStore secureSettingsStore,
        IAgentProvider agentProvider,
        IVoiceService? voiceService = null)
    {
        InitializeComponent();

        _agentOperator = agentOperator ?? throw new ArgumentNullException(nameof(agentOperator));
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
        _workflowRepository = workflowRepository ?? throw new ArgumentNullException(nameof(workflowRepository));
        _workflowEngine = workflowEngine ?? throw new ArgumentNullException(nameof(workflowEngine));
        _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
        _secureSettingsStore = secureSettingsStore ?? throw new ArgumentNullException(nameof(secureSettingsStore));
        _agentProvider = agentProvider ?? throw new ArgumentNullException(nameof(agentProvider));
        _voiceService = voiceService;

        Loaded += OnWindowLoaded;
    }

    public void AttachCompanionWindow(CompanionWindow companionWindow)
    {
        _companionWindow = companionWindow;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        await LoadSettingsAsync();
        await RefreshTasksAsync();
        await RefreshWorkflowsAsync();
        await RefreshMemoriesAsync();
    }

    #region Navigation Tab Switching

    private void SetActiveTab(Button activeBtn, FrameworkElement activeView)
    {
        NavButtonChat.Style = (Style)FindResource("NikiSecondaryButtonStyle");
        NavButtonTasks.Style = (Style)FindResource("NikiSecondaryButtonStyle");
        NavButtonWorkflows.Style = (Style)FindResource("NikiSecondaryButtonStyle");
        NavButtonMemory.Style = (Style)FindResource("NikiSecondaryButtonStyle");
        NavButtonSettings.Style = (Style)FindResource("NikiSecondaryButtonStyle");

        activeBtn.Style = (Style)FindResource("NikiPrimaryButtonStyle");

        ViewOperator.Visibility = Visibility.Collapsed;
        ViewTasks.Visibility = Visibility.Collapsed;
        ViewWorkflows.Visibility = Visibility.Collapsed;
        ViewMemory.Visibility = Visibility.Collapsed;
        ViewSettings.Visibility = Visibility.Collapsed;

        activeView.Visibility = Visibility.Visible;
    }

    private void OnNavChatClick(object sender, RoutedEventArgs e) => SetActiveTab(NavButtonChat, ViewOperator);
    private void OnNavTasksClick(object sender, RoutedEventArgs e)
    {
        SetActiveTab(NavButtonTasks, ViewTasks);
        _ = RefreshTasksAsync();
    }
    private void OnNavWorkflowsClick(object sender, RoutedEventArgs e)
    {
        SetActiveTab(NavButtonWorkflows, ViewWorkflows);
        _ = RefreshWorkflowsAsync();
    }
    private void OnNavMemoryClick(object sender, RoutedEventArgs e)
    {
        SetActiveTab(NavButtonMemory, ViewMemory);
        _ = RefreshMemoriesAsync();
    }
    private void OnNavSettingsClick(object sender, RoutedEventArgs e) => SetActiveTab(NavButtonSettings, ViewSettings);

    private void OnTogglePetClick(object sender, RoutedEventArgs e)
    {
        if (_companionWindow != null)
        {
            _companionWindow.ToggleVisibility();
        }
    }

    #endregion

    #region Operator & Chat Logic

    private void OnInputPromptKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift))
        {
            e.Handled = true;
            SendMessage();
        }
    }

    private void OnSendClick(object sender, RoutedEventArgs e)
    {
        SendMessage();
    }

    private async void SendMessage()
    {
        var text = InputPrompt.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        InputPrompt.Clear();
        ButtonSend.IsEnabled = false;
        TextBusyIndicator.Visibility = Visibility.Visible;

        // Add user bubble
        AddMessageBubble("User", text, isUser: true);

        bool trackTask = CheckTrackTask.IsChecked == true;

        try
        {
            var request = new AgentOperatorRequest(
                UserPrompt: text,
                ConversationHistory: _conversationHistory,
                ForceDurableTracking: trackTask
            );

            var response = await _agentOperator.ExecuteAsync(request);

            // Display any executed tools
            if (response.ExecutedToolCalls != null && response.ExecutedToolCalls.Count > 0)
            {
                foreach (var tool in response.ExecutedToolCalls)
                {
                    AddToolCallBadge(tool.ToolName, tool.ArgumentsJson);
                }
            }

            // Display Assistant response
            AddMessageBubble("Niki AI", response.ResponseText, isUser: false);

            _conversationHistory.Add(AgentMessage.User(text));
            _conversationHistory.Add(AgentMessage.Assistant(response.ResponseText));

            if (!string.IsNullOrWhiteSpace(response.TaskId))
            {
                TextFooterStatus.Text = $"Active Task: {response.TaskId} (Completed: {response.IsCompleted})";
                _ = RefreshTasksAsync();
            }
        }
        catch (Exception ex)
        {
            AddMessageBubble("System Error", ex.Message, isUser: false, isError: true);
        }
        finally
        {
            ButtonSend.IsEnabled = true;
            TextBusyIndicator.Visibility = Visibility.Collapsed;
            ScrollMessages.ScrollToBottom();
        }
    }

    private void AddMessageBubble(string senderName, string content, bool isUser, bool isError = false)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 10),
            MaxWidth = 620,
            HorizontalAlignment = isUser ? WpfHorizontalAlignment.Right : WpfHorizontalAlignment.Left,
            Background = isUser
                ? (Brush)FindResource("PrimaryBrandBrush")
                : (isError ? new SolidColorBrush(Color.FromArgb(50, 239, 68, 68)) : (Brush)FindResource("Surface2Brush"))
        };

        var stack = new StackPanel();
        var header = new TextBlock
        {
            Text = senderName,
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = isUser ? new SolidColorBrush(Colors.Black) : (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var body = new TextBlock
        {
            Text = content,
            FontSize = 12.5,
            Foreground = isUser ? new SolidColorBrush(Colors.Black) : (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18
        };

        stack.Children.Add(header);
        stack.Children.Add(body);
        border.Child = stack;

        PanelMessages.Children.Add(border);
    }

    private void AddToolCallBadge(string toolName, string arguments)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(40, 59, 130, 246)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 59, 130, 246)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = WpfHorizontalAlignment.Left
        };

        var text = new TextBlock
        {
            Text = $"⚡ Tool Invocation: {toolName} → {arguments}",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontFamily = new FontFamily("Consolas")
        };

        border.Child = text;
        PanelMessages.Children.Add(border);
    }

    private async void OnVoiceClick(object sender, RoutedEventArgs e)
    {
        if (_voiceService == null) return;

        ButtonVoice.IsEnabled = false;
        try
        {
            var result = await _voiceService.ToggleListeningAsync();
            if (result != null && !string.IsNullOrWhiteSpace(result.Prompt))
            {
                InputPrompt.Text = result.Prompt;
                SendMessage();
            }
        }
        catch (Exception ex)
        {
            AddMessageBubble("Voice Error", ex.Message, isUser: false, isError: true);
        }
        finally
        {
            ButtonVoice.IsEnabled = true;
            ButtonVoice.Content = _voiceService.State == VoiceSessionState.Listening ? "🔴" : "🎤";
        }
    }

    #endregion

    #region Tasks Logic

    private async Task RefreshTasksAsync()
    {
        try
        {
            var tasks = await _taskRepository.GetAllAsync(includeArchived: false, limit: 100);
            ListTasks.ItemsSource = tasks;
        }
        catch { }
    }

    private void OnRefreshTasksClick(object sender, RoutedEventArgs e) => _ = RefreshTasksAsync();

    private void OnTaskSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListTasks.SelectedItem is AgentTask task)
        {
            TextFooterStatus.Text = $"Selected Task: {task.Title} [{task.Status}]";
        }
    }

    #endregion

    #region Workflows Logic

    private async Task RefreshWorkflowsAsync()
    {
        try
        {
            var workflows = await _workflowRepository.GetAllWorkflowsAsync();
            ListWorkflows.ItemsSource = workflows;
        }
        catch { }
    }

    private void OnRefreshWorkflowsClick(object sender, RoutedEventArgs e) => _ = RefreshWorkflowsAsync();

    private async void OnRunWorkflowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string wfId)
        {
            btn.IsEnabled = false;
            TextFooterStatus.Text = $"Running workflow '{wfId}'...";
            try
            {
                var run = await _workflowEngine.ExecuteWorkflowAsync(wfId);
                TextFooterStatus.Text = $"Workflow '{wfId}' completed: {run.Status}";
            }
            catch (Exception ex)
            {
                TextFooterStatus.Text = $"Workflow '{wfId}' failed: {ex.Message}";
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }

    #endregion

    #region Memory Logic

    private async Task RefreshMemoriesAsync()
    {
        try
        {
            var memories = await _memoryService.GetAllMemoriesAsync();
            ListMemories.ItemsSource = memories;
        }
        catch { }
    }

    private async void OnSaveMemoryClick(object sender, RoutedEventArgs e)
    {
        var text = InputMemoryContent.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        InputMemoryContent.Clear();
        await _memoryService.SaveExplicitMemoryAsync(text, MemoryCategory.LongTerm, "Saved via MainWindow");
        await RefreshMemoriesAsync();
    }

    private async void OnDeleteMemoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string memId)
        {
            await _memoryService.DeleteMemoryAsync(memId);
            await RefreshMemoriesAsync();
        }
    }

    #endregion

    #region Settings Logic

    private async Task LoadSettingsAsync()
    {
        try
        {
            var endpoint = await _secureSettingsStore.GetSecretAsync("ai_provider_endpoint");
            InputEndpointUrl.Text = string.IsNullOrWhiteSpace(endpoint) ? _agentProvider.Config.EndpointUrl : endpoint;

            var model = await _secureSettingsStore.GetSecretAsync("ai_provider_model");
            InputModelName.Text = string.IsNullOrWhiteSpace(model) ? _agentProvider.Config.ModelName : model;

            var apiKey = await _secureSettingsStore.GetSecretAsync(_agentProvider.Config.ApiKeySecretKey);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                InputApiKey.Password = apiKey;
            }
        }
        catch { }
    }

    private async void OnSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        var endpoint = InputEndpointUrl.Text.Trim();
        var model = InputModelName.Text.Trim();
        var apiKey = InputApiKey.Password.Trim();

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            await _secureSettingsStore.SetSecretAsync("ai_provider_endpoint", endpoint);
            _agentProvider.Config.EndpointUrl = endpoint;
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            await _secureSettingsStore.SetSecretAsync("ai_provider_model", model);
            _agentProvider.Config.ModelName = model;
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            await _secureSettingsStore.SetSecretAsync(_agentProvider.Config.ApiKeySecretKey, apiKey);
        }

        TextSettingsFeedback.Text = "Settings successfully saved (credentials protected by Windows DPAPI).";
    }

    private async void OnTestConnectionClick(object sender, RoutedEventArgs e)
    {
        TextSettingsFeedback.Text = "Testing AI provider connectivity...";
        var result = await _agentProvider.TestConnectionAsync();
        if (result.IsSuccess)
        {
            TextSettingsFeedback.Text = $"Connection Success! Latency: {result.LatencyMs}ms (Model: {result.ModelUsed})";
            BorderConnectionStatus.Background = (Brush)FindResource("StatusSuccessBrush");
            TextConnectionStatus.Text = "Connected";
        }
        else
        {
            TextSettingsFeedback.Text = $"Connection Failed: {result.ErrorMessage}";
            BorderConnectionStatus.Background = (Brush)FindResource("StatusErrorBrush");
            TextConnectionStatus.Text = "Error";
        }
    }

    #endregion
}
