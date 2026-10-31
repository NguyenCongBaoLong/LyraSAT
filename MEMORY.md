# Architecture Invariants & Decisions - LyraSAT

## 1. Highlight System (DoTest.jsx)
- **Scope**: Reading Passage, Question Text, Option choices during active test session.
- **Engine**: CSS Custom Highlight API (`CSS.highlights`) with DOM fallback.
- **Persistence**: Session-only in React state. Does not write to DB and resets on F5 or test submit.
- **Interactions**:
  - `handleSelectOption` checks `window.getSelection()` to avoid accidental option selection on text dragging.
  - Floating palette uses `onMouseDown={e => e.preventDefault()}` to preserve text selection range before color click.

## 2. Question Comment & Discussion System
- **Placement**: Under each question in `ScoreHistory.jsx` review modal.
- **Data Model**:
  - `QuestionComments`: `Id`, `QuestionId`, `UserId`, `ParentCommentId`, `Content`, `AttachmentUrl`, `AttachmentType`, `AudioDuration`, `IsPinned`, `IsEdited`, `IsDeleted`.
  - `CommentReactions`: `(CommentId, UserId)` unique composite index for toggle likes.
  - `Notifications`: Tracks unread replies and new question discussions.
- **Permissions**:
  - Teacher & Admin: Can delete ANY comment, and can Pin/Unpin comments.
  - Author: Can edit and delete their own comment.
  - All users: Can reply in thread, react with heart/like, view public discussion.
- **Storage & Compression (Supabase Free Plan Protection)**:
  - Binary files (images, audio voice notes, videos) are saved in server static storage (`wwwroot/uploads/comments`), NOT in Supabase storage buckets.
  - Supabase database only stores lightweight string URLs (`/uploads/comments/...`), keeping database storage footprint negligible.
  - Images are compressed client-side via HTML5 canvas to WebP (max 1200px, quality 0.72) saving ~85% bandwidth before uploading.
  - Voice notes are recorded via `MediaRecorder` configured at 24kbps mono Opus (~180KB per minute).
