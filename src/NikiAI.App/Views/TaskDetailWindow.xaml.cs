using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NikiAI.Core.Tasks;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;

namespace NikiAI.App.Views;

/// <summary>
/// Interaction logic for TaskDetailWindow.xaml.
/// Provides master-detail task inspection, truthful progress display,
/// audit event timeline, real task creation, status transition, and soft-delete archiving.
/// </summary>
public partial class TaskDetailWindow : Window
{
    private readonly ITaskRepository _taskRepository;
    private List<AgentTask> _allLoadedTasks = new();
    private AgentTask? _selectedTask;
    private bool _showArchived;

    public TaskDetailWindow(ITaskRepository taskRepository)
    {
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        await ReloadTasksAsync();
    }

    public async Task ReloadTasksAsync()
    {
        try
        {
            var tasks = await _taskRepository.GetAllAsync(includeArchived: _showArchived);
            _allLoadedTasks = tasks.OrderByDescending(t => t.CreatedAt).ToList();
            ApplyFilterAndDisplay();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load tasks: {ex.Message}", "Niki AI Storage", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyFilterAndDisplay()
    {
        if (TextTaskCount == null || ListBoxTasks == null || TextBoxSearch == null || ComboBoxStatusFilter == null)
        {
            return;
        }

        var query = TextBoxSearch.Text?.Trim() ?? string.Empty;
        var selectedStatusItem = (ComboBoxStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();

        var filtered = _allLoadedTasks.AsEnumerable();

        if (!string.IsNullOrEmpty(selectedStatusItem) && selectedStatusItem != "All Statuses")
        {
            if (Enum.TryParse<AgentTaskStatus>(selectedStatusItem, out var filterStatus))
            {
                filtered = filtered.Where(t => t.Status == filterStatus);
            }
        }

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var resultList = filtered.ToList();
        TextTaskCount.Text = $"{resultList.Count} of {_allLoadedTasks.Count} tasks";

        ListBoxTasks.Items.Clear();
        foreach (var task in resultList)
        {
            var itemPanel = CreateTaskListItem(task);
            var listItem = new ListBoxItem
            {
                Content = itemPanel,
                Tag = task
            };
            ListBoxTasks.Items.Add(listItem);
        }

        if (_selectedTask != null)
        {
            var reselect = ListBoxTasks.Items
                .OfType<ListBoxItem>()
                .FirstOrDefault(i => (i.Tag as AgentTask)?.Id == _selectedTask.Id);

            if (reselect != null)
            {
                ListBoxTasks.SelectedItem = reselect;
            }
            else
            {
                ShowEmptyState();
            }
        }
        else
        {
            ShowEmptyState();
        }
    }

    private UIElement CreateTaskListItem(AgentTask task)
    {
        var container = new Grid();
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Title Row
        var titleBlock = new TextBlock
        {
            Text = task.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(titleBlock, 0);
        container.Children.Add(titleBlock);

        // Subtitle Row: Status badge + created date
        var subPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var (bgBrush, fgBrush) = GetStatusBrushes(task.Status);
        var badge = new Border
        {
            Background = bgBrush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1)
        };
        var badgeText = new TextBlock
        {
            Text = task.Status.ToString(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = fgBrush
        };
        badge.Child = badgeText;
        subPanel.Children.Add(badge);

        if (task.IsArchived)
        {
            var archBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x40, 0x1A, 0x1A)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(4, 0, 0, 0)
            };
            archBadge.Child = new TextBlock
            {
                Text = "Archived",
                FontSize = 9,
                Foreground = (Brush)FindResource("StatusErrorBrush")
            };
            subPanel.Children.Add(archBadge);
        }

        var timeBlock = new TextBlock
        {
            Text = task.CreatedAt.ToString("g", CultureInfo.CurrentCulture),
            FontSize = 10,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        subPanel.Children.Add(timeBlock);

        Grid.SetRow(subPanel, 1);
        container.Children.Add(subPanel);

        return container;
    }

    private (Brush Background, Brush Foreground) GetStatusBrushes(AgentTaskStatus status)
    {
        return status switch
        {
            AgentTaskStatus.Completed => (
                new SolidColorBrush(Color.FromArgb(0x33, 0x22, 0xC5, 0x5E)),
                (Brush)FindResource("StatusSuccessBrush")),
            AgentTaskStatus.Running => (
                new SolidColorBrush(Color.FromArgb(0x33, 0x60, 0xA5, 0xFA)),
                (Brush)FindResource("StatusInfoBrush")),
            AgentTaskStatus.Pending or AgentTaskStatus.Waiting or AgentTaskStatus.NeedsApproval => (
                new SolidColorBrush(Color.FromArgb(0x33, 0xF5, 0x9E, 0x0B)),
                (Brush)FindResource("StatusWarningBrush")),
            AgentTaskStatus.Failed => (
                new SolidColorBrush(Color.FromArgb(0x33, 0xEF, 0x44, 0x44)),
                (Brush)FindResource("StatusErrorBrush")),
            AgentTaskStatus.Cancelled => (
                new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                (Brush)FindResource("TextSecondaryBrush")),
            _ => (
                new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                (Brush)FindResource("TextPrimaryBrush"))
        };
    }

    private void ShowEmptyState()
    {
        _selectedTask = null;
        GridEmptyState.Visibility = Visibility.Visible;
        GridDetailPanel.Visibility = Visibility.Collapsed;
    }

    private async void OnTaskSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListBoxTasks.SelectedItem is ListBoxItem item && item.Tag is AgentTask task)
        {
            await DisplayTaskDetailsAsync(task);
        }
        else
        {
            ShowEmptyState();
        }
    }

    private async Task DisplayTaskDetailsAsync(AgentTask task)
    {
        _selectedTask = task;
        GridEmptyState.Visibility = Visibility.Collapsed;
        GridDetailPanel.Visibility = Visibility.Visible;

        // Header info
        TextDetailTitle.Text = task.Title;
        TextDetailTaskId.Text = $"ID: #{task.Id}";
        TextDetailCreated.Text = $"Created: {task.CreatedAt:yyyy-MM-dd HH:mm:ss}";
        TextDetailUpdated.Text = task.CompletedAt.HasValue
            ? $"Completed: {task.CompletedAt.Value:yyyy-MM-dd HH:mm:ss}"
            : (task.StartedAt.HasValue ? $"Started: {task.StartedAt.Value:yyyy-MM-dd HH:mm:ss}" : $"Created: {task.CreatedAt:yyyy-MM-dd HH:mm:ss}");

        var (bgBrush, fgBrush) = GetStatusBrushes(task.Status);
        BorderDetailStatusBadge.Background = bgBrush;
        TextDetailStatus.Text = task.Status.ToString();
        TextDetailStatus.Foreground = fgBrush;

        TextDetailPriority.Text = task.Priority.ToString();
        BorderDetailArchivedBadge.Visibility = task.IsArchived ? Visibility.Visible : Visibility.Collapsed;

        // Truthful progress: never display fake progress
        if (task.ProgressPercentage.HasValue)
        {
            ProgressBarTask.Visibility = Visibility.Visible;
            ProgressBarTask.Value = task.ProgressPercentage.Value;
            TextTruthfulProgressPercent.Text = $"{task.ProgressPercentage.Value:0.#}%";
        }
        else
        {
            ProgressBarTask.Visibility = Visibility.Collapsed;
            TextTruthfulProgressPercent.Text = "Not measurable (null)";
        }

        TextDetailStage.Text = string.IsNullOrWhiteSpace(task.StructuredGoal)
            ? "Goal: Standard Lifecycle"
            : $"Goal: {task.StructuredGoal}";

        // Description / Natural Language Request
        TextDetailDescription.Text = string.IsNullOrWhiteSpace(task.NaturalLanguageRequest)
            ? (string.IsNullOrWhiteSpace(task.ResultSummary) ? "No description provided." : task.ResultSummary)
            : task.NaturalLanguageRequest;

        // Action buttons
        bool isTerminal = task.Status is AgentTaskStatus.Completed or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled;
        ButtonCancelTask.IsEnabled = !isTerminal && !task.IsArchived;
        ButtonArchiveTask.IsEnabled = !task.IsArchived;
        ButtonArchiveTask.Content = task.IsArchived ? "Archived (Preserved)" : "Archive (Soft Delete)";

        // Load Audit Events
        await LoadTaskAuditEventsAsync(task.Id);
    }

    private async Task LoadTaskAuditEventsAsync(string taskId)
    {
        ListBoxAuditEvents.Items.Clear();
        try
        {
            var events = await _taskRepository.GetEventsAsync(taskId);
            TextEventCount.Text = $"{events.Count} audit events recorded";

            foreach (var evt in events)
            {
                var evtPanel = new StackPanel { Margin = new Thickness(2) };

                var topRow = new Grid();
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var typeBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x2A, 0xF9, 0x73, 0x16)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1)
                };
                typeBadge.Child = new TextBlock
                {
                    Text = evt.EventType.ToString(),
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("PrimaryBrandBrush")
                };
                Grid.SetColumn(typeBadge, 0);
                topRow.Children.Add(typeBadge);

                var timeText = new TextBlock
                {
                    Text = evt.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    FontSize = 9.5,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                };
                Grid.SetColumn(timeText, 2);
                topRow.Children.Add(timeText);

                evtPanel.Children.Add(topRow);

                if (!string.IsNullOrWhiteSpace(evt.Message))
                {
                    var msgText = new TextBlock
                    {
                        Text = evt.Message,
                        FontSize = 11,
                        Foreground = (Brush)FindResource("TextPrimaryBrush"),
                        Margin = new Thickness(0, 3, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    };
                    evtPanel.Children.Add(msgText);
                }

                if (!string.IsNullOrWhiteSpace(evt.DetailsJson))
                {
                    var metaText = new TextBlock
                    {
                        Text = evt.DetailsJson,
                        FontSize = 9.5,
                        Foreground = (Brush)FindResource("TextSecondaryBrush"),
                        Margin = new Thickness(0, 2, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    };
                    evtPanel.Children.Add(metaText);
                }

                var item = new ListBoxItem { Content = evtPanel };
                ListBoxAuditEvents.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            TextEventCount.Text = $"Error loading events: {ex.Message}";
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (TextSearchPlaceholder == null || TextBoxSearch == null) return;
        TextSearchPlaceholder.Visibility = string.IsNullOrEmpty(TextBoxSearch.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyFilterAndDisplay();
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilterAndDisplay();
    }

    private async void OnShowArchivedChecked(object sender, RoutedEventArgs e)
    {
        _showArchived = CheckBoxShowArchived.IsChecked == true;
        await ReloadTasksAsync();
    }

    // New Task Modal Handlers (REAL user task creation)
    private void OnNewTaskClick(object sender, RoutedEventArgs e)
    {
        InputNewTaskTitle.Text = string.Empty;
        InputNewTaskStage.Text = "Standard Goal";
        InputNewTaskDescription.Text = string.Empty;
        OverlayNewTask.Visibility = Visibility.Visible;
        InputNewTaskTitle.Focus();
    }

    private void OnCancelNewTaskModalClick(object sender, RoutedEventArgs e)
    {
        OverlayNewTask.Visibility = Visibility.Collapsed;
    }

    private async void OnSubmitNewTaskModalClick(object sender, RoutedEventArgs e)
    {
        var title = InputNewTaskTitle.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBox.Show("Please enter a valid task title.", "Required Field", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var goal = string.IsNullOrWhiteSpace(InputNewTaskStage.Text) ? "Standard Goal" : InputNewTaskStage.Text.Trim();
        var description = InputNewTaskDescription.Text.Trim();

        var newTask = new AgentTask(
            title: title,
            naturalLanguageRequest: description,
            structuredGoal: goal,
            priority: AgentTaskPriority.Normal)
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Status = AgentTaskStatus.Pending,
            ProgressPercentage = null, // Truthful progress: null until measured!
            IsArchived = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            await _taskRepository.CreateAsync(newTask);
            OverlayNewTask.Visibility = Visibility.Collapsed;
            await ReloadTasksAsync();

            // Select newly created task
            var reselect = ListBoxTasks.Items
                .OfType<ListBoxItem>()
                .FirstOrDefault(i => (i.Tag as AgentTask)?.Id == newTask.Id);

            if (reselect != null)
            {
                ListBoxTasks.SelectedItem = reselect;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create task: {ex.Message}", "Storage Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnCancelTaskClick(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to cancel '{_selectedTask.Title}'?",
            "Confirm Task Cancellation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                await _taskRepository.TransitionStatusAsync(
                    _selectedTask.Id,
                    AgentTaskStatus.Cancelled,
                    "Task cancelled by user via Task Detail UI");

                await ReloadTasksAsync();
                var updated = await _taskRepository.GetByIdAsync(_selectedTask.Id);
                if (updated != null)
                {
                    await DisplayTaskDetailsAsync(updated);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to cancel task: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void OnArchiveTaskClick(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) return;

        try
        {
            await _taskRepository.ArchiveAsync(_selectedTask.Id, "Task archived (soft-deleted) by user via Task Detail UI");
            await ReloadTasksAsync();

            if (_showArchived)
            {
                var updated = await _taskRepository.GetByIdAsync(_selectedTask.Id);
                if (updated != null)
                {
                    await DisplayTaskDetailsAsync(updated);
                }
            }
            else
            {
                ShowEmptyState();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to archive task: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseWindowClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
