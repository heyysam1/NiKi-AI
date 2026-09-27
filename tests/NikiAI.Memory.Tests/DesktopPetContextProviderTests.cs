using NikiAI.Core.Character;
using NikiAI.Core.Memory;
using NikiAI.Core.Notifications;
using NikiAI.Core.Tasks;
using NikiAI.Memory;
using Xunit;

namespace NikiAI.Memory.Tests;

public sealed class DesktopPetContextProviderTests
{
    private readonly InMemoryMemoryStore _memoryStore = new();
    private readonly MemoryService _memoryService;
    private readonly DesktopPetContextProvider _provider;

    public DesktopPetContextProviderTests()
    {
        _memoryService = new MemoryService(_memoryStore);
        _provider = new DesktopPetContextProvider(
            taskRepository: null,
            notificationRepository: null);
    }

    [Fact]
    public void GetContextSnapshot_ReturnsValidEphemeralSnapshot()
    {
        var snapshot = _provider.GetContextSnapshot();

        Assert.NotNull(snapshot);
        Assert.True(snapshot.SessionDuration >= TimeSpan.Zero);
        Assert.True(Enum.IsDefined(typeof(TimeOfDayBucket), snapshot.TimeOfDay));
        Assert.Equal(0, snapshot.ActiveNotificationCount);
        Assert.Null(snapshot.LastPetInteractionTime);
    }

    [Fact]
    public void RecordUserInteraction_UpdatesInteractionTimestamp()
    {
        _provider.RecordUserInteraction();
        var snapshot = _provider.GetContextSnapshot();

        Assert.NotNull(snapshot.LastPetInteractionTime);
        Assert.True(DateTimeOffset.UtcNow - snapshot.LastPetInteractionTime.Value < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetContextSnapshot_WhenMemoryDisabled_StillReturnsSnapshotIndependently()
    {
        await _memoryService.SetMemoryEnabledAsync(false);
        _provider.RecordUserInteraction();

        var snapshot = _provider.GetContextSnapshot();

        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.LastPetInteractionTime);
        Assert.True(snapshot.SessionDuration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task GetContextSnapshot_DoesNotPersistAnyMemoryItems()
    {
        _provider.RecordUserInteraction();
        _ = _provider.GetContextSnapshot();

        var storedMemories = await _memoryService.GetAllMemoriesAsync();

        // Must NOT create any memory items!
        Assert.Empty(storedMemories);
    }

    [Fact]
    public void GetContextSnapshot_OnlyExposesTaskState_NotTaskTitleOrContent()
    {
        var snapshot = _provider.GetContextSnapshot();
        Assert.Null(snapshot.CurrentTaskState);

        // Reflection proof that PetContextSnapshot contains no title or content fields/properties
        var type = typeof(PetContextSnapshot);
        Assert.Null(type.GetProperty("ActiveTaskTitle"));
        Assert.Null(type.GetProperty("TaskTitle"));
        Assert.Null(type.GetProperty("Title"));
        Assert.Null(type.GetProperty("TaskContent"));
    }
}
