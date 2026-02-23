using System;
using Godot;
using HypeReborn.Hype.Runtime.Parsing;

namespace HypeReborn.Hype.Player;

public enum HypeMotionAnimationState
{
    Idle,
    Walk,
    Run,
    Jump,
    Fall
}

public readonly struct HypeCharacterAnimationSettings
{
    public required bool PauseWhenIdle { get; init; }
    public required bool UseMovementDrivenAnimation { get; init; }
    public required float AnimationSpeedMultiplier { get; init; }
    public required int IdleStartFrame { get; init; }
    public required int IdleEndFrame { get; init; }
    public required int WalkStartFrame { get; init; }
    public required int WalkEndFrame { get; init; }
    public required int RunStartFrame { get; init; }
    public required int RunEndFrame { get; init; }
    public required int JumpStartFrame { get; init; }
    public required int JumpEndFrame { get; init; }
    public required int FallStartFrame { get; init; }
    public required int FallEndFrame { get; init; }
    public required float WalkReferenceSpeed { get; init; }
    public required float RunReferenceSpeed { get; init; }
    public required float IdleSpeedScale { get; init; }
    public required float WalkSpeedScale { get; init; }
    public required float RunSpeedScale { get; init; }
    public required float AirSpeedScale { get; init; }
}

public sealed class HypeCharacterAnimator
{
    private float _frameClock;
    private HypeMotionAnimationState _motionState = HypeMotionAnimationState.Idle;
    private float _motionHorizontalSpeed;
    private (int Start, int End) _activeFrameRange = (0, 0);

    public void SetMotion(HypeMotionAnimationState state, float horizontalSpeed)
    {
        _motionState = state;
        _motionHorizontalSpeed = horizontalSpeed;
    }

    public void Reset()
    {
        _frameClock = 0f;
        _motionState = HypeMotionAnimationState.Idle;
        _motionHorizontalSpeed = 0f;
        _activeFrameRange = (0, 0);
    }

    public void Advance(
        HypeCharacterActorAsset actor,
        int currentFrame,
        float delta,
        HypeCharacterAnimationSettings settings,
        Action<int, bool> applyFrame)
    {
        if (settings.PauseWhenIdle && _motionState == HypeMotionAnimationState.Idle)
        {
            return;
        }

        var frameCount = actor.Frames.Count;
        if (frameCount == 0)
        {
            return;
        }

        var range = ResolveFrameRange(frameCount, settings);
        if (_activeFrameRange != range)
        {
            _activeFrameRange = range;
            _frameClock = range.Start;
            applyFrame(range.Start, false);
            currentFrame = range.Start;
        }

        var rangeLength = Math.Max(1, (range.End - range.Start) + 1);
        var speedScale = Math.Max(0f, ResolveMotionSpeedScale(settings));
        var fps = Math.Max(0f, actor.FramesPerSecond * Math.Max(0.01f, settings.AnimationSpeedMultiplier) * speedScale);
        if (fps <= float.Epsilon || rangeLength == 1)
        {
            if (currentFrame != range.Start)
            {
                applyFrame(range.Start, false);
            }
            return;
        }

        _frameClock += delta * fps;
        var localFrame = (int)MathF.Floor(_frameClock - range.Start) % rangeLength;
        if (localFrame < 0)
        {
            localFrame += rangeLength;
        }

        var frame = range.Start + localFrame;
        if (frame != currentFrame)
        {
            applyFrame(frame, false);
        }
    }

    private (int Start, int End) ResolveFrameRange(int frameCount, HypeCharacterAnimationSettings settings)
    {
        if (frameCount <= 0)
        {
            return (0, 0);
        }

        if (!settings.UseMovementDrivenAnimation)
        {
            return (0, frameCount - 1);
        }

        return _motionState switch
        {
            HypeMotionAnimationState.Idle => ResolveFrameRangeWithFallback(frameCount, settings.IdleStartFrame, settings.IdleEndFrame),
            HypeMotionAnimationState.Walk => ResolveFrameRangeWithFallback(frameCount, settings.WalkStartFrame, settings.WalkEndFrame),
            HypeMotionAnimationState.Run => ResolveFrameRangeWithFallback(
                frameCount,
                settings.RunStartFrame,
                settings.RunEndFrame,
                fallbackStart: settings.WalkStartFrame,
                fallbackEnd: settings.WalkEndFrame),
            HypeMotionAnimationState.Jump => ResolveFrameRangeWithFallback(
                frameCount,
                settings.JumpStartFrame,
                settings.JumpEndFrame,
                fallbackStart: settings.WalkStartFrame,
                fallbackEnd: settings.WalkEndFrame),
            HypeMotionAnimationState.Fall => ResolveFrameRangeWithFallback(
                frameCount,
                settings.FallStartFrame,
                settings.FallEndFrame,
                fallbackStart: settings.JumpStartFrame,
                fallbackEnd: settings.JumpEndFrame),
            _ => (0, frameCount - 1)
        };
    }

    private static (int Start, int End) ResolveFrameRangeWithFallback(
        int frameCount,
        int start,
        int end,
        int fallbackStart = -1,
        int fallbackEnd = -1)
    {
        if (TryResolveExplicitRange(frameCount, start, end, out var explicitRange))
        {
            return explicitRange;
        }

        if (TryResolveExplicitRange(frameCount, fallbackStart, fallbackEnd, out var fallbackRange))
        {
            return fallbackRange;
        }

        return (0, frameCount - 1);
    }

    private static bool TryResolveExplicitRange(int frameCount, int start, int end, out (int Start, int End) range)
    {
        range = (0, 0);
        if (frameCount <= 0 || (start < 0 && end < 0))
        {
            return false;
        }

        var resolvedStart = Math.Max(0, start);
        var resolvedEnd = end < 0 ? frameCount - 1 : Math.Min(end, frameCount - 1);
        if (resolvedEnd < resolvedStart)
        {
            (resolvedStart, resolvedEnd) = (resolvedEnd, resolvedStart);
        }

        range = (resolvedStart, resolvedEnd);
        return true;
    }

    private float ResolveMotionSpeedScale(HypeCharacterAnimationSettings settings)
    {
        if (!settings.UseMovementDrivenAnimation)
        {
            return 1f;
        }

        return _motionState switch
        {
            HypeMotionAnimationState.Idle => settings.IdleSpeedScale,
            HypeMotionAnimationState.Walk => settings.WalkSpeedScale * ResolveReferenceSpeedScale(settings.WalkReferenceSpeed),
            HypeMotionAnimationState.Run => settings.RunSpeedScale * ResolveReferenceSpeedScale(settings.RunReferenceSpeed),
            HypeMotionAnimationState.Jump => settings.AirSpeedScale,
            HypeMotionAnimationState.Fall => settings.AirSpeedScale,
            _ => 1f
        };
    }

    private float ResolveReferenceSpeedScale(float referenceSpeed)
    {
        if (referenceSpeed <= 0f)
        {
            return 1f;
        }

        var normalized = _motionHorizontalSpeed / referenceSpeed;
        return Mathf.Clamp(normalized, 0.35f, 2.2f);
    }
}
