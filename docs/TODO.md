# TODO

## Tagging and Organization
- Tag Relationships
  - Implement UI for defining tag siblings (synonyms).
  - Allow creation of tag parent relationships.
  - Reindex existing media after relationship changes.
- Tag Autocomplete
  - Provide real-time tag suggestions as the user types.
  - Debounce requests to avoid excessive database calls.
- Ratings
  - Add support for like/dislike rating system.
  - Display ratings in search and media views.

## Import and Download
- Local Import
  - Support drag-and-drop folder import.
  - Extract tags and metadata from filenames.
- Remote Import
  - Implement URL watchers for scheduled downloads.
  - Add downloader plugins for common booru sites.

## Duplicate Management
- Perceptual Hashing
  - Generate perceptual hashes for imported media.
  - Compare hashes to flag potential duplicates.
- Duplicate Filter UI
  - Side-by-side viewer to decide which file to keep.
  - Automatic selection of the higher-quality file.

## Networking and Sync
- Tag Repository
  - Implement server component for sharing tag mappings.
  - Client synchronization with remote tag repository.
- Content Updates
  - Support exporting and importing updates via packages.
  - Provide progress UI for update downloads.

## User Interface
- Keyboard Shortcuts
  - Add keybindings for tagging, rating and navigation.
  - Configurable shortcut editor in settings.
- Layout Customization
  - Allow reordering of panels and tabs.
  - Persist layout preferences per user.

