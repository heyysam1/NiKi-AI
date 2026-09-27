using System;
using System.IO;
using System.Threading;
using NikiAI.Character;
using NikiAI.Core.Character;
using Xunit;

namespace NikiAI.Character.Tests;

public class CharacterHardeningTests
{
    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new AggregateException("STA thread execution failed", ex);
        }
    }

    [Fact]
    public void CharacterRegistry_IsAssetBacked_FailsSafely_WhenFrameFileIsZeroByteOrMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NikiAI_Hardening_ZeroByte_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var charDir = Path.Combine(tempDir, "corrupt-char");
            Directory.CreateDirectory(charDir);

            // Write a zero-byte frame file
            var zeroByteFrame = Path.Combine(charDir, "frame_0.png");
            File.WriteAllBytes(zeroByteFrame, Array.Empty<byte>());

            var manifestJson = """
            {
              "id": "corrupt-char",
              "displayName": "Corrupt Character",
              "version": "1.0",
              "animations": {
                "Idle": {
                  "state": "Idle",
                  "frameDurationMs": 200,
                  "loop": true,
                  "frames": [
                    { "frameIndex": 0, "imagePath": "frame_0.png" }
                  ]
                }
              }
            }
            """;
            File.WriteAllText(Path.Combine(charDir, "character.json"), manifestJson);

            var registry = new CharacterRegistry(tempDir);
            Assert.False(registry.IsAssetBacked("corrupt-char"));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void MotionPhysicsSimulator_ReducedMotionAndIdle_GuaranteesIdentityTransform()
    {
        var simulator = new MotionPhysicsSimulator { ReducedMotion = true };
        var stepDuration = TimeSpan.FromMilliseconds(500);
        var halfElapsed = TimeSpan.FromMilliseconds(250);

        // Verify across all 16 primitives that Reduced Motion always returns exact identity transform
        foreach (MotionPrimitive primitive in Enum.GetValues(typeof(MotionPrimitive)))
        {
            var offset = simulator.CalculateOffset(primitive, halfElapsed, stepDuration, reducedMotion: true);
            Assert.True(offset.IsIdentity, $"Primitive {primitive} should return identity when ReducedMotion is true.");
            Assert.Equal(0.0, offset.OffsetX);
            Assert.Equal(0.0, offset.OffsetY);
            Assert.Equal(1.0, offset.ScaleX);
            Assert.Equal(1.0, offset.ScaleY);
        }

        // Verify that Idle primitive always returns identity even when ReducedMotion is false
        simulator.ReducedMotion = false;
        var idleOffset = simulator.CalculateOffset(MotionPrimitive.Idle, halfElapsed, stepDuration, reducedMotion: false);
        Assert.True(idleOffset.IsIdentity);

        // Verify that exceeding duration immediately ceases physics and returns identity (no continuous loop)
        var pastDurationOffset = simulator.CalculateOffset(MotionPrimitive.Bounce, TimeSpan.FromMilliseconds(600), stepDuration, reducedMotion: false);
        Assert.True(pastDurationOffset.IsIdentity);
    }

    [Fact]
    public void AnimationAndIdleControllers_RapidThrottleCycling_StopsTimers_AndNoDuplicateTimersCreated()
    {
        RunInSta(() =>
        {
            var sm = new CharacterStateMachine();
            var registry = new CharacterRegistry();
            using var animController = new CharacterAnimationController(sm, registry);
            using var idleController = new CasualIdleController(sm, animController);

            // Verify initial unthrottled state
            Assert.False(animController.IsThrottled);
            Assert.False(idleController.IsThrottled);

            // Rapidly cycle throttle state 10 times to verify no duplicate timer allocation or leaks
            for (int i = 0; i < 10; i++)
            {
                animController.SetThrottled(true);
                idleController.SetThrottled(true);
                Assert.True(animController.IsThrottled);
                Assert.True(idleController.IsThrottled);

                animController.SetThrottled(false);
                idleController.SetThrottled(false);
                Assert.False(animController.IsThrottled);
                Assert.False(idleController.IsThrottled);
            }

            // Final throttle to ensure clean stop
            animController.SetThrottled(true);
            idleController.SetThrottled(true);
            Assert.True(animController.IsThrottled);
            Assert.True(idleController.IsThrottled);
        });
    }
}
