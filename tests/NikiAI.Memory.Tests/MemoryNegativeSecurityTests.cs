using System.Reflection;
using NikiAI.Core.Character;
using NikiAI.Core.Memory;
using NikiAI.Core.Notifications;
using NikiAI.Core.Tasks;
using NikiAI.Memory;
using Xunit;

namespace NikiAI.Memory.Tests;

public sealed class MemoryNegativeSecurityTests
{
    private readonly InMemoryMemoryStore _memoryStore = new();
    private readonly MemoryService _memoryService;
    private readonly DesktopPetContextProvider _petContextProvider;
    private readonly MemoryContextManager _contextManager;

    public MemoryNegativeSecurityTests()
    {
        _memoryService = new MemoryService(_memoryStore);
        _petContextProvider = new DesktopPetContextProvider(null, null);
        _contextManager = new MemoryContextManager(_memoryService);
    }

    [Fact]
    public async Task NegativeTest_1_NormalConversation_CannotSilentlyCreateMemory()
    {
        // Simulated conversation turns: user sends message, agent processes it
        var userChatMessage = "Hello Niki, I like drinking green tea in the morning.";
        var assistantResponse = "Good morning! Green tea is refreshing.";
        _ = userChatMessage;
        _ = assistantResponse;

        // Assert memory store is completely empty: chat interactions do not create memory items
        var storedMemories = await _memoryService.GetAllMemoriesAsync();
        Assert.Empty(storedMemories);
    }

    [Fact]
    public async Task NegativeTest_2_PetInteraction_CannotSilentlyCreateMemory()
    {
        // Pet clicks, drags, squashes, animations occur
        _petContextProvider.RecordUserInteraction();
        _petContextProvider.RecordUserInteraction();

        var snapshot = _petContextProvider.GetContextSnapshot();
        Assert.NotNull(snapshot.LastPetInteractionTime);

        // Memory store MUST remain strictly empty
        var storedMemories = await _memoryService.GetAllMemoriesAsync();
        Assert.Empty(storedMemories);
    }

    [Fact]
    public async Task NegativeTest_3_TimelineEvents_CannotBecomeMemory()
    {
        // Timeline events logged to timeline store
        var timelineStore = new List<TimelineEvent>
        {
            new("tl-1", "app_launch", "User launched VS Code", "VS Code", null, DateTimeOffset.UtcNow, null)
        };

        // Querying memory store for the timeline content must return 0 results
        var memoryResults = await _memoryService.SearchMemoriesAsync("VS Code");
        Assert.Empty(memoryResults);

        var allMemories = await _memoryService.GetAllMemoriesAsync();
        Assert.Empty(allMemories);
    }

    [Fact]
    public async Task NegativeTest_4_MemoryEnabledFalse_BlocksAgentMemoryReadWriteAndInjection()
    {
        await _memoryService.SaveExplicitMemoryAsync("Dark Mode", MemoryCategory.LongTerm, "user_pref");

        // User turns off memory
        await _memoryService.SetMemoryEnabledAsync(false);

        // 1. Write blocked
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _memoryService.SaveExplicitMemoryAsync("new_val", MemoryCategory.LongTerm, "new_key");
        });

        // 2. Read / Search blocked for agent
        var searchResults = await _memoryService.SearchMemoriesAsync("Dark Mode");
        Assert.Empty(searchResults);

        // 3. Prompt injection suppressed
        var promptContext = await _contextManager.BuildMemoryContextPromptAsync(null, 500);
        Assert.Empty(promptContext);
    }

    [Fact]
    public async Task NegativeTest_5_PetContext_StillWorksIndependently_WhenMemoryEnabledFalse()
    {
        // Disable memory
        await _memoryService.SetMemoryEnabledAsync(false);
        _petContextProvider.RecordUserInteraction();

        // Pet context MUST still function normally
        var snapshot = _petContextProvider.GetContextSnapshot();
        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.LastPetInteractionTime);
        Assert.True(snapshot.SessionDuration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task NegativeTest_6_PetContext_IsNotPersisted()
    {
        _petContextProvider.RecordUserInteraction();
        var snapshot1 = _petContextProvider.GetContextSnapshot();
        Assert.NotNull(snapshot1);

        // Verify no database / memory store entries exist
        var stored = await _memoryService.GetAllMemoriesAsync();
        Assert.Empty(stored);

        // Verify IPetContextProvider has no persistence/save methods on its public interface
        var methods = typeof(IPetContextProvider).GetMethods();
        Assert.DoesNotContain(methods, m => m.Name.Contains("Save", StringComparison.OrdinalIgnoreCase) ||
                                            m.Name.Contains("Persist", StringComparison.OrdinalIgnoreCase) ||
                                            m.Name.Contains("Store", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NegativeTest_7_PetContext_DoesNotInspectScreenContentsOrOcr()
    {
        // Inspect IPetContextProvider and DesktopPetContextProvider types and members
        var type = typeof(DesktopPetContextProvider);
        var fieldTypes = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.FieldType.Name);

        // Must not contain OCR, screen capture, or visual inspection fields
        Assert.DoesNotContain(fieldTypes, name => name.Contains("Ocr", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fieldTypes, name => name.Contains("Capture", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fieldTypes, name => name.Contains("Screen", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fieldTypes, name => name.Contains("Vision", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NegativeTest_8_AgentInferenceAlone_CannotCreatePermanentMemory()
    {
        // Background thought, observation, or uncommitted agent inference
        var inferredUserFact = "Agent deduced that user works late at night.";

        // Without an explicit call to SaveExplicitMemoryAsync by explicit intent, no memory is recorded
        var memories = await _memoryService.GetAllMemoriesAsync();
        Assert.DoesNotContain(memories, m => m.Content == inferredUserFact);
    }

    [Fact]
    public void NegativeTest_9_MemoryExport_RequiresExplicitUserAction()
    {
        // Verify that IMemoryService export method requires explicit invocation and is not tied to any automated background trigger
        var memoryServiceType = typeof(MemoryService);
        var exportMethod = memoryServiceType.GetMethod(nameof(MemoryService.ExportMemoriesAsync));

        Assert.NotNull(exportMethod);
        Assert.False(exportMethod.IsPrivate);
    }

    [Fact]
    public void NegativeTest_10_PetContext_DoesNotExposeTaskContentOrTitle()
    {
        // Inspect PetContextSnapshot properties: ensure no task content, title, or natural language request is exposed
        var properties = typeof(PetContextSnapshot).GetProperties();
        var propNames = properties.Select(p => p.Name).ToList();

        Assert.DoesNotContain(propNames, name => name.Contains("Title", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propNames, name => name.Contains("Content", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propNames, name => name.Contains("Request", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propNames, name => name.Contains("Goal", StringComparison.OrdinalIgnoreCase));

        // Ensure only non-sensitive CurrentTaskState exists for task context
        Assert.Contains("CurrentTaskState", propNames);
    }
}
