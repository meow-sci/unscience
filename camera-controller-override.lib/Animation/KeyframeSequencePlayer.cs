using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.CameraControllerOverrideLib.Animation;

/// <summary>
/// Playback state for the keyframe sequence player.
/// </summary>
public enum PlaybackState
{
    /// <summary>
    /// Not playing, at beginning position.
    /// </summary>
    Stopped,
    
    /// <summary>
    /// Actively playing through the sequence.
    /// </summary>
    Playing,
    
    /// <summary>
    /// Paused at current position, can be resumed.
    /// </summary>
    Paused
}

/// <summary>
/// Controls playback of a sequence of keyframe animations with optional transitions.
/// Manages state, timing, and transitions between keyframes.
/// </summary>
public class KeyframeSequencePlayer
{
    // Public read-only properties
    
    /// <summary>
    /// The sequence of keyframes to play.
    /// </summary>
    public List<Keyframe> Keyframes { get; } = new List<Keyframe>();
    
    /// <summary>
    /// Index of the currently playing keyframe (0-based).
    /// </summary>
    public int CurrentKeyframeIndex { get; private set; }
    
    /// <summary>
    /// Current playback state (Stopped, Playing, or Paused).
    /// </summary>
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;
    
    /// <summary>
    /// Time elapsed within the current keyframe animation in seconds.
    /// </summary>
    public double CurrentKeyframeElapsedTime { get; private set; }
    
    /// <summary>
    /// Total time elapsed since Play() was called in seconds.
    /// </summary>
    public double TotalElapsedTime { get; private set; }
    
    /// <summary>
    /// Total duration of the entire sequence including all keyframes.
    /// </summary>
    public double TotalDuration
    {
        get
        {
            double total = 0.0;
            for (int i = 0; i < Keyframes.Count; i++)
            {
                var keyframe = Keyframes[i];
                total += keyframe.Animation.DurationSeconds;
            }
            return total;
        }
    }
    
    /// <summary>
    /// Gets or sets whether to return to start position after the sequence completes.
    /// </summary>
    public bool ReturnToStartEnabled
    {
        get => _returnToStartEnabled;
        set => _returnToStartEnabled = value;
    }
    
    /// <summary>
    /// Gets or sets the duration of the return-to-start animation in seconds (clamped to 1.0-10.0).
    /// </summary>
    public double ReturnToStartDuration
    {
        get => _returnToStartDuration;
        set => _returnToStartDuration = Math.Clamp(value, 1.0, 10.0);
    }
    
    /// <summary>
    /// Gets or sets the easing type for the return-to-start animation.
    /// </summary>
    public EasingType ReturnToStartEasing
    {
        get => _returnToStartEasing;
        set => _returnToStartEasing = value;
    }
    
    /// <summary>
    /// Gets or sets the starting easing power for the return-to-start animation.
    /// </summary>
    public double ReturnToStartEasingPowerStart
    {
        get => _returnToStartEasingPowerStart;
        set => _returnToStartEasingPowerStart = value;
    }
    
    /// <summary>
    /// Gets or sets the ending easing power for the return-to-start animation.
    /// </summary>
    public double ReturnToStartEasingPowerEnd
    {
        get => _returnToStartEasingPowerEnd;
        set => _returnToStartEasingPowerEnd = value;
    }
    
    /// <summary>
    /// Gets whether the player is currently returning to start position.
    /// </summary>
    public bool IsReturningToStart => _isReturningToStart;
    
    /// <summary>
    /// Gets the time elapsed during the return-to-start animation in seconds.
    /// </summary>
    public double ReturnElapsedTime => _returnElapsedTime;
    
    // Private state
    
    /// <summary>
    /// Flag to track if the current keyframe animation has been initialized.
    /// </summary>
    private bool _currentKeyframeInitialized;
    
    /// <summary>
    /// Camera offset from target at the start of the sequence.
    /// </summary>
    private double3 _sequenceStartOffset;
    
    /// <summary>
    /// Camera rotation at the start of the sequence.
    /// </summary>
    private doubleQuat _sequenceStartRotation;
    
    /// <summary>
    /// Flag to track if the sequence has started (and captured start position).
    /// </summary>
    private bool _hasStartedSequence = false;
    
    /// <summary>
    /// Whether to return to start position after sequence completes.
    /// </summary>
    private bool _returnToStartEnabled = true;
    
    /// <summary>
    /// Duration of the return-to-start animation in seconds.
    /// </summary>
    private double _returnToStartDuration = 3.0;
    
