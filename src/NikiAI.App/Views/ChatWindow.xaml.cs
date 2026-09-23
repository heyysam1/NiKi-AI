using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NikiAI.Agent;
using NikiAI.Core.Agent;
using NikiAI.Core.Character;
using NikiAI.Core.Lifecycle;
using NikiAI.Core.Security;
using NikiAI.Core.Voice;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using Keyboard = System.Windows.Input.Keyboard;
using Key = System.Windows.Input.Key;
using Brush = System.Windows.Media.Brush;
using Orientation = System.Windows.Controls.Orientation;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace NikiAI.App.Views;

/// <summary>
/// Interaction logic for ChatWindow.xaml.
/// Hosts conversation workspace, streaming LLM interaction, AI provider settings drawer,
/// and voice command interaction with Push-to-Talk and audio synthesis.
/// </summary>
public partial class ChatWindow : Window
{
    private readonly IAgentProvider _agentProvider;
    private readonly ProviderCredentialManager _credentialManager;
    private readonly ICharacterStateMachine? _stateMachine;
    private readonly ISecureSettingsStore? _settingsStore;
    private readonly IAppLifecycleManager? _lifecycleManager;
    private readonly IVoiceService? _voiceService;

    private readonly List<AgentMessage> _messages = new();
    private CancellationTokenSource? _inFlightCts;
    private bool _isGenerating;

    public ChatWindow(
        IAgentProvider agentProvider,
        ProviderCredentialManager credentialManager,
        ICharacterStateMachine? stateMachine = null,
        ISecureSettingsStore? settingsStore = null,
        IAppLifecycleManager? lifecycleManager = null,
        IVoiceService? voiceService = null)
    {
        InitializeComponent();
        _agentProvider = agentProvider;
        _credentialManager = credentialManager;
        _stateMachine = stateMachine;
        _settingsStore = settingsStore;
        _lifecycleManager = lifecycleManager;
        _voiceService = voiceService;

        UpdateProviderHeader();
        Loaded += OnWindowLoaded;
        Closing += (s, e) => _lifecycleManager?.HandleWindowClosing(this, e);

        if (_voiceService != null)
        {
            _voiceService.StateChanged += OnVoiceStateChanged;
            _voiceService.TranscriptReceived += OnVoiceTranscriptReceived;
        }
    }

    private void OnVoiceStateChanged(object? sender, VoiceSessionState state)
    {
        Dispatcher.InvokeAsync(() =>
        {
            switch (state)
            {
                case VoiceSessionState.Listening:
                    ButtonVoice.Content = "🔴 Listening...";
                    ButtonVoice.Foreground = (Brush)FindResource("StatusErrorBrush");
                    break;
                case VoiceSessionState.Thinking:
                    ButtonVoice.Content = "⏳ Thinking...";
                    ButtonVoice.Foreground = (Brush)FindResource("PrimaryBrandBrush");
                    break;
                case VoiceSessionState.Speaking:
                    ButtonVoice.Content = "🔊 Speaking...";
                    ButtonVoice.Foreground = (Brush)FindResource("StatusSuccessBrush");
                    break;
                case VoiceSessionState.Idle:
                default:
                    ButtonVoice.Content = "🎤 Voice";
                    ButtonVoice.Foreground = (Brush)FindResource("TextPrimaryBrush");
                    break;
            }
        });
    }

