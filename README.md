# Field Language

Jellyfin plugin that pulls individual metadata fields in different languages.

Jellyfin's metadata language is one setting per library: everything comes back in one language or
nothing does. This lets you keep titles in English while descriptions come in Turkish, or any other
combination, set per library and per item type.

## Fields

| Item type | Fields |
|---|---|
| Movie, Series | Title, Description, Tagline, Genres |
| Season, Episode | Title, Description |

Limited to what TMDb returns per language. Season and episode payloads only carry name and overview.

If TMDb has no data for a field in the language you asked for, the existing value is kept rather
than blanked.

## Requirements

- Jellyfin 10.11.x
- A TMDb API key (free, v3). Jellyfin's own key is compiled into the server assembly and can't be
  reached from a plugin.

## Install

Download the release, drop `Jellyfin.Plugin.FieldLang.dll` and `meta.json` into
`<config>/plugins/Field Language_1.0.0.0/`, restart the server.

Then in Dashboard → Plugins → Field Language: paste your TMDb key, expand a library, put a language
code next to the fields you want, save.

Rules are applied by the **Apply per-field metadata languages** scheduled task, which runs daily.
Run it from Dashboard → Scheduled Tasks to apply a change immediately.

## Locking

Each rule has a lock toggle, on by default. Locking pins the field so Jellyfin's normal metadata
provider stops overwriting it. Unlocked, the provider rewrites it on every refresh and the next task
run puts it back, so the wrong language shows in between.

Unticking a lock removes it, so you can always release one from the config page.

Tagline has no lock in Jellyfin and always works the unlocked way.

## How it works

Everything runs from the scheduled task, which rewrites the mapped fields after the normal providers
have finished.

It is deliberately not an `IRemoteMetadataProvider`. That's the obvious design and it doesn't work.
`MetadataService.ExecuteRemoteProviders` merges with:

```csharp
if (replaceData || target.Genres.Length == 0) { target.Genres = source.Genres; }
```

A provider ordered after TMDb only overwrites a populated field when `replaceData` is true, which
means manual full refreshes only. On the scheduled refreshes that do most of the work it does
nothing at all. Running separately avoids provider ordering entirely.

The task only writes when a value actually differs, so re-running it over a correct library costs no
database writes.

## Adding a field

One entry in `FieldCatalog`, which carries the field's id, label, lock field and how to copy the
value onto an item. The config page renders itself from that catalog over `GET /FieldLang/Schema`,
so there's no UI to update.

## Build

```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet build Jellyfin.Plugin.FieldLang/Jellyfin.Plugin.FieldLang.csproj -c Release
```

Output lands in `Jellyfin.Plugin.FieldLang/bin/Release/net9.0/`.

## Prior art

[Polyglot](https://github.com/Maronato/jellyfin-plugin-polyglot) solves a related problem a
different way: whole mirrored libraries, one per language, via hardlinks. This plugin works at field
level inside a single library.

Long-standing upstream requests for the same thing:
[#10076](https://github.com/jellyfin/jellyfin/issues/10076),
[jellyfin-web#332](https://github.com/jellyfin/jellyfin-web/issues/332),
[#16400](https://github.com/jellyfin/jellyfin/issues/16400).

## License

MIT
