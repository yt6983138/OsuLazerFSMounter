# OsuLazerFSMounter

This is a tool to mount osu!lazer's hash-based file storage (User file storage) as a virtual drive on Windows. It allows you to access the game's files directly from the mounted drive, making it easier to view.

> Warning: This tool is in very WIP stage.

> Warning: Modifying the hash-based storage is not recommended from osu official, use this tool at your own risk.

## Status

Core:
- [x] Mount as a virtual drive
- [x] Reading
- [ ] Writing, Deleting, Renaming/Moving
    - [x] Skins
    - [ ] Beatmaps (need to update their corresponding collection, and individual beatmap hash)

CLI:
- [x] Mount as a virtual drive
- [x] Reading
- [ ] Writing, Deleting, Renaming/Moving
    - [x] Without osu running
    - [ ] With osu running (live mode)
- [ ] Command interface
    - [x] Specifying realm file location
    - [x] Write enable flag
    - [ ] Live mode

Plugin (as a fake ruleset):
- [x] Mount as a virtual drive
- [ ] Reading
    - [ ] Update the filesystem when a beatmap/skin is added, modified, or removed
- [ ] Writing, Deleting, Renaming/Moving
    - [ ] Invalidate all internal game cache when filesystem is written
- [ ] Interface
    - [x] Mount button
    - [ ] Unmount button
        - [ ] If the filesystem is unmounted with open handle, rewrite them back
    - [ ] Toggle read-write/read-only
    - [ ] Reload skin
    - [ ] Reload beatmap (?)
