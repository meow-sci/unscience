# Bring Your Own Music (BYO-Music) - Custom Music Player

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

A simple music playback system that loads and plays audio playlists from KSA's asset system. Provides ImGui controls for triggering music playback on-demand.

## Overview

BYO-Music lets you:
- **Load custom playlists** - Select from available music assets
- **Play music on demand** - Click to start playback
- **Simple controls** - Minimal UI for audio control
- **Asset-based system** - Uses KSA's ModLibrary for music loading

## Features

- **Playlist loading** - Access playlists via ModLibrary asset system
- **Play triggering** - Simple button-based playback control
- **Asset integration** - Seamless KSA music system integration
- **No persistence** - Music plays during session; doesn't auto-save state
- **Extensible** - Easy to add more playlists or playback controls

## Architecture

### Core Classes

#### MusicPlayer
Static wrapper for music playback functionality.

**Key Methods**:
- `GetPlaylist(string assetId)` - Retrieve music playlist from ModLibrary
- `Play(MusicPlayList playlist)` - Start playlist playback

**Implementation**:
```csharp
public static class MusicPlayer
{
    public static MusicPlayList GetPlaylist(string assetId)
    {
        return ModLibrary.Get<MusicPlayList>(assetId);
    }
    
    public static void Play(MusicPlayList playlist)
    {
        playlist?.PlayMusic();
    }
}
```

#### UI (Mod.cs)

ImGui window with:
- **Playlist selector dropdown** - Choose from available playlists
- **Play button** - Trigger selected playlist playback
- **Stop button** - Halt current playback
- **Now playing display** - Show current playlist name
- **Volume control** (future) - Adjust playback volume

## Music Assets

Music playlists are loaded from KSA's ModLibrary:

**Asset Types**:
- `MusicPlayList` - Collections of audio tracks with metadata

**Built-in Playlists**:
- `SabotageMusic` - Example playlist (demonstration)

**Adding Playlists**:
1. Define in mod assets
2. Register with ModLibrary
3. Reference by asset ID in UI

## Usage Example

```csharp
// Load a playlist
var playlistId = "SabotageMusic";
var playlist = MusicPlayer.GetPlaylist(playlistId);

// Play it
if (playlist != null)
{
    MusicPlayer.Play(playlist);
}

// Stop (if gameplay needs it)
playlist?.Stop();
```

## Implementation Details

### Asset Loading Pattern

```csharp
// ModLibrary handles type-safe asset retrieval
var playlist = ModLibrary.Get<MusicPlayList>(assetId);

// Null-safe checks
if (playlist != null)
{
    playlist.PlayMusic();
}
```

### Playlist Interface

KSA's `MusicPlayList` likely contains:

```csharp
public class MusicPlayList
{
    public string Name { get; set; }
    public List<AudioClip> Tracks { get; set; }
    
    public void PlayMusic();
    public void StopMusic();
    public void SetVolume(float volume);
}
```

## Configuration

**Playlists**:
- Define available playlists in UI dropdown
- Each playlist has unique asset ID

**Example Config**:
```csharp
var playlists = new[]
{
    "SabotageMusic",
    "AmbientMusic",
    "ActionMusic"
};
```

## Notes for Future Development

- **Volume control** - Add playback volume adjustment
- **Pause/Resume** - Support pausing current playback
- **Next/Previous** - Track-level navigation
- **Shuffle** - Randomize track order
- **Repeat modes** - One/all/off repeat options
- **Crossfade** - Smooth transitions between tracks
- **Equalizer**: Frequency band control
- **Save state**: Restore last played playlist on load
- **Playlist editor**: Create/edit playlists in-game

## Integration Points

- **ModLibrary**: Asset loading system
- **MusicPlayList**: KSA audio system
- **ImGui**: UI rendering

## Performance

- **Loading**: Minimal overhead—one dictionary lookup per play
- **Streaming**: Handled by KSA's audio system
- **Memory**: Playlists managed by ModLibrary (reference counting)

## Technical Notes

### Asset ID Pattern
Asset IDs follow KSA's naming convention:
```
{ModName}.{AssetType}.{AssetName}
```

Example: `MeowSci.Music.SabotageMusic`

### Audio Formats
Supported formats depend on KSA's audio implementation (likely WAV, MP3, OGG).

## Dependencies

- **KSA Game**: ModLibrary, MusicPlayList, audio system
