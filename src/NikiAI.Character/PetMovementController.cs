using System.Drawing;
using NikiAI.Core.Character;
using Point = System.Drawing.Point;

namespace NikiAI.Character;

/// <summary>
/// Controls desktop pet movement intent, facing direction, walk/run transitions, and surface navigation.
/// Integrates seamlessly with CharacterStateMachine and respects anti-annoyance boundaries.
/// </summary>
public class PetMovementController : IPetMovementController
{
    public event EventHandler<Point>? PositionChanged;
    public event EventHandler<MovementIntent>? MovementIntentChanged;

    private readonly IPetSurfaceManager _surfaceManager;
    private readonly ICharacterStateMachine? _stateMachine;
    private readonly Size _characterSize;

    public DesktopPetFollowMode FollowMode { get; set; } = DesktopPetFollowMode.StayOnSurface;
    public bool IsMoving { get; private set; }
    public Point CurrentPosition { get; private set; }
    public PetSurface CurrentSurface { get; private set; }
    public MovementIntent? CurrentIntent { get; private set; }

    private Point _targetPosition;
    private const double WalkSpeed = 250.0; // pixels per second
    private const double RunSpeed = 450.0;  // pixels per second

    public PetMovementController(
        IPetSurfaceManager surfaceManager,
        ICharacterStateMachine? stateMachine = null,
        Size? characterSize = null)
    {
        _surfaceManager = surfaceManager ?? throw new ArgumentNullException(nameof(surfaceManager));
        _stateMachine = stateMachine;
        _characterSize = characterSize ?? new Size(150, 100);

        CurrentSurface = _surfaceManager.GetPrimaryWorkAreaSurface();
        CurrentPosition = new Point(
            CurrentSurface.Bounds.Left + (CurrentSurface.Bounds.Width / 2) - (_characterSize.Width / 2),
            CurrentSurface.Bounds.Top
        );
        _targetPosition = CurrentPosition;
    }

    public void SetPosition(Point newPosition)
    {
        CurrentPosition = _surfaceManager.ClampToSafeWorkArea(newPosition, _characterSize);
        _targetPosition = CurrentPosition;
        IsMoving = false;

        // Check if placed on top of an available window surface
        var availableSurfaces = _surfaceManager.GetAvailableSurfaces();
        var matchingSurface = availableSurfaces.FirstOrDefault(s =>
            s.SurfaceType == PetSurfaceType.ApplicationWindowTopEdge &&
            s.Bounds.Contains(CurrentPosition));

        if (matchingSurface != null)
        {
            CurrentSurface = matchingSurface;
        }
        else if (CurrentSurface.SurfaceType != PetSurfaceType.DesktopWorkArea)
        {
            CurrentSurface = _surfaceManager.GetPrimaryWorkAreaSurface();
        }

        if (_stateMachine != null && (_stateMachine.CurrentState is CharacterState.Walk or CharacterState.Run))
        {
            _stateMachine.SetState(CharacterState.Idle);
        }

        PositionChanged?.Invoke(this, CurrentPosition);
    }

    public void ResetToSafeSurface()
    {
        CurrentSurface = _surfaceManager.GetPrimaryWorkAreaSurface();
        var safePoint = new Point(
            CurrentSurface.Bounds.Left + (CurrentSurface.Bounds.Width / 2) - (_characterSize.Width / 2),
            CurrentSurface.Bounds.Top
        );
        SetPosition(safePoint);
    }

    public void Update(TimeSpan elapsed)
    {
        // Check surface validity; recover if active window disappeared
        if (!_surfaceManager.IsSurfaceValid(CurrentSurface))
        {
            CurrentSurface = _surfaceManager.GetPrimaryWorkAreaSurface();
            var recoveryTarget = new Point(
                CurrentSurface.Bounds.Left + (CurrentSurface.Bounds.Width / 2) - (_characterSize.Width / 2),
                CurrentSurface.Bounds.Top
            );
            _targetPosition = _surfaceManager.ClampToSafeWorkArea(recoveryTarget, _characterSize);
        }

        // Evaluate target surface per follow mode
        var desiredSurface = _surfaceManager.SelectTargetSurface(FollowMode, CurrentSurface);
        if (desiredSurface.SurfaceId != CurrentSurface.SurfaceId)
        {
            CurrentSurface = desiredSurface;
            var rawTarget = new Point(
                CurrentSurface.Bounds.Left + (CurrentSurface.Bounds.Width / 2) - (_characterSize.Width / 2),
                CurrentSurface.Bounds.Top
            );
            _targetPosition = _surfaceManager.ClampToSafeWorkArea(rawTarget, _characterSize);
        }

        // Distance check
        var dx = _targetPosition.X - CurrentPosition.X;
        var dy = _targetPosition.Y - CurrentPosition.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance > 5.0)
        {
            IsMoving = true;
            var isRunning = distance > 350.0;
            var speed = isRunning ? RunSpeed : WalkSpeed;
            var desiredState = isRunning ? CharacterState.Run : CharacterState.Walk;
            var facingLeft = dx < 0;

            CurrentIntent = new MovementIntent(
                TargetX: _targetPosition.X,
                TargetY: _targetPosition.Y,
                FacingLeft: facingLeft,
                DesiredState: desiredState,
                Velocity: speed
            );
            MovementIntentChanged?.Invoke(this, CurrentIntent);

            // Interpolate position
            var step = speed * Math.Max(0.01, elapsed.TotalSeconds);
            if (step >= distance)
            {
                CurrentPosition = _targetPosition;
            }
            else
            {
                var ratio = step / distance;
                var newX = (int)Math.Round(CurrentPosition.X + dx * ratio);
                var newY = (int)Math.Round(CurrentPosition.Y + dy * ratio);
                CurrentPosition = new Point(newX, newY);
            }

            // Sync state machine if not busy with user interaction
            if (_stateMachine != null && _stateMachine.CurrentState is CharacterState.Idle or CharacterState.Walk or CharacterState.Run)
            {
                if (_stateMachine.CurrentState != desiredState)
                {
                    _stateMachine.SetState(desiredState);
                }
            }

            PositionChanged?.Invoke(this, CurrentPosition);
        }
        else
        {
            if (IsMoving)
            {
                IsMoving = false;
                CurrentIntent = null;

                if (_stateMachine != null && _stateMachine.CurrentState is CharacterState.Walk or CharacterState.Run)
                {
                    _stateMachine.SetState(CharacterState.Idle);
                }
            }
        }
    }
}
