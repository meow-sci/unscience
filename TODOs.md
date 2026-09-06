# work items to do

- [x] refactor: only publish a single unscience mod, not all the individual mods.  keep the csproj for clean code demarcation to ensure each mod has its own vertical code that wont break others and only shares code via deliberate csproj sharing like ksa-abstractions.lib
- [x] completely remove defunct/unused mods: unladen-swallow, steely-eyed-missile-kitten, stampy, space-tape, red-alert, geeforce, mesh-deform, con-man, average-twr
- [x] copy pngs to <UNSCIENCE_DATA>/pngs during import for both graffiti and free-fallin, read from there automatically
- [x] kitten-animations: per-kitten targeting (not just currently controlled)
- [x] garrys-torch: scaling on independent axis
- [x] garrys-torch: when a weldee (source) is welded, animations on that thing (like light part actuation) are not working
- [ ] godzilla (new mod): vessel scaling.  "smart" scaling where we dont break part offsets and not smart (what garrys-torch does now)
- [x] zippo: disco light show
- [ ] byo-music: can this be tied to in-game location / 3d sound? repeat? "remember me" giant bender.  we should be able to import sound files in supported formats like ogg (not sure if brutal supports wav, mp3 as well) using our existing file/dir picker, and place them into a <UNSCIENCE_DATA>/sounds dir (copy them there like we do for pngs and glbs) and use this folder of files to drive filterable dropdowns to pick sounds.  can we target a particular vessel and have the sound be tied to it in 3d space?  if so can we set it on a loop like a continuous repeat with an option for how long between each repeat?
- [ ] pyro: animation cycle options.  an on/off infinite loop with a time for how long off and how long on in seconds, use dragfloat sliders.  this should be a runtime state thing.
- [ ] pebbles: i want the imported glbs to get copied to a <UNSCIENCE_DATA>/glbs folder using the same file/folder browser/picker pattern that we do for the PNGs so that anything importing glbs can share this, and any mesh pickers should automatically detect from this folder.  there will be a new mod "sphinx" trying to reuse these after this refactor.
- [ ] sphinx: place arbitrary statics from imported GLB models (meshes and textures), auto align with ground, rotation, translation offsets, scaling options. note we have a shared glbs import pattern already from pebbles, reuse that similar to how we reuse the pngs for graffiti and free-fallin. the intent is to place one-off statically positioned (probably) large textured meshes.  most likely the user will import a textured glb and we should support the easy path of just using that mesh+texture, but also note the user may want to change the texture to some other png we have available in the common pngs UNSCIENCE_DATA png dir.  also keep in mind this is just for fun and we want it to mostly "just work" so importing glbs with advanced unsupported features, if we can just ignore unsupported stuff and make it still work like pebbles does now, thats preferable.
- [ ] SKIP FOR NOW: its-so-shiny: completely broken
- [ ] SKIP FOR NOW: blinky: completely broken
- [ ]