    private void OnVoiceTranscriptReceived(object? sender, string transcript)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (string.IsNullOrWhiteSpace(TextBoxPrompt.Text))
            {
                TextBoxPrompt.Text = transcript;
            }
        });
    }

    public Action? OpenTasksAction { get; set; }
    public Action? OpenMemoryAction { get; set; }
    public Action? OpenWidgetsAction { get; set; }
    public Action? OpenWorkflowsAction { get; set; }

    private void OnNavChatClick(object sender, RoutedEventArgs e)
    {
        ScrollViewerMessages.ScrollToBottom();
        TextBoxPrompt.Focus();
    }

    private void OnNavTasksClick(object sender, RoutedEventArgs e)
    {
        OpenTasksAction?.Invoke();
    }

    private void OnNavMemoryClick(object sender, RoutedEventArgs e)
    {
        OpenMemoryAction?.Invoke();
    }

    private void OnNavWidgetsClick(object sender, RoutedEventArgs e)
    {
        OpenWidgetsAction?.Invoke();
    }

    private void OnNavWorkflowsClick(object sender, RoutedEventArgs e)
    {
        OpenWorkflowsAction?.Invoke();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        TextBoxPrompt.Focus();
    }

    public void UpdateProviderHeader()
    {
        var config = _agentProvider.Config;
        TextModelBadge.Text = $"Provider: {_agentProvider.ProviderName} ({config.ModelName})";
        TextConnectionStatus.Text = "Ready";
        BorderStatusDot.Background = (Brush)FindResource("StatusSuccessBrush");
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var config = _agentProvider.Config;
        InputEndpointUrl.Text = config.EndpointUrl;
        InputModelName.Text = config.ModelName;
        InputApiKey.Password = string.Empty;

        var existingKey = _credentialManager.GetApiKey(config.ApiKeySecretKey);
        TextMaskedKeyStatus.Text = string.IsNullOrEmpty(existingKey)
            ? "Stored Key: (Not configured)"
            : $"Stored Key: {ProviderCredentialManager.MaskApiKey(existingKey)}";

        // Load Lifecycle & Voice settings
        if (_lifecycleManager != null)
        {
            CheckBoxStartWithWindows.IsChecked = _lifecycleManager.StartWithWindows;
            ComboBoxCloseBehavior.SelectedIndex = _lifecycleManager.CloseBehavior == AppCloseBehavior.MinimizeToTray ? 0 : 1;
            ButtonTogglePetVisibility.Content = _lifecycleManager.IsPetVisible ? "Hide Desktop Pet" : "Show Desktop Pet";
        }

        if (_voiceService != null)
        {
            CheckBoxVoiceOutput.IsChecked = _voiceService.IsAudioOutputEnabled;
        }

        BorderTestResult.Visibility = Visibility.Collapsed;
        OverlaySettings.Visibility = Visibility.Visible;
    }

    private void OnTogglePetVisibilityClick(object sender, RoutedEventArgs e)
    {
        if (_lifecycleManager != null)
        {
            _lifecycleManager.TogglePet();
            ButtonTogglePetVisibility.Content = _lifecycleManager.IsPetVisible ? "Hide Desktop Pet" : "Show Desktop Pet";
        }
    }

    private void OnCancelSettingsClick(object sender, RoutedEventArgs e)
    {
        OverlaySettings.Visibility = Visibility.Collapsed;
    }

    private async void OnTestConnectionClick(object sender, RoutedEventArgs e)
    {
        ButtonTestConnection.IsEnabled = false;
        BorderTestResult.Visibility = Visibility.Visible;
        TextTestResult.Text = "Testing connection...";
        TextTestResult.Foreground = (Brush)FindResource("TextPrimaryBrush");

        try
        {
            var tempKey = InputApiKey.Password;
            if (string.IsNullOrWhiteSpace(tempKey))
            {
                tempKey = _credentialManager.GetApiKey(_agentProvider.Config.ApiKeySecretKey);
            }

            var previousKey = _credentialManager.GetApiKey(_agentProvider.Config.ApiKeySecretKey);
            if (!string.IsNullOrWhiteSpace(tempKey))
            {
                _credentialManager.SaveApiKey(_agentProvider.Config.ApiKeySecretKey, tempKey);
            }

            var result = await _agentProvider.TestConnectionAsync(CancellationToken.None);
            if (result.IsSuccess)
            {
                TextTestResult.Text = $"✓ Connection successful! (Latency: {result.LatencyMs}ms)\nModel '{result.ModelUsed}' is responsive.";
                TextTestResult.Foreground = (Brush)FindResource("StatusSuccessBrush");
            }
            else
            {
                TextTestResult.Text = $"✕ Connection failed: {result.ErrorMessage}";
                TextTestResult.Foreground = (Brush)FindResource("StatusErrorBrush");
            }

            // Restore previous key if not saved yet
            if (string.IsNullOrWhiteSpace(InputApiKey.Password) && previousKey != null)
            {
                _credentialManager.SaveApiKey(_agentProvider.Config.ApiKeySecretKey, previousKey);
            }
        }
        catch (Exception ex)
        {
            TextTestResult.Text = $"✕ Connection error: {ex.Message}";
            TextTestResult.Foreground = (Brush)FindResource("StatusErrorBrush");
        }
        finally
        {
            ButtonTestConnection.IsEnabled = true;
        }
    }

    private void OnSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        var newBaseUrl = InputEndpointUrl.Text.Trim();
        var newModelName = InputModelName.Text.Trim();
        var newApiKey = InputApiKey.Password;

        if (!string.IsNullOrWhiteSpace(newApiKey))
        {
            _credentialManager.SaveApiKey(_agentProvider.Config.ApiKeySecretKey, newApiKey);
        }

        _agentProvider.Config.EndpointUrl = newBaseUrl;
        _agentProvider.Config.ModelName = newModelName;

        // Save Lifecycle and Voice Settings
        if (_lifecycleManager != null)
        {
            _lifecycleManager.StartWithWindows = CheckBoxStartWithWindows.IsChecked == true;
            _lifecycleManager.CloseBehavior = ComboBoxCloseBehavior.SelectedIndex == 1
                ? AppCloseBehavior.ExitApplication
                : AppCloseBehavior.MinimizeToTray;
        }

        if (_voiceService != null)
        {
            _voiceService.IsAudioOutputEnabled = CheckBoxVoiceOutput.IsChecked == true;
        }

        UpdateProviderHeader();
        OverlaySettings.Visibility = Visibility.Collapsed;
    }

    private void OnClearChatClick(object sender, RoutedEventArgs e)
    {
        if (_isGenerating) return;
        _messages.Clear();
        PanelMessages.Children.Clear();
        AddWelcomeMessage();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnVoiceButtonClick(object sender, RoutedEventArgs e)
    {
        if (_voiceService != null)
        {
            await _voiceService.ToggleListeningAsync();
        }
    }

    private void AddWelcomeMessage()
    {
        var border = new Border
        {
            Background = (Brush)FindResource("Surface1Brush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 12),
            MaxWidth = 680,
            HorizontalAlignment = WpfHorizontalAlignment.Left
        };

        var stack = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new TextBlock
        {
            Text = "Niki AI",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = (Brush)FindResource("PrimaryBrandBrush")
        });
        header.Children.Add(new TextBlock
        {
            Text = "System Assistant",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        stack.Children.Add(header);
        stack.Children.Add(new TextBlock
        {
            Text = "Hello! I'm Niki, your Windows desktop AI companion. You can chat with me, ask questions, or assign background tasks while you work. How can I assist you today?",
            FontSize = 13,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19
        });

        border.Child = stack;
        PanelMessages.Children.Add(border);
    }

    private void OnPromptPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            SubmitPrompt();
        }
    }

    private void OnSendClick(object sender, RoutedEventArgs e)
    {
        SubmitPrompt();
    }

    public async void SubmitPrompt(string? explicitPrompt = null)
    {
        var text = explicitPrompt ?? TextBoxPrompt.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || _isGenerating) return;

        if (explicitPrompt == null)
        {
            TextBoxPrompt.Text = string.Empty;
        }

        _isGenerating = true;
        SetGeneratingState(true);

        // 1. Append User Message
        AddMessageBubble("User", text, isUser: true);
        _messages.Add(AgentMessage.User(text));

        // 2. Synchronize Character State: Listening -> Thinking
        _stateMachine?.SetState(CharacterState.Listening);
        await Task.Delay(180);
        _stateMachine?.SetState(CharacterState.Thinking);

        // 3. Prepare In-Flight Cancellation & Streaming UI
        _inFlightCts = new CancellationTokenSource();
        var assistantBubble = AddMessageBubble("Niki AI", "", isUser: false);
        var messageBuilder = new StringBuilder();
        bool hasStartedStreaming = false;

        try
        {
            var request = new ChatCompletionRequest(
                Messages: new List<AgentMessage>(_messages),
                Model: _agentProvider.Config.ModelName,
                Temperature: _agentProvider.Config.Temperature,
                MaxTokens: _agentProvider.Config.MaxTokens
            );

            await foreach (var chunk in _agentProvider.StreamResponseAsync(request, _inFlightCts.Token))
            {
                if (!hasStartedStreaming)
                {
                    hasStartedStreaming = true;
                    // Switch character to Working while streaming chunks
                    _stateMachine?.SetState(CharacterState.Working);
                }

                messageBuilder.Append(chunk);
                UpdateAssistantBubbleText(assistantBubble, messageBuilder.ToString());
                ScrollViewerMessages.ScrollToBottom();
            }

            var finalContent = messageBuilder.ToString();
            _messages.Add(AgentMessage.Assistant(finalContent));

            // Transition Character: Happy on completion!
            _stateMachine?.SetState(CharacterState.Happy);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                if (_stateMachine?.CurrentState == CharacterState.Happy)
                {
                    _stateMachine.SetState(CharacterState.Idle);
                }
            };
            timer.Start();
        }
        catch (OperationCanceledException)
        {
            messageBuilder.Append(" [Generation stopped by user]");
            UpdateAssistantBubbleText(assistantBubble, messageBuilder.ToString());
            _stateMachine?.SetState(CharacterState.Idle);
        }
        catch (Exception ex)
        {
            var errorMessage = $"✕ Provider Error: {ex.Message}";
            messageBuilder.Append(messageBuilder.Length > 0 ? $"\n\n{errorMessage}" : errorMessage);
            UpdateAssistantBubbleText(assistantBubble, messageBuilder.ToString(), isError: true);

            // Transition Character: Error on failure!
            _stateMachine?.SetState(CharacterState.Error);
            var errTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };
            errTimer.Tick += (s, args) =>
            {
                errTimer.Stop();
                if (_stateMachine?.CurrentState == CharacterState.Error)
                {
                    _stateMachine.SetState(CharacterState.Idle);
                }
            };
            errTimer.Start();
        }
        finally
        {
            _isGenerating = false;
            SetGeneratingState(false);
            _inFlightCts?.Dispose();
            _inFlightCts = null;
            ScrollViewerMessages.ScrollToBottom();
        }
    }

    private void OnCancelGenerationClick(object sender, RoutedEventArgs e)
    {
        if (_isGenerating && _inFlightCts != null)
        {
            _inFlightCts.Cancel();
        }
    }

    private void SetGeneratingState(bool isGenerating)
    {
        TextBoxPrompt.IsEnabled = !isGenerating;
        ButtonSend.Visibility = isGenerating ? Visibility.Collapsed : Visibility.Visible;
        ButtonCancelGeneration.Visibility = isGenerating ? Visibility.Visible : Visibility.Collapsed;

        TextConnectionStatus.Text = isGenerating ? "Generating..." : "Ready";
        BorderStatusDot.Background = isGenerating
            ? (Brush)FindResource("PrimaryBrandBrush")
            : (Brush)FindResource("StatusSuccessBrush");
    }

    private Border AddMessageBubble(string senderName, string content, bool isUser)
    {
        var border = new Border
        {
            Background = isUser
                ? (Brush)FindResource("Surface2Brush")
                : (Brush)FindResource("Surface1Brush"),
            BorderBrush = isUser
                ? (Brush)FindResource("StrongBorderBrush")
                : (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 12),
            MaxWidth = 680,
            HorizontalAlignment = isUser ? WpfHorizontalAlignment.Right : WpfHorizontalAlignment.Left
        };

        var stack = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

        var nameBlock = new TextBlock
        {
            Text = senderName,
            FontWeight = FontWeights.Bold,
            FontSize = 11.5,
            Foreground = isUser
                ? (Brush)FindResource("TextSecondaryBrush")
                : (Brush)FindResource("PrimaryBrandBrush")
        };
        header.Children.Add(nameBlock);

        var timeBlock = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm"),
            FontSize = 10,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(timeBlock);

        stack.Children.Add(header);

        var contentBlock = new TextBlock
        {
            Name = "TextMessageContent",
            Text = content,
            FontSize = 13,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19
        };
        stack.Children.Add(contentBlock);

        border.Child = stack;
        PanelMessages.Children.Add(border);
        ScrollViewerMessages.ScrollToBottom();

        return border;
    }

    private void UpdateAssistantBubbleText(Border bubble, string text, bool isError = false)
    {
        if (bubble.Child is StackPanel stack && stack.Children.Count >= 2 && stack.Children[1] is TextBlock tb)
        {
            tb.Text = text;
            if (isError)
            {
                tb.Foreground = (Brush)FindResource("StatusErrorBrush");
            }
        }
    }
}
