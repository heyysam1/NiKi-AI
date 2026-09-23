using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NikiAI.Core.Memory;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace NikiAI.App.Views;

public partial class MemoryManagementWindow : Window
{
    private readonly IMemoryService _memoryService;
    private readonly ITimelineRepository _timelineRepository;
    private List<MemoryItemViewModel> _allItems = new();

    public MemoryManagementWindow(IMemoryService memoryService, ITimelineRepository timelineRepository)
    {
        InitializeComponent();
        _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
        _timelineRepository = timelineRepository ?? throw new ArgumentNullException(nameof(timelineRepository));

        Loaded += async (s, e) => await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var isEnabled = _memoryService.IsMemoryEnabled();
            MemoryEnabledCheckBox.IsChecked = isEnabled;
            UpdateMemoryStatusLabel(isEnabled);

            var items = await _memoryService.GetAllMemoriesAsync();
            _allItems = items.Select(m => new MemoryItemViewModel(m)).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load memories: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateMemoryStatusLabel(bool isEnabled)
    {
        if (isEnabled)
        {
            MemoryStatusText.Text = "Enabled";
            MemoryStatusText.Foreground = (System.Windows.Media.Brush)FindResource("StatusSuccessBrush");
        }
        else
        {
            MemoryStatusText.Text = "Disabled (Privacy Mode)";
            MemoryStatusText.Foreground = (System.Windows.Media.Brush)FindResource("StatusWarningBrush");
        }
    }

    private void ApplyFilter()
    {
        var categoryFilter = (CategoryFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var pinnedOnly = PinnedOnlyCheckBox.IsChecked == true;
        var searchQuery = SearchBox.Text?.Trim() ?? "";

        var filtered = _allItems.AsEnumerable();

        if (!string.IsNullOrEmpty(categoryFilter) && categoryFilter != "All Categories")
        {
            filtered = filtered.Where(x => x.Category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (pinnedOnly)
        {
            filtered = filtered.Where(x => x.IsPinnedRaw);
        }

        if (!string.IsNullOrEmpty(searchQuery))
        {
            filtered = filtered.Where(x =>
                x.ContextExplanation.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                x.Content.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                x.Category.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
        }

        MemoryListView.ItemsSource = filtered.OrderByDescending(x => x.IsPinnedRaw).ThenByDescending(x => x.RawUpdatedAt).ToList();
    }

    private async void OnMemoryEnabledToggled(object sender, RoutedEventArgs e)
    {
        var isEnabled = MemoryEnabledCheckBox.IsChecked == true;
        await _memoryService.SetMemoryEnabledAsync(isEnabled);
        UpdateMemoryStatusLabel(isEnabled);
    }

    private void OnCategoryFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void OnPinnedFilterChanged(object sender, RoutedEventArgs e)
    {
        ApplyFilter();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async void OnDeleteSelectedClicked(object sender, RoutedEventArgs e)
    {
        if (MemoryListView.SelectedItem is not MemoryItemViewModel selected)
        {
            MessageBox.Show("Please select a memory item to delete.", "Delete Memory", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Are you sure you want to delete memory?\n\nContent: {selected.Content}",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            await _memoryService.DeleteMemoryAsync(selected.Id);
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete memory: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnClearCategoryClicked(object sender, RoutedEventArgs e)
    {
        var categoryFilter = (CategoryFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        MemoryCategory? category = null;

        if (!string.IsNullOrEmpty(categoryFilter) && categoryFilter != "All Categories")
        {
            if (Enum.TryParse<MemoryCategory>(categoryFilter, true, out var cat))
            {
                category = cat;
            }
        }

        var targetText = category.HasValue ? $"category '{category}'" : "ALL categories";
        var confirm = MessageBox.Show(
            $"Are you sure you want to permanently clear all memories in {targetText}?\n\nThis operation cannot be undone.",
            "Confirm Memory Clear",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            if (category.HasValue)
            {
                await _memoryService.ClearCategoryAsync(category.Value);
            }
            else
            {
                await _memoryService.ClearCategoryAsync(MemoryCategory.LongTerm);
                await _memoryService.ClearCategoryAsync(MemoryCategory.Project);
            }

            MessageBox.Show("Memories cleared successfully.", "Memories Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to clear memories: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnExportMemoriesClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Memories (JSON)",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            FileName = $"NikiAI_Memories_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = await _memoryService.ExportMemoriesAsync("json");
                await File.WriteAllTextAsync(dialog.FileName, json);
                MessageBox.Show($"Memories successfully exported to:\n{dialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void OnExportTimelineClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Activity Timeline (JSON)",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            FileName = $"NikiAI_Timeline_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = await _memoryService.ExportTimelineAsync("json");
                await File.WriteAllTextAsync(dialog.FileName, json);
                MessageBox.Show($"Timeline successfully exported to:\n{dialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public sealed class MemoryItemViewModel
    {
        public MemoryItemViewModel(MemoryItem item)
        {
            Id = item.Id;
            Category = item.Category.ToString();
            ContextExplanation = item.ContextExplanation ?? "";
            Content = item.Content;
            IsPinned = item.IsPinned ? "📌" : "";
            IsPinnedRaw = item.IsPinned;
            RawUpdatedAt = item.UpdatedAt ?? item.CreatedAt;
            UpdatedAtFormatted = (item.UpdatedAt ?? item.CreatedAt).ToLocalTime().ToString("g");
        }

        public string Id { get; }
        public string Category { get; }
        public string ContextExplanation { get; }
        public string Content { get; }
        public string IsPinned { get; }
        public bool IsPinnedRaw { get; }
        public DateTimeOffset RawUpdatedAt { get; }
        public string UpdatedAtFormatted { get; }
    }
}
