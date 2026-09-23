using System.IO;
using System.Speech.Synthesis;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Voice;

namespace NikiAI.Voice;

/// <summary>
/// Built-in Windows SAPI implementation of ITtsProvider.
/// Generates uncompressed WAV audio streams locally without external API dependencies.
/// Serves as the zero-configuration base and resilient fallback provider.
/// </summary>
public class WindowsSapiTtsProvider : ITtsProvider
{
    public const string DefaultProviderId = "windows-sapi";
    private readonly ILogger<WindowsSapiTtsProvider>? _logger;

    public string ProviderId => DefaultProviderId;
    public string DisplayName => "Windows SAPI (Local Built-in)";
    public bool IsAvailable { get; private set; }

    public WindowsSapiTtsProvider(ILogger<WindowsSapiTtsProvider>? logger = null)
    {
        _logger = logger;
        CheckAvailability();
    }

    private void CheckAvailability()
    {
        try
        {
            using var synth = new SpeechSynthesizer();
            var voices = synth.GetInstalledVoices();
            IsAvailable = voices != null && voices.Count > 0;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Windows SAPI TTS is not available on this system.");
            IsAvailable = false;
        }
    }

    public Task<IReadOnlyList<VoiceDescriptor>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return Task.FromResult<IReadOnlyList<VoiceDescriptor>>(Array.Empty<VoiceDescriptor>());
        }

        try
        {
            using var synth = new SpeechSynthesizer();
            var voices = synth.GetInstalledVoices()
                .Where(v => v.Enabled)
                .Select(v => new VoiceDescriptor(
                    Id: v.VoiceInfo.Name,
                    Name: v.VoiceInfo.Name,
                    Gender: v.VoiceInfo.Gender.ToString(),
                    Locale: v.VoiceInfo.Culture?.Name,
                    ProviderId: ProviderId
                ))
                .ToList();

            return Task.FromResult<IReadOnlyList<VoiceDescriptor>>(voices);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to retrieve installed Windows SAPI voices.");
            return Task.FromResult<IReadOnlyList<VoiceDescriptor>>(Array.Empty<VoiceDescriptor>());
        }
    }

    public Task<Stream> SynthesizeSpeechAsync(string text, string? voiceId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var ms = new MemoryStream();
        try
        {
            using var synth = new SpeechSynthesizer();
            synth.SetOutputToWaveStream(ms);
            synth.Rate = 0;
            synth.Volume = 90;

            if (!string.IsNullOrWhiteSpace(voiceId))
            {
                try
                {
                    synth.SelectVoice(voiceId);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Could not select requested voice '{VoiceId}', using default.", voiceId);
                }
            }
            else
            {
                // Prefer female voice by default if available
                var installedVoices = synth.GetInstalledVoices();
                var femaleVoice = installedVoices.FirstOrDefault(v => v.Enabled && v.VoiceInfo.Gender == VoiceGender.Female);
                if (femaleVoice != null)
                {
                    synth.SelectVoice(femaleVoice.VoiceInfo.Name);
                }
            }

            synth.Speak(text);
            ms.Position = 0;
            return Task.FromResult<Stream>(ms);
        }
        catch (Exception ex)
        {
            ms.Dispose();
            _logger?.LogError(ex, "Error synthesizing SAPI speech.");
            throw;
        }
    }
}
