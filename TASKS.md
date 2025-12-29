# Actionable Tasks for Hydrus Replacement

This document outlines a list of actionable tasks to move Octans towards being a functional replacement for Hydrus
Network.

## 1. Core Logic & Data

- [x] **Tag Hierarchy Support**
    - *Future Optimization:* Implement Recursive Common Table Expressions (CTE) for `GetDescendantsAsync` if tag
      relationships grow very large.

## 2. User Interface (Client)

- [ ] **Tag Management UI**
    - Currently, there is no way to manage tags, siblings, or parents in the UI.
    - **Task:** Create a "Tag Manager" page.
        - List all tags (paged).
        - Edit tag text/namespace.
        - Manage Siblings (Merge tags).
        - Manage Parents (Add/Remove parent-child links).

## 3. Extensibility

- [ ] **Lua Downloader UI**
    - Downloaders are loaded from Lua files.
    - **Task:** Add a UI to view installed downloaders, reload them, and perhaps test them with a URL to see what they
      extract.

## 4. Maintenance & Optimization

- [ ] **Database Optimization**
    - Ensure indexes exist on foreign keys in `TagParents`, `TagSiblings`.
    - Monitor search performance with large tag sets.
    - **Task:** Optimize `DuplicateService` comparison algorithm (currently O(N^2)). Use BK-tree or similar.

- [ ] **Code Refactoring**
    - **Task:** Review `TagSibling` and `TagParent` 'Status' property necessity (Hydrus legacy).
    - [x] **Task:** Convert DTO types in `ImportModels.cs` to `record`s.

## 5. Testing

- [ ] **Integration Testing**
    - Add more integration tests for the full search pipeline with complex tag hierarchies.

## Unsorted

### Tagging and Organization

- Tag Relationships
    - Implement UI for defining tag siblings (synonyms).
    - Allow creation of tag parent relationships.
    - Reindex existing media after relationship changes.
- Ratings
    - Add support for like/dislike rating system.
    - Display ratings in search and media views.

### Import and Download

- Local Import
    - Support drag-and-drop folder import.
    - Extract tags and metadata from filenames.

### User Interface

- Layout Customization
    - Allow reordering of panels and tabs.
    - Persist layout preferences per user.
