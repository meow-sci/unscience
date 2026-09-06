# Audio — BYO Music integration

Cataloged against KSA 2026.9.7.5402, `../ksa-game-assemblies/current/decomp`. BYO Music is now an
Unscience `ISubmod`; its standalone host remains a compile-only development reference.

| Integration | Source / invariant | Consumer |
|---|---|---|
| `GameAudio.System : FmodSystem` | `KSA/GameAudio.cs:55,104,269`; use the game's initialized Core system, not a second listener/system. KSA initializes right-handed 3D coordinates. | `byo-music.lib/VesselSound.cs` |
| `FmodSystem.TryCreateStream`, `Mode._3d/NonBlocking/LoopOff`, `CreateSoundExInfo` | `Brutal.FmodApi/Fmod.cs:1191`; nul-terminated UTF-8 filename using the same wrapper as stock `CreateFmodSound`. Own one stream per source. | constructor |
| `Sound.TryGetOpenState`, `OpenState.Ready/Error`, `Sound.TryRelease` | `Fmod.cs:1872,2267`; poll nonblocking readiness, fail after 15 seconds, release on completion/error/stop/unload. Never spin or sleep the game thread during decode. | stream lifecycle |
| `ModLibrary.Get<ChannelGroupReference>("Sfx").ChannelGroup` | `GameAudio.cs:111-142`; built-in Sfx group registered at audio startup; gain respects SFX/master settings. | playback start |
| `FmodSystem.TryPlaySound`, channel `TrySetPaused`, `TrySetVolume`, `TryIsPlaying`, `TryStop` | `Fmod.cs:1341,2695,2704,2735,2984`; create paused, set gain/position/loop before unpausing. Invalid/stolen channel handles count as completion. | playback lifecycle |
| `Channel.TrySetMode`, `TrySetLoopCount`, `Mode.LoopNormal/LoopOff/_3dLinearRolloff` | `Fmod.cs:2943,3774`; infinite native looping for zero gaps. Positive gaps use managed completion-based real-time scheduling; no simultaneous replay. | live settings |
| `SpatialAudio(Vehicle).PositionView()/VelocityView()` | `KSA/SpatialAudio.cs:38-72`; computes position/velocity relative to `GameAudio.GetAudioCamera`, then rotates ego coordinates into listener view. Same path as `ChannelWrapper.SetSpatialAudio:434`. | per-frame follow |
| `Channel.TrySet3dAttributes/MinMaxDistance/Spread/DopplerLevel` | `Fmod.cs:3259,3306,3488,3550`; point-source stereo spread, linear range, zero Doppler for stable music during warp. No atmosphere attenuation is added. | 3D playback |
| `Vehicle.Id/IsDisposed`, `VehicleProvider.FindVehicle` identity | Re-EVA/system changes must not retarget an old source to a new object with the same id. | cleanup |
| StarMap / ISubmod / HiddenUiFrameHook | `ByoMusicSubmod.Update` runs with collapsed UI and hidden HUD in Unscience; floating shared browser dispatched separately; disposal stops all streams. Existing HotkeyGuard covers typing. | Unscience + development host |

No new reflection or Harmony lookup. `Brutal.Fmod.dll` is a compile-only game reference and is not
packaged. `SharedFileLibrary`, `SoundLibrary`, and `LibraryFileBrowser` in abstractions share the
existing PNG import behavior; PNG facades retain consumer APIs. The file catalog is persistent;
playback settings are runtime only. Format extensions do not guarantee codecs: errors surface in
individual entries. Supported loose WAV, MP3, Ogg Vorbis formats are documented in
[FMOD's Core loading reference](https://www.fmod.com/docs/2.03/api/loading-and-playing-sounds-in-the-core-api.html).

Legacy `MusicPlayer.GetPlaylist/Play` still references `ModLibrary.Get<MusicPlayList>` and
`MusicPlayList.PlayMusic(out _)`; the new UI does not depend on the old SabotageMusic demo asset.

Validation: full solution builds against 5402; managed tests cover loop/gap logic and copied catalog
semantics. Native format decoding, stereo localization, camera changes, SFX/master gain, repeat
transitions, target loss and unload still require an in-game audio pass.
