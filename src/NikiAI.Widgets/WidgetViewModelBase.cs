using System.ComponentModel;
using System.Runtime.CompilerServices;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// Common view-model base for all widgets.
/// Implements the unified widget design language:
/// - icon;
/// - title;
/// - one primary value;
/// - optional secondary data;
/// - one small action.
/// Also provides presentation state tracking (Active, Empty, Loading, Unavailable, Error) and cancellation support.
/// </summary>
public abstract class WidgetViewModelBase : IWidget, INotifyPropertyChanged
{
    private WidgetSurfaceVariant _surfaceVariant = WidgetSurfaceVariant.Glass;
    private WidgetSizeOption _sizeOption = WidgetSizeOption.Standard;
    private bool _isVisible = true;

    private string? _primaryDisplayValue;
    private string? _secondaryDisplayValue;
    private string? _actionLabel;
    private bool _isActionEnabled = true;
    private WidgetPresentationState _presentationState = WidgetPresentationState.Loading;

    private bool _isLoading;
    private bool _hasError;
    private string? _errorMessage;

    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract WidgetCategory Category { get; }
    public virtual string IconGlyph => "❖";

    public WidgetSurfaceVariant SurfaceVariant
    {
        get => _surfaceVariant;
        set => SetField(ref _surfaceVariant, value);
    }

    public WidgetSizeOption SizeOption
    {
        get => _sizeOption;
        set => SetField(ref _sizeOption, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public string? PrimaryDisplayValue
    {
        get => _primaryDisplayValue;
        protected set => SetField(ref _primaryDisplayValue, value);
    }

    public string? SecondaryDisplayValue
    {
        get => _secondaryDisplayValue;
        protected set => SetField(ref _secondaryDisplayValue, value);
    }

    public string? ActionLabel
    {
        get => _actionLabel;
        protected set => SetField(ref _actionLabel, value);
    }

    public bool IsActionEnabled
    {
        get => _isActionEnabled;
        protected set => SetField(ref _isActionEnabled, value);
    }

    public WidgetPresentationState PresentationState
    {
        get => _presentationState;
        protected set => SetField(ref _presentationState, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        protected set => SetField(ref _isLoading, value);
    }

    public bool HasError
    {
        get => _hasError;
        protected set => SetField(ref _hasError, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        protected set => SetField(ref _errorMessage, value);
    }

    public event EventHandler? StateChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public virtual Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task ExecuteActionAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    protected void NotifyStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        NotifyStateChanged();
        return true;
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
