# Legacy standalone references

Only Unscience is distributed. BYO Music now implements `ISubmod` and is bundled; its current
integration map is [audio.md](audio.md). The development host still applies shared HotkeyGuard.

The retained `byo-music.lib/MusicPlayer` helper directly uses `ModLibrary.Get<MusicPlayList>(string)`
and `MusicPlayList.PlayMusic(out ChannelWrapper?, ulong delaySamples=0)` (KSA 5402:
`KSA/ModLibrary.cs`, `KSA/MusicPlayList.cs:21`). It is available to callers but the new panel uses
owned FMOD streams. The old SabotageMusic demonstration assets do not drive the distributed UI.
