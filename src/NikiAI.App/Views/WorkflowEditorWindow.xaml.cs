using System.Windows;
using System.Windows.Controls;
using NikiAI.Core.Workflows;

namespace NikiAI.App.Views;

public partial class WorkflowEditorWindow : Window
{
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IWorkflowEngine _workflowEngine;
    private WorkflowDefinition? _selectedWorkflow;

    public WorkflowEditorWindow(IWorkflowRepository workflowRepository, IWorkflowEngine workflowEngine)
    {
        InitializeComponent();
        _workflowRepository = workflowRepository ?? throw new ArgumentNullException(nameof(workflowRepository));
        _workflowEngine = workflowEngine ?? throw new ArgumentNullException(nameof(workflowEngine));

        _workflowEngine.RunStatusChanged += OnRunStatusChanged;
        Loaded += async (s, e) => await LoadWorkflowsAsync();
    }

    private async Task LoadWorkflowsAsync()
    {
        try
        {
            var workflows = await _workflowRepository.GetAllWorkflowsAsync();
            WorkflowsListBox.ItemsSource = workflows;

            if (workflows.Count > 0)
            {
                WorkflowsListBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to load workflows: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnWorkflowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (WorkflowsListBox.SelectedItem is WorkflowDefinition wf)
        {
            _selectedWorkflow = wf;
            SelectedWorkflowName.Text = wf.Name;
            SelectedWorkflowDescription.Text = wf.Description;
            TriggerTypeText.Text = wf.Trigger.ToString();
            TimeoutText.Text = $"{wf.TimeoutSeconds}s";
            CompletionText.Text = wf.CompletionBehavior.ToString();

            var actionViewModels = wf.Actions.Select((act, index) => new
            {
                StepNumber = index + 1,
                act.Name,
                ActionType = act.ActionType.ToString(),
                ToolId = act.ToolId ?? "-",
                ApprovalRequiredText = act.RequiresApproval ? "Yes" : "No"
            }).ToList();

            ActionsListView.ItemsSource = actionViewModels;
            ExecutionStatusText.Text = "Ready";
            ExecutionDetailsText.Text = string.Empty;
        }
    }

    private async void OnRunWorkflowClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkflow == null)
        {
            System.Windows.MessageBox.Show("Please select a workflow to run.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RunWorkflowButton.IsEnabled = false;
        ExecutionStatusText.Text = "Running...";
        ExecutionDetailsText.Text = $"Starting {_selectedWorkflow.Name}...";

        try
        {
            var result = await _workflowEngine.ExecuteWorkflowAsync(_selectedWorkflow.Id);
            ExecutionStatusText.Text = result.Status.ToString();
            ExecutionDetailsText.Text = result.SanitizedStatusInfo ?? string.Empty;
        }
        catch (Exception ex)
        {
            ExecutionStatusText.Text = "Failed";
            ExecutionDetailsText.Text = ex.Message;
        }
        finally
        {
            RunWorkflowButton.IsEnabled = true;
        }
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await LoadWorkflowsAsync();
    }

    private void OnRunStatusChanged(WorkflowRunRecord record)
    {
        if (_selectedWorkflow != null && record.WorkflowId == _selectedWorkflow.Id)
        {
            Dispatcher.InvokeAsync(() =>
            {
                ExecutionStatusText.Text = record.Status.ToString();
                ExecutionDetailsText.Text = record.SanitizedStatusInfo ?? $"Step {record.CurrentStep}";
            });
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _workflowEngine.RunStatusChanged -= OnRunStatusChanged;
        base.OnClosed(e);
    }
}
