# BYO Music — vessel sounds

Open **F11 → BYO Music** in Unscience. Import an **Ogg Vorbis (.ogg), WAV, or MP3**, choose a
sound and a vessel using the filterable pickers, then **Play on vessel**. The browser copies files
into `My Games/Kitten Space Agency/.unscience/sounds`. Files already there appear automatically
(within two seconds) or immediately after **Refresh library**. Originals can be moved/deleted after
import; duplicate names receive a numbered suffix. The browser shares the PNG import implementation.

Sound follows the selected vessel relative to KSA's audio camera, with distance falloff and stable
pitch. **Range** is the distance where the sound fades out; **Volume** also respects KSA's SFX/master
settings. Stereo files are positioned as a point source. Audio plays in real time, including in space
and time warp. Multiple vessels/files can play independently. Replaying the same sound on the same
vessel replaces the previous playback.

Enable **Repeat** and choose a **Gap between plays** in seconds. Zero uses FMOD's native continuous
loop; a positive gap starts after each completed play. Source-file padding remains audible silence
(the mod does not trim the recording). The live entries expose repeat/gap/volume/range edits,
**Stop**, **Stop all**, and **Clear stopped**. Target disappearance and unload stop and release streams.
Playback state is session only; imported files persist.

`VesselSound` owns each FMOD stream/channel, polls nonblocking decode without spinning the game
thread, positions/configures the paused channel before starting, and reports decoder/backend errors.
A file extension alone cannot guarantee a supported codec; a malformed file or unsupported codec reports
an error rather than leaving a stuck playback. [FMOD's format reference](https://www.fmod.com/docs/2.03/api/loading-and-playing-sounds-in-the-core-api.html)
confirms built-in WAV, MP3 and Ogg Vorbis support. KSA uses these same Core APIs through `Brutal.Fmod`.

`ByoMusicSubmod` is shared by Unscience and the compile-only development host. The old `MusicPlayer`
playlist API remains available to callers but is not the panel's playback path. No new Harmony patch
or global soundtrack replacement is required. See [audio integration](../scope/audio.md).

Validation: full solution compilation and `dotnet run --project byo-music.tests` cover the managed
repeat scheduler and shared copied-file catalog. Native decoding, 3D listening, SFX volume and
stop/unload behavior still require an in-game audio check.
