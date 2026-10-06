# unscience

**unscience** is a Kitten Space Agency mod that lets you do ubsurd, silly things to KSA.

It breaks physics, moves things around, resizes things, makes noises, changes colors and textures, and much more.

I describe it as a *supermod* that includes a suite of *submods*, each one has a fun unique name tangentially related to what it does, usually from some kind of pop culture reference.

# game compatibility

The current checkout compiles against KSA **2026.10.7.5541**, including the IVA camera addition
and Graffiti descriptor migration. Kitchen Sink and shared save checks pass; in-game acceptance
is pending. The last full integration audit remains **2026.9.22.5482**. See
[KSA compatibility](unscience/README.md#ksa-compatibility) for user-visible changes and
[ISSUES.md](ISSUES.md) for known gaps.

Kitchen Sink includes **Unlock IVA Camera**: enter IVA, enable the toggle, and use free cam
movement while retaining IVA interiors, lighting and configured ray tracing. Turning it off or
using **Return to Seat** restores the seated camera. Speed and the unlocked view are scene-saved.
See [Kitchen Sink controls](kitchen-sink/README.md#unlock-iva-camera).

Kitchen Sink also offers **See Inside Capsule (Experimental)**, off by default: it hides the
stock medium capsule's two opaque window surfaces and reveals its existing IVA cabin/glass.
The switch is scene-saved and reversible; native visual acceptance is pending. See
[the experiment](kitchen-sink/README.md#capsule-glass-experiment).

# releases

GitHub Actions uses `1.${GITHUB_RUN_NUMBER}.0` for stable releases from `main`
and `1.${GITHUB_RUN_NUMBER}.0-beta` for prereleases from `feature/*` branches.
The major version is fixed at `1` and the patch version at `0`. The same version
is used in `mod.toml`, the release title, and the ZIP filename; Git tags add a `v`
prefix. Reruns preserve an existing release. After publishing, main retains its
10 newest releases and all beta branches share the five newest prereleases.
Older releases and their tags are deleted, including releases using the previous
date-based main and timestamp-based feature formats. Other branches build only.

# media

![icon](docs/media/icon_full.webp)

![thug-life](docs/media/thug-life_01_sm.webp)

![garrys-torch](docs/media/garrys-torch_01_sm.webp)

![zippo](docs/media/zippo_01_sm.webp)

![godzilla 01](docs/media/godzilla_01_sm.webp)

![godzilla 02](docs/media/godzilla_02_sm.webp)

![free-fallin](docs/media/free-fallin_01_sm.webp)




