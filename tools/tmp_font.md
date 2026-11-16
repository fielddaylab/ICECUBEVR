# Caption SDF font asset (card N3)

How `Assets/Fonts/Caption SDF.asset` is generated, and how to swap the typeface
when owner decision C1 changes.

Everything is driven by one script, `Assets/Editor/TMPCaptionFontBuilder.cs`, so
the asset is reproducible from the command line and nothing depends on a human
clicking through the Font Asset Creator window.

## Generator settings

These are the constants at the top of `TMPCaptionFontBuilder.cs`. This table is
the documentation the card asks for; if you change a constant, change it here too.

| Setting              | Value                                        |
|----------------------|----------------------------------------------|
| Source font          | `Assets/TextMesh Pro/Fonts/LiberationSans.ttf` |
| Sampling point size  | 90                                           |
| Padding              | 9                                            |
| Packing mode         | Best Short Side Fit                          |
| Atlas resolution     | 1024 x 1024 (single page, Alpha8)            |
| Render mode          | `SDFAA`                                      |
| Atlas population     | **Static** (set after baking)                |
| Multi-atlas          | **Off**                                      |
| Font features        | Kerning included                             |

`Static` + multi-atlas off is deliberate and load-bearing for WebGL. A dynamic
atlas rasterizes glyphs at runtime via FreeType, which means shipping the `.ttf`
and paying main-thread time mid-frame. Once baked, the asset's
`m_SourceFontFile` is `{fileID: 0}` — the font reference is gone, so the player
*cannot* rasterize even if asked.

## Character set: two tiers

A 1024x1024 SDFAA page at 90pt holds roughly 200 glyphs. "ASCII + all of Latin-1
+ all of Latin Extended-A" is 328 characters and physically does not fit, so the
set is split by priority and packed until the page is full:

**Required** (95 glyphs, must all be present or the build fails):
- Printable ASCII, `U+0020`–`U+007E`
- Every character appearing in a string literal in `Assets/Scripts/Main.cs`
  (this covers `subtitle_strings` and the alert/label strings that share the
  same caption path). Currently pure ASCII, so it is a subset of the above.

**Optional** (packed in this order; whatever does not fit is dropped):
1. Typographic punctuation — en/em dash, curly quotes, ellipsis
   (`U+2013`, `U+2014`, `U+2018`–`U+201D`, `U+2026`). First because a caption
   writer reaches for these constantly.
2. Latin-1 Supplement `U+00A0`–`U+00FF` — accented Latin for Western European text.
3. Latin Extended-A `U+0100`–`U+017F` — Central/Eastern European. Dropped first.

The exact set that landed in the asset is committed to
`Assets/Fonts/caption_charset.txt`, so a rebuild is deterministic and a diff
shows exactly which glyphs moved.

Currently 105 of 233 optional glyphs fit (all punctuation and all of Latin-1,
plus a little Latin Extended-A); the remaining 128 are dropped. **This is
expected, not a failure** — they are absent from the atlas, not from the font.
See `optional_dropped_cps` in `tools/tmp_font_report.txt`.

> If a future corpus needs accented glyphs, do not enable multi-atlas. Either
> drop the sampling point size (72pt roughly doubles the glyph budget) or trim
> the optional tier. Multi-atlas costs an extra draw call per page.

## Rebuilding

Requires the Unity 2019.4.40f1 container (the project is built headless):

```sh
docker exec icecube-unity-n3 /opt/unity/Editor/Unity \
  -quit -batchmode -nographics -silent-crashes \
  -logFile /out/caption_font.log \
  -projectPath /project \
  -executeMethod IceCube.TMPCaptionFontBuilder.BuildCaptionFont
```

In a headed editor the same thing is on the menu: **icecube > TMP > 2. Build
Caption SDF Font**.

Exit codes: `0` ok · `2` TMP essentials or source font missing · `3` atlas came
back the wrong size · `4` **kill criterion** — the typeface lacks a required
glyph · `5` the required glyphs do not fit the atlas (a settings problem, not a
font one: lower the point size or raise the atlas size).

### First-time setup / TMP essentials

`Assets/TextMesh Pro/**` is committed, so this is normally unnecessary. On a
fresh checkout that lacks it:

```sh
# NOTE: no -quit. The package import is asynchronous and the script exits
# itself from the importPackageCompleted callback; -quit would discard it.
docker exec icecube-unity-n3 /opt/unity/Editor/Unity \
  -batchmode -nographics -silent-crashes \
  -logFile /out/tmp_import.log -projectPath /project \
  -executeMethod IceCube.TMPCaptionFontBuilder.RebuildAll
```

`RebuildAll` imports the essentials and then builds the font in one pass.

## Swapping the typeface (owner decision C1)

1. Drop the new `.ttf`/`.otf` into `Assets/Fonts/`. Make sure the importer has
   **Include Font Data** on, or `CreateFontAsset` returns null.
2. Point `SourceFontPath` in `TMPCaptionFontBuilder.cs` at it (one line).
3. Rebuild with the command above.

Then read `tools/tmp_font_report.txt`. If `subtitle_corpus_missing` is anything
other than `(none)`, the build will have already failed with exit 4 and the log
names the exact code points — that is the card's kill criterion, and it needs an
owner decision on the font before N4 proceeds.

Update the settings table above if the point size or atlas size changed.

## Glyph report

`tools/tmp_font_report.txt` is regenerated on every build and is the artifact
the acceptance criteria check. The fields that matter:

- `required_all_present` — must be `True`
- `subtitle_corpus_missing` — must be `(none)`
- `atlas` — must be `1024x1024`
- `population_mode` — must be `Static`
- `multi_atlas` — must be `False`
- `optional_dropped` / `optional_dropped_cps` — informational only

## Material preset

`Assets/Fonts/Caption SDF - Outline.mat`, shader
`TextMeshPro/Mobile/Distance Field` (the mobile variant is the cheap one, which
is what we want on a Chromebook):

- Outline: width `0.15`, black, `OUTLINE_ON`
- Underlay (drop shadow): offset `0.5 / -0.5`, black at alpha `0.6`,
  dilate `0.1`, softness `0.3`, `UNDERLAY_ON`
- `_GradientScale` is `padding + 1` = `10`, and `_TextureWidth/Height` mirror the
  atlas. These three must agree with the font asset or text renders at the wrong
  weight.

Captions sit over both snow and black sky, hence outline *and* shadow.

Outline width is in SDF spread units (0..1), not pixels — it scales with the
text, which is why it is `0.15` and not a pixel count.

## Gotcha: TMP's `missingCharacters` lies

`TMP_FontAsset.TryAddCharacters` reports a single `missingCharacters` string that
mixes two unrelated failures:

- the glyph is genuinely not in the font file (`glyphIndex == 0`), and
- the glyph exists but did not fit in the atlas (leftovers from
  `m_CharactersToAdd` get appended to the very same list).

Only the first is a font problem. An earlier version of this script treated the
combined list as the kill criterion and wrongly reported LiberationSans as
missing 130 Latin Extended-A glyphs — the font has every one of them (verified
independently with fontTools); the 1024 page was simply full.

So coverage is probed directly against the face with
`FontEngine.TryGetGlyphIndex` (`MissingFromFontFace`) *before* anything is
written, and the kill criterion is evaluated on that result only. Atlas capacity
is reported separately. Do not collapse these two checks back together.