    /// <summary>
    /// Easing type for the return-to-start animation.
    /// </summary>
    private EasingType _returnToStartEasing = EasingType.EaseInOut;
    
    /// <summary>
    /// Starting easing power for the return-to-start animation.
    /// </summary>
    private double _returnToStartEasingPowerStart = 3.0;
    
    /// <summary>
    /// Ending easing power for the return-to-start animation.
    /// </summary>
    private double _returnToStartEasingPowerEnd = 3.0;
    
    /// <summary>
    /// Flag indicating if currently returning to start position.
    /// </summary>
    private bool _isReturningToStart = false;
    
    /// <summary>
    /// Time elapsed during return-to-start animation.
    /// </summary>
    private double _returnElapsedTime = 0.0;
    
    /// <summary>
    /// Camera offset from target at the end of sequence (where return-to-start begins).
    /// </summary>
    private double3 _returnFromOffset;
    
    /// <summary>
    /// Camera rotation at the end of sequence (where return-to-start begins).
    /// </summary>
    private doubleQuat _returnFromRotation;
    
    /// <summary>
    /// The controller instance that started the current playback.
    /// Used to prevent double-updates when both OrbitController and FlyController
    /// fire OnFrame in the same frame with different Transform3D instances.
    /// </summary>
    private Controller? _activeController;
    
    // Public methods
    
    /// <summary>
    /// Start playing the sequence from the beginning.
    /// Resets ALL state to ensure clean playback every time.
    /// </summary>
    public void Play()
    {
        // Reset playback position
        State = PlaybackState.Playing;
        CurrentKeyframeIndex = 0;
        CurrentKeyframeElapsedTime = 0.0;
        TotalElapsedTime = 0.0;
        _currentKeyframeInitialized = false;
        
        // Reset sequence start state - CRITICAL: must be false so start offset is captured fresh
        _hasStartedSequence = false;
        _sequenceStartOffset = double3.Zero;
        _sequenceStartRotation = doubleQuat.Identity;
        
        // Reset return-to-start state
        _isReturningToStart = false;
        _returnElapsedTime = 0.0;
        _returnFromOffset = double3.Zero;
        _activeController = null;
        
        // Reset all animation state - CRITICAL: ensures no stale state from previous runs
        foreach (var keyframe in Keyframes)
        {
            keyframe.Animation.Reset();
        }
        
        Console.WriteLine($"[KeyframeSequencePlayer] Play: Reset ALL state, {Keyframes.Count} keyframes");
    }
    
    /// <summary>
    /// Pause playback at the current position.
    /// </summary>
    public void Pause()
    {
        if (State == PlaybackState.Playing)
        {
            State = PlaybackState.Paused;
            Console.WriteLine("[KeyframeSequencePlayer] Paused");
        }
    }
    
    /// <summary>
    /// Resume playback from the paused position.
    /// </summary>
    public void Resume()
    {
        if (State == PlaybackState.Paused)
        {
            State = PlaybackState.Playing;
            Console.WriteLine("[KeyframeSequencePlayer] Resumed");
        }
    }
    
    /// <summary>
    /// Stop playback and reset ALL state to initial values.
    /// </summary>
    public void Stop()
    {
        // Reset playback position
        State = PlaybackState.Stopped;
        CurrentKeyframeIndex = 0;
        CurrentKeyframeElapsedTime = 0.0;
        TotalElapsedTime = 0.0;
        _currentKeyframeInitialized = false;
        
        // Reset sequence start state
        _hasStartedSequence = false;
        _sequenceStartOffset = double3.Zero;
        _sequenceStartRotation = doubleQuat.Identity;
        
        // Reset return-to-start state
        _isReturningToStart = false;
        _returnElapsedTime = 0.0;
        _returnFromOffset = double3.Zero;
        _returnFromRotation = doubleQuat.Identity;
        _activeController = null;
        
        // Reset all animation state
        foreach (var keyframe in Keyframes)
        {
            keyframe.Animation.Reset();
        }
        
        Console.WriteLine("[KeyframeSequencePlayer] Stop: Reset ALL state");
    }
    
