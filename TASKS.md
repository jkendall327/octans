# Actionable Tasks for Hydrus Replacement

This document outlines a list of actionable tasks to move Octans towards being a functional replacement for Hydrus
Network.

## 1. Core Logic & Data

- [x] **Tag Hierarchy Support**
    - Implemented `TagParentService` for managing parent-child relationships.
    - Integrated tag expansion into `HashSearcher` to support hierarchical search (searching "animal" finds "cat").
    - *Future Optimization:* Implement Recursive Common Table Expressions (CTE) for `GetDescendantsAsync` if tag
      relationships grow very large.

- [ ] **Duplicate Detection & Management**
    - The backend (`IPerceptualHashProvider`, `ReimportChecker`) exists but needs a proper management UI.
    - **Task:** Create a service to find potential duplicates using PHash distance.
    - **Task:** Create a UI to present duplicate candidates side-by-side for user resolution (Keep A, Keep B, Merge,
      Ignore).

- [ ] **Subscription System**
    - `SubscriptionService` and background workers exist.
    - **Task:** Implement `Subscription` entity management (CRUD).
    - **Task:** Implement the actual logic to fetch data from different downloaders (Galleries/Queries) periodically.

## 2. User Interface (Client)

- [ ] **Tag Management UI**
    - Currently, there is no way to manage tags, siblings, or parents in the UI.
    - **Task:** Create a "Tag Manager" page.
        - List all tags (paged).
        - Edit tag text/namespace.
        - Manage Siblings (Merge tags).
        - Manage Parents (Add/Remove parent-child links).

- [ ] **Subscription UI**
    - **Task:** Create a "Subscriptions" page.
        - Add new subscription (Select Downloader, Enter Query/URL, Set Frequency).
        - View status of subscriptions (Last run, new items found).

- [ ] **Importer UI Improvements**
    - The current import UI is basic.
    - **Task:** Add options to tag imports immediately.
    - **Task:** Show progress and results of imports more clearly (e.g., "Imported 50 files, 2 duplicates skipped").

- [ ] **File Details & Notes**
    - Hydrus allows rich notes and metadata editing.
    - **Task:** Add a "Details" pane in the Gallery view to show all metadata, file info, and allow adding text notes.

## 3. Extensibility

- [ ] **Lua Downloader UI**
    - Downloaders are loaded from Lua files.
    - **Task:** Add a UI to view installed downloaders, reload them, and perhaps test them with a URL to see what they
      extract.

## 4. Maintenance & Optimization

- [ ] **Database Optimization**
    - Ensure indexes exist on foreign keys in `TagParents`, `TagSiblings`.
    - Monitor search performance with large tag sets.

## 5. Testing

- [ ] **Integration Testing**
    - Add more integration tests for the full search pipeline with complex tag hierarchies.

## Unsorted

### Tagging and Organization

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

### Import and Download

- Local Import
    - Support drag-and-drop folder import.
    - Extract tags and metadata from filenames.
- Remote Import
    - Implement URL watchers for scheduled downloads.
    - Add downloader plugins for common booru sites.

### Duplicate Management

- Perceptual Hashing
    - Generate perceptual hashes for imported media.
    - Compare hashes to flag potential duplicates.
- Duplicate Filter UI
    - Side-by-side viewer to decide which file to keep.
    - Automatic selection of the higher-quality file.

### User Interface

- Keyboard Shortcuts
    - Add keybindings for tagging, rating and navigation.
    - Configurable shortcut editor in settings.
- Layout Customization
    - Allow reordering of panels and tabs.
    - Persist layout preferences per user.
