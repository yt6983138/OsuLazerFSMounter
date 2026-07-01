# OsuLazerFSMounter

This is a tool to mount osu!lazer's hash-based file storage (User file storage) as a virtual drive on Windows. It allows you to access the game's files directly from the mounted drive, making it easier to view.

> Warning: This tool is in very WIP stage, and is NOT thoroughly tested (yet).

> Warning: Modifying the hash-based storage is not recommended from osu official, use this tool at your own risk.

## Status

Core:
- [x] Mount as a virtual drive
- [x] Reading
    - [x] Skins
    - [x] Beatmaps
- [x] Writing, Deleting, Renaming/Moving
    - [x] Skins
    - [x] Beatmaps (need to update their corresponding collection, and individual beatmap hash)
- [x] Unmount button
    - [x] If the filesystem is unmounted with open handle, rewrite them back

CLI:
- [x] Mount as a virtual drive
- [x] Reading
    - [x] Skins
    - [x] Beatmaps
- [ ] Writing, Deleting, Renaming/Moving
    - [x] Without osu running
    - [ ] With osu running (live mode)
- [ ] Command interface
    - [x] Specifying realm file location
    - [x] Write enable flag
    - [ ] Live mode

Plugin (as a fake ruleset):
- [x] Mount as a virtual drive
- [x] Reading
    - [x] Update the filesystem when something is added, modified, or removed
        - [x] Skins
        - [x] Beatmaps
- [x] Writing, Deleting, Renaming/Moving
    - [ ] Invalidate all internal game cache when filesystem is written
        - [ ] Beatmap (Warning: cache invalidation is kinda broken, and may cause various issues like unable to load song)
        - [x] Skins (doesn't really have cache so it works fine)
- [ ] Interface
    - [x] Mount button
    - [x] Unmount button
    - [x] Toggle read-write/read-only
    - [x] Reload skin
    - [ ] Reload beatmap (?)