    /// <summary>
    /// Add a keyframe to the sequence.
    /// </summary>
    /// <param name="animation">The animation to add.</param>
    /// <param name="includeTransitionIn">Whether to include a smooth transition from the previous keyframe.</param>
    /// <param name="transitionDuration">Duration of the transition-in animation in seconds.</param>
    /// <param name="transitionEasing">Easing function for the transition-in animation.</param>
    public void AddKeyframe(IKeyframeAnimation animation)
    {
        var keyframe = new Keyframe(animation)
        {
            Id = Keyframes.Count + 1
        };
        Keyframes.Add(keyframe);
        Console.WriteLine($"[KeyframeSequencePlayer] Added keyframe {keyframe.Id}: {animation.Name}");
    }
    
    /// <summary>
    /// Remove a keyframe from the sequence by index.
    /// </summary>
    /// <param name="index">The index of the keyframe to remove.</param>
    public void RemoveKeyframe(int index)
    {
        if (index < 0 || index >= Keyframes.Count)
        {
            Console.WriteLine($"[KeyframeSequencePlayer] Cannot remove keyframe at invalid index {index}");
            return;
        }
        
        // If currently playing this keyframe, stop playback
        if (State == PlaybackState.Playing && CurrentKeyframeIndex == index)
        {
            Console.WriteLine($"[KeyframeSequencePlayer] Removing currently playing keyframe, stopping playback");
            Stop();
        }
        // If the current keyframe is after the removed one, adjust the index
        else if (CurrentKeyframeIndex > index)
        {
            CurrentKeyframeIndex--;
        }
        
        Keyframes.RemoveAt(index);
        
        // Renumber remaining keyframes
        for (int i = 0; i < Keyframes.Count; i++)
        {
            Keyframes[i].Id = i + 1;
        }
        
        Console.WriteLine($"[KeyframeSequencePlayer] Removed keyframe at index {index}");
    }
    
    /// <summary>
    /// Move a keyframe from one position to another in the sequence.
    /// </summary>
    /// <param name="fromIndex">The current index of the keyframe.</param>
    /// <param name="toIndex">The new index for the keyframe.</param>
    public void MoveKeyframe(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Keyframes.Count)
        {
            Console.WriteLine($"[KeyframeSequencePlayer] Cannot move keyframe from invalid index {fromIndex}");
            return;
        }
        
        if (toIndex < 0 || toIndex >= Keyframes.Count)
        {
            Console.WriteLine($"[KeyframeSequencePlayer] Cannot move keyframe to invalid index {toIndex}");
            return;
        }
        
        if (fromIndex == toIndex)
        {
            return;
        }
        
        var keyframe = Keyframes[fromIndex];
        Keyframes.RemoveAt(fromIndex);
        Keyframes.Insert(toIndex, keyframe);
        
        // Renumber keyframes
        for (int i = 0; i < Keyframes.Count; i++)
        {
            Keyframes[i].Id = i + 1;
        }
        
        // Adjust current keyframe index if needed
        if (State == PlaybackState.Playing || State == PlaybackState.Paused)
        {
            if (CurrentKeyframeIndex == fromIndex)
            {
                CurrentKeyframeIndex = toIndex;
            }
            else if (fromIndex < CurrentKeyframeIndex && toIndex >= CurrentKeyframeIndex)
            {
                CurrentKeyframeIndex--;
            }
            else if (fromIndex > CurrentKeyframeIndex && toIndex <= CurrentKeyframeIndex)
            {
                CurrentKeyframeIndex++;
            }
        }
        
        Console.WriteLine($"[KeyframeSequencePlayer] Moved keyframe from index {fromIndex} to {toIndex}");
    }
    
    /// <summary>
    /// Clear all keyframes from the sequence.
    /// Stops playback if currently playing.
    /// </summary>
    public void Clear()
    {
        if (State != PlaybackState.Stopped)
        {
            Stop();
        }
        
        Keyframes.Clear();
        Console.WriteLine("[KeyframeSequencePlayer] Cleared all keyframes");
    }
    
    /// <summary>
    /// Update the sequence playback for the current frame.
    /// </summary>
    /// <param name="controller">The camera controller.</param>
    /// <param name="transform">The camera transform to modify.</param>
    /// <param name="deltaTime">Time elapsed since last frame in seconds.</param>
    /// <returns>True if the sequence is controlling the camera, false to allow normal camera control.</returns>
    public bool Update(Controller controller, Transform3D transform, double deltaTime)
    {
        // Don't control camera if stopped, paused, or no keyframes
        if (State != PlaybackState.Playing || Keyframes.Count == 0)
        {
            return false;
        }
        
        // Lock to the first controller that calls Update during this playback session.
        // Both OrbitController.OnFrame and FlyController.OnFrame are patched with the
        // same prefix, so Update can be called twice per frame with different Transform3D
        // instances. Only the first (active) controller should drive the animation.
        if (_activeController == null)
            _activeController = controller;
        else if (controller != _activeController)
            return true; // Still skip normal camera control, but don't run animation logic
        
        // Capture start offset and rotation on first frame of playback
        if (!_hasStartedSequence)
        {
            _sequenceStartOffset = AnimationHelpers.GetTargetOffset(controller, transform);
            _sequenceStartRotation = transform.LocalRotation;
            _hasStartedSequence = true;
            Console.WriteLine($"[KeyframeSequencePlayer] Captured start offset: {_sequenceStartOffset}, rotation: {_sequenceStartRotation}");
        }
        
        // Handle return-to-start animation
        // Smoothly interpolate BOTH position and rotation:
        // 1. Lerp OFFSET (relative to the CURRENT target) from end to start
        // 2. Slerp ROTATION from end to start
        // 3. Apply both to transform
        if (_isReturningToStart)
        {
            // Accumulate return time BEFORE using it
            _returnElapsedTime += deltaTime;
            
            double t = _returnElapsedTime / _returnToStartDuration;
            double easedT = AnimationHelpers.ApplyEasing(t, _returnToStartEasing, _returnToStartEasingPowerStart, _returnToStartEasingPowerEnd);
            
            // Lerp position offset
            double3 currentOffset = double3.Lerp(_returnFromOffset, _sequenceStartOffset, easedT);
            AnimationHelpers.SetTargetOffset(controller, transform, currentOffset);
            
            // Slerp rotation (quaternion interpolation)
            doubleQuat currentRotation = doubleQuat.Slerp(_returnFromRotation, _sequenceStartRotation, easedT);
            transform.LocalRotation = currentRotation;
            
            if (_returnElapsedTime >= _returnToStartDuration)
            {
                _isReturningToStart = false;
                State = PlaybackState.Stopped;
                Console.WriteLine("[KeyframeSequencePlayer] Return to start complete");
            }
            
            return true;
        }
        
        // Sequence complete
        if (CurrentKeyframeIndex >= Keyframes.Count)
        {
            Stop();
            return false;
        }
        
        // Get current keyframe
        var keyframe = Keyframes[CurrentKeyframeIndex];
        
        // Initialize keyframe animation on first update
        if (!_currentKeyframeInitialized)
        {
            Console.WriteLine($"[KeyframeSequencePlayer] Initializing keyframe {CurrentKeyframeIndex + 1}: camera pos = {transform.PositionEcl}");
            keyframe.Animation.Initialize(controller, transform);
            _currentKeyframeInitialized = true;
            CurrentKeyframeElapsedTime = 0.0;
            Console.WriteLine($"[KeyframeSequencePlayer] Started keyframe {CurrentKeyframeIndex + 1}: {keyframe.Animation.Name}");
        }
        
        // Accumulate time BEFORE Update so animations know time at END of frame
        CurrentKeyframeElapsedTime += deltaTime;
        TotalElapsedTime += deltaTime;
        
        // Update keyframe animation
        bool complete = keyframe.Animation.Update(controller, transform, deltaTime, CurrentKeyframeElapsedTime);
        
        // Keyframe animation finished
        if (complete)
        {
            Console.WriteLine($"[KeyframeSequencePlayer] Keyframe {CurrentKeyframeIndex + 1} complete, camera pos = {transform.PositionEcl}");
            
            // Move to next keyframe
            CurrentKeyframeIndex++;
            _currentKeyframeInitialized = false;
            
            // Check if sequence is complete
            if (CurrentKeyframeIndex >= Keyframes.Count)
            {
                Console.WriteLine("[KeyframeSequencePlayer] Sequence complete");
                
                if (_returnToStartEnabled)
                {
                    // Capture current offset and rotation, then start return-to-start
                    _returnFromOffset = AnimationHelpers.GetTargetOffset(controller, transform);
                    _returnFromRotation = transform.LocalRotation;
                    _isReturningToStart = true;
                    _returnElapsedTime = 0.0;
                    Console.WriteLine($"[KeyframeSequencePlayer] Starting return to start from offset: {_returnFromOffset}, rotation: {_returnFromRotation}");
                }
                else
                {
                    // No return-to-start, just stop
                    Stop();
                }
            }
        }
        
        return true; // Skip normal controller while playing keyframe
    }
}
